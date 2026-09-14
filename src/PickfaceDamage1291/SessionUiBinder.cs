using System.Text.Json;

namespace PickfaceDamage1291;

internal static class SessionUiBinder
{
    public static void Bind(MainForm form)
    {
        var session = AppSession.Current ?? throw new InvalidOperationException("Chưa có phiên đăng nhập.");
        form.Text = $"Cập nhật hư hỏng Pickface 1291 — {session.Profile.Username} ({session.Profile.Role.ToUpperInvariant()})";

        var tabs = FindFirst<TabControl>(form) ?? throw new InvalidOperationException("Không tìm thấy TabControl chính.");
        ApplyPermissions(tabs, session.Profile);
        AddAccountTab(tabs);

        if (session.Profile.IsAdmin)
        {
            AddAdminTabs(tabs);
            _ = BindAdminOperatorGuardAsync(form, tabs, session);
        }
        else
        {
            BindUserOperatorGuard(form, tabs, session);
        }

        ApplyButtonPermissions(form, session.Profile);
    }

    private static void ApplyPermissions(TabControl tabs, FirebaseUserProfile profile)
    {
        if (profile.IsAdmin) return;

        RemoveTabIfDenied(tabs, "Nhập hư hỏng", profile.HasPermission("damage_entry"));
        RemoveTabIfDenied(tabs, "Danh sách đã nhập", profile.HasPermission("view_reports"));
        RemoveTabIfDenied(tabs, "Danh mục SKU", profile.HasPermission("import_sku"));
    }

    private static void RemoveTabIfDenied(TabControl tabs, string title, bool allowed)
    {
        if (allowed) return;
        var page = tabs.TabPages.Cast<TabPage>().FirstOrDefault(x => string.Equals(x.Text, title, StringComparison.Ordinal));
        if (page is not null) tabs.TabPages.Remove(page);
    }

    private static void ApplyButtonPermissions(Control root, FirebaseUserProfile profile)
    {
        if (profile.IsAdmin) return;
        foreach (var button in FindAll<Button>(root))
        {
            if (string.Equals(button.Text, "Đồng bộ lại", StringComparison.Ordinal) && !profile.HasPermission("sync_google"))
                button.Enabled = false;
            if ((button.Text.Contains("Kết nối Google", StringComparison.OrdinalIgnoreCase) ||
                 button.Text.Contains("Kiểm tra Drive", StringComparison.OrdinalIgnoreCase)) &&
                !profile.HasPermission("sync_google"))
                button.Enabled = false;
        }
    }

