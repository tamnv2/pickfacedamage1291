using System.Text.Json;

namespace PickfaceDamage1291;

internal sealed class AdminUsersControl : UserControl
{
    private readonly DataGridView _grid = new();
    private readonly Label _status = new();
    private List<FirebaseUserProfile> _users = [];

    public AdminUsersControl()
    {
        Dock = DockStyle.Fill;
        Padding = new Padding(18);
        Font = new Font("Segoe UI", 10F);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var box = new GroupBox
        {
            Text = "Quản lý tài khoản",
            Dock = DockStyle.Fill,
            Padding = new Padding(12)
        };
        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        var refresh = Button("Làm mới");
        var add = Button("+ Tạo USER");
        var edit = Button("Sửa USER");
        var toggle = Button("Khóa / Mở lại USER");
        refresh.Click += async (_, _) => await RefreshAsync();
        add.Click += async (_, _) => await AddAsync();
        edit.Click += async (_, _) => await EditAsync();
        toggle.Click += async (_, _) => await ToggleAsync();
        _status.AutoSize = true;
        _status.Padding = new Padding(15, 8, 0, 0);
        tools.Controls.AddRange([refresh, add, edit, toggle, _status]);
        box.Controls.Add(tools);

        ConfigureGrid();
        root.Controls.Add(box, 0, 0);
        root.Controls.Add(_grid, 0, 1);
        Controls.Add(root);

        Load += async (_, _) => await RefreshAsync();
    }

