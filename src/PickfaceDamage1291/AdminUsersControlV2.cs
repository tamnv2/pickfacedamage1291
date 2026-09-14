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
        AutoScaleMode = AutoScaleMode.Dpi;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var box = new GroupBox { Text = "Quản lý tài khoản", Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(12) };
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Padding = new Padding(8) };
        var refresh = Button("Làm mới");
        var add = Button("+ Tạo USER");
        var edit = Button("Sửa USER");
        var toggle = Button("Khóa / Mở lại USER");
        refresh.Click += async (_, _) => await RefreshAsync();
        add.Click += async (_, _) => await AddAsync();
        edit.Click += async (_, _) => await EditAsync();
        toggle.Click += async (_, _) => await ToggleAsync();
        _status.AutoSize = true;
        _status.Padding = new Padding(15, 9, 0, 0);
        tools.Controls.AddRange([refresh, add, edit, toggle, _status]);
        box.Controls.Add(tools);
        ConfigureGrid();
        root.Controls.Add(box, 0, 0);
        root.Controls.Add(_grid, 0, 1);
        Controls.Add(root);
        Load += async (_, _) => await RefreshAsync();
    }

    private static Button Button(string text) => new() { Text = text, AutoSize = true, Height = 36, Padding = new Padding(8, 0, 8, 0), Margin = new Padding(3, 3, 8, 3) };

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
        _grid.ColumnHeadersHeight = 38;
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
                var perms = u.IsAdmin ? "Toàn quyền" : string.Join(", ", u.Permissions.Where(x => x.Value).Select(x => PermissionLabel(x.Key)));
                _grid.Rows.Add(u.Username, u.DisplayName, u.Email, u.Role.ToUpperInvariant(), u.Active ? "Hoạt động" : "Đã khóa", perms);
            }
            _status.Text = $"{_users.Count:N0} tài khoản";
        }
        catch (Exception ex)
        {
            _status.Text = "Không tải được";
            MessageBox.Show(this, ex.Message, "Không tải được tài khoản", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            var created = await FirebaseClient.CreateUserAsync(session, dialog.Email, dialog.Username, dialog.DisplayNameValue, dialog.Permissions);
            MessageBox.Show(this, $"Đã tạo USER {created.Username}.\n\nFirebase đã gửi email đặt lại mật khẩu tới {created.Email}. Người dùng tự đặt mật khẩu; ADMIN không biết mật khẩu.", "Đã tạo tài khoản", MessageBoxButtons.OK, MessageBoxIcon.Information);
            await RefreshAsync();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Không tạo được USER", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        finally { UseWaitCursor = false; }
    }

    private async Task EditAsync()
    {
        var session = AppSession.Current;
        var selected = Selected();
        if (session is null || !session.Profile.IsAdmin || selected is null) return;
        if (selected.IsAdmin) { MessageBox.Show(this, "Tài khoản ADMIN bootstrap không sửa role/quyền tại màn này.", "ADMIN"); return; }
        using var dialog = new UserEditorDialog(selected);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        selected.DisplayName = dialog.DisplayNameValue;
        selected.Permissions = dialog.Permissions;
        try { await FirebaseClient.UpdateUserProfileAsync(session, selected); await RefreshAsync(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Không cập nhật được USER", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private async Task ToggleAsync()
    {
        var session = AppSession.Current;
        var selected = Selected();
        if (session is null || !session.Profile.IsAdmin || selected is null) return;
        if (selected.IsAdmin) { MessageBox.Show(this, "Không khóa tài khoản ADMIN bootstrap từ ứng dụng.", "Không cho phép"); return; }
        var next = !selected.Active;
        var text = next ? "Mở lại tài khoản này?" : "Khóa tài khoản khỏi ứng dụng?\n\nLịch sử vẫn được giữ; Firebase Auth record không bị xóa vật lý để bảo toàn audit.";
        if (MessageBox.Show(this, text, next ? "Mở lại USER" : "Khóa USER", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        selected.Active = next;
        try { await FirebaseClient.UpdateUserProfileAsync(session, selected); await RefreshAsync(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Không thay đổi được trạng thái", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
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
    private readonly CheckBox _damage = new() { Text = "Nhập hư hỏng", AutoSize = true };
    private readonly CheckBox _reports = new() { Text = "Xem danh sách đã nhập", AutoSize = true };
    private readonly CheckBox _sku = new() { Text = "Cập nhật danh mục SKU", AutoSize = true };
    private readonly CheckBox _sync = new() { Text = "Đồng bộ Google", AutoSize = true };
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
        Width = 620;
        Height = 650;
        MinimumSize = new Size(560, 580);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10F);

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(24) };
        var form = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, ColumnCount = 2, RowCount = 0, Padding = new Padding(0, 0, 14, 18) };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(form, "Tên tài khoản", _username);
        AddRow(form, "Họ tên", _name);
        AddRow(form, "Email", _email);

        var permissions = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Top };
        permissions.Controls.AddRange([_damage, _reports, _sku, _sync]);
        AddRow(form, "Quyền USER", permissions);

        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(360, 0),
            ForeColor = Color.DimGray,
            Text = _editing
                ? "Email và tên tài khoản được giữ cố định để tránh lệch Firebase Authentication. Có thể sửa họ tên và quyền USER."
                : "Khi tạo mới, hệ thống tạo tài khoản Firebase và gửi email đặt lại mật khẩu. ADMIN không lưu/biết mật khẩu của USER."
        };
        AddRow(form, "Ghi chú", note);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 12, 0, 0) };
        var save = new Button { Text = "Lưu", AutoSize = true, Height = 38, Padding = new Padding(12, 0, 12, 0) };
        var cancel = new Button { Text = "Hủy", AutoSize = true, Height = 38, Padding = new Padding(12, 0, 12, 0) };
        save.Click += (_, _) => Save();
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        buttons.Controls.AddRange([save, cancel]);
        AddRow(form, "", buttons);
        scroll.Controls.Add(form);
        Controls.Add(scroll);

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

    private static void AddRow(TableLayoutPanel form, string label, Control control)
    {
        var row = form.RowCount++;
        form.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        form.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 8, 8, 14), Margin = new Padding(0, 4, 0, 4) }, 0, row);
        control.Margin = new Padding(3, 4, 3, 14);
        if (control.Dock == DockStyle.None && control is not FlowLayoutPanel) control.Dock = DockStyle.Top;
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