    private static void BindUserOperatorGuard(Form form, TabControl tabs, FirebaseSession session)
    {
        var manager = AppSession.OperatorManager;
        if (manager is null) return;
        var damageTab = tabs.TabPages.Cast<TabPage>().FirstOrDefault(x => x.Text == "Nhập hư hỏng");
        var send = damageTab is null ? null : FindAll<Button>(damageTab).FirstOrDefault(x => x.Text.Contains("GỬI THÔNG TIN", StringComparison.OrdinalIgnoreCase));
        var status = damageTab is null ? null : FindAll<Label>(damageTab).FirstOrDefault(x => x.Text.Contains("Dữ liệu được lưu", StringComparison.OrdinalIgnoreCase));

        void ApplyState(string message, bool allowed)
        {
            if (form.IsDisposed) return;
            void Work()
            {
                if (send is not null) send.Enabled = allowed && session.Profile.HasPermission("damage_entry");
                if (status is not null) status.Text = message;
            }
            if (form.InvokeRequired) form.BeginInvoke((Action)Work); else Work();
        }

        manager.StateChanged += ApplyState;
        manager.Kicked += message =>
        {
            if (form.IsDisposed) return;
            void Work()
            {
                MessageBox.Show(form, message + "\n\nỨng dụng sẽ đóng phiên này. Dữ liệu local đã lưu không bị xóa.", "Phiên đã bị ngắt", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                form.Close();
            }
            if (form.InvokeRequired) form.BeginInvoke((Action)Work); else Work();
        };

        ApplyState(
            session.OfflineMode ? "OFFLINE — đang trong thời hạn lease dự phòng 30 phút." : "Online — tài khoản đang giữ quyền nhập duy nhất.",
            manager.CanCreateDamage);
    }

    private static async Task BindAdminOperatorGuardAsync(Form form, TabControl tabs, FirebaseSession session)
    {
        var damageTab = tabs.TabPages.Cast<TabPage>().FirstOrDefault(x => x.Text == "Nhập hư hỏng");
        var send = damageTab is null ? null : FindAll<Button>(damageTab).FirstOrDefault(x => x.Text.Contains("GỬI THÔNG TIN", StringComparison.OrdinalIgnoreCase));
        var status = damageTab is null ? null : FindAll<Label>(damageTab).FirstOrDefault(x => x.Text.Contains("Dữ liệu được lưu", StringComparison.OrdinalIgnoreCase));
        if (send is null) return;

        if (session.OfflineMode)
        {
            send.Enabled = false;
            if (status is not null) status.Text = "ADMIN đang offline: không thể xác minh active_operator nên chức năng tạo phiếu bị khóa an toàn.";
            return;
        }

        async Task ApplySnapshotAsync()
        {
            try
            {
                var snapshot = await FirebaseClient.GetActiveOperatorSnapshotAsync(session);
                if (form.IsDisposed) return;
                void Work()
                {
                    var free = snapshot.Value is null;
                    send.Enabled = free;
                    if (status is not null)
                        status.Text = free
                            ? "ADMIN: hiện không có USER giữ quyền nhập. ADMIN có thể nhập; hệ thống sẽ khóa ngay khi USER nhận active_operator."
                            : $"ADMIN: USER {snapshot.Value!.Username} đang giữ quyền nhập. Chỉ xem, không được tạo phiếu.";
                }
                if (form.InvokeRequired) form.BeginInvoke((Action)Work); else Work();
            }
            catch
            {
                if (form.IsDisposed) return;
                void Work()
                {
                    send.Enabled = false;
                    if (status is not null) status.Text = "Không xác minh được active_operator — khóa Gửi để tránh hai người nhập cùng lúc.";
                }
                if (form.InvokeRequired) form.BeginInvoke((Action)Work); else Work();
            }
        }

        await ApplySnapshotAsync();
        _ = Task.Run(async () =>
        {
            while (!form.IsDisposed)
            {
                try
                {
                    using var response = await FirebaseClient.OpenActiveOperatorStreamAsync(session, CancellationToken.None);
                    if (!response.IsSuccessStatusCode) throw new HttpRequestException();
                    await using var stream = await response.Content.ReadAsStreamAsync();
                    using var reader = new StreamReader(stream);
                    string? eventName = null;
                    string? data = null;
                    while (!form.IsDisposed && !reader.EndOfStream)
                    {
                        var line = await reader.ReadLineAsync();
                        if (line is null) break;
                        if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase)) eventName = line[6..].Trim();
                        else if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) data = line[5..].Trim();
                        else if (line.Length == 0)
                        {
                            if ((eventName == "put" || eventName == "patch") && !string.IsNullOrWhiteSpace(data))
                                await ApplySnapshotAsync();
                            eventName = null;
                            data = null;
                        }
                    }
                }
                catch
                {
                    if (form.IsDisposed) break;
                    try { await Task.Delay(3000); } catch { }
                    await ApplySnapshotAsync();
                }
            }
        });
    }

    private static void AddAdminTabs(TabControl tabs)
    {
        if (!tabs.TabPages.Cast<TabPage>().Any(x => x.Text == "Quản lý nhân sự"))
        {
            var users = new TabPage("Quản lý nhân sự") { BackColor = Color.WhiteSmoke };
            users.Controls.Add(new AdminUsersControl());
            tabs.TabPages.Add(users);
        }
        if (!tabs.TabPages.Cast<TabPage>().Any(x => x.Text == "Lịch sử"))
        {
            var audit = new TabPage("Lịch sử") { BackColor = Color.WhiteSmoke };
            audit.Controls.Add(new AuditControl());
            tabs.TabPages.Add(audit);
        }
    }

    private static void AddAccountTab(TabControl tabs)
    {
        if (tabs.TabPages.Cast<TabPage>().Any(x => x.Text == "Tài khoản")) return;
        var page = new TabPage("Tài khoản") { BackColor = Color.WhiteSmoke };
        page.Controls.Add(new MyAccountControl());
        tabs.TabPages.Add(page);
    }

    private static T? FindFirst<T>(Control root) where T : Control
    {
        if (root is T self) return self;
        foreach (Control child in root.Controls)
        {
            var found = FindFirst<T>(child);
            if (found is not null) return found;
        }
        return null;
    }

    private static IEnumerable<T> FindAll<T>(Control root) where T : Control
    {
        if (root is T self) yield return self;
        foreach (Control child in root.Controls)
        foreach (var found in FindAll<T>(child))
            yield return found;
    }
}