    private static Button Button(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Height = 34,
        Padding = new Padding(7, 0, 7, 0),
        Margin = new Padding(3, 3, 7, 3)
    };

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.BackgroundColor = Color.White;
        _grid.RowTemplate.Height = 34;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Username", HeaderText = "Tài khoản", FillWeight = 65 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Họ tên", FillWeight = 110 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Email", HeaderText = "Email", FillWeight = 120 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Role", HeaderText = "Quyền", FillWeight = 50 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Active", HeaderText = "Trạng thái", FillWeight = 65 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Perms", HeaderText = "Quyền USER", FillWeight = 160 });
    }

    private async Task RefreshAsync()
    {
        var session = AppSession.Current;
        if (session is null || !session.Profile.IsAdmin) return;
        try
        {
            _status.Text = "Đang tải...";
            _users = await FirebaseClient.ListUsersAsync(session);
            _grid.Rows.Clear();
            foreach (var u in _users)
            {
                var perms = u.IsAdmin
                    ? "Toàn quyền"
                    : string.Join(", ", u.Permissions.Where(x => x.Value).Select(x => PermissionLabel(x.Key)));
                _grid.Rows.Add(u.Username, u.DisplayName, u.Email, u.Role.ToUpperInvariant(), u.Active ? "Hoạt động" : "Đã khóa", perms);
            }
            _status.Text = $"{_users.Count:N0} tài khoản";
        }
        catch (Exception ex)
        {
            _status.Text = "Không tải được";
            MessageBox.Show(this, ex.Message + "\n\nNếu vừa cập nhật source Security Rules, OWNER cần Publish lại rules mới.", "Không tải được tài khoản", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private FirebaseUserProfile? Selected()
    {
        if (_grid.SelectedRows.Count == 0) return null;
        var index = _grid.SelectedRows[0].Index;
        return index >= 0 && index < _users.Count ? _users[index] : null;
    }

    private async Task AddAsync()
    {
        var session = AppSession.Current;
        if (session is null || !session.Profile.IsAdmin) return;
        using var dialog = new UserEditorDialog(null);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            UseWaitCursor = true;
            var created = await FirebaseClient.CreateUserAsync(
                session,
                dialog.Email,
                dialog.Username,
                dialog.DisplayNameValue,
                dialog.Permissions);
            MessageBox.Show(this,
                $"Đã tạo USER {created.Username}.\n\nFirebase đã gửi email đặt lại mật khẩu tới {created.Email}. Người dùng tự đặt mật khẩu qua email; ADMIN không cần biết mật khẩu.",
                "Đã tạo tài khoản", MessageBoxButtons.OK, MessageBoxIcon.Information);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Không tạo được USER", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private async Task EditAsync()
    {
        var session = AppSession.Current;
        var selected = Selected();
        if (session is null || !session.Profile.IsAdmin || selected is null) return;
        if (selected.IsAdmin)
        {
            MessageBox.Show(this, "Tài khoản ADMIN bootstrap không sửa role/quyền tại màn này.", "ADMIN");
            return;
        }

        using var dialog = new UserEditorDialog(selected);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        selected.DisplayName = dialog.DisplayNameValue;
        selected.Permissions = dialog.Permissions;
        try
        {
            await FirebaseClient.UpdateUserProfileAsync(session, selected);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Không cập nhật được USER", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task ToggleAsync()
    {
        var session = AppSession.Current;
        var selected = Selected();
        if (session is null || !session.Profile.IsAdmin || selected is null) return;
        if (selected.IsAdmin)
        {
            MessageBox.Show(this, "Không khóa tài khoản ADMIN bootstrap từ ứng dụng.", "Không cho phép");
            return;
        }

        var next = !selected.Active;
        var text = next ? "Mở lại tài khoản này?" : "Khóa/Xóa tài khoản khỏi ứng dụng?\n\nLịch sử vẫn được giữ. Firebase Auth record không bị xóa vật lý để bảo toàn audit.";
        if (MessageBox.Show(this, text, next ? "Mở lại USER" : "Khóa USER", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        selected.Active = next;
        try
        {
            await FirebaseClient.UpdateUserProfileAsync(session, selected);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Không thay đổi được trạng thái", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    internal static string PermissionLabel(string key) => key switch
    {
        "damage_entry" => "Nhập hư hỏng",
        "view_reports" => "Xem danh sách",
        "import_sku" => "Cập nhật SKU",
        "sync_google" => "Đồng bộ Google",
        _ => key
    };
}

internal sealed class UserEditorDialog : Form
{
    private readonly TextBox _username = new();
    private readonly TextBox _name = new();
    private readonly TextBox _email = new();
    private readonly CheckBox _damage = new() { Text = "Nhập hư hỏng" };
    private readonly CheckBox _reports = new() { Text = "Xem danh sách đã nhập" };
    private readonly CheckBox _sku = new() { Text = "Cập nhật danh mục SKU" };
    private readonly CheckBox _sync = new() { Text = "Đồng bộ Google" };
    private readonly bool _editing;

    public string Username => _username.Text.Trim();
    public string DisplayNameValue => _name.Text.Trim();
    public string Email => _email.Text.Trim();
    public Dictionary<string, bool> Permissions => new(StringComparer.OrdinalIgnoreCase)
    {
        ["damage_entry"] = _damage.Checked,
        ["view_reports"] = _reports.Checked,
        ["import_sku"] = _sku.Checked,
        ["sync_google"] = _sync.Checked
    };

    public UserEditorDialog(FirebaseUserProfile? profile)
    {
        _editing = profile is not null;
        Text = _editing ? "Sửa USER" : "Tạo USER";
        Width = 520;
        Height = 470;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Segoe UI", 10F);

        var form = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 2, RowCount = 7 };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(form, "Tên tài khoản", _username, 0);
        AddRow(form, "Họ tên", _name, 1);
        AddRow(form, "Email", _email, 2);

        var permissions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        permissions.Controls.AddRange([_damage, _reports, _sku, _sync]);
        form.Controls.Add(new Label { Text = "Quyền USER", AutoSize = true, Padding = new Padding(0, 5, 0, 0) }, 0, 3);
        form.Controls.Add(permissions, 1, 3);
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));

        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(300, 0),
            Text = _editing
                ? "Email và tên tài khoản không sửa tại đây để tránh lệch Firebase Authentication. Người dùng có thể xử lý thông tin cá nhân theo luồng tài khoản."
                : "Khi tạo mới, hệ thống sinh mật khẩu tạm ngẫu nhiên rồi gửi email đặt lại mật khẩu cho người dùng."
        };
        form.Controls.Add(new Label(), 0, 4);
        form.Controls.Add(note, 1, 4);
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var save = new Button { Text = "Lưu", AutoSize = true };
        var cancel = new Button { Text = "Hủy", AutoSize = true };
        save.Click += (_, _) => Save();
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        buttons.Controls.AddRange([save, cancel]);
        form.Controls.Add(new Label(), 0, 5);
        form.Controls.Add(buttons, 1, 5);
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        Controls.Add(form);

        if (profile is not null)
        {
            _username.Text = profile.Username;
            _name.Text = profile.DisplayName;
            _email.Text = profile.Email;
            _username.ReadOnly = true;
            _email.ReadOnly = true;
            _damage.Checked = profile.HasPermission("damage_entry");
            _reports.Checked = profile.HasPermission("view_reports");
            _sku.Checked = profile.HasPermission("import_sku");
            _sync.Checked = profile.HasPermission("sync_google");
        }
        else
        {
            _damage.Checked = true;
            _reports.Checked = true;
            _sync.Checked = true;
        }
    }

    private static void AddRow(TableLayoutPanel form, string label, Control control, int row)
    {
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        form.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, row);
        control.Dock = DockStyle.Top;
        form.Controls.Add(control, 1, row);
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(DisplayNameValue) || string.IsNullOrWhiteSpace(Email))
        {
            MessageBox.Show(this, "Nhập đủ tên tài khoản, họ tên và email.", "Thiếu thông tin");
            return;
        }
        if (!Email.Contains('@') || Email.StartsWith('@') || Email.EndsWith('@'))
        {
            MessageBox.Show(this, "Email không hợp lệ.", "Kiểm tra email");
            return;
        }
        if (!_editing && Username.Any(char.IsWhiteSpace))
        {
            MessageBox.Show(this, "Tên tài khoản không dùng khoảng trắng.", "Kiểm tra tài khoản");
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }
}
