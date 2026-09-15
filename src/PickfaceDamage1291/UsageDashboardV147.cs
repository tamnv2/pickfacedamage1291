using System.Text.Json;

namespace PickfaceDamage1291;

internal sealed record UsageDeviceV147(
    string DeviceId,
    string Username,
    string AppVersion,
    DateTimeOffset LastSeen,
    bool Active);

internal sealed record UsageAggregateV147(
    IReadOnlyDictionary<string, long> HourMetrics,
    IReadOnlyDictionary<string, long> DayMetrics,
    IReadOnlyDictionary<string, long> MonthMetrics,
    IReadOnlyDictionary<string, long> ActiveGauges,
    IReadOnlyList<UsageDeviceV147> Devices,
    int ActiveDeviceCount,
    int MonthDeviceCount,
    DateTimeOffset? LatestSnapshot,
    int ScannedEvents,
    bool HistoryTruncated)
{
    public long Hour(string key) => HourMetrics.TryGetValue(key, out var value) ? value : 0;
    public long Day(string key) => DayMetrics.TryGetValue(key, out var value) ? value : 0;
    public long Month(string key) => MonthMetrics.TryGetValue(key, out var value) ? value : 0;
    public long ActiveGauge(string key) => ActiveGauges.TryGetValue(key, out var value) ? value : 0;
}

/// <summary>
/// v1.4.7 usage view keeps the v1.4.6 metering format but synchronizes every running machine
/// more frequently. The dashboard always picks the newest snapshot per device, identifies devices
/// active in the last 90 minutes, and keeps month-to-date counters from the newest snapshot of all
/// devices seen in the current month. No token, credential, request body, SKU or image is stored.
/// </summary>
internal static class UsageDashboardV147
{
    private static readonly TimeSpan SnapshotCadence = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ActiveWindow = TimeSpan.FromMinutes(90);
    private static System.Windows.Forms.Timer? _snapshotTimer;
    private static bool _syncBusy;

