namespace PickfaceDamage1291;

internal sealed class LoginFormV130 : Form
{
    private readonly TextBox _username = new();
    private readonly TextBox _password = new();
    private readonly CheckBox _allowOffline = new();
    private readonly CheckBox _keepSignedIn = new();
    private readonly Label _status = new();
    private readonly Button _login = new();
    private readonly Button _offline = new();
    private FirebaseSession? _cached;

    public LoginFormV130()
    {
        Text = "Đăng nhập — Pickface Damage 1291";
        Width = 540;
        Height = 520;
        MinimumSize = new Size(520, 500);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(30),
            ColumnCount = 1,
            RowCount = 9
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(new Label
        {
            Text = "PICKFACE DAMAGE 1291",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 17F, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        _username.CharacterCasing = CharacterCasing.Lower;
        root.Controls.Add(BuildInput("Tài khoản", _username), 0, 1);
        _password.UseSystemPasswordChar = true;
        _password.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            _ = LoginAsync();
        };
        root.Controls.Add(BuildInput("Mật khẩu", _password), 0, 2);

        var prefs = LoginPreferencesStore.Load();
        _allowOffline.Text = "Cho phép đăng nhập khi offline";
        _allowOffline.Checked = prefs.AllowOfflineLogin;
        _allowOffline.AutoSize = true;
        _allowOffline.Margin = new Padding(0, 4, 0, 6);
        _keepSignedIn.Text = "Duy trì đăng nhập";
        _keepSignedIn.Checked = prefs.KeepSignedIn;
        _keepSignedIn.AutoSize = true;
        var options = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = false
        };
        options.Controls.AddRange([_allowOffline, _keepSignedIn]);
        root.Controls.Add(options, 0, 3);

        _login.Text = "ĐĂNG NHẬP";
        _login.Height = 40;
        _login.Dock = DockStyle.Top;
        _login.Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold);
        _login.Click += async (_, _) => await LoginAsync();
        root.Controls.Add(_login, 0, 4);

        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        var forgot = new Button { Text = "Quên mật khẩu", AutoSize = true, Height = 34 };
        forgot.Click += async (_, _) => await ForgotPasswordAsync();
        _offline.Text = "Tiếp tục offline";
        _offline.AutoSize = true;
        _offline.Height = 34;
        _offline.Click += (_, _) => ResumeOffline();
        tools.Controls.AddRange([forgot, _offline]);
        root.Controls.Add(tools, 0, 5);

        _status.AutoSize = true;
        _status.MaximumSize = new Size(455, 0);
        _status.ForeColor = Color.DimGray;
        root.Controls.Add(_status, 0, 6);

