using System.Net.Mail;
using System.Text;
using System.Text.Json;

namespace PickfaceDamage1291;

internal static class AccountSelfService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static async Task VerifyCurrentPasswordAsync(
        FirebaseSession session,
        string currentPassword,
        CancellationToken ct = default)
    {
        EnsureOnline(session);
        if (string.IsNullOrWhiteSpace(currentPassword))
            throw new InvalidOperationException("Chưa nhập mật khẩu hiện tại.");

        var reauthenticated = await UsernameAuthService.SignInAsync(session.Profile.Username, currentPassword, ct);
        if (!string.Equals(reauthenticated.Uid, session.Uid, StringComparison.Ordinal))
            throw new InvalidOperationException("Không xác minh được tài khoản hiện tại.");
    }

    public static async Task ChangePasswordAsync(
        FirebaseSession session,
        string currentPassword,
        string newPassword,
        CancellationToken ct = default)
    {
        EnsureOnline(session);
        if (string.IsNullOrWhiteSpace(currentPassword))
            throw new InvalidOperationException("Chưa nhập mật khẩu hiện tại.");
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
            throw new InvalidOperationException("Mật khẩu mới phải có ít nhất 6 ký tự.");
        if (string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
            throw new InvalidOperationException("Mật khẩu mới phải khác mật khẩu hiện tại.");

        var reauthenticated = await UsernameAuthService.SignInAsync(session.Profile.Username, currentPassword, ct);
        if (!string.Equals(reauthenticated.Uid, session.Uid, StringComparison.Ordinal))
            throw new InvalidOperationException("Không xác minh được tài khoản hiện tại.");

        using var response = await Http.PostAsync(
            $"https://identitytoolkit.googleapis.com/v1/accounts:update?key={Uri.EscapeDataString(CloudConfig.FirebaseApiKey)}",
            JsonContent(new
            {
                idToken = reauthenticated.IdToken,
                password = newPassword,
                returnSecureToken = true
            }),
            ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(ReadFirebaseError(body, "Không đổi được mật khẩu."));

        ApplySessionFromFirebaseResponse(session, body);
        SecureSessionStore.Save(session);
        await FirebaseClient.AppendAuditAsync(session, "ACCOUNT_PASSWORD_CHANGED", new { }, ct: ct);
    }

    public static async Task ChangeEmailAsync(
        FirebaseSession session,
        string currentPassword,
        string newEmail,
        CancellationToken ct = default)
    {
        EnsureOnline(session);
        newEmail = newEmail.Trim();
        if (string.IsNullOrWhiteSpace(currentPassword))
            throw new InvalidOperationException("Chưa nhập mật khẩu hiện tại.");
        if (!IsValidEmail(newEmail))
            throw new InvalidOperationException("Email mới không hợp lệ.");
        if (string.Equals(newEmail, session.Profile.Email, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Email mới đang trùng email hiện tại.");

        await FirebaseClient.EnsureFreshAsync(session, ct);
        if (!RuntimeConfigService.IsGoogleGatewayConfigured)
            await RuntimeConfigService.InitializeAsync(ct);
        if (!RuntimeConfigService.IsGoogleGatewayConfigured)
            throw new InvalidOperationException("Gateway tài khoản chưa sẵn sàng.");

        using var content = new StringContent(
            JsonSerializer.Serialize(new
            {
                action = "change_own_email",
                id_token = session.IdToken,
                payload = new
                {
                    current_password = currentPassword,
                    new_email = newEmail
                }
            }),
            Encoding.UTF8,
            "application/json");

        using var response = await Http.PostAsync(RuntimeConfigService.GoogleGatewayUrl, content, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Gateway lỗi HTTP {(int)response.StatusCode}.");

        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        if (!root.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            throw new InvalidOperationException(ReadGatewayError(root, "Không đổi được email."));

        var oldEmail = session.Profile.Email;
        session.Email = root.TryGetProperty("email", out var emailNode) ? emailNode.GetString() ?? newEmail : newEmail;
        session.Profile.Email = session.Email;
        session.IdToken = root.TryGetProperty("id_token", out var idToken) ? idToken.GetString() ?? session.IdToken : session.IdToken;
        session.RefreshToken = root.TryGetProperty("refresh_token", out var refreshToken) ? refreshToken.GetString() ?? session.RefreshToken : session.RefreshToken;
        var expires = root.TryGetProperty("expires_in", out var expiresNode) && long.TryParse(expiresNode.ToString(), out var parsed)
            ? parsed
            : 3600;
        session.AccessTokenExpiresUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, expires - 30));
        SecureSessionStore.Save(session);
        await FirebaseClient.AppendAuditAsync(session, "ACCOUNT_EMAIL_CHANGED", new { old_email = oldEmail, new_email = session.Email }, ct: ct);
    }

    private static void ApplySessionFromFirebaseResponse(FirebaseSession session, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("idToken", out var idToken) && !string.IsNullOrWhiteSpace(idToken.GetString()))
            session.IdToken = idToken.GetString()!;
        if (root.TryGetProperty("refreshToken", out var refreshToken) && !string.IsNullOrWhiteSpace(refreshToken.GetString()))
            session.RefreshToken = refreshToken.GetString()!;
        if (root.TryGetProperty("email", out var email) && !string.IsNullOrWhiteSpace(email.GetString()))
        {
            session.Email = email.GetString()!;
            session.Profile.Email = session.Email;
        }
        var expires = root.TryGetProperty("expiresIn", out var expiresNode) && long.TryParse(expiresNode.ToString(), out var parsed)
            ? parsed
            : 3600;
        session.AccessTokenExpiresUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, expires - 30));
        session.OfflineMode = false;
    }

    private static void EnsureOnline(FirebaseSession session)
    {
        if (session.OfflineMode)
            throw new InvalidOperationException("Cần kết nối mạng để thay đổi thông tin đăng nhập.");
    }

    private static bool IsValidEmail(string value)
    {
        try
        {
            var address = new MailAddress(value);
            return string.Equals(address.Address, value, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static StringContent JsonContent(object value)
        => new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private static string ReadGatewayError(JsonElement root, string fallback)
        => root.TryGetProperty("error", out var error) && !string.IsNullOrWhiteSpace(error.GetString())
            ? error.GetString()!
            : fallback;

    private static string ReadFirebaseError(string body, string fallback)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var message = doc.RootElement
                .GetProperty("error")
                .GetProperty("message")
                .GetString();
            return message switch
            {
                "INVALID_PASSWORD" => "Mật khẩu hiện tại không đúng.",
                "WEAK_PASSWORD : Password should be at least 6 characters" => "Mật khẩu mới phải có ít nhất 6 ký tự.",
                "TOKEN_EXPIRED" or "INVALID_ID_TOKEN" => "Phiên đăng nhập đã hết hạn. Hãy đăng nhập lại.",
                _ when !string.IsNullOrWhiteSpace(message) => message,
                _ => fallback
            };
        }
        catch
        {
            return fallback;
        }
    }
}

internal sealed class AdminPasswordConfirmDialog : Form
{
    private readonly TextBox _password = new() { UseSystemPasswordChar = true };
    public string Password => _password.Text;

    public AdminPasswordConfirmDialog(int reportCount)
    {
        Text = "Xác nhận mật khẩu ADMIN";
        Width = 500;
        Height = 225;
        MinimumSize = new Size(460, 210);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10F);

        var table = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 2, RowCount = 0 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var note = new Label
        {
            Text = $"Đang xác nhận xoá {reportCount:N0} phiếu. Mật khẩu không được lưu lại.",
            AutoSize = true,
            MaximumSize = new Size(420, 0),
            ForeColor = Color.DimGray
        };
        AddRow(table, "", note);
        AddRow(table, "Mật khẩu hiện tại", _password);

        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var ok = new Button { Text = "Xác nhận xoá", AutoSize = true };
        var cancel = new Button { Text = "Hủy", AutoSize = true, DialogResult = DialogResult.Cancel };
        AppUiStyle.StyleButton(ok, ButtonVisual.Danger);
        AppUiStyle.StyleButton(cancel, ButtonVisual.Normal);
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_password.Text))
            {
                MessageBox.Show(this, "Nhập mật khẩu hiện tại của tài khoản ADMIN.", "Thiếu mật khẩu", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        };
        actions.Controls.AddRange([ok, cancel]);
        AddRow(table, "", actions);
        Controls.Add(table);
        AcceptButton = ok;
        CancelButton = cancel;
        Shown += (_, _) => _password.Focus();
    }

    private static void AddRow(TableLayoutPanel table, string label, Control control)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 8, 12, 12) }, 0, row);
        control.Dock = control is FlowLayoutPanel ? DockStyle.Top : DockStyle.Fill;
        control.Margin = new Padding(3, 3, 3, 12);
        table.Controls.Add(control, 1, row);
    }
}

