using System.Reflection;

namespace PickfaceDamage1291;

/// <summary>
/// v1.4.6 UI/business patch kept isolated from the stable v1.4.x form implementation.
/// It adds report-only Base Units override, required-field gating and the ADMIN usage dashboard.
/// No SKU master row is modified by this runtime.
/// </summary>
internal static class V146Runtime
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly HashSet<Form> PatchedDialogs = [];
    private static EventHandler? _idleHandler;
    private static System.Windows.Forms.Timer? _usageTimer;
    private static bool _periodicSyncBusy;

    public static void Apply(MainForm main)
    {
        try
        {
            var state = BuildMainEntryState(main);
            RewriteOccurrenceLabelsAndOrder(state.Location, state.Date, state.Time, state.Quantity, state.Shift);
            AddBaseUnitOverrideUi(state.BaseOverride);
            AddRequiredFieldGate(state);
            AddAdminUsageDashboard(main);
            AttachReportGridLabels(main);
            AttachEditDialogWatcher(main);
            AttachUsageSnapshotTimer(main);
            state.SyncSkuMaster(resetOverrideIfSkuChanged: false);
            state.UpdateSendState();
            AppLog.Info("V146_RUNTIME_APPLIED", "Đã áp dụng UI/logic v1.4.6.");
        }
        catch (Exception ex)
        {
            AppLog.Exception("V146_RUNTIME_APPLY_FAILED", ex);
        }
    }

    private static MainEntryState BuildMainEntryState(MainForm form)
    {
        var sku = Field<TextBox>(form, "_sku");
        var productName = Field<TextBox>(form, "_productName");
        var location = Field<TextBox>(form, "_location");
        var date = Field<DateTimePicker>(form, "_date");
        var time = Field<TimeWheelPicker>(form, "_time");
        var shift = Field<ExclusiveShiftPicker>(form, "_shift");
        var quantity = Field<NumericUpDown>(form, "_quantity");
        var baseUnit = Field<TextBox>(form, "_baseUnit");
        var send = Field<Button>(form, "_send");
        var entryStatus = Field<Label>(form, "_entryStatus");

        var overrideState = new BaseOverrideState(sku, baseUnit, preserveInitialReportValue: false);
        return new MainEntryState(form, sku, productName, location, date, time, shift, quantity, baseUnit, send, entryStatus, overrideState);
    }

    private static void RewriteOccurrenceLabelsAndOrder(
        Control location,
        Control date,
        Control time,
        Control quantity,
        Control shift)
    {
        if (location.Parent is not TableLayoutPanel table) return;
        var locationLabel = RowLabel(table, location);
        var dateLabel = RowLabel(table, date);
        var timeLabel = RowLabel(table, time);
        var quantityLabel = RowLabel(table, quantity);
        var shiftLabel = RowLabel(table, shift);

        if (locationLabel is not null) locationLabel.Text = "Vị trí phát hiện hư hỏng thực tế *";
        if (dateLabel is not null) dateLabel.Text = "Ngày phát hiện hư hỏng thực tế *";
        if (timeLabel is not null) timeLabel.Text = "Giờ phát hiện hư hỏng thực tế *";
        if (shiftLabel is not null) shiftLabel.Text = "Ca ghi nhân *";

        table.SuspendLayout();
        try
        {
            SetRow(table, location, locationLabel, 0);
            SetRow(table, date, dateLabel, 1);
            SetRow(table, time, timeLabel, 2);
            SetRow(table, quantity, quantityLabel, 3);
            SetRow(table, shift, shiftLabel, 4);
        }
        finally
        {
            table.ResumeLayout(true);
        }
    }

    private static void AddBaseUnitOverrideUi(BaseOverrideState state)
    {
        if (state.BaseUnit.Parent is not TableLayoutPanel table) return;

        state.OverrideCheck.AutoSize = true;
        state.OverrideCheck.Text = "Thay đổi Base Units thực tế hư hỏng";
        state.OverrideCheck.Padding = new Padding(0, 5, 0, 5);

        state.OverrideSelect.DropDownStyle = ComboBoxStyle.DropDownList;
        state.OverrideSelect.Dock = DockStyle.Top;
        state.OverrideSelect.Visible = false;

        AddRuntimeRow(table, "Điều chỉnh Base Units", state.OverrideCheck);
        state.OverrideLabel = AddRuntimeRow(table, "Base Units thực tế hư hỏng", state.OverrideSelect);
        state.OverrideLabel.Visible = false;
        AddRuntimeRow(table, "Phạm vi áp dụng", new Label
        {
            AutoSize = true,
            MaximumSize = new Size(900, 0),
            ForeColor = Color.DimGray,
            Text = "Base Units được chọn chỉ áp dụng cho phiếu hư hỏng hiện tại; không sửa Base Units trong danh mục SKU lưu trữ."
        });

        state.Attach();
    }

    private static void AddRequiredFieldGate(MainEntryState state)
    {
        if (state.Send.Parent is not TableLayoutPanel table) return;
        var sendPos = table.GetPositionFromControl(state.Send);
        var statusPos = table.GetPositionFromControl(state.EntryStatus);
        var statusLabel = statusPos.Row >= 0 ? table.GetControlFromPosition(0, statusPos.Row) : null;

        table.SuspendLayout();
        try
        {
            if (statusPos.Row >= 0)
            {
                table.RowCount = Math.Max(table.RowCount + 1, statusPos.Row + 2);
                table.SetRow(state.EntryStatus, statusPos.Row + 1);
                if (statusLabel is not null) table.SetRow(statusLabel, statusPos.Row + 1);
            }

            state.RequiredNote.AutoSize = true;
            state.RequiredNote.MaximumSize = new Size(900, 0);
            state.RequiredNote.ForeColor = Color.Firebrick;
            state.RequiredNote.Padding = new Padding(0, 0, 0, 8);
            var noteRow = sendPos.Row + 1;
            table.Controls.Add(state.RequiredNote, 1, noteRow);
        }
        finally
        {
            table.ResumeLayout(true);
        }

        state.AttachValidationEvents();
    }

    private static void AttachReportGridLabels(MainForm main)
    {
        var tabs = Field<TabControl>(main, "_tabs");
        var grid = Field<DataGridView>(main, "_reportGrid");

        void ApplyHeaders()
        {
            TrySetHeader(grid, "Date", "Ngày phát hiện hư hỏng thực tế");
            TrySetHeader(grid, "Time", "Giờ phát hiện hư hỏng thực tế");
            TrySetHeader(grid, "Shift", "Ca ghi nhân");
            TrySetHeader(grid, "Location", "Vị trí phát hiện hư hỏng thực tế");
        }

        tabs.SelectedIndexChanged += (_, _) =>
        {
            if (string.Equals(tabs.SelectedTab?.Text, "Danh sách đã nhập", StringComparison.Ordinal))
                main.BeginInvoke((Action)ApplyHeaders);
        };
        main.Shown += (_, _) => ApplyHeaders();
    }

    private static void TrySetHeader(DataGridView grid, string name, string header)
    {
        try
        {
            if (grid.Columns.Contains(name)) grid.Columns[name]!.HeaderText = header;
        }
        catch { }
    }

    private static void AttachEditDialogWatcher(MainForm main)
    {
        _idleHandler = (_, _) =>
        {
            foreach (Form form in Application.OpenForms)
            {
                if (form is not DamageReportEditDialog dialog || PatchedDialogs.Contains(dialog)) continue;
                PatchedDialogs.Add(dialog);
                dialog.FormClosed += (_, _) => PatchedDialogs.Remove(dialog);
                try { PatchEditDialog(dialog); }
                catch (Exception ex) { AppLog.Exception("V146_EDIT_PATCH_FAILED", ex); }
            }
        };
        Application.Idle += _idleHandler;
        main.FormClosed += (_, _) =>
        {
            if (_idleHandler is not null) Application.Idle -= _idleHandler;
            _idleHandler = null;
            PatchedDialogs.Clear();
        };
    }

    private static void PatchEditDialog(DamageReportEditDialog dialog)
    {
        var sku = Field<TextBox>(dialog, "_sku");
        var baseUnit = Field<TextBox>(dialog, "_base");
        var location = Field<TextBox>(dialog, "_location");
        var date = Field<DateTimePicker>(dialog, "_date");
        var time = Field<TimeWheelPicker>(dialog, "_time");
        var shift = Field<ExclusiveShiftPicker>(dialog, "_shift");
        var quantity = Field<NumericUpDown>(dialog, "_qty");

        RewriteOccurrenceLabelsAndOrder(location, date, time, quantity, shift);
        var overrideState = new BaseOverrideState(sku, baseUnit, preserveInitialReportValue: true);
        AddBaseUnitOverrideUi(overrideState);
        overrideState.SyncSkuMaster(resetOverrideIfSkuChanged: false);
        overrideState.RestoreInitialOverrideIfNeeded();
    }

    private static void AddAdminUsageDashboard(MainForm main)
    {
        if (AppSession.Current?.Profile.IsAdmin != true) return;
        var tabs = Field<TabControl>(main, "_tabs");
        var settings = tabs.TabPages.Cast<TabPage>().FirstOrDefault(x => string.Equals(x.Text, "Cài đặt", StringComparison.Ordinal));
        if (settings is null) return;
        var scroll = FindControl<FlowLayoutPanel>(settings);
        if (scroll is null) return;

        var box = new GroupBox
        {
            Text = "Theo dõi giới hạn free / mức sử dụng toàn mô hình — ADMIN",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, 18),
            Padding = new Padding(12),
            Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold),
            Width = Math.Max(700, scroll.ClientSize.Width - scroll.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 12)
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(10),
            Font = new Font("Segoe UI", 9.5F)
        };

        var explanation = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(1100, 0),
            ForeColor = Color.DimGray,
            Text = "Số liệu được đo từ chính request của ứng dụng và snapshot các máy đang dùng. Không lưu token/credential. Máy gửi snapshot tối đa 4 giờ/lần; bấm Đồng bộ & làm mới để gửi ngay máy hiện tại. Các quota phụ thuộc loại tài khoản hoặc nằm ngoài phạm vi project/root sẽ không tự bịa % sử dụng."
        };

        var commands = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true };
        var refresh = new Button
        {
            Text = "Đồng bộ & làm mới",
            AutoSize = true,
            MinimumSize = new Size(0, 40),
            Padding = new Padding(12, 4, 12, 4)
        };
        var summary = new Label { AutoSize = true, Padding = new Padding(12, 10, 0, 0) };
        commands.Controls.AddRange([refresh, summary]);

        var grid = new DataGridView
        {
            Dock = DockStyle.Top,
            Height = 430,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.Fixed3D,
            RowTemplate = { Height = 38 },
            ColumnHeadersHeight = 42
        };
        AddUsageColumns(grid);

        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(1100, 0),
            ForeColor = Color.DimGray,
            Text = "Mốc Firebase RTDB 10 GB download/tháng và 1 GB storage được dùng như ngưỡng cảnh báo Spark trong app; byte mạng là số đo phía client, không thay thế meter/billing console. GitHub API dùng mốc public không token 60 request/giờ/IP. Drive quota là quota cấp tài khoản nên project không quét ngoài Drive root để tính %."
        };

        root.Controls.Add(explanation, 0, 0);
        root.Controls.Add(commands, 0, 1);
        root.Controls.Add(grid, 0, 2);
        root.Controls.Add(note, 0, 3);
        box.Controls.Add(root);
        scroll.Controls.Add(box);

        refresh.Click += async (_, _) => await RefreshUsageDashboardAsync(refresh, summary, grid);
        main.Shown += async (_, _) => await RefreshUsageDashboardAsync(refresh, summary, grid, quiet: true);
    }

    private static void AddUsageColumns(DataGridView grid)
    {
        grid.Columns.Clear();
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
        })
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = header, FillWeight = weight });
    }

    private static async Task RefreshUsageDashboardAsync(Button refresh, Label summary, DataGridView grid, bool quiet = false)
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
            summary.Text = "Đang đồng bộ snapshot và tổng hợp các máy...";
            var aggregate = await UsageTelemetry.LoadAggregateAsync(session);
            RenderUsageGrid(grid, aggregate);
            var latest = aggregate.LatestSnapshot?.ToString("dd/MM/yyyy HH:mm:ss") ?? "-";
            summary.Text = $"Thiết bị có snapshot tháng này: {aggregate.DeviceCount:N0} | Snapshot mới nhất: {latest} | Đã quét {aggregate.ScannedEvents:N0} log" +
                           (aggregate.HistoryTruncated ? " | Lưu ý: lịch sử quét đã chạm giới hạn 1.000 log" : string.Empty);
        }
        catch (Exception ex)
        {
            summary.Text = "Chưa tổng hợp được usage toàn mô hình: " + ex.Message;
            if (!quiet) AppLog.Exception("USAGE_DASHBOARD_REFRESH_FAILED", ex);
        }
        finally
        {
            refresh.Enabled = true;
        }
    }

    private static void RenderUsageGrid(DataGridView grid, UsageAggregate a)
    {
        grid.Rows.Clear();
        const long tenGiB = 10L * 1024 * 1024 * 1024;
        const long oneGiB = 1L * 1024 * 1024 * 1024;

        AddCountRow(grid, "Firebase RTDB đọc", "request", a.Hour("firebase_rtdb_reads"), a.Day("firebase_rtdb_reads"), a.Month("firebase_rtdb_reads"), "Không có % theo request", null, "Đếm nghiệp vụ GET RTDB do app phát sinh.");
        AddCountRow(grid, "Firebase RTDB ghi", "request", a.Hour("firebase_rtdb_writes"), a.Day("firebase_rtdb_writes"), a.Month("firebase_rtdb_writes"), "Không có % theo request", null, "PUT/DELETE; ETag retry có thể phát sinh thêm request thực tế phía server.");
        AddBytesRow(grid, "Firebase RTDB tải xuống", a.Hour("firebase_rtdb_download_bytes"), a.Day("firebase_rtdb_download_bytes"), a.Month("firebase_rtdb_download_bytes"), "10 GB/tháng (Spark)", tenGiB, "Đo byte response nhìn thấy ở client/relay; dùng cảnh báo sớm.");
        AddBytesRow(grid, "Firebase RTDB tải lên", a.Hour("firebase_rtdb_upload_bytes"), a.Day("firebase_rtdb_upload_bytes"), a.Month("firebase_rtdb_upload_bytes"), "—", null, "Payload app gửi cho RTDB.");
        AddTextRow(grid, "Firebase RTDB lưu trữ", "byte", "—", "—", "—", UsageTelemetry.FormatBytes(oneGiB) + " (Spark)", "—", "Client không có API an toàn để đọc chính xác database storage hiện tại.");

        AddCountRow(grid, "Firebase Authentication / token", "request", a.Hour("firebase_auth_requests"), a.Day("firebase_auth_requests"), a.Month("firebase_auth_requests"), "Phụ thuộc API/plan", null, "Chỉ đếm request đăng nhập/refresh; không lưu hoặc hiển thị token.");

        AddCountRow(grid, "Google Apps Script Gateway", "request", a.Hour("apps_script_requests"), a.Day("apps_script_requests"), a.Month("apps_script_requests"), "Phụ thuộc loại tài khoản", null, "Bao gồm sync, audit và RTDB relay qua gateway.");
        AddBytesRow(grid, "Gateway tải lên", a.Hour("apps_script_upload_bytes"), a.Day("apps_script_upload_bytes"), a.Month("apps_script_upload_bytes"), "—", null, "Bao gồm ảnh base64 khi sync_report.");
        AddBytesRow(grid, "Gateway tải xuống", a.Hour("apps_script_download_bytes"), a.Day("apps_script_download_bytes"), a.Month("apps_script_download_bytes"), "—", null, "Response JSON/ảnh trả về qua gateway.");

        AddCountRow(grid, "Google Sheet đọc", "gateway op", SumGateway(a, MetricPeriod.Hour, "pull_changes", "pull_products", "list_audit"), SumGateway(a, MetricPeriod.Day, "pull_changes", "pull_products", "list_audit"), SumGateway(a, MetricPeriod.Month, "pull_changes", "pull_products", "list_audit"), "Quota Apps Script/Sheets", null, "Đếm action đọc Sheet; một action có thể đọc nhiều dòng/ô.");
        AddCountRow(grid, "Google Sheet ghi", "gateway op", SumGateway(a, MetricPeriod.Hour, "sync_report", "push_products", "append_audit", "delete_audit_range"), SumGateway(a, MetricPeriod.Day, "sync_report", "push_products", "append_audit", "delete_audit_range"), SumGateway(a, MetricPeriod.Month, "sync_report", "push_products", "append_audit", "delete_audit_range"), "Quota Apps Script/Sheets", null, "Đếm action ghi; append audit/sync report/push catalog.");
        AddTextRow(grid, "Google Sheets dung lượng", "cell", "—", "—", "—", "10.000.000 ô / spreadsheet", "—", "Không quét toàn Sheet mỗi lần chỉ để đo quota vì chính thao tác đó làm tăng tải.");

        AddCountRow(grid, "Google Drive đọc", "gateway op", GatewayAction(a, "get_image", MetricPeriod.Hour), GatewayAction(a, "get_image", MetricPeriod.Day), GatewayAction(a, "get_image", MetricPeriod.Month), "Quota tài khoản Google", null, "get_image; không quét Drive ngoài root dự án.");
        AddCountRow(grid, "Google Drive ghi", "gateway op", SumGateway(a, MetricPeriod.Hour, "upload_log", "sync_report"), SumGateway(a, MetricPeriod.Day, "upload_log", "sync_report"), SumGateway(a, MetricPeriod.Month, "upload_log", "sync_report"), "Quota tài khoản Google", null, "sync_report có thể ghi nhiều ảnh trong một operation.");
        AddTextRow(grid, "Google Drive storage", "byte", "—", "—", "—", "Quota cấp tài khoản", "—", "Không tính % vì scope khóa không cho quét dữ liệu Drive ngoài root dự án.");

        var githubHour = a.Hour("github_api_requests");
        AddCountRow(grid, "GitHub API updater", "request", githubHour, a.Day("github_api_requests"), a.Month("github_api_requests"), "60 request/giờ/IP", 60, "Updater public không dùng token; Office có thể chặn GitHub nhưng business runtime không phụ thuộc GitHub.", percentValue: githubHour);
        AddBytesRow(grid, "GitHub tải release", a.Hour("github_download_bytes"), a.Day("github_download_bytes"), a.Month("github_download_bytes"), "—", null, "Chỉ phát sinh khi kiểm tra/tải cập nhật.");

        AddTextRow(grid, "SQLite local (các máy snapshot)", "byte", "—", "—", UsageTelemetry.FormatBytes(a.Gauge("local_database_bytes")), "Ổ đĩa local", "—", "Tổng footprint DB của các thiết bị có snapshot; không phải cloud quota.");
        AddTextRow(grid, "Ảnh local/cache (các máy snapshot)", "byte", "—", "—", UsageTelemetry.FormatBytes(a.Gauge("local_images_bytes")), "Ổ đĩa local", "—", "Có thể trùng ảnh cache giữa các máy; dùng để theo dõi dung lượng máy.");
    }

    private enum MetricPeriod { Hour, Day, Month }

    private static long GatewayAction(UsageAggregate a, string action, MetricPeriod period)
    {
        var key = "gateway_action_" + action;
        return period switch
        {
            MetricPeriod.Hour => a.Hour(key),
            MetricPeriod.Day => a.Day(key),
            _ => a.Month(key)
        };
    }

    private static long SumGateway(UsageAggregate a, MetricPeriod period, params string[] actions) =>
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

    private static void AttachUsageSnapshotTimer(MainForm main)
    {
        _usageTimer?.Dispose();
        _usageTimer = new System.Windows.Forms.Timer { Interval = (int)TimeSpan.FromMinutes(30).TotalMilliseconds };
        _usageTimer.Tick += async (_, _) =>
        {
            if (_periodicSyncBusy) return;
            _periodicSyncBusy = true;
            try { await UsageTelemetry.SyncSnapshotAsync(force: false); }
            catch { }
            finally { _periodicSyncBusy = false; }
        };
        main.Shown += async (_, _) =>
        {
            _usageTimer.Start();
            try { await UsageTelemetry.SyncSnapshotAsync(force: false); } catch { }
        };
        main.FormClosed += (_, _) =>
        {
            _usageTimer?.Stop();
            _usageTimer?.Dispose();
            _usageTimer = null;
        };
    }

    private static Label AddRuntimeRow(TableLayoutPanel table, string label, Control control)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var title = new Label
        {
            Text = label,
            AutoSize = true,
            Padding = new Padding(0, 8, 12, 14),
            Margin = new Padding(0, 4, 0, 4),
            Font = new Font("Segoe UI", 10F)
        };
        control.Margin = new Padding(3, 4, 3, 12);
        if (control.Dock == DockStyle.None && control is not FlowLayoutPanel && control is not TimeWheelPicker && control is not ExclusiveShiftPicker)
            control.Dock = DockStyle.Top;
        table.Controls.Add(title, 0, row);
        table.Controls.Add(control, 1, row);
        return title;
    }

    private static void SetRow(TableLayoutPanel table, Control control, Control? label, int row)
    {
        table.SetRow(control, row);
        if (label is not null) table.SetRow(label, row);
    }

    private static Label? RowLabel(TableLayoutPanel table, Control control)
    {
        var pos = table.GetPositionFromControl(control);
        if (pos.Row < 0) return null;
        return table.GetControlFromPosition(0, pos.Row) as Label;
    }

    private static T Field<T>(object owner, string name) where T : class
    {
        var field = owner.GetType().GetField(name, PrivateInstance)
                    ?? throw new MissingFieldException(owner.GetType().Name, name);
        return field.GetValue(owner) as T
               ?? throw new InvalidOperationException($"Field {owner.GetType().Name}.{name} không phải {typeof(T).Name}.");
    }

    private static T? FindControl<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match) return match;
            var nested = FindControl<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    private sealed class MainEntryState
    {
        public MainForm Form { get; }
        public TextBox Sku { get; }
        public TextBox ProductName { get; }
        public TextBox Location { get; }
        public DateTimePicker Date { get; }
        public TimeWheelPicker Time { get; }
        public ExclusiveShiftPicker Shift { get; }
        public NumericUpDown Quantity { get; }
        public TextBox BaseUnit { get; }
        public Button Send { get; }
        public Label EntryStatus { get; }
        public BaseOverrideState BaseOverride { get; }
        public Label RequiredNote { get; } = new();

        private bool _changingEnabled;
        private bool _saving;
        private bool _externalAllowed;

        public MainEntryState(MainForm form, TextBox sku, TextBox productName, TextBox location, DateTimePicker date, TimeWheelPicker time, ExclusiveShiftPicker shift, NumericUpDown quantity, TextBox baseUnit, Button send, Label entryStatus, BaseOverrideState baseOverride)
        {
            Form = form;
            Sku = sku;
            ProductName = productName;
            Location = location;
            Date = date;
            Time = time;
            Shift = shift;
            Quantity = quantity;
            BaseUnit = baseUnit;
            Send = send;
            EntryStatus = entryStatus;
            BaseOverride = baseOverride;
            _externalAllowed = send.Enabled;
            BaseOverride.Changed = () => UpdateSendState();
        }

        public void AttachValidationEvents()
        {
            Sku.TextChanged += (_, _) =>
            {
                SyncSkuMaster(resetOverrideIfSkuChanged: true);
                if (_saving && string.IsNullOrWhiteSpace(Sku.Text)) _saving = false;
                UpdateSendState();
            };
            Location.TextChanged += (_, _) => UpdateSendState();
            Quantity.ValueChanged += (_, _) => UpdateSendState();
            Shift.SelectedShiftChanged += (_, _) => UpdateSendState();
            BaseUnit.TextChanged += (_, _) => UpdateSendState();

            Send.MouseDown += (_, _) => _saving = true;
            Send.Click += (_, _) => Form.BeginInvoke((Action)(() =>
            {
                if (Send.Enabled) _saving = false;
                UpdateSendState();
            }));
            Send.EnabledChanged += (_, _) =>
            {
                if (_changingEnabled) return;
                if (_saving)
                {
                    if (!Send.Enabled) return;
                    _saving = false;
                    _externalAllowed = true;
                    UpdateSendState();
                    return;
                }
                _externalAllowed = Send.Enabled;
                UpdateSendState();
            };
        }

        public void SyncSkuMaster(bool resetOverrideIfSkuChanged) =>
            BaseOverride.SyncSkuMaster(resetOverrideIfSkuChanged);

        public void UpdateSendState()
        {
            var missing = new List<string>();
            var skuText = ExcelImportService.Clean(Sku.Text).ToUpperInvariant();
            var product = skuText.Length == 0 ? null : Database.GetProduct(skuText);
            if (product is null) missing.Add("SKU hợp lệ");
            if (!LocationNormalizer.TryNormalize(Location.Text, out _, out _)) missing.Add("Vị trí phát hiện hư hỏng thực tế");
            if (Quantity.Value < 1) missing.Add("Số lượng hư hỏng");
            if (string.IsNullOrWhiteSpace(Shift.SelectedShift)) missing.Add("Ca ghi nhân");
            if (!BaseOverride.HasValidEffectiveBaseUnit) missing.Add(BaseOverride.OverrideCheck.Checked ? "Base Units thực tế hư hỏng" : "Base Units SKU");

            var complete = missing.Count == 0;
            RequiredNote.Visible = !complete;
            RequiredNote.Text = complete
                ? string.Empty
                : "Chưa nhập đủ thông tin bắt buộc nên không thể gửi. Thiếu: " + string.Join(", ", missing) + ".";

            if (!complete)
            {
                SetEnabled(false);
                return;
            }

            if (!_saving) SetEnabled(_externalAllowed);
        }

        private void SetEnabled(bool enabled)
        {
            if (Send.Enabled == enabled) return;
            _changingEnabled = true;
            try { Send.Enabled = enabled; }
            finally { _changingEnabled = false; }
        }
    }

    private sealed class BaseOverrideState
    {
        private readonly bool _preserveInitialReportValue;
        private bool _suppress;
        private bool _applyingBase;
        private string _currentSku = string.Empty;
        private readonly string _initialEffective;

        public TextBox Sku { get; }
        public TextBox BaseUnit { get; }
        public CheckBox OverrideCheck { get; } = new();
        public ComboBox OverrideSelect { get; } = new();
        public Label OverrideLabel { get; set; } = new();
        public string StoredBaseUnit { get; private set; } = string.Empty;
        public Action? Changed { get; set; }

        public bool HasValidEffectiveBaseUnit =>
            OverrideCheck.Checked
                ? OverrideSelect.SelectedItem is string selected && !string.IsNullOrWhiteSpace(selected)
                : !string.IsNullOrWhiteSpace(StoredBaseUnit);

        public BaseOverrideState(TextBox sku, TextBox baseUnit, bool preserveInitialReportValue)
        {
            Sku = sku;
            BaseUnit = baseUnit;
            _preserveInitialReportValue = preserveInitialReportValue;
            _initialEffective = baseUnit.Text.Trim();
        }

        public void Attach()
        {
            Sku.TextChanged += (_, _) =>
            {
                SyncSkuMaster(resetOverrideIfSkuChanged: true);
                Changed?.Invoke();
            };
            OverrideCheck.CheckedChanged += (_, _) =>
            {
                if (_suppress) return;
                if (OverrideCheck.Checked)
                {
                    ReloadAvailableBaseUnits();
                    OverrideSelect.SelectedIndex = -1;
                    SetOverrideVisible(true);
                }
                else
                {
                    OverrideSelect.SelectedIndex = -1;
                    SetOverrideVisible(false);
                    ApplyEffectiveBase(StoredBaseUnit);
                }
                Changed?.Invoke();
            };
            OverrideSelect.SelectedIndexChanged += (_, _) =>
            {
                if (_suppress || !OverrideCheck.Checked) return;
                if (OverrideSelect.SelectedItem is string selected)
                    ApplyEffectiveBase(selected);
                Changed?.Invoke();
            };
            BaseUnit.TextChanged += (_, _) =>
            {
                if (_applyingBase || !OverrideCheck.Checked) return;
                if (OverrideSelect.SelectedItem is not string selected || string.IsNullOrWhiteSpace(selected)) return;
                if (!string.Equals(BaseUnit.Text.Trim(), selected, StringComparison.OrdinalIgnoreCase))
                    ApplyEffectiveBase(selected);
            };
        }

        public void SyncSkuMaster(bool resetOverrideIfSkuChanged)
        {
            var normalized = ExcelImportService.Clean(Sku.Text).ToUpperInvariant();
            var changedSku = !string.Equals(normalized, _currentSku, StringComparison.OrdinalIgnoreCase);
            if (changedSku)
            {
                _currentSku = normalized;
                if (resetOverrideIfSkuChanged && OverrideCheck.Checked)
                {
                    var oldSuppress = _suppress;
                    _suppress = true;
                    try
                    {
                        OverrideCheck.Checked = false;
                        OverrideSelect.SelectedIndex = -1;
                        SetOverrideVisible(false);
                    }
                    finally { _suppress = oldSuppress; }
                }
            }

            var product = normalized.Length == 0 ? null : Database.GetProduct(normalized);
            StoredBaseUnit = product?.BaseUnit?.Trim() ?? string.Empty;
            if (!OverrideCheck.Checked)
                ApplyEffectiveBase(StoredBaseUnit);
        }

        public void RestoreInitialOverrideIfNeeded()
        {
            if (!_preserveInitialReportValue) return;
            var initial = _initialEffective.Trim();
            if (initial.Length == 0 || string.Equals(initial, StoredBaseUnit, StringComparison.OrdinalIgnoreCase)) return;

            var oldSuppress = _suppress;
            _suppress = true;
            try
            {
                ReloadAvailableBaseUnits();
                if (!OverrideSelect.Items.Cast<object>().Any(x => string.Equals(Convert.ToString(x), initial, StringComparison.OrdinalIgnoreCase)))
                    OverrideSelect.Items.Add(initial);
                OverrideCheck.Checked = true;
                SetOverrideVisible(true);
                SelectValue(initial);
                ApplyEffectiveBase(initial);
            }
            finally { _suppress = oldSuppress; }
            Changed?.Invoke();
        }

        private void ReloadAvailableBaseUnits()
        {
            var previous = OverrideSelect.SelectedItem as string;
            var values = Database.GetProducts(string.Empty, int.MaxValue)
                .Select(x => (x.BaseUnit ?? string.Empty).Trim())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            var oldSuppress = _suppress;
            _suppress = true;
            try
            {
                OverrideSelect.BeginUpdate();
                OverrideSelect.Items.Clear();
                foreach (var value in values) OverrideSelect.Items.Add(value);
                OverrideSelect.EndUpdate();
                if (!string.IsNullOrWhiteSpace(previous)) SelectValue(previous);
            }
            finally { _suppress = oldSuppress; }
        }

        private void SelectValue(string value)
        {
            for (var i = 0; i < OverrideSelect.Items.Count; i++)
            {
                if (!string.Equals(Convert.ToString(OverrideSelect.Items[i]), value, StringComparison.OrdinalIgnoreCase)) continue;
                OverrideSelect.SelectedIndex = i;
                return;
            }
        }

        private void SetOverrideVisible(bool visible)
        {
            OverrideSelect.Visible = visible;
            OverrideLabel.Visible = visible;
        }

        private void ApplyEffectiveBase(string value)
        {
            value ??= string.Empty;
            if (string.Equals(BaseUnit.Text, value, StringComparison.Ordinal)) return;
            _applyingBase = true;
            try { BaseUnit.Text = value; }
            finally { _applyingBase = false; }
        }
    }
}