internal sealed class MyAccountControl : UserControl
{
    private readonly TextBox _username = new();
    private readonly TextBox _name = new();
    private readonly TextBox _email = new();
    private readonly TextBox _role = new();
    private readonly Label _status = new();

    public MyAccountControl()
    {
        Dock = DockStyle.Fill;
        Padding = new Padding(24);
        Font = new Font("Segoe UI", 10F);
        var session = AppSession.Current ?? throw new InvalidOperationException("Chưa đăng nhập.");

        var box = new GroupBox { Text = "Thông tin tài khoản", Dock = DockStyle.Top, Height = 390, Padding = new Padding(16) };
        var form = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 7 };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _username.Text = session.Profile.Username;
        _username.ReadOnly = true;
        _name.Text = session.Profile.DisplayName;
        _email.Text = session.Profile.Email;
        _email.ReadOnly = true;
        _role.Text = session.Profile.Role.ToUpperInvariant();
        _role.ReadOnly = true;
        AddRow(form, "Tên tài khoản", _username, 0);
        AddRow(form, "Họ tên hiển thị", _name, 1);
        AddRow(form, "Email đăng ký", _email, 2);
        AddRow(form, "Quyền", _role, 3);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
        var save = new Button { Text = "Lưu họ tên", AutoSize = true };
        var reset = new Button { Text = "Gửi email đặt lại mật khẩu", AutoSize = true };
        save.Click += async (_, _) => await SaveNameAsync();
        reset.Click += async (_, _) => await ResetPasswordAsync();
        actions.Controls.AddRange([save, reset]);
        form.Controls.Add(new Label { Text = "Thao tác", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, 4);
        form.Controls.Add(actions, 1, 4);
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        _status.AutoSize = true;
        form.Controls.Add(new Label(), 0, 5);
        form.Controls.Add(_status, 1, 5);
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            ForeColor = Color.DimGray,
            Text = "Email đăng nhập không đổi trực tiếp ở bản này để tránh lệch giữa Firebase Authentication và hồ sơ ứng dụng. ADMIN cũng không được xem hoặc biết mật khẩu của USER."
        };
        form.Controls.Add(new Label(), 0, 6);
        form.Controls.Add(note, 1, 6);
        form.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        box.Controls.Add(form);
        Controls.Add(box);
    }

    private static void AddRow(TableLayoutPanel form, string label, Control control, int row)
    {
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        form.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, row);
        control.Dock = DockStyle.Top;
        control.BackColor = control.ReadOnly ? Color.White : SystemColors.Window;
        form.Controls.Add(control, 1, row);
    }

    private async Task SaveNameAsync()
    {
        var session = AppSession.Current;
        if (session is null || session.OfflineMode)
        {
            MessageBox.Show(this, "Cần online để cập nhật thông tin tài khoản.", "Offline");
            return;
        }
        var value = _name.Text.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            MessageBox.Show(this, "Họ tên không được để trống.", "Thiếu thông tin");
            return;
        }
        try
        {
            session.Profile.DisplayName = value;
            await FirebaseClient.UpdateUserProfileAsync(session, session.Profile);
            SecureSessionStore.Save(session);
            _status.Text = "Đã cập nhật họ tên.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Không cập nhật được", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task ResetPasswordAsync()
    {
        var session = AppSession.Current;
        if (session is null) return;
        try
        {
            await FirebaseClient.SendPasswordResetAsync(session.Profile.Email);
            _status.Text = "Đã gửi email đặt lại mật khẩu.";
            MessageBox.Show(this, "Firebase đã gửi email đặt lại mật khẩu tới email đăng ký.", "Đã gửi email", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Không gửi được email", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
