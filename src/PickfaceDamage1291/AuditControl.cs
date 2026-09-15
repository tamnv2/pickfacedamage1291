using System.Text.Json;

namespace PickfaceDamage1291;

internal sealed class AuditControl : UserControl
{
    private const int PageSize = 100;
    private readonly DataGridView _grid = new();
    private readonly Label _status = new();
    private readonly Button _previous = new();
    private readonly Button _next = new();
    private readonly DateTimePicker _deleteFrom = new();
    private readonly DateTimePicker _deleteTo = new();
    private readonly List<long?> _pageCursors = [null];
    private int _pageIndex;
    private AuditPage? _currentPage;

    public AuditControl()
    {
        Dock = DockStyle.Fill;
        Padding = new Padding(18);
        Font = new Font("Segoe UI", 10F);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var box = new GroupBox
        {
            Text = "Lịch sử thao tác hệ thống",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12)
        };
        var tools = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            AutoScroll = false
        };
        var refresh = new Button { Text = "Làm mới", AutoSize = true, Height = 34, Padding = new Padding(8, 0, 8, 0) };
        refresh.Click += async (_, _) => await RefreshAsync(resetPaging: true);

        _previous.Text = "← Trang trước";
        _previous.AutoSize = true;
        _previous.Height = 34;
        _previous.Click += async (_, _) => await PreviousPageAsync();
        _next.Text = "Trang sau →";
        _next.AutoSize = true;
        _next.Height = 34;
        _next.Click += async (_, _) => await NextPageAsync();

        _deleteFrom.Format = DateTimePickerFormat.Custom;
        _deleteFrom.CustomFormat = "dd/MM/yyyy";
        _deleteFrom.Width = 125;
        _deleteFrom.Value = DateTime.Today;
        _deleteTo.Format = DateTimePickerFormat.Custom;
        _deleteTo.CustomFormat = "dd/MM/yyyy";
        _deleteTo.Width = 125;
        _deleteTo.Value = DateTime.Today;
        var delete = new Button { Text = "Xóa logs theo ngày", AutoSize = true, Height = 34, Padding = new Padding(8, 0, 8, 0) };
        delete.Click += async (_, _) => await DeleteRangeAsync();

        _status.AutoSize = true;
        _status.Padding = new Padding(15, 8, 0, 0);
        tools.Controls.AddRange([
            refresh,
            _previous,
            _next,
            new Label { Text = "Xóa từ", AutoSize = true, Padding = new Padding(12, 8, 0, 0) },
            _deleteFrom,
            new Label { Text = "đến", AutoSize = true, Padding = new Padding(5, 8, 0, 0) },
            _deleteTo,
            delete,
            _status
        ]);
        box.Controls.Add(tools);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.BackgroundColor = Color.White;
        _grid.RowTemplate.Height = 34;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Time", HeaderText = "Thời gian", FillWeight = 95 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "User", HeaderText = "Tài khoản", FillWeight = 70 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Action", HeaderText = "Thao tác", FillWeight = 90 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Device", HeaderText = "Thiết bị", FillWeight = 115 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Session", HeaderText = "Phiên", FillWeight = 100 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Details", HeaderText = "Chi tiết thay đổi", FillWeight = 220 });

        root.Controls.Add(box, 0, 0);
        root.Controls.Add(_grid, 0, 1);
        Controls.Add(root);
        Load += async (_, _) => await RefreshAsync(resetPaging: true);
    }

    public async Task RefreshAsync(bool resetPaging = false)
    {
        var session = AppSession.Current;
        if (session is null || !session.Profile.IsAdmin || session.OfflineMode)
        {
            _status.Text = "Cần ADMIN online để xem lịch sử.";
            _grid.Rows.Clear();
            UpdatePagingButtons();
            return;
        }

        if (resetPaging)
        {
            _pageIndex = 0;
            _pageCursors.Clear();
            _pageCursors.Add(null);
        }

        try
        {
            _status.Text = $"Đang tải trang {_pageIndex + 1}...";
            var cursor = _pageCursors[_pageIndex];
            _currentPage = await AuditAdminService.ListPageAsync(session, cursor, PageSize);
            _grid.Rows.Clear();
            foreach (var e in _currentPage.Entries)
            {
                _grid.Rows.Add(
                    FormatTime(e),
                    e.Username,
                    ActionLabel(e.Action),
                    e.DeviceId,
                    Short(e.SessionId, 14),
                    FormatDetails(e.Details));
            }
            _status.Text = $"Trang {_pageIndex + 1} • {_currentPage.Entries.Count:N0}/{PageSize} logs";
            UpdatePagingButtons();
        }
        catch (Exception ex)
        {
            _status.Text = "Không tải được lịch sử";
            UpdatePagingButtons();
            MessageBox.Show(this, ex.Message + "\n\nNếu vừa cập nhật Security Rules, OWNER cần Publish rules mới.", "Lịch sử", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task NextPageAsync()
    {
        if (_currentPage?.HasMore != true || !_currentPage.NextBeforeServerTime.HasValue) return;
        var nextIndex = _pageIndex + 1;
        if (_pageCursors.Count <= nextIndex)
            _pageCursors.Add(_currentPage.NextBeforeServerTime);
        else
            _pageCursors[nextIndex] = _currentPage.NextBeforeServerTime;
        _pageIndex = nextIndex;
        await RefreshAsync();
    }

    private async Task PreviousPageAsync()
    {
        if (_pageIndex <= 0) return;
        _pageIndex--;
        await RefreshAsync();
    }

    private void UpdatePagingButtons()
    {
        _previous.Enabled = _pageIndex > 0;
        _next.Enabled = _currentPage?.HasMore == true;
    }

    private async Task DeleteRangeAsync()
    {
        var session = AppSession.Current;
        if (session is null || !session.Profile.IsAdmin || session.OfflineMode)
        {
            MessageBox.Show(this, "Cần ADMIN online để xóa logs.", "Không thể xử lý", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var from = _deleteFrom.Value.Date;
        var to = _deleteTo.Value.Date;
        if (from > to)
        {
            MessageBox.Show(this, "Ngày bắt đầu phải nhỏ hơn hoặc bằng ngày kết thúc.", "Khoảng ngày không hợp lệ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            this,
            $"Xóa toàn bộ logs từ {from:dd/MM/yyyy} đến {to:dd/MM/yyyy}?\n\nLog ghi nhận chính hành động xóa sẽ được giữ lại và không thể xóa bằng chức năng này.",
            "Xác nhận xóa logs",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        try
        {
            var fromLocal = DateTime.SpecifyKind(from, DateTimeKind.Local);
            var toExclusiveLocal = DateTime.SpecifyKind(to.AddDays(1), DateTimeKind.Local);
            var fromMs = new DateTimeOffset(fromLocal).ToUnixTimeMilliseconds();
            var toMs = new DateTimeOffset(toExclusiveLocal).ToUnixTimeMilliseconds() - 1;
            var deleted = await AuditAdminService.DeleteRangeAsync(session, fromMs, toMs);
            MessageBox.Show(this, $"Đã xóa {deleted:N0} logs trong phạm vi đã chọn.", "Hoàn tất", MessageBoxButtons.OK, MessageBoxIcon.Information);
            await RefreshAsync(resetPaging: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Không xóa được logs", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static string FormatTime(FirebaseAuditEntry e)
    {
        var server = AuditAdminService.ParseServerTime(e.ServerTime);
        if (server > 0) return DateTimeOffset.FromUnixTimeMilliseconds(server).ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");
        return DateTimeOffset.TryParse(e.ClientTime, out var client)
            ? client.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss")
            : e.ClientTime;
    }

    private static string FormatDetails(object? value)
    {
        if (value is null) return "-";
        try
        {
            if (value is JsonElement element)
                return element.ValueKind is JsonValueKind.Object or JsonValueKind.Array ? element.GetRawText() : element.ToString();
            return JsonSerializer.Serialize(value);
        }
        catch
        {
            return Convert.ToString(value) ?? "-";
        }
    }

    private static string ActionLabel(string action) => action switch
    {
        "LOGIN_SUCCESS" => "Đăng nhập",
        "LOGOUT" => "Đăng xuất",
        "OPERATOR_ACQUIRED" => "Nhận quyền nhập",
        "OPERATOR_TAKEOVER" => "Chuyển quyền / kick phiên cũ",
        "OPERATOR_RELEASED" => "Nhả quyền nhập",
        "USER_CREATED" => "Tạo USER",
        "USER_PROFILE_UPDATED" => "Sửa/khóa USER",
        "DAMAGE_CREATED" => "Nhập hư hỏng",
        "DAMAGE_REPORT_CREATED" => "Nhập hư hỏng",
        "AUDIT_LOGS_DELETED" => "Xóa logs",
        _ => action
    };

    private static string Short(string value, int max) => string.IsNullOrWhiteSpace(value) ? "-" : value.Length <= max ? value : value[..max] + "…";
}
