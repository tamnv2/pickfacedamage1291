using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PickfaceDamage1291;

internal sealed record UsageAggregate(
    IReadOnlyDictionary<string, long> HourMetrics,
    IReadOnlyDictionary<string, long> DayMetrics,
    IReadOnlyDictionary<string, long> MonthMetrics,
    IReadOnlyDictionary<string, long> Gauges,
    int DeviceCount,
    DateTimeOffset? LatestSnapshot,
    int ScannedEvents,
    bool HistoryTruncated)
{
    public long Hour(string key) => HourMetrics.TryGetValue(key, out var value) ? value : 0;
    public long Day(string key) => DayMetrics.TryGetValue(key, out var value) ? value : 0;
    public long Month(string key) => MonthMetrics.TryGetValue(key, out var value) ? value : 0;
    public long Gauge(string key) => Gauges.TryGetValue(key, out var value) ? value : 0;
}

/// <summary>
/// Lightweight client-side meter for the resources this app actually touches. Counters are
/// cumulative per hour/day/month and are persisted locally. Every few hours each active device
/// sends one compact USAGE_SNAPSHOT_V146 event through the existing audit channel; ADMIN can then
/// aggregate the newest snapshot for each device without adding another backend or widening scope.
/// The meter intentionally never stores credentials, tokens, request bodies, SKU data or images.
/// </summary>
internal static class UsageTelemetry
{
    public const string SnapshotAction = "USAGE_SNAPSHOT_V146";
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private static TelemetryState? _state;
    private static DateTime _lastPersistUtc = DateTime.MinValue;
    private static readonly TimeSpan PersistInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan SnapshotInterval = TimeSpan.FromHours(4);

    private static string StateFile => Path.Combine(AppPaths.Data, "usage-telemetry-v146.json");