    public static void Attach(MainForm main)
    {
        AttachReportHeaderFix(main);
        AttachSnapshotHeartbeat(main);
        if (AppSession.Current?.Profile.IsAdmin != true) return;

        var tabs = FindAll<TabControl>(main).FirstOrDefault();
        var settings = tabs?.TabPages.Cast<TabPage>().FirstOrDefault(x => string.Equals(x.Text, "Cài đặt", StringComparison.Ordinal));
        if (settings is null) return;
        var scroll = FindAll<FlowLayoutPanel>(settings).FirstOrDefault(x => x.FlowDirection == FlowDirection.TopDown);
        if (scroll is null) return;

        foreach (var old in FindAll<GroupBox>(settings)
                     .Where(x => x.Text.StartsWith("Theo dõi giới hạn free / mức sử dụng toàn mô hình", StringComparison.OrdinalIgnoreCase)))
            old.Visible = false;

        var box = new GroupBox
        {
            Text = "Theo dõi giới hạn free / mức sử dụng toàn mô hình — ADMIN",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, 18),
            Padding = new Padding(12),
            Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold),
            Width = Math.Max(760, scroll.ClientSize.Width - scroll.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 12)
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(10),
            Font = new Font("Segoe UI", 9.5F)
        };

        var explanation = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(1200, 0),
            ForeColor = Color.DimGray,
            Text = "Mỗi máy đang chạy tự đồng bộ một snapshot usage khi mở ứng dụng và khoảng 30 phút/lần. Dashboard lấy snapshot mới nhất theo device_id nên không cộng trùng một máy. Máy có snapshot trong 90 phút gần nhất được coi là đang hoạt động. Tổng tháng vẫn giữ phần usage đã phát sinh của máy đã hoạt động trong tháng, kể cả khi máy đó hiện đã tắt."
        };

        var commands = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            WrapContents = true,
            Padding = new Padding(0, 8, 0, 8)
        };
        var refresh = new Button
        {
            Text = "Đồng bộ máy này & làm mới toàn mô hình",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, 42),
            Padding = new Padding(14, 6, 14, 6),
            Margin = new Padding(4, 4, 10, 6)
        };
        var summary = new Label { AutoSize = true, Padding = new Padding(10, 11, 0, 0) };
        commands.Controls.AddRange([refresh, summary]);

        var metricsGrid = NewGrid(430);
        AddUsageColumns(metricsGrid);

        var deviceTitle = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
            Padding = new Padding(0, 10, 0, 5),
            Text = "Máy đang đồng nhất trong thống kê"
        };
        var deviceGrid = NewGrid(185);
        AddDeviceColumns(deviceGrid);

        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(1200, 0),
            ForeColor = Color.DimGray,
            Text = "Snapshot của từng máy là số đo phía ứng dụng. Byte mạng không thay thế meter/billing console của nhà cung cấp. Nếu một máy không còn xuất hiện ở trạng thái Hoạt động, kiểm tra máy đó đã chạy v1.4.7, đang online và đã đồng bộ trong 90 phút gần nhất. GitHub chỉ phục vụ cập nhật; business runtime vẫn dùng Google/Firebase theo mô hình hiện hành."
        };

        root.Controls.Add(explanation, 0, 0);
        root.Controls.Add(commands, 0, 1);
        root.Controls.Add(metricsGrid, 0, 2);
        root.Controls.Add(deviceTitle, 0, 3);
        root.Controls.Add(deviceGrid, 0, 4);
        root.Controls.Add(note, 0, 5);
        box.Controls.Add(root);
        scroll.Controls.Add(box);

        refresh.Click += async (_, _) => await RefreshAsync(refresh, summary, metricsGrid, deviceGrid, quiet: false);
        main.Shown += async (_, _) => await RefreshAsync(refresh, summary, metricsGrid, deviceGrid, quiet: true);
        scroll.SizeChanged += (_, _) =>
        {
            box.Width = Math.Max(760, scroll.ClientSize.Width - scroll.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 12);
        };
    }

    private static void AttachReportHeaderFix(MainForm main)
    {
        foreach (var grid in FindAll<DataGridView>(main))
        {
            void Fix()
            {
                if (grid.Columns.Contains("Shift")) grid.Columns["Shift"]!.HeaderText = "Ca ghi nhận";
            }

            Fix();
            grid.ColumnAdded += (_, e) =>
            {
                if (string.Equals(e.Column.Name, "Shift", StringComparison.Ordinal))
                    e.Column.HeaderText = "Ca ghi nhận";
            };
        }
    }

    private static void AttachSnapshotHeartbeat(MainForm main)
    {
        _snapshotTimer?.Dispose();
        _snapshotTimer = new System.Windows.Forms.Timer { Interval = (int)SnapshotCadence.TotalMilliseconds };
        _snapshotTimer.Tick += async (_, _) => await SyncCurrentDeviceAsync();

        main.Shown += async (_, _) =>
        {
            _snapshotTimer.Start();
            await SyncCurrentDeviceAsync();
        };
        main.FormClosed += (_, _) =>
        {
            _snapshotTimer?.Stop();
            _snapshotTimer?.Dispose();
            _snapshotTimer = null;
        };
    }

    private static async Task SyncCurrentDeviceAsync()
    {
        if (_syncBusy) return;
        _syncBusy = true;
        try
        {
            await UsageTelemetry.SyncSnapshotAsync(force: true);
        }
        catch (Exception ex)
        {
            try { AppLog.Exception("V147_USAGE_SYNC_FAILED", ex); } catch { }
        }
        finally
        {
            _syncBusy = false;
        }
    }

    private static async Task RefreshAsync(Button refresh, Label summary, DataGridView metricsGrid, DataGridView deviceGrid, bool quiet)
    {
        var session = AppSession.Current;
        if (session is null || !session.Profile.IsAdmin)
        {
            summary.Text = "Chỉ ADMIN được xem.";
            return;
        }

        try
        {
            refresh.Enabled = false;
            summary.Text = "Đang đồng bộ máy hiện tại và tổng hợp snapshot mới nhất của các máy...";
            await SyncCurrentDeviceAsync();
            var aggregate = await LoadAggregateAsync(session);
            RenderMetrics(metricsGrid, aggregate);
            RenderDevices(deviceGrid, aggregate.Devices);
            var latest = aggregate.LatestSnapshot?.ToString("dd/MM/yyyy HH:mm:ss") ?? "-";
            summary.Text = $"Máy đang hoạt động: {aggregate.ActiveDeviceCount:N0} | Máy có usage tháng này: {aggregate.MonthDeviceCount:N0} | Snapshot mới nhất: {latest}" +
                           (aggregate.HistoryTruncated ? $" | Lịch sử quét giới hạn {aggregate.ScannedEvents:N0} log" : string.Empty);
        }
        catch (Exception ex)
        {
            summary.Text = "Chưa tổng hợp được usage toàn mô hình: " + ex.Message;
            if (!quiet) AppLog.Exception("V147_USAGE_DASHBOARD_FAILED", ex);
        }
        finally
        {
            refresh.Enabled = true;
        }
    }

    private static async Task<UsageAggregateV147> LoadAggregateAsync(FirebaseSession session, CancellationToken ct = default)
    {
        const int pageSize = 100;
        const int maxPages = 15;
        long? before = null;
        var scanned = 0;
        var truncated = false;
        var latestByDevice = new Dictionary<string, SnapshotRecord>(StringComparer.OrdinalIgnoreCase);

        for (var pageNo = 0; pageNo < maxPages; pageNo++)
        {
            ct.ThrowIfCancellationRequested();
            var page = await AuditAdminService.ListPageAsync(session, before, pageSize, ct);
            scanned += page.Entries.Count;

            foreach (var entry in page.Entries)
            {
                if (!string.Equals(entry.Action, UsageTelemetry.SnapshotAction, StringComparison.Ordinal)) continue;
                var deviceId = (entry.DeviceId ?? string.Empty).Trim();
                if (deviceId.Length == 0) continue;
                var serverTime = AuditAdminService.ParseServerTime(entry.ServerTime);
                if (serverTime <= 0) continue;
                var snapshot = ParseSnapshot(entry, serverTime);
                if (snapshot is null) continue;

                if (!latestByDevice.TryGetValue(deviceId, out var current) || snapshot.ServerTime > current.ServerTime)
                    latestByDevice[deviceId] = snapshot;
            }

            if (!page.HasMore || page.NextBeforeServerTime is null) break;
            before = page.NextBeforeServerTime;
            if (pageNo == maxPages - 1) truncated = true;
        }

        var now = DateTimeOffset.Now;
        var hourKey = now.ToString("yyyy-MM-dd-HH");
        var dayKey = now.ToString("yyyy-MM-dd");
        var monthKey = now.ToString("yyyy-MM");
        var activeCutoff = now - ActiveWindow;

        var hour = NewMetrics();
        var day = NewMetrics();
        var month = NewMetrics();
        var gauges = NewMetrics();
        var deviceRows = new List<UsageDeviceV147>();
        var monthDevices = 0;
        var activeDevices = 0;
        long latestServerTime = 0;

        foreach (var snapshot in latestByDevice.Values)
        {
            var lastSeen = DateTimeOffset.FromUnixTimeMilliseconds(snapshot.ServerTime).ToLocalTime();
            var active = lastSeen >= activeCutoff;
            var inMonth = string.Equals(snapshot.Month, monthKey, StringComparison.Ordinal);

            if (inMonth)
            {
                monthDevices++;
                Merge(month, snapshot.MonthMetrics);
                if (string.Equals(snapshot.Day, dayKey, StringComparison.Ordinal)) Merge(day, snapshot.DayMetrics);
                if (string.Equals(snapshot.Hour, hourKey, StringComparison.Ordinal)) Merge(hour, snapshot.HourMetrics);
            }

            if (active)
            {
                activeDevices++;
                Merge(gauges, snapshot.Gauges);
            }

            if (inMonth || active)
            {
                deviceRows.Add(new UsageDeviceV147(snapshot.DeviceId, snapshot.Username, snapshot.AppVersion, lastSeen, active));
                latestServerTime = Math.Max(latestServerTime, snapshot.ServerTime);
            }
        }

        deviceRows = deviceRows
            .OrderByDescending(x => x.Active)
            .ThenByDescending(x => x.LastSeen)
            .ThenBy(x => x.Username, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        DateTimeOffset? latest = latestServerTime > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(latestServerTime).ToLocalTime()
            : null;

        return new UsageAggregateV147(hour, day, month, gauges, deviceRows, activeDevices, monthDevices, latest, scanned, truncated);
    }

    private static SnapshotRecord? ParseSnapshot(FirebaseAuditEntry entry, long serverTime)
    {
        try
        {
            if (entry.Details is null) return null;
            var root = entry.Details is JsonElement element
                ? element
                : JsonSerializer.SerializeToElement(entry.Details);
            if (root.ValueKind != JsonValueKind.Object) return null;

            var schema = Int(root, "schema");
            if (schema != 1) return null;
            var month = Str(root, "month");
            if (month.Length == 0) return null;

            return new SnapshotRecord(
                (entry.DeviceId ?? string.Empty).Trim(),
                (entry.Username ?? string.Empty).Trim(),
                Str(root, "app_version"),
                serverTime,
                Str(root, "hour"),
                Str(root, "day"),
                month,
                ReadMetrics(root, "hour_metrics"),
                ReadMetrics(root, "day_metrics"),
                ReadMetrics(root, "month_metrics"),
                ReadMetrics(root, "gauges"));
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, long> ReadMetrics(JsonElement root, string property)
    {
        var result = NewMetrics();
        if (!root.TryGetProperty(property, out var node) || node.ValueKind != JsonValueKind.Object) return result;
        foreach (var item in node.EnumerateObject())
        {
            if (item.Value.ValueKind == JsonValueKind.Number && item.Value.TryGetInt64(out var number))
                result[item.Name] = Math.Max(0, number);
            else if (long.TryParse(item.Value.ToString(), out var parsed))
                result[item.Name] = Math.Max(0, parsed);
        }
        return result;
    }

    private static string Str(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var node) || node.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return string.Empty;
        return node.ValueKind == JsonValueKind.String ? node.GetString() ?? string.Empty : node.ToString();
    }

    private static int Int(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var node)) return 0;
        if (node.ValueKind == JsonValueKind.Number && node.TryGetInt32(out var value)) return value;
        return int.TryParse(node.ToString(), out value) ? value : 0;
    }

    private static DataGridView NewGrid(int height) => new()
    {
        Dock = DockStyle.Top,
        Height = height,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        BackgroundColor = Color.White,
        BorderStyle = BorderStyle.Fixed3D,
        RowTemplate = { Height = 36 },
        ColumnHeadersHeight = 40
    };

    private static void AddUsageColumns(DataGridView grid)
    {
        foreach (var (name, header, weight) in new[]
        {
            ("Resource", "Tài nguyên / quota", 170),
            ("Unit", "Đơn vị", 70),
            ("Hour", "Giờ này", 75),
            ("Day", "Hôm nay", 80),
            ("Month", "Tháng này", 90),
            ("Limit", "Giới hạn free / tham chiếu", 135),
            ("Percent", "%", 55),
            ("Note", "Ghi chú", 210)
        }) grid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = header, FillWeight = weight });
    }

    private static void AddDeviceColumns(DataGridView grid)
    {
        foreach (var (name, header, weight) in new[]
        {
            ("Device", "Thiết bị", 95),
            ("User", "Tài khoản gần nhất", 120),
            ("Version", "Phiên bản", 80),
            ("LastSeen", "Snapshot gần nhất", 120),
            ("State", "Trạng thái", 90)
        }) grid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = header, FillWeight = weight });
    }

    private static void RenderDevices(DataGridView grid, IReadOnlyList<UsageDeviceV147> devices)
    {
        grid.Rows.Clear();
        foreach (var d in devices)
        {
            var shortId = d.DeviceId.Length <= 12 ? d.DeviceId : d.DeviceId[..12];
            grid.Rows.Add(shortId, string.IsNullOrWhiteSpace(d.Username) ? "-" : d.Username, string.IsNullOrWhiteSpace(d.AppVersion) ? "-" : d.AppVersion,
                d.LastSeen.ToString("dd/MM/yyyy HH:mm:ss"), d.Active ? "Hoạt động" : "Không hoạt động");
        }
    }

    private static void RenderMetrics(DataGridView grid, UsageAggregateV147 a)
    {
        grid.Rows.Clear();
        const long tenGiB = 10L * 1024 * 1024 * 1024;
        const long oneGiB = 1L * 1024 * 1024 * 1024;

        AddCountRow(grid, "Firebase RTDB đọc", "request", a.Hour("firebase_rtdb_reads"), a.Day("firebase_rtdb_reads"), a.Month("firebase_rtdb_reads"), "Không có % theo request", null, "Đếm GET RTDB do toàn bộ máy phát sinh.");
        AddCountRow(grid, "Firebase RTDB ghi", "request", a.Hour("firebase_rtdb_writes"), a.Day("firebase_rtdb_writes"), a.Month("firebase_rtdb_writes"), "Không có % theo request", null, "Đếm PUT/DELETE; retry ETag có thể tạo thêm request thực tế.");
        AddBytesRow(grid, "Firebase RTDB tải xuống", a.Hour("firebase_rtdb_download_bytes"), a.Day("firebase_rtdb_download_bytes"), a.Month("firebase_rtdb_download_bytes"), "10 GB/tháng (Spark)", tenGiB, "Byte response nhìn thấy ở client/relay; dùng làm cảnh báo sớm.");
        AddBytesRow(grid, "Firebase RTDB tải lên", a.Hour("firebase_rtdb_upload_bytes"), a.Day("firebase_rtdb_upload_bytes"), a.Month("firebase_rtdb_upload_bytes"), "—", null, "Payload app gửi cho RTDB.");
        AddTextRow(grid, "Firebase RTDB lưu trữ", "byte", "—", "—", "—", UsageTelemetry.FormatBytes(oneGiB) + " (Spark)", "—", "Client không có API an toàn để đọc chính xác database storage hiện tại.");

        AddCountRow(grid, "Firebase Authentication / token", "request", a.Hour("firebase_auth_requests"), a.Day("firebase_auth_requests"), a.Month("firebase_auth_requests"), "Phụ thuộc API/plan", null, "Đếm request đăng nhập/refresh; không lưu hoặc hiển thị token.");
        AddCountRow(grid, "Google Apps Script Gateway", "request", a.Hour("apps_script_requests"), a.Day("apps_script_requests"), a.Month("apps_script_requests"), "Phụ thuộc loại tài khoản", null, "Bao gồm sync, audit và RTDB relay qua gateway.");
        AddBytesRow(grid, "Gateway tải lên", a.Hour("apps_script_upload_bytes"), a.Day("apps_script_upload_bytes"), a.Month("apps_script_upload_bytes"), "—", null, "Bao gồm ảnh base64 khi sync_report.");
        AddBytesRow(grid, "Gateway tải xuống", a.Hour("apps_script_download_bytes"), a.Day("apps_script_download_bytes"), a.Month("apps_script_download_bytes"), "—", null, "Response JSON/ảnh trả về qua gateway.");

        AddCountRow(grid, "Google Sheet đọc", "gateway op", SumGateway(a, MetricPeriod.Hour, "pull_changes", "pull_products", "list_audit"), SumGateway(a, MetricPeriod.Day, "pull_changes", "pull_products", "list_audit"), SumGateway(a, MetricPeriod.Month, "pull_changes", "pull_products", "list_audit"), "Quota Apps Script/Sheets", null, "Đếm action đọc Sheet; một action có thể đọc nhiều dòng/ô.");
        AddCountRow(grid, "Google Sheet ghi", "gateway op", SumGateway(a, MetricPeriod.Hour, "sync_report", "push_products", "append_audit", "delete_audit_range"), SumGateway(a, MetricPeriod.Day, "sync_report", "push_products", "append_audit", "delete_audit_range"), SumGateway(a, MetricPeriod.Month, "sync_report", "push_products", "append_audit", "delete_audit_range"), "Quota Apps Script/Sheets", null, "Đếm action ghi do toàn bộ máy phát sinh.");
        AddTextRow(grid, "Google Sheets dung lượng", "cell", "—", "—", "—", "10.000.000 ô / spreadsheet", "—", "Không full-scan Sheet chỉ để đo quota vì chính thao tác đó làm tăng tải.");

        AddCountRow(grid, "Google Drive đọc", "gateway op", GatewayAction(a, "get_image", MetricPeriod.Hour), GatewayAction(a, "get_image", MetricPeriod.Day), GatewayAction(a, "get_image", MetricPeriod.Month), "Quota tài khoản Google", null, "get_image; không quét Drive ngoài root dự án.");
        AddCountRow(grid, "Google Drive ghi", "gateway op", SumGateway(a, MetricPeriod.Hour, "upload_log", "sync_report"), SumGateway(a, MetricPeriod.Day, "upload_log", "sync_report"), SumGateway(a, MetricPeriod.Month, "upload_log", "sync_report"), "Quota tài khoản Google", null, "sync_report có thể ghi nhiều ảnh trong một operation.");
        AddTextRow(grid, "Google Drive storage", "byte", "—", "—", "—", "Quota cấp tài khoản", "—", "Không tính % bằng cách quét dữ liệu Drive ngoài root dự án.");

        var githubHour = a.Hour("github_api_requests");
        AddCountRow(grid, "GitHub API updater", "request", githubHour, a.Day("github_api_requests"), a.Month("github_api_requests"), "60 request/giờ/IP", 60, "Updater public không dùng token; business runtime không phụ thuộc GitHub.", githubHour);
        AddBytesRow(grid, "GitHub tải release", a.Hour("github_download_bytes"), a.Day("github_download_bytes"), a.Month("github_download_bytes"), "—", null, "Chỉ phát sinh khi kiểm tra/tải cập nhật.");

        AddTextRow(grid, "SQLite local — máy đang hoạt động", "byte", "—", "—", UsageTelemetry.FormatBytes(a.ActiveGauge("local_database_bytes")), "Ổ đĩa local", "—", "Tổng footprint DB của các máy có snapshot trong 90 phút gần nhất.");
        AddTextRow(grid, "Ảnh local/cache — máy đang hoạt động", "byte", "—", "—", UsageTelemetry.FormatBytes(a.ActiveGauge("local_images_bytes")), "Ổ đĩa local", "—", "Có thể trùng cache giữa máy; chỉ dùng theo dõi dung lượng local.");
    }

    private enum MetricPeriod { Hour, Day, Month }

    private static long GatewayAction(UsageAggregateV147 a, string action, MetricPeriod period)
    {
        var key = "gateway_action_" + action;
        return period switch
        {
            MetricPeriod.Hour => a.Hour(key),
            MetricPeriod.Day => a.Day(key),
            _ => a.Month(key)
        };
    }

    private static long SumGateway(UsageAggregateV147 a, MetricPeriod period, params string[] actions) =>
        actions.Sum(x => GatewayAction(a, x, period));

    private static void AddCountRow(DataGridView grid, string resource, string unit, long hour, long day, long month, string limitText, long? limit, string note, long? percentValue = null)
    {
        var value = percentValue ?? month;
        var percent = limit is > 0 ? $"{Math.Min(9999, value * 100d / limit.Value):N2}%" : "—";
        grid.Rows.Add(resource, unit, hour.ToString("N0"), day.ToString("N0"), month.ToString("N0"), limitText, percent, note);
    }

    private static void AddBytesRow(DataGridView grid, string resource, long hour, long day, long month, string limitText, long? limit, string note)
    {
        var percent = limit is > 0 ? $"{Math.Min(9999, month * 100d / limit.Value):N2}%" : "—";
        grid.Rows.Add(resource, "byte", UsageTelemetry.FormatBytes(hour), UsageTelemetry.FormatBytes(day), UsageTelemetry.FormatBytes(month), limitText, percent, note);
    }

    private static void AddTextRow(DataGridView grid, string resource, string unit, string hour, string day, string month, string limit, string percent, string note) =>
        grid.Rows.Add(resource, unit, hour, day, month, limit, percent, note);

    private static Dictionary<string, long> NewMetrics() => new(StringComparer.OrdinalIgnoreCase);

    private static void Merge(Dictionary<string, long> target, IReadOnlyDictionary<string, long>? source)
    {
        if (source is null) return;
        foreach (var (key, value) in source)
        {
            if (value <= 0) continue;
            target.TryGetValue(key, out var current);
            try { target[key] = checked(current + value); }
            catch (OverflowException) { target[key] = long.MaxValue; }
        }
    }

    private static IEnumerable<T> FindAll<T>(Control root) where T : Control
    {
        if (root is T self) yield return self;
        foreach (Control child in root.Controls)
        foreach (var found in FindAll<T>(child)) yield return found;
    }

    private sealed record SnapshotRecord(
        string DeviceId,
        string Username,
        string AppVersion,
        long ServerTime,
        string Hour,
        string Day,
        string Month,
        IReadOnlyDictionary<string, long> HourMetrics,
        IReadOnlyDictionary<string, long> DayMetrics,
        IReadOnlyDictionary<string, long> MonthMetrics,
        IReadOnlyDictionary<string, long> Gauges);
}
