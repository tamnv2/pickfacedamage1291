namespace PickfaceDamage1291;

internal static class NotificationCenter
{
    private static readonly List<ToastForm> Toasts = [];
    private const int MaxToasts = 5;
    private const int Margin = 16;
    private const int Gap = 8;

    public static void Show(IWin32Window? owner, string text, string caption = "Thông báo", MessageBoxIcon icon = MessageBoxIcon.Information)
    {
        var ownerControl = owner as Control ?? Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.Visible);
        if (ownerControl is not null && ownerControl.InvokeRequired)
        {
            ownerControl.BeginInvoke((Action)(() => Show(owner, text, caption, icon)));
            return;
        }

        for (var i = Toasts.Count - 1; i >= 0; i--)
        {
            if (Toasts[i].IsDisposed) Toasts.RemoveAt(i);
        }

        while (Toasts.Count >= MaxToasts)
        {
            var oldest = Toasts[0];
            Toasts.RemoveAt(0);
            if (!oldest.IsDisposed) oldest.Close();
        }

        var toast = new ToastForm(caption, text, icon);
        toast.FormClosed += (_, _) =>
        {
            Toasts.Remove(toast);
            Reposition(ownerControl);
        };
        Toasts.Add(toast);
        Reposition(ownerControl);
        toast.Show();
        toast.BringToFront();
    }

    private static void Reposition(Control? owner)
    {
        var area = ResolveArea(owner);
        var y = area.Bottom - Margin;
        for (var i = Toasts.Count - 1; i >= 0; i--)
        {
            var toast = Toasts[i];
            if (toast.IsDisposed) continue;
            y -= toast.Height;
            toast.Location = new Point(area.Left + Margin, y);
            y -= Gap;
        }
    }

    private static Rectangle ResolveArea(Control? owner)
    {
        try
        {
            if (owner is not null && owner.IsHandleCreated)
            {
                var p = owner.PointToScreen(Point.Empty);
                return new Rectangle(p, owner.ClientSize);
            }
        }
        catch { }
        return Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
    }

    private sealed class ToastForm : Form
    {
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = 3000 };

        public ToastForm(string caption, string text, MessageBoxIcon icon)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            Width = 430;
            Height = 88;
            BackColor = SystemColors.Info;
            Padding = new Padding(12, 9, 12, 9);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.Controls.Add(new Label
            {
                Text = string.IsNullOrWhiteSpace(caption) ? "Thông báo" : caption,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
                AutoEllipsis = true
            }, 0, 0);
            root.Controls.Add(new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.5F),
                AutoEllipsis = true
            }, 0, 1);
            Controls.Add(root);

            _timer.Tick += (_, _) => Close();
            Shown += (_, _) => _timer.Start();
            FormClosed += (_, _) => _timer.Dispose();
            Click += (_, _) => Close();
            foreach (Control c in root.Controls) c.Click += (_, _) => Close();
        }
    }
}

// App-wide presentation policy: dialogs are reserved for user decisions.
// All one-way OK messages become non-blocking notifications.
internal static class MessageBox
{
    private static bool NeedsDecision(MessageBoxButtons buttons) => buttons != MessageBoxButtons.OK;

    public static DialogResult Show(string text)
        => Show(null, text, "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.None);

    public static DialogResult Show(string text, string caption)
        => Show(null, text, caption, MessageBoxButtons.OK, MessageBoxIcon.None);

    public static DialogResult Show(string text, string caption, MessageBoxButtons buttons)
        => Show(null, text, caption, buttons, MessageBoxIcon.None);

    public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        => Show(null, text, caption, buttons, icon);

    public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton)
        => Show(null, text, caption, buttons, icon, defaultButton);

    public static DialogResult Show(IWin32Window? owner, string text)
        => Show(owner, text, "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.None);

    public static DialogResult Show(IWin32Window? owner, string text, string caption)
        => Show(owner, text, caption, MessageBoxButtons.OK, MessageBoxIcon.None);

    public static DialogResult Show(IWin32Window? owner, string text, string caption, MessageBoxButtons buttons)
        => Show(owner, text, caption, buttons, MessageBoxIcon.None);

    public static DialogResult Show(IWin32Window? owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
    {
        if (NeedsDecision(buttons))
            return System.Windows.Forms.MessageBox.Show(owner, text, caption, buttons, icon);

        NotificationCenter.Show(owner, text, caption, icon);
        return DialogResult.OK;
    }

    public static DialogResult Show(IWin32Window? owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton)
    {
        if (NeedsDecision(buttons))
            return System.Windows.Forms.MessageBox.Show(owner, text, caption, buttons, icon, defaultButton);

        NotificationCenter.Show(owner, text, caption, icon);
        return DialogResult.OK;
    }
}
