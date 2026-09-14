using System.Text.Json;

namespace PickfaceDamage1291;

internal sealed class AuditControl : UserControl
{
    private readonly DataGridView _grid = new();
    private readonly Label _status = new();

    public AuditControl()
    {
        Dock = DockStyle.Fill;
        Padding = new Padding(18);
        Font = new Font("Segoe UI", 10F);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var box = new GroupBox { Text = "Lịch sử thao tác hệ thống", Dock = DockStyle.Fill, Padding = new Padding(12) };
        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        var refresh = new Button { Text = "Làm mới", AutoSize = true, Height = 34, Padding = new Padding(8, 0, 8, 0) };
        refresh.Click += async (_, _) => await RefreshAsync();
        _status.AutoSize = true;
        _status.Padding = new Padding(15, 8, 0, 0);
        tools.Controls.AddRange([refresh, _status]);
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
        Load += async (_, _) => await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        var session = AppSession.Current;
        if (session is null || !session.Profile.IsAdmin || session.OfflineMode)
        {
            _status.Text = "Cần ADMIN online để xem lịch sử.";
            return;
        }
        try
        {
            _status.Text = "Đang tải 500 sự kiện gần nhất...";
            var entries = await FirebaseClient.ListAuditAsync(session, 500);
            _grid.Rows.Clear();
            foreach (var e in entries)
            {
                _grid.Rows.Add(
                    FormatTime(e),
                    e.Username,
                    ActionLabel(e.Action),
                    e.DeviceId,
                    Short(e.SessionId, 14),
                    FormatDetails(e.Details));
            }
            _status.Text = $"{entries.Count:N0} sự kiện gần nhất";
        }
        catch (Exception ex)
        {
            _status.Text = "Không tải được lịch sử";
            MessageBox.Show(this, ex.Message + "\n\nNếu vừa cập nhật Security Rules, OWNER cần Publish lại rules mới.", "Lịch sử", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static string FormatTime(FirebaseAuditEntry e)
    {
        var server = ParseServerTime(e.ServerTime);
        if (server > 0) return DateTimeOffset.FromUnixTimeMilliseconds(server).ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");
        return DateTimeOffset.TryParse(e.ClientTime, out var client)
            ? client.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss")
            : e.ClientTime;
    }

    private static long ParseServerTime(object? value)
    {
        if (value is JsonElement element && element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var n)) return n;
        if (value is long l) return l;
        return long.TryParse(Convert.ToString(value), out var parsed) ? parsed : 0;
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
        "OPERATOR_ACQUIRED" => "Nhận quyền nhập",
        "OPERATOR_TAKEOVER" => "Chuyển quyền / kick phiên cũ",
        "OPERATOR_RELEASED" => "Nhả quyền nhập",
        "USER_CREATED" => "Tạo USER",
        "USER_PROFILE_UPDATED" => "Sửa/khóa USER",
        "DAMAGE_CREATED" => "Nhập hư hỏng",
        _ => action
    };

    private static string Short(string value, int max) => string.IsNullOrWhiteSpace(value) ? "-" : value.Length <= max ? value : value[..max] + "…";
}
