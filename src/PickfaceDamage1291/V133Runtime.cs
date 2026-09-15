using System.Runtime.CompilerServices;

namespace PickfaceDamage1291;

internal enum ButtonVisual
{
    Normal,
    Primary,
    Danger
}

internal static class AppUiStyle
{
    public static void StyleAllButtons(Control root)
    {
        foreach (var button in FindAll<Button>(root))
            StyleButton(button, Classify(button.Text));
    }

    public static void StyleButton(Button button, ButtonVisual visual)
    {
        button.UseVisualStyleBackColor = false;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.AutoEllipsis = false;

        // All main UI buttons share one geometry. AutoSize + a minimum height is DPI-safe:
        // text can grow when Windows scaling requires it, but buttons never collapse below 40 px.
        if (button.Dock != DockStyle.Fill)
        {
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        }
        button.Padding = new Padding(14, 5, 14, 5);
        button.Margin = new Padding(4, 4, 8, 4);
        button.MinimumSize = new Size(button.MinimumSize.Width, 40);

        switch (visual)
        {
            case ButtonVisual.Primary:
                button.BackColor = Color.FromArgb(37, 99, 235);
                button.ForeColor = Color.White;
                button.FlatAppearance.BorderColor = Color.FromArgb(29, 78, 216);
                break;
            case ButtonVisual.Danger:
                button.BackColor = Color.FromArgb(220, 38, 38);
                button.ForeColor = Color.White;
                button.FlatAppearance.BorderColor = Color.FromArgb(185, 28, 28);
                break;
            default:
                button.BackColor = Color.FromArgb(226, 232, 240);
                button.ForeColor = Color.FromArgb(30, 41, 59);
                button.FlatAppearance.BorderColor = Color.FromArgb(148, 163, 184);
                break;
        }
    }

    private static ButtonVisual Classify(string? text)
    {
        var value = (text ?? string.Empty).Trim();
        if (value.Contains("Xoá", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Xóa", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Đăng xuất", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Bỏ ảnh", StringComparison.OrdinalIgnoreCase))
            return ButtonVisual.Danger;

        if (value.StartsWith("GỬI", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("LƯU", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Đồng bộ", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("Cập nhật lên", StringComparison.OrdinalIgnoreCase) ||
            value is "Đổi mật khẩu" or "Đổi email")
            return ButtonVisual.Primary;

        return ButtonVisual.Normal;
    }

    private static IEnumerable<T> FindAll<T>(Control root) where T : Control
    {
        if (root is T self) yield return self;
        foreach (Control child in root.Controls)
        foreach (var item in FindAll<T>(child))
            yield return item;
    }
}

internal static class EntryInputRules
{
    private static readonly ConditionalWeakTable<TextBox, object> BoundDigits = new();
    private static readonly ConditionalWeakTable<TextBox, object> BoundLocation = new();

    public static void AttachDigitsOnly(TextBox box)
    {
        if (BoundDigits.TryGetValue(box, out _)) return;
        BoundDigits.Add(box, new object());
        box.KeyPress += (_, e) =>
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true;
        };
        box.TextChanged += (_, _) => Sanitize(box, c => char.IsDigit(c));
    }

    public static void AttachLocation(TextBox box)
    {
        if (BoundLocation.TryGetValue(box, out _)) return;
        BoundLocation.Add(box, new object());
        box.KeyPress += (_, e) =>
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && e.KeyChar != '.') e.Handled = true;
        };
        box.Enter += (_, _) =>
        {
            var editable = LocationNormalizer.ToEditableInput(box.Text);
            if (string.Equals(editable, box.Text, StringComparison.Ordinal)) return;
            box.Text = editable;
            box.SelectionStart = box.TextLength;
        };
        box.TextChanged += (_, _) => SanitizeLocation(box);
    }

    private static void SanitizeLocation(TextBox box)
    {
        if (box.IsDisposed) return;
        var original = box.Text;

        // Canonical values are assigned by the application after validation or loaded from stored records.
        // A focused textbox is always an editing surface, so pasted text is still reduced to digits/dots.
        if (!box.Focused && LocationNormalizer.IsCanonicalStoredValue(original)) return;

        var cleaned = new string(original.Where(c => char.IsDigit(c) || c == '.').ToArray());
        if (string.Equals(original, cleaned, StringComparison.Ordinal)) return;
        var caret = Math.Min(box.SelectionStart, cleaned.Length);
        box.Text = cleaned;
        box.SelectionStart = caret;
    }

    private static void Sanitize(TextBox box, Func<char, bool> allowed)
    {
        if (box.IsDisposed) return;
        var original = box.Text;
        var cleaned = new string(original.Where(allowed).ToArray());
        if (string.Equals(original, cleaned, StringComparison.Ordinal)) return;
        var caret = Math.Min(box.SelectionStart, cleaned.Length);
        box.Text = cleaned;
        box.SelectionStart = caret;
    }
}

internal static class V133Runtime
{
    private const string AccountActionsTag = "v133-account-actions";