    public static async Task<string?> TryReadGatewayActionAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            if (!IsAppsScript(request.RequestUri) || request.Content is null) return null;
            var body = await request.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(body)) return null;
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("action", out var actionNode) || actionNode.ValueKind != JsonValueKind.String) return null;
            var action = (actionNode.GetString() ?? string.Empty).Trim().ToLowerInvariant();
            return IsSafeActionName(action) ? action : null;
        }
        catch
        {
            return null;
        }
    }

    public static void RecordHttpRequest(Uri? uri, HttpMethod method, long requestBytes, string? gatewayAction = null)
    {
        try
        {
            if (uri is null) return;
            requestBytes = Math.Max(0, requestBytes);
            if (IsFirebaseRtdb(uri))
            {
                Add(method == HttpMethod.Get ? "firebase_rtdb_reads" : "firebase_rtdb_writes", 1);
                if (requestBytes > 0) Add("firebase_rtdb_upload_bytes", requestBytes);
                return;
            }

            if (IsFirebaseAuth(uri))
            {
                Add("firebase_auth_requests", 1);
                if (requestBytes > 0) Add("firebase_auth_upload_bytes", requestBytes);
                return;
            }

            if (IsAppsScript(uri))
            {
                Add("apps_script_requests", 1);
                if (requestBytes > 0) Add("apps_script_upload_bytes", requestBytes);
                if (!string.IsNullOrWhiteSpace(gatewayAction) && IsSafeActionName(gatewayAction))
                {
                    Add("gateway_action_" + gatewayAction, 1);
                    if (requestBytes > 0) Add("gateway_action_" + gatewayAction + "_upload_bytes", requestBytes);
                }
                return;
            }

            if (IsGitHubApi(uri))
            {
                Add("github_api_requests", 1);
                if (requestBytes > 0) Add("github_upload_bytes", requestBytes);
                return;
            }

            if (IsGitHub(uri))
            {
                Add("github_download_requests", 1);
                if (requestBytes > 0) Add("github_upload_bytes", requestBytes);
            }
        }
        catch
        {
            // Usage tracking must never break a business request.
        }
    }

    public static void RecordHttpResponse(Uri? uri, long responseBytes, string? gatewayAction = null)
    {
        try
        {
            if (uri is null) return;
            responseBytes = Math.Max(0, responseBytes);
            if (responseBytes <= 0) return;

            if (IsFirebaseRtdb(uri)) Add("firebase_rtdb_download_bytes", responseBytes);
            else if (IsFirebaseAuth(uri)) Add("firebase_auth_download_bytes", responseBytes);
            else if (IsAppsScript(uri))
            {
                Add("apps_script_download_bytes", responseBytes);
                if (!string.IsNullOrWhiteSpace(gatewayAction) && IsSafeActionName(gatewayAction))
                    Add("gateway_action_" + gatewayAction + "_download_bytes", responseBytes);
            }
            else if (IsGitHub(uri)) Add("github_download_bytes", responseBytes);
        }
        catch
        {
        }
    }

    public static async Task<bool> SyncSnapshotAsync(bool force = false, CancellationToken ct = default)
    {
        var session = AppSession.Current;
        if (session is null || session.OfflineMode || !RuntimeConfigService.IsGoogleGatewayConfigured) return false;

        SnapshotPayload snapshot;
        lock (Gate)
        {
            var state = StateLocked();
            EnsurePeriodsLocked(state);
            if (!force && state.LastSnapshotUtc is { } last && DateTime.UtcNow - last < SnapshotInterval) return false;
            snapshot = BuildSnapshotLocked(state);
            state.LastSnapshotUtc = DateTime.UtcNow;
            PersistLocked(state, true);
        }

        try
        {
            await FirebaseClient.AppendAuditAsync(session, SnapshotAction, new
            {
                schema = snapshot.Schema,
                app_version = snapshot.AppVersion,
                hour = snapshot.Hour,
                day = snapshot.Day,
                month = snapshot.Month,
                hour_metrics = snapshot.HourMetrics,
                day_metrics = snapshot.DayMetrics,
                month_metrics = snapshot.MonthMetrics,
                gauges = snapshot.Gauges
            }, AppSession.OperatorManager?.SessionId, ct);
            await FirebaseClient.FlushAuditOutboxAsync(session, ct);
            return true;
        }
        catch (Exception ex)
        {
            try { AppLog.Exception("USAGE_SNAPSHOT_SYNC_FAILED", ex); } catch { }
            return false;
        }
    }

    public static async Task<UsageAggregate> LoadAggregateAsync(FirebaseSession adminSession, CancellationToken ct = default)
    {
        if (!adminSession.Profile.IsAdmin || adminSession.OfflineMode)
            throw new InvalidOperationException("Cần ADMIN online để xem mức sử dụng toàn mô hình.");

        await SyncSnapshotAsync(force: true, ct);

        const int pageSize = 100;
        const int maxPages = 10;
        long? before = null;
        var latestByDevice = new Dictionary<string, (SnapshotPayload Snapshot, long ServerTime)>(StringComparer.OrdinalIgnoreCase);
        var scanned = 0;
        var historyTruncated = false;

        for (var pageNo = 0; pageNo < maxPages; pageNo++)
        {
            ct.ThrowIfCancellationRequested();
            var page = await AuditAdminService.ListPageAsync(adminSession, before, pageSize, ct);
            scanned += page.Entries.Count;

            foreach (var entry in page.Entries)
            {
                if (!string.Equals(entry.Action, SnapshotAction, StringComparison.Ordinal)) continue;
                var deviceId = (entry.DeviceId ?? string.Empty).Trim();
                if (deviceId.Length == 0 || latestByDevice.ContainsKey(deviceId)) continue;
                var parsed = ParseSnapshot(entry.Details);
                if (parsed is null) continue;
                latestByDevice[deviceId] = (parsed, AuditAdminService.ParseServerTime(entry.ServerTime));
            }

            if (!page.HasMore || page.NextBeforeServerTime is null) break;
            before = page.NextBeforeServerTime;
            if (pageNo == maxPages - 1) historyTruncated = true;
        }

        var now = DateTime.Now;
        var hourKey = now.ToString("yyyy-MM-dd-HH");
        var dayKey = now.ToString("yyyy-MM-dd");
        var monthKey = now.ToString("yyyy-MM");
        var hour = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var day = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var month = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var gauges = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        long latestServerTime = 0;
        var devices = 0;

        foreach (var (_, item) in latestByDevice)
        {
            var snap = item.Snapshot;
            if (!string.Equals(snap.Month, monthKey, StringComparison.Ordinal)) continue;
            devices++;
            Merge(month, snap.MonthMetrics);
            Merge(gauges, snap.Gauges);
            if (string.Equals(snap.Day, dayKey, StringComparison.Ordinal)) Merge(day, snap.DayMetrics);
            if (string.Equals(snap.Hour, hourKey, StringComparison.Ordinal)) Merge(hour, snap.HourMetrics);
            latestServerTime = Math.Max(latestServerTime, item.ServerTime);
        }

        DateTimeOffset? latest = latestServerTime > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(latestServerTime).ToLocalTime()
            : null;

        return new UsageAggregate(hour, day, month, gauges, devices, latest, scanned, historyTruncated);
    }

    public static string FormatBytes(long bytes)
    {
        bytes = Math.Max(0, bytes);
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes:N0} B" : $"{value:N2} {units[unit]}";
    }

    private static void Add(string metric, long value)
    {
        if (value <= 0 || string.IsNullOrWhiteSpace(metric)) return;
        lock (Gate)
        {
            var state = StateLocked();
            EnsurePeriodsLocked(state);
            AddTo(state.HourMetrics, metric, value);
            AddTo(state.DayMetrics, metric, value);
            AddTo(state.MonthMetrics, metric, value);
            state.LastUpdatedUtc = DateTime.UtcNow;
            PersistLocked(state, false);
        }
    }

    private static TelemetryState StateLocked()
    {
        if (_state is not null) return _state;
        try
        {
            if (File.Exists(StateFile))
                _state = JsonSerializer.Deserialize<TelemetryState>(File.ReadAllText(StateFile), JsonOptions);
        }
        catch
        {
            _state = null;
        }
        _state ??= new TelemetryState();
        EnsurePeriodsLocked(_state);
        return _state;
    }

    private static void EnsurePeriodsLocked(TelemetryState state)
    {
        var now = DateTime.Now;
        var hour = now.ToString("yyyy-MM-dd-HH");
        var day = now.ToString("yyyy-MM-dd");
        var month = now.ToString("yyyy-MM");

        if (!string.Equals(state.Month, month, StringComparison.Ordinal))
        {
            state.Month = month;
            state.MonthMetrics = NewMetrics();
            state.Day = day;
            state.DayMetrics = NewMetrics();
            state.Hour = hour;
            state.HourMetrics = NewMetrics();
        }
        else if (!string.Equals(state.Day, day, StringComparison.Ordinal))
        {
            state.Day = day;
            state.DayMetrics = NewMetrics();
            state.Hour = hour;
            state.HourMetrics = NewMetrics();
        }
        else if (!string.Equals(state.Hour, hour, StringComparison.Ordinal))
        {
            state.Hour = hour;
            state.HourMetrics = NewMetrics();
        }
    }

    private static SnapshotPayload BuildSnapshotLocked(TelemetryState state)
    {
        var gauges = NewMetrics();
        gauges["local_database_bytes"] = SafeFileSize(AppPaths.DatabaseFile);
        gauges["local_images_bytes"] = SafeDirectorySize(AppPaths.Images);
        gauges["local_pending_images_bytes"] = SafeDirectorySize(AppPaths.PendingImages);

        return new SnapshotPayload
        {
            Schema = 1,
            AppVersion = VersionUpdateService.CurrentVersionText,
            Hour = state.Hour,
            Day = state.Day,
            Month = state.Month,
            HourMetrics = Copy(state.HourMetrics),
            DayMetrics = Copy(state.DayMetrics),
            MonthMetrics = Copy(state.MonthMetrics),
            Gauges = gauges
        };
    }

    private static SnapshotPayload? ParseSnapshot(object? details)
    {
        try
        {
            if (details is null) return null;
            var element = details is JsonElement json ? json : JsonSerializer.SerializeToElement(details, JsonOptions);
            var snapshot = element.Deserialize<SnapshotPayload>(JsonOptions);
            if (snapshot is null || snapshot.Schema != 1 || string.IsNullOrWhiteSpace(snapshot.Month)) return null;
            snapshot.HourMetrics ??= NewMetrics();
            snapshot.DayMetrics ??= NewMetrics();
            snapshot.MonthMetrics ??= NewMetrics();
            snapshot.Gauges ??= NewMetrics();
            return snapshot;
        }
        catch
        {
            return null;
        }
    }

    private static void PersistLocked(TelemetryState state, bool force)
    {
        var now = DateTime.UtcNow;
        if (!force && now - _lastPersistUtc < PersistInterval) return;
        try
        {
            Directory.CreateDirectory(AppPaths.Data);
            var temp = StateFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(state, JsonOptions));
            File.Move(temp, StateFile, true);
            _lastPersistUtc = now;
        }
        catch
        {
        }
    }

    private static long SafeFileSize(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : 0; } catch { return 0; }
    }

    private static long SafeDirectorySize(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return 0;
            long total = 0;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { total = checked(total + new FileInfo(file).Length); }
                catch (OverflowException) { return long.MaxValue; }
                catch { }
            }
            return total;
        }
        catch { return 0; }
    }

    private static void Merge(Dictionary<string, long> target, IReadOnlyDictionary<string, long>? source)
    {
        if (source is null) return;
        foreach (var (key, value) in source) AddTo(target, key, Math.Max(0, value));
    }

    private static void AddTo(Dictionary<string, long> target, string key, long value)
    {
        if (value <= 0) return;
        target.TryGetValue(key, out var current);
        try { target[key] = checked(current + value); }
        catch (OverflowException) { target[key] = long.MaxValue; }
    }

    private static Dictionary<string, long> NewMetrics() => new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, long> Copy(IReadOnlyDictionary<string, long> source) =>
        source.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);

    private static bool IsFirebaseRtdb(Uri? uri)
    {
        if (uri is null) return false;
        var host = uri.Host;
        return host.EndsWith(".firebaseio.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".firebasedatabase.app", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFirebaseAuth(Uri? uri)
    {
        if (uri is null) return false;
        return string.Equals(uri.Host, "identitytoolkit.googleapis.com", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(uri.Host, "securetoken.googleapis.com", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAppsScript(Uri? uri)
    {
        if (uri is null) return false;
        return string.Equals(uri.Host, "script.google.com", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.EndsWith(".googleusercontent.com", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGitHubApi(Uri? uri) =>
        uri is not null && string.Equals(uri.Host, "api.github.com", StringComparison.OrdinalIgnoreCase);

    private static bool IsGitHub(Uri? uri)
    {
        if (uri is null) return false;
        var host = uri.Host;
        return string.Equals(host, "github.com", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(host, "api.github.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSafeActionName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64) return false;
        foreach (var ch in value)
            if (!(char.IsLetterOrDigit(ch) || ch == '_')) return false;
        return true;
    }

    private sealed class TelemetryState
    {
        public string Hour { get; set; } = string.Empty;
        public string Day { get; set; } = string.Empty;
        public string Month { get; set; } = string.Empty;
        public Dictionary<string, long> HourMetrics { get; set; } = NewMetrics();
        public Dictionary<string, long> DayMetrics { get; set; } = NewMetrics();
        public Dictionary<string, long> MonthMetrics { get; set; } = NewMetrics();
        public DateTime LastUpdatedUtc { get; set; }
        public DateTime? LastSnapshotUtc { get; set; }
    }

    private sealed class SnapshotPayload
    {
        [JsonPropertyName("schema")]
        public int Schema { get; set; }

        [JsonPropertyName("app_version")]
        public string AppVersion { get; set; } = string.Empty;

        [JsonPropertyName("hour")]
        public string Hour { get; set; } = string.Empty;

        [JsonPropertyName("day")]
        public string Day { get; set; } = string.Empty;

        [JsonPropertyName("month")]
        public string Month { get; set; } = string.Empty;

        [JsonPropertyName("hour_metrics")]
        public Dictionary<string, long>? HourMetrics { get; set; } = NewMetrics();

        [JsonPropertyName("day_metrics")]
        public Dictionary<string, long>? DayMetrics { get; set; } = NewMetrics();

        [JsonPropertyName("month_metrics")]
        public Dictionary<string, long>? MonthMetrics { get; set; } = NewMetrics();

        [JsonPropertyName("gauges")]
        public Dictionary<string, long>? Gauges { get; set; } = NewMetrics();
    }
}