        root.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(455, 0),
            ForeColor = Color.DimGray,
            Text = "Cho phép đăng nhập khi offline: giữ phiên đã xác thực để dùng theo logic offline hiện tại. Duy trì đăng nhập: mở lại app sẽ tự vào thẳng khi phiên còn hợp lệ."
        }, 0, 7);

        root.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(455, 0),
            ForeColor = Color.DimGray,
            Text = "USER nhập hư hỏng chỉ được tiếp tục offline khi lease 30 phút còn hiệu lực. ADMIN không chiếm quyền nhập của USER."
        }, 0, 8);

        Controls.Add(root);
        LoadCachedSession();
        Shown += async (_, _) => await TryBootstrapAliasesFromCachedAdminAsync();
    }

    private static Control BuildInput(string title, TextBox box)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        panel.Controls.Add(new Label { Text = title, AutoSize = true }, 0, 0);
        box.Dock = DockStyle.Top;
        box.Height = 32;
        panel.Controls.Add(box, 0, 1);
        return panel;
    }

    private void LoadCachedSession()
    {
        _cached = SecureSessionStore.Load();
        if (_cached is null)
        {
            _offline.Enabled = false;
            _status.Text = "Chưa có phiên đã xác thực trên laptop này.";
            return;
        }

        _username.Text = _cached.Profile.Username;
        RefreshOfflineAvailability();
    }

    private void RefreshOfflineAvailability()
    {
        if (_cached is null || !_allowOffline.Checked)
        {
            _offline.Enabled = false;
            return;
        }

        if (_cached.Profile.IsAdmin || !_cached.Profile.HasPermission("damage_entry"))
        {
            _offline.Enabled = true;
            _status.Text = $"Có phiên đã lưu: {_cached.Profile.Username} ({_cached.Profile.Role.ToUpperInvariant()}).";
            return;
        }

        var leaseValid = _cached.CachedOperator is not null &&
                         _cached.CachedOperator.Uid == _cached.Uid &&
                         _cached.CachedOperator.LeaseUntil > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _offline.Enabled = leaseValid;
        _status.Text = leaseValid
            ? $"Có lease offline của {_cached.Profile.Username} đến {_cached.CachedOperator!.LeaseUntilLocal:dd/MM/yyyy HH:mm:ss}."
            : "Phiên USER đã lưu nhưng lease offline không còn hiệu lực. Cần Internet để đăng nhập/giành quyền lại.";
    }

    private LoginPreferences CurrentPreferences() => new()
    {
        AllowOfflineLogin = _allowOffline.Checked,
        KeepSignedIn = _keepSignedIn.Checked
    };

    private async Task LoginAsync()
    {
        var username = _username.Text.Trim();
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(_password.Text))
        {
            MessageBox.Show(this, "Nhập tài khoản và mật khẩu.", "Thiếu thông tin");
            return;
        }

        LoginPreferencesStore.Save(CurrentPreferences());
        SetBusy(true, "Đang xác thực tài khoản...");
        try
        {
            var session = await UsernameAuthService.SignInAsync(username, _password.Text);
            if (!await SessionBootstrap.PrepareOnlineAsync(session, this)) return;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            _status.Text = "Đăng nhập chưa thành công.";
            MessageBox.Show(this, ex.Message, "Không đăng nhập được", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _password.Clear();
            SetBusy(false, _status.Text);
        }
    }

    private async Task ForgotPasswordAsync()
    {
        var username = _username.Text.Trim();
        if (string.IsNullOrWhiteSpace(username))
        {
            MessageBox.Show(this, "Nhập tên tài khoản trước.", "Quên mật khẩu");
            _username.Focus();
            return;
        }

        SetBusy(true, "Đang gửi yêu cầu đặt lại mật khẩu...");
        try
        {
            await UsernameAuthService.SendPasswordResetAsync(username);
            _status.Text = "Nếu tài khoản hợp lệ, email đặt lại mật khẩu sẽ được gửi tới email đăng ký.";
            MessageBox.Show(this, _status.Text, "Đã gửi yêu cầu", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Không gửi được yêu cầu", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            SetBusy(false, _status.Text);
        }
    }

    private void ResumeOffline()
    {
        LoginPreferencesStore.Save(CurrentPreferences());
        _cached = SecureSessionStore.Load();
        if (_cached is null)
        {
            LoadCachedSession();
            return;
        }
        if (!SessionBootstrap.PrepareOffline(_cached, this))
        {
            RefreshOfflineAvailability();
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    private async Task TryBootstrapAliasesFromCachedAdminAsync()
    {
        if (_cached is null || !_cached.Profile.IsAdmin) return;
        try
        {
            _cached.OfflineMode = false;
            await FirebaseClient.EnsureFreshAsync(_cached);
            var profile = await FirebaseClient.GetProfileAsync(_cached.Uid, _cached.IdToken);
            if (profile?.Active != true || !profile.IsAdmin) return;
            _cached.Profile = profile;
            await UsernameAuthService.SyncAliasesAsync(_cached);
            _status.Text = "Danh sách tài khoản đăng nhập đã sẵn sàng.";
        }
        catch
        {
            // Migration/bootstrap is best effort. Normal username login will show a concrete error if gateway setup is incomplete.
        }
    }

    private void SetBusy(bool busy, string text)
    {
        _login.Enabled = !busy;
        _username.Enabled = !busy;
        _password.Enabled = !busy;
        _allowOffline.Enabled = !busy;
        _keepSignedIn.Enabled = !busy;
        UseWaitCursor = busy;
        _status.Text = text;
        if (!busy) RefreshOfflineAvailability();
    }
}