    public static void Apply(MainForm form)
    {
        form.MinimumSize = new Size(900, 680);
        AttachEntryRules(form);
        AddAccountSelfService(form);
        RefreshPresentation(form);

        form.Load += (_, _) => ScheduleRefresh(form);
        form.Shown += (_, _) => ScheduleRefresh(form);

        var tabs = FindAll<TabControl>(form).FirstOrDefault();
        if (tabs is not null)
        {
            tabs.SelectedIndexChanged += (_, _) =>
            {
                AddAccountSelfService(form);
                RefreshPresentation(form);
            };
        }
    }

    private static void AttachEntryRules(MainForm form)
    {
        var sku = GetField<TextBox>(form, "_sku");
        var location = GetField<TextBox>(form, "_location");
        if (sku is not null) EntryInputRules.AttachDigitsOnly(sku);
        if (location is not null) EntryInputRules.AttachLocation(location);
    }

    private static void ScheduleRefresh(MainForm form)
    {
        if (!form.IsHandleCreated || form.IsDisposed) return;
        form.BeginInvoke((Action)(() =>
        {
            if (form.IsDisposed) return;
            AddAccountSelfService(form);
            RefreshPresentation(form);
        }));
    }

    private static void RefreshPresentation(MainForm form)
    {
        AppUiStyle.StyleAllButtons(form);
    }

    private static void AddAccountSelfService(MainForm form)
    {
        var tabs = FindAll<TabControl>(form).FirstOrDefault();
        var account = tabs?.TabPages.Cast<TabPage>().FirstOrDefault(x => x.Text == "Tài khoản");
        if (account is null) return;
        var table = FindAll<TableLayoutPanel>(account).FirstOrDefault();
        if (table is null) return;
        if (table.Controls.Cast<Control>().Any(x => Equals(x.Tag, AccountActionsTag))) return;

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            WrapContents = true,
            Tag = AccountActionsTag
        };
        var changePassword = new Button { Text = "Đổi mật khẩu", AutoSize = true, Height = 36, Padding = new Padding(10, 0, 10, 0) };
        var changeEmail = new Button { Text = "Đổi email", AutoSize = true, Height = 36, Padding = new Padding(10, 0, 10, 0) };
        AppUiStyle.StyleButton(changePassword, ButtonVisual.Primary);
        AppUiStyle.StyleButton(changeEmail, ButtonVisual.Primary);
        changePassword.Click += async (_, _) => await ChangePasswordAsync(form);
        changeEmail.Click += async (_, _) => await ChangeEmailAsync(form, account);
        actions.Controls.AddRange([changePassword, changeEmail]);

        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(new Label
        {
            Text = "Bảo mật tài khoản",
            AutoSize = true,
            Padding = new Padding(0, 8, 10, 14),
            Margin = new Padding(0, 4, 0, 4)
        }, 0, row);
        actions.Margin = new Padding(3, 4, 3, 14);
        table.Controls.Add(actions, 1, row);
    }

    private static async Task ChangePasswordAsync(Form owner)
    {
        var session = AppSession.Current;
        if (session is null) return;
        if (session.OfflineMode)
        {
            NotificationCenter.Show(owner, "Cần kết nối mạng để đổi mật khẩu.", "Tài khoản", MessageBoxIcon.Warning);
            return;
        }

        using var dialog = new ChangePasswordDialog();
        if (dialog.ShowDialog(owner) != DialogResult.OK) return;
        try
        {
            await AccountSelfService.ChangePasswordAsync(session, dialog.CurrentPassword, dialog.NewPassword);
            NotificationCenter.Show(owner, "Đã đổi mật khẩu thành công.", "Tài khoản", MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            NotificationCenter.Show(owner, ex.Message, "Không đổi được mật khẩu", MessageBoxIcon.Warning);
        }
    }

    private static async Task ChangeEmailAsync(Form owner, TabPage accountTab)
    {
        var session = AppSession.Current;
        if (session is null) return;
        if (session.OfflineMode)
        {
            NotificationCenter.Show(owner, "Cần kết nối mạng để đổi email.", "Tài khoản", MessageBoxIcon.Warning);
            return;
        }

        using var dialog = new ChangeEmailDialog(session.Profile.Email);
        if (dialog.ShowDialog(owner) != DialogResult.OK) return;
        var confirm = MessageBox.Show(
            owner,
            $"Đổi email tài khoản sang:\n{dialog.NewEmail}\n\nSau khi đổi, tài khoản vẫn đăng nhập bằng tên tài khoản hiện tại. Tiếp tục?",
            "Xác nhận đổi email",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        try
        {
            await AccountSelfService.ChangeEmailAsync(session, dialog.CurrentPassword, dialog.NewEmail);
            var emailBox = FindAll<TextBox>(accountTab)
                .FirstOrDefault(x => x.ReadOnly && x.Text.Contains('@'));
            if (emailBox is not null) emailBox.Text = session.Profile.Email;
            NotificationCenter.Show(owner, "Đã đổi email và đồng bộ tài khoản đăng nhập.", "Tài khoản", MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            NotificationCenter.Show(owner, ex.Message, "Không đổi được email", MessageBoxIcon.Warning);
        }
    }

    private static T? GetField<T>(object target, string name) where T : class
        => target.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(target) as T;

    private static IEnumerable<T> FindAll<T>(Control root) where T : Control
    {
        if (root is T self) yield return self;
        foreach (Control child in root.Controls)
        foreach (var item in FindAll<T>(child))
            yield return item;
    }
}
