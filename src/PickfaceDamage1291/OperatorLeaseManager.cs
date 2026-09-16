using System.Diagnostics;
using System.Text.Json;

namespace PickfaceDamage1291;

internal sealed class OperatorLeaseManager : IAsyncDisposable
{
    private const int HeartbeatSeconds = 60;
    private const int OnlineThresholdMs = 120_000;
    private const int LeaseDurationMs = 30 * 60 * 1000;

    private readonly FirebaseSession _session;
    private readonly string _deviceId;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _renewGate = new(1, 1);
    private string _sessionId;
    private Task? _heartbeatTask;
    private Task? _streamTask;
    private long _monotonicLeaseDeadline;
    private int _kickRaised;

    public event Action<string, bool>? StateChanged;
    public event Action<string>? Kicked;

    public bool CanCreateDamage { get; private set; }
    public bool IsAcquired { get; private set; }
    public string SessionId => _sessionId;
    public ActiveOperatorRecord? CurrentLease => _session.CachedOperator;

    public OperatorLeaseManager(FirebaseSession session, bool reuseCachedLease = false)
    {
        _session = session;
        _deviceId = SecureSessionStore.GetOrCreateDeviceId();
        _sessionId = reuseCachedLease && session.CachedOperator is not null
            ? session.CachedOperator.SessionId
            : Guid.NewGuid().ToString("N");

        if (reuseCachedLease && session.CachedOperator is not null &&
            string.Equals(session.CachedOperator.Uid, session.Uid, StringComparison.Ordinal) &&
            session.CachedOperator.LeaseUntil > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
        {
            IsAcquired = true;
            CanCreateDamage = true;
            var remainingMs = Math.Max(0, session.CachedOperator.LeaseUntil - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            _monotonicLeaseDeadline = Stopwatch.GetTimestamp() + (long)(remainingMs / 1000d * Stopwatch.Frequency);
        }
    }

    public async Task<OperatorAcquireResult> AcquireAsync(
        Func<ActiveOperatorRecord, bool, bool> confirmTakeover,
        CancellationToken ct = default)
    {
        if (_session.Profile.IsAdmin)
            return new OperatorAcquireResult(false, "ADMIN không tham gia active_operator.", null);
        if (_session.OfflineMode)
            return IsAcquired
                ? new OperatorAcquireResult(true, "Đang sử dụng lease offline đã xác nhận trước đó.", _session.CachedOperator)
                : new OperatorAcquireResult(false, "Không có lease offline còn hiệu lực trên laptop này.", null);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var snapshot = await FirebaseClient.GetActiveOperatorSnapshotAsync(_session, ct);
            var now = await FirebaseClient.GetServerNowMsAsync(_session, ct);
            var current = snapshot.Value;

            if (current is not null && string.Equals(current.SessionId, _sessionId, StringComparison.Ordinal))
            {
                SetLease(current, "Đã xác nhận quyền nhập hiện tại.");
                StartMonitoring();
                return new OperatorAcquireResult(true, "Đã xác nhận quyền nhập hiện tại.", current);
            }

            if (current is not null)
            {
                var online = now - current.LastSeen <= OnlineThresholdMs;
                var leaseActive = current.LeaseUntil > now;

                if (!online && leaseActive)
                {
                    return new OperatorAcquireResult(
                        false,
                        $"{current.Username} có thể đang làm việc OFFLINE. Lease còn hiệu lực đến {current.LeaseUntilLocal:dd/MM/yyyy HH:mm:ss}. Không thể chiếm quyền trước khi lease hết hạn.",
                        current);
                }

                if (!confirmTakeover(current, online))
                    return new OperatorAcquireResult(false, "Đã hủy đăng nhập vào vùng nhập liệu.", current);
            }

            var generation = (current?.Generation ?? 0) + 1;
            var lease = new ActiveOperatorRecord
            {
                Uid = _session.Uid,
                Username = _session.Profile.Username,
                SessionId = _sessionId,
                DeviceId = _deviceId,
                Generation = generation,
                AcquiredAt = now,
                LastSeen = now,
                LeaseUntil = now + LeaseDurationMs
            };

            if (!await FirebaseClient.PutActiveOperatorAsync(_session, lease, snapshot.ETag, ct))
                continue;

            SetLease(lease, current is null ? "Đã nhận quyền nhập." : "Đã chuyển quyền nhập sang phiên này.");
            try
            {
                await FirebaseClient.AppendAuditAsync(
                    _session,
                    current is null ? "OPERATOR_ACQUIRED" : "OPERATOR_TAKEOVER",
                    current is null
                        ? new { new_session = _sessionId }
                        : new { previous_uid = current.Uid, previous_username = current.Username, previous_session = current.SessionId, previous_online = now - current.LastSeen <= OnlineThresholdMs },
                    _sessionId,
                    ct);
            }
            catch
            {
                // Audit failure must not invalidate an already-acquired operator lease.
            }
            StartMonitoring();
            return new OperatorAcquireResult(true, "Đã nhận quyền nhập.", lease);
        }

        return new OperatorAcquireResult(false, "Trạng thái người đang sử dụng thay đổi liên tục. Hãy thử lại.", null);
    }

    public void StartOfflineMonitoring()
    {
        if (!IsAcquired) return;
        StateChanged?.Invoke("OFFLINE — đang dùng quyền nhập đã cấp trước đó.", true);
        StartMonitoring();
    }

    public async Task<bool> CheckAdminMayCreateAsync(CancellationToken ct = default)
    {
        if (!_session.Profile.IsAdmin) return CanCreateDamage;
        if (_session.OfflineMode) return false;
        try
        {
            var snapshot = await FirebaseClient.GetActiveOperatorSnapshotAsync(_session, ct);
            return snapshot.Value is null;
        }
        catch
        {
            return false;
        }
    }

    private void StartMonitoring()
    {
        if (_heartbeatTask is null)
            _heartbeatTask = Task.Run(() => HeartbeatLoopAsync(_cts.Token));
        if (_streamTask is null && !_session.OfflineMode)
            _streamTask = Task.Run(() => StreamLoopAsync(_cts.Token));
    }

    private void SetLease(ActiveOperatorRecord lease, string message)
    {
        _session.CachedOperator = lease;
        _session.OfflineMode = false;
        IsAcquired = true;
        CanCreateDamage = true;
        _monotonicLeaseDeadline = Stopwatch.GetTimestamp() + (long)(LeaseDurationMs / 1000d * Stopwatch.Frequency);
        SecureSessionStore.Save(_session);
        StateChanged?.Invoke(message, true);
    }

    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(HeartbeatSeconds), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (ct.IsCancellationRequested) break;
            if (_session.OfflineMode)
            {
                await TryReconnectAsync(ct);
                EvaluateOfflineDeadline();
                continue;
            }

            await RenewOnceAsync(ct);
        }
    }

    private async Task RenewOnceAsync(CancellationToken ct)
    {
        if (!await _renewGate.WaitAsync(0, ct)) return;
        try
        {
            var snapshot = await FirebaseClient.GetActiveOperatorSnapshotAsync(_session, ct);
            if (snapshot.Value is null || !string.Equals(snapshot.Value.SessionId, _sessionId, StringComparison.Ordinal))
            {
                RaiseKicked(snapshot.Value is null
                    ? "Phiên nhập không còn giữ quyền trên hệ thống."
                    : $"Quyền nhập đã chuyển sang tài khoản {snapshot.Value.Username}.");
                return;
            }

            var now = await FirebaseClient.GetServerNowMsAsync(_session, ct);
            var current = snapshot.Value;
            var renewed = new ActiveOperatorRecord
            {
                Uid = current.Uid,
                Username = current.Username,
                SessionId = current.SessionId,
                DeviceId = current.DeviceId,
                Generation = current.Generation + 1,
                AcquiredAt = current.AcquiredAt,
                LastSeen = now,
                LeaseUntil = now + LeaseDurationMs
            };

            if (!await FirebaseClient.PutActiveOperatorAsync(_session, renewed, snapshot.ETag, ct))
                return;

            SetLease(renewed, "Online — quyền nhập đang hoạt động.");
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            AppLog.Exception("OPERATOR_HEARTBEAT_ONLINE_FAILED", ex, new Dictionary<string, object?>
            {
                ["firebase_rtdb_route"] = NetworkHttpClientFactory.IsRtdbRelayPreferred ? "google_relay" : "direct_firebase"
            });
            _session.OfflineMode = true;
            SecureSessionStore.Save(_session);
            StateChanged?.Invoke("Mất kết nối — chuyển sang thời gian dự phòng offline tối đa 30 phút.", IsOfflineLeaseStillValid());
            EvaluateOfflineDeadline();
        }
        finally
        {
            _renewGate.Release();
        }
    }

    private async Task TryReconnectAsync(CancellationToken ct)
    {
        try
        {
            _session.OfflineMode = false;
            await FirebaseClient.EnsureFreshAsync(_session, ct);
            var snapshot = await FirebaseClient.GetActiveOperatorSnapshotAsync(_session, ct);
            if (snapshot.Value is null || !string.Equals(snapshot.Value.SessionId, _sessionId, StringComparison.Ordinal))
            {
                RaiseKicked(snapshot.Value is null
                    ? "Kết nối đã trở lại nhưng phiên cũ không còn quyền nhập."
                    : $"Kết nối đã trở lại: quyền nhập hiện thuộc {snapshot.Value.Username}.");
                return;
            }
            await RenewOnceAsync(ct);
            if (_streamTask is null || _streamTask.IsCompleted)
                _streamTask = Task.Run(() => StreamLoopAsync(_cts.Token));
        }
        catch
        {
            _session.OfflineMode = true;
            EvaluateOfflineDeadline();
        }
    }

    private bool IsOfflineLeaseStillValid()
    {
        if (!IsAcquired || _session.CachedOperator is null) return false;
        if (_monotonicLeaseDeadline > 0)
            return Stopwatch.GetTimestamp() < _monotonicLeaseDeadline;
        return _session.CachedOperator.LeaseUntil > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    private void EvaluateOfflineDeadline()
    {
        var valid = IsOfflineLeaseStillValid();
        CanCreateDamage = valid;
        if (!valid)
            StateChanged?.Invoke("OFFLINE quá thời hạn lease — chỉ giữ dữ liệu/bản nháp local, không được xác nhận phiếu mới.", false);
    }

    private async Task StreamLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && !_session.OfflineMode)
        {
            try
            {
                using var response = await FirebaseClient.OpenActiveOperatorStreamAsync(_session, ct);
                if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Realtime stream {(int)response.StatusCode}");
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var reader = new StreamReader(stream);
                string? eventName = null;
                string? data = null;

                while (!ct.IsCancellationRequested && !reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (line is null) break;
                    if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
                        eventName = line[6..].Trim();
                    else if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                        data = line[5..].Trim();
                    else if (line.Length == 0)
                    {
                        if ((eventName == "put" || eventName == "patch") && !string.IsNullOrWhiteSpace(data))
                            HandleStreamData(data);
                        eventName = null;
                        data = null;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                if (ct.IsCancellationRequested) break;
                try { await Task.Delay(TimeSpan.FromSeconds(3), ct); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private void HandleStreamData(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (!root.TryGetProperty("path", out var pathNode) || pathNode.GetString() != "/") return;
            if (!root.TryGetProperty("data", out var dataNode)) return;
            if (dataNode.ValueKind == JsonValueKind.Null)
            {
                RaiseKicked("Phiên nhập đã bị kết thúc trên hệ thống.");
                return;
            }
            var current = dataNode.Deserialize<ActiveOperatorRecord>();
            if (current is not null && !string.Equals(current.SessionId, _sessionId, StringComparison.Ordinal))
                RaiseKicked($"Phiên làm việc đã được chuyển sang tài khoản {current.Username} trên thiết bị khác.");
        }
        catch
        {
            // Malformed stream event is ignored; heartbeat is the secondary correctness check.
        }
    }

    private void RaiseKicked(string message)
    {
        if (Interlocked.Exchange(ref _kickRaised, 1) != 0) return;
        CanCreateDamage = false;
        IsAcquired = false;
        Kicked?.Invoke(message);
        _cts.Cancel();
    }

    public async Task ReleaseAsync(bool normalLogout = true)
    {
        if (_session.Profile.IsAdmin || string.IsNullOrWhiteSpace(_sessionId)) return;
        try
        {
            if (!_session.OfflineMode)
            {
                var snapshot = await FirebaseClient.GetActiveOperatorSnapshotAsync(_session);
                if (snapshot.Value is not null && string.Equals(snapshot.Value.SessionId, _sessionId, StringComparison.Ordinal))
                    await FirebaseClient.DeleteActiveOperatorAsync(_session, snapshot.ETag);
                if (normalLogout)
                    await FirebaseClient.AppendAuditAsync(_session, "OPERATOR_RELEASED", new { session = _sessionId }, _sessionId);
            }
        }
        catch
        {
            // Lease will expire by itself if the machine/network disappears.
        }
        finally
        {
            _session.CachedOperator = null;
            SecureSessionStore.Save(_session);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try
        {
            var tasks = new[] { _heartbeatTask, _streamTask }.Where(x => x is not null).Cast<Task>().ToArray();
            if (tasks.Length > 0) await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch { }
        _cts.Dispose();
        _renewGate.Dispose();
    }
}

internal sealed record OperatorAcquireResult(bool Success, string Message, ActiveOperatorRecord? Existing);
