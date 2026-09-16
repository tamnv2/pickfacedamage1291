namespace PickfaceDamage1291;

internal static class V130Runtime
{
    private static bool _logoutRequested;

    public static bool ConsumeLogoutRequested()
    {
        var value = _logoutRequested;
        _logoutRequested = false;
        return value;
    }

    public static void Apply(MainForm form)
    {
        ApplySafeEntryWorkspaceLayout(form);
        AddLogoutSettings(form);
        form.Shown += async (_, _) =>
        {
            var session = AppSession.Current;
            if (session is { OfflineMode: false } && session.Profile.IsAdmin)
            {
                try { await UsernameAuthService.SyncAliasesAsync(session); } catch { }
            }
        };
    }

    private static void ApplySafeEntryWorkspaceLayout(Form form)
    {
        var tabs = FindAll<TabControl>(form).FirstOrDefault();
        var tab = tabs?.TabPages.Cast<TabPage>().FirstOrDefault(x => x.Text == "Nhập hư hỏng");
        if (tab is null || tab.Controls.OfType<SplitContainer>().Any()) return;

        var oldFlow = tab.Controls.OfType<FlowLayoutPanel>().FirstOrDefault();
        if (oldFlow is null) return;
        var groups = oldFlow.Controls.OfType<GroupBox>().ToList();
        var product = groups.FirstOrDefault(x => x.Text.StartsWith("1.", StringComparison.Ordinal));
        var occurrence = groups.FirstOrDefault(x => x.Text.StartsWith("2.", StringComparison.Ordinal));
        var images = groups.FirstOrDefault(x => x.Text.StartsWith("3.", StringComparison.Ordinal));
        var submit = groups.FirstOrDefault(x => x.Text.StartsWith("4.", StringComparison.Ordinal));
        if (product is null || occurrence is null || images is null || submit is null) return;

        // Build the replacement tree first. Do not set PanelMinSize while SplitContainer still has its default width.
        // That ordering was the root cause of the v1.2.2 SplitterDistance exception.
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 7,
            BackColor = SystemColors.ControlDark
        };
        var left = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(14),
            BackColor = Color.WhiteSmoke
        };

        oldFlow.Controls.Remove(product);
        oldFlow.Controls.Remove(occurrence);
        oldFlow.Controls.Remove(images);
        oldFlow.Controls.Remove(submit);

        product.Margin = new Padding(0, 0, 0, 14);
        occurrence.Margin = new Padding(0, 0, 0, 14);
        submit.Margin = new Padding(0, 0, 0, 14);
        left.Controls.Add(product);
        left.Controls.Add(occurrence);
        left.Controls.Add(submit);

        RebuildImageSection(images);
        split.Panel1.Controls.Add(left);
        split.Panel2.Padding = new Padding(12);
        split.Panel2.BackColor = Color.WhiteSmoke;
        split.Panel2.Controls.Add(images);

        tab.SuspendLayout();
        try
        {
            tab.Controls.Clear();
            tab.Controls.Add(split);
            oldFlow.Dispose();
        }
        finally
        {
            tab.ResumeLayout(true);
        }

        void Configure()
        {
            if (split.IsDisposed || split.Width < 720) return;
            try
            {
                split.Panel1MinSize = 0;
                split.Panel2MinSize = 0;
                var maxLeft = Math.Max(260, split.Width - split.SplitterWidth - 320);
                var desired = Math.Clamp((int)Math.Round(split.Width * 0.45), 300, maxLeft);
                if (desired > 0 && desired < split.Width - split.SplitterWidth)
                    split.SplitterDistance = desired;

                var rightAvailable = split.Width - split.SplitterWidth - split.SplitterDistance;
                split.Panel1MinSize = Math.Min(300, Math.Max(0, split.SplitterDistance));
                split.Panel2MinSize = Math.Min(320, Math.Max(0, rightAvailable));
            }
            catch (ArgumentOutOfRangeException)
            {
                // Never allow a DPI/resize edge case to remove the entry workspace again.
                split.Panel1MinSize = 0;
                split.Panel2MinSize = 0;
            }
        }

        split.HandleCreated += (_, _) => split.BeginInvoke((Action)Configure);
        split.SizeChanged += (_, _) =>
        {
            if (split.IsHandleCreated && !split.IsDisposed) split.BeginInvoke((Action)Configure);
        };
    }

    private static void RebuildImageSection(GroupBox imageBox)
    {
        var preview = FindAll<PictureBox>(imageBox).FirstOrDefault();
        var list = FindAll<ListBox>(imageBox).FirstOrDefault();
        var actions = FindAll<FlowLayoutPanel>(imageBox).FirstOrDefault(x => x.Controls.OfType<Button>().Any());
        if (preview is null || list is null || actions is null) return;

        preview.Parent?.Controls.Remove(preview);
        list.Parent?.Controls.Remove(list);
        actions.Parent?.Controls.Remove(actions);
        foreach (Control old in imageBox.Controls.Cast<Control>().ToArray()) old.Dispose();
        imageBox.Controls.Clear();

        imageBox.Text = "Hình ảnh hiện trạng — tối đa 5 ảnh";
        imageBox.Dock = DockStyle.Fill;
        imageBox.AutoSize = false;
        imageBox.Margin = new Padding(0);
        imageBox.Padding = new Padding(12);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(10)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
        actions.Dock = DockStyle.Fill;
        actions.AutoSize = false;
        actions.WrapContents = true;
        preview.Dock = DockStyle.Fill;
        preview.SizeMode = PictureBoxSizeMode.Zoom;
        preview.BorderStyle = BorderStyle.FixedSingle;
        preview.BackColor = Color.White;
        list.Dock = DockStyle.Fill;
        list.IntegralHeight = false;
        layout.Controls.Add(actions, 0, 0);
        layout.Controls.Add(preview, 0, 1);
        layout.Controls.Add(list, 0, 2);
        imageBox.Controls.Add(layout);
    }

    private static void AddLogoutSettings(Form form)
    {
        var tabs = FindAll<TabControl>(form).FirstOrDefault();
        var settings = tabs?.TabPages.Cast<TabPage>().FirstOrDefault(x => x.Text == "Cài đặt");
        if (settings is null) return;
        var flow = settings.Controls.OfType<FlowLayoutPanel>().FirstOrDefault();
        if (flow is null || flow.Controls.OfType<GroupBox>().Any(x => x.Text == "Phiên đăng nhập")) return;

        var session = AppSession.Current;
        var prefs = LoginPreferencesStore.Load();
        var box = new GroupBox
        {
            Text = "Phiên đăng nhập",
            Width = 900,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, 18),
            Padding = new Padding(12),
            Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold)
        };
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 0,
            Padding = new Padding(12)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(panel, "Tài khoản", new Label { Text = session?.Profile.Username ?? "-", AutoSize = true });

        var allowOffline = new CheckBox
        {
            Text = "Cho phép đăng nhập offline",
            Checked = prefs.AllowOfflineLogin,
            AutoSize = true,
            Margin = new Padding(0, 2, 18, 2)
        };
        var keepSignedIn = new CheckBox
        {
            Text = "Duy trì đăng nhập khi mở lại ứng dụng",
            Checked = prefs.KeepSignedIn,
            AutoSize = true,
            Margin = new Padding(0, 2, 18, 2)
        };
        var options = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight
        };
        options.Controls.AddRange([allowOffline, keepSignedIn]);
        AddRow(panel, "Tùy chọn đăng nhập", options);

        var preferenceStatus = new Label
        {
            AutoSize = true,
            ForeColor = Color.DimGray,
            MaximumSize = new Size(620, 0)
        };
        AddRow(panel, "Trạng thái", preferenceStatus);

        void PersistPreferences()
        {
            var next = new LoginPreferences
            {
                AllowOfflineLogin = allowOffline.Checked,
                KeepSignedIn = keepSignedIn.Checked
            };
            LoginPreferencesStore.Save(next);

            if (next.AllowOfflineLogin || next.KeepSignedIn)
            {
                if (AppSession.Current is { } active) SecureSessionStore.Save(active);
            }
            else
            {
                SecureSessionStore.Clear();
            }

            preferenceStatus.Text = $"Đăng nhập offline: {(next.AllowOfflineLogin ? "Bật" : "Tắt")} • Duy trì đăng nhập: {(next.KeepSignedIn ? "Bật" : "Tắt")}. Thay đổi có hiệu lực ngay.";
            AppLog.Info("LOGIN_PREFERENCES_CHANGED", "Đã cập nhật tùy chọn phiên đăng nhập trong khi ứng dụng đang chạy.",
                new Dictionary<string, object?>
                {
                    ["allow_offline_login"] = next.AllowOfflineLogin,
                    ["keep_signed_in"] = next.KeepSignedIn
                });
        }

        allowOffline.CheckedChanged += (_, _) => PersistPreferences();
        keepSignedIn.CheckedChanged += (_, _) => PersistPreferences();
        preferenceStatus.Text = $"Đăng nhập offline: {(prefs.AllowOfflineLogin ? "Bật" : "Tắt")} • Duy trì đăng nhập: {(prefs.KeepSignedIn ? "Bật" : "Tắt")}. Có thể thay đổi ngay tại đây.";

        var logout = new Button { Text = "Đăng xuất tài khoản", AutoSize = true, Height = 36, Padding = new Padding(10, 0, 10, 0) };
        logout.Click += (_, _) =>
        {
            var choice = MessageBox.Show(form, "Đăng xuất tài khoản hiện tại?", "Xác nhận đăng xuất", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (choice != DialogResult.Yes) return;
            _logoutRequested = true;
            form.Close();
        };
        AddRow(panel, "Thao tác", logout);
        box.Controls.Add(panel);
        flow.Controls.Add(box);
    }

    private static void AddRow(TableLayoutPanel panel, string label, Control control)
    {
        var row = panel.RowCount++;
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 8, 10, 12) }, 0, row);
        control.Margin = new Padding(3, 4, 3, 10);
        panel.Controls.Add(control, 1, row);
    }

    private static IEnumerable<T> FindAll<T>(Control root) where T : Control
    {
        if (root is T self) yield return self;
        foreach (Control child in root.Controls)
        foreach (var found in FindAll<T>(child)) yield return found;
    }
}