internal sealed class ChangePasswordDialog : Form
{
    private readonly TextBox _current = new() { UseSystemPasswordChar = true };
    private readonly TextBox _newPassword = new() { UseSystemPasswordChar = true };
    private readonly TextBox _confirm = new() { UseSystemPasswordChar = true };

    public string CurrentPassword => _current.Text;
    public string NewPassword => _newPassword.Text;

    public ChangePasswordDialog()
    {
        Text = "Đổi mật khẩu";
        Width = 520;
        Height = 300;
        MinimumSize = new Size(480, 280);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10F);

        var table = BuildTable();
        AddRow(table, "Mật khẩu hiện tại", _current);
        AddRow(table, "Mật khẩu mới", _newPassword);
        AddRow(table, "Nhập lại mật khẩu mới", _confirm);

        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.RightToLeft };
        var ok = new Button { Text = "Đổi mật khẩu", AutoSize = true, Height = 36 };
        var cancel = new Button { Text = "Hủy", AutoSize = true, Height = 36, DialogResult = DialogResult.Cancel };
        AppUiStyle.StyleButton(ok, ButtonVisual.Primary);
        AppUiStyle.StyleButton(cancel, ButtonVisual.Normal);
        ok.Click += (_, _) =>
        {
            if (_newPassword.Text.Length < 6)
            {
                MessageBox.Show(this, "Mật khẩu mới phải có ít nhất 6 ký tự.", "Kiểm tra mật khẩu", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!string.Equals(_newPassword.Text, _confirm.Text, StringComparison.Ordinal))
            {
                MessageBox.Show(this, "Hai lần nhập mật khẩu mới chưa khớp.", "Kiểm tra mật khẩu", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        };
        actions.Controls.AddRange([ok, cancel]);
        AddRow(table, string.Empty, actions);
        Controls.Add(table);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    private static TableLayoutPanel BuildTable()
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 2, RowCount = 0 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return table;
    }

    private static void AddRow(TableLayoutPanel table, string label, Control control)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 8, 12, 12) }, 0, row);
        control.Dock = control is FlowLayoutPanel ? DockStyle.Top : DockStyle.Fill;
        control.Margin = new Padding(3, 3, 3, 12);
        table.Controls.Add(control, 1, row);
    }
}

