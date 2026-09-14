namespace PickfaceDamage1291;

internal sealed class LoginForm : Form
{
    private readonly TextBox _email = new();
    private readonly TextBox _password = new();
    private readonly Label _status = new();
    private readonly Button _login = new();
    private readonly Button _offline = new();
    private FirebaseSession? _cached;

    public LoginForm()
    {
        Text = "Đăng nhập — Pickface Damage 1291";
        Width = 520;
        Height = 430;
        MinimumSize = new Size(500, 400);
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
            RowCount = 7
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var title = new Label
        {
            Text = "PICKFACE DAMAGE 1291",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 17F, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        root.Controls.Add(title, 0, 0);

        root.Controls.Add(BuildInput("Email đăng nhập", _email), 0, 1);
        _password.UseSystemPasswordChar = true;
        _password.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            _ = LoginAsync();
        };
        root.Controls.Add(BuildInput("Mật khẩu", _password), 0, 2);

        _login.Text = "ĐĂNG NHẬP";
        _login.Height = 40;
        _login.Dock = DockStyle.Top;
        _login.Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold);
        _login.Click += async (_, _) => await LoginAsync();
        root.Controls.Add(_login, 0, 3);

        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        var forgot = new Button { Text = "Quên mật khẩu", AutoSize = true, Height = 34 };
        forgot.Click += async (_, _) => await ForgotPasswordAsync();
        _offline.Text = "Tiếp tục offline";
        _offline.AutoSize = true;
        _offline.Height = 34;
        _offline.Click += (_, _) => ResumeOffline();
        tools.Controls.AddRange([forgot, _offline]);
        root.Controls.Add(tools, 0, 4);

        _status.AutoSize = true;
        _status.MaximumSize = new Size(430, 0);
        _status.ForeColor = Color.DimGray;
        root.Controls.Add(_status, 0, 5);

        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(430, 0),
            ForeColor = Color.DimGray,
            Text = "Offline chỉ dùng được nếu tài khoản đã đăng nhập online trên chính laptop này trước đó. USER chỉ được tiếp tục khi lease offline 30 phút còn hiệu lực. ADMIN không chiếm quyền nhập của USER."
        };
        root.Controls.Add(note, 0, 6);
        Controls.Add(root);

        LoadCachedSession();
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

        _email.Text = _cached.Email;
        if (_cached.Profile.IsAdmin)
        {
            _offline.Enabled = true;
            _status.Text = $"Có phiên đã lưu: {_cached.Profile.Username} (ADMIN).";
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

    private async Task LoginAsync()
    {
        var email = _email.Text.Trim();
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(_password.Text))
        {
            MessageBox.Show(this, "Nhập email và mật khẩu.", "Thiếu thông tin");
            return;
        }

        SetBusy(true, "Đang xác thực Firebase...");
        try
        {
            var session = await FirebaseClient.SignInAsync(email, _password.Text);
            OperatorLeaseManager? manager = null;

            if (!session.Profile.IsAdmin)
            {
                manager = new OperatorLeaseManager(session);
                _status.Text = "Đang kiểm tra người đang giữ quyền nhập...";
                var result = await manager.AcquireAsync((existing, online) =>
                {
                    var message = online
                        ? $"Tài khoản {existing.Username} đang ONLINE trên thiết bị khác.\n\nNếu tiếp tục, phiên đó sẽ bị ngắt ngay và quyền nhập chuyển sang tài khoản {session.Profile.Username}.\n\nTiếp tục?"
                        : $"Phiên {existing.Username} đã hết lease/không còn hoạt động.\n\nNhận quyền nhập cho tài khoản {session.Profile.Username}?";
                    return MessageBox.Show(this, message, "Chuyển quyền nhập", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
                });

                if (!result.Success)
                {
                    await manager.DisposeAsync();
                    manager = null;
                    MessageBox.Show(this, result.Message, "Chưa thể vào vùng nhập liệu", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }

            session.OfflineMode = false;
            AppSession.Current = session;
            AppSession.OperatorManager = manager;
            SecureSessionStore.Save(session);
            try
            {
                await FirebaseClient.AppendAuditAsync(session, "LOGIN_SUCCESS", new { role = session.Profile.Role }, manager?.SessionId);
            }
            catch
            {
                // Login remains valid if audit temporarily fails.
            }

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
        var email = _email.Text.Trim();
        if (string.IsNullOrWhiteSpace(email))
        {
            MessageBox.Show(this, "Nhập email đăng ký trước.", "Quên mật khẩu");
            _email.Focus();
            return;
        }

        SetBusy(true, "Đang gửi email lấy lại mật khẩu...");
        try
        {
            await FirebaseClient.SendPasswordResetAsync(email);
            MessageBox.Show(this, "Đã yêu cầu Firebase gửi email đặt lại mật khẩu. Kiểm tra hộp thư và thư rác.", "Đã gửi email", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _status.Text = "Đã gửi yêu cầu đặt lại mật khẩu.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Không gửi được email", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            SetBusy(false, _status.Text);
        }
    }

    private void ResumeOffline()
    {
        _cached = SecureSessionStore.Load();
        if (_cached is null)
        {
            LoadCachedSession();
            return;
        }

        _cached.OfflineMode = true;
        if (_cached.Profile.IsAdmin)
        {
            AppSession.Current = _cached;
            AppSession.OperatorManager = null;
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        var manager = new OperatorLeaseManager(_cached, reuseCachedLease: true);
        if (!manager.IsAcquired)
        {
            manager.DisposeAsync().AsTask().GetAwaiter().GetResult();
            MessageBox.Show(this, "Lease offline không còn hiệu lực. Cần kết nối Internet để đăng nhập lại.", "Không thể làm offline", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            LoadCachedSession();
            return;
        }

        AppSession.Current = _cached;
        AppSession.OperatorManager = manager;
        manager.StartOfflineMonitoring();
        SecureSessionStore.Save(_cached);
        DialogResult = DialogResult.OK;
        Close();
    }

    private void SetBusy(bool busy, string text)
    {
        _login.Enabled = !busy;
        _offline.Enabled = !busy && (_cached?.Profile.IsAdmin == true ||
            (_cached?.CachedOperator is not null && _cached.CachedOperator.LeaseUntil > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        _email.Enabled = !busy;
        _password.Enabled = !busy;
        UseWaitCursor = busy;
        _status.Text = text;
    }
}