internal sealed class ChangeEmailDialog : Form
{
    private readonly TextBox _email = new();
    private readonly TextBox _password = new() { UseSystemPasswordChar = true };

    public string NewEmail => _email.Text.Trim();
    public string CurrentPassword => _password.Text;

    public ChangeEmailDialog(string currentEmail)
    {
        Text = "Thay đổi email";
        Width = 540;
        Height = 280;
        MinimumSize = new Size(500, 260);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10F);

        var table = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 2, RowCount = 0 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(table, "Email hiện tại", new TextBox { Text = currentEmail, ReadOnly = true, BackColor = Color.White });
        AddRow(table, "Email mới", _email);
        AddRow(table, "Mật khẩu hiện tại", _password);

        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.RightToLeft };
        var ok = new Button { Text = "Đổi email", AutoSize = true, Height = 36 };
        var cancel = new Button { Text = "Hủy", AutoSize = true, Height = 36, DialogResult = DialogResult.Cancel };
        AppUiStyle.StyleButton(ok, ButtonVisual.Primary);
        AppUiStyle.StyleButton(cancel, ButtonVisual.Normal);
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_email.Text) || !_email.Text.Contains('@'))
            {
                MessageBox.Show(this, "Email mới không hợp lệ.", "Kiểm tra email", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        };
        actions.Controls.AddRange([ok, cancel]);
        AddRow(table, string.Empty, actions);
        Controls.Add(table);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    private static void AddRow(TableLayoutPanel table, string label, Control control)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 8, 12, 12) }, 0, row);
        control.Dock = control is FlowLayoutPanel ? DockStyle.Top : DockStyle.Fill;
        control.Margin = new Padding(3, 3, 3, 12);
        table.Controls.Add(control, 1, row);
    }
}
