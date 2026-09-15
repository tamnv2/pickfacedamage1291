namespace PickfaceDamage1291;

/// <summary>
/// v1.3.1 presentation corrections. Kept separate from the business form so the fixes can be
/// reviewed/rolled back independently without changing the accepted data-entry logic.
/// </summary>
internal static class V131Runtime
{
    public static void Apply(MainForm form)
    {
        ApplyWindowTitle(form);
        MoveSessionSectionToAccount(form);
        FixReportsLayout(form);
        FixAuditLayout(form);

        form.Shown += (_, _) =>
        {
            ApplyWindowTitle(form);
            MoveSessionSectionToAccount(form);
            FixReportsLayout(form);
            FixAuditLayout(form);
        };
    }

    private static void ApplyWindowTitle(Form form)
    {
        var session = AppSession.Current;
        form.Text = session is null
            ? "Supra DC Hưng Yên"
            : $"Supra DC Hưng Yên - {session.Profile.Username} ({session.Profile.Role.ToUpperInvariant()})";
    }

    private static void MoveSessionSectionToAccount(Form form)
    {
        var tabs = FindAll<TabControl>(form).FirstOrDefault();
        if (tabs is null) return;

        var settings = tabs.TabPages.Cast<TabPage>().FirstOrDefault(x => x.Text == "Cài đặt");
        var account = tabs.TabPages.Cast<TabPage>().FirstOrDefault(x => x.Text == "Tài khoản");
        if (account is null) return;

        var sessionBox = settings is null
            ? null
            : FindAll<GroupBox>(settings).FirstOrDefault(x => x.Text == "Phiên đăng nhập");

        // The section may already have been moved by an earlier Apply call.
        sessionBox ??= FindAll<GroupBox>(account).FirstOrDefault(x => x.Text == "Phiên đăng nhập");
        if (sessionBox is null) return;

        if (sessionBox.Parent is not null && !IsDescendantOf(sessionBox, account))
            sessionBox.Parent.Controls.Remove(sessionBox);

        if (IsDescendantOf(sessionBox, account)) return;

        var scroll = FindAll<Panel>(account).FirstOrDefault(x => x.AutoScroll);
        if (scroll is null) return;

        sessionBox.Dock = DockStyle.Top;
        sessionBox.AutoSize = true;
        sessionBox.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        sessionBox.Margin = new Padding(0, 12, 0, 0);
        scroll.Controls.Add(sessionBox);
        sessionBox.BringToFront();
    }

    private static bool IsDescendantOf(Control control, Control ancestor)
    {
        for (Control? p = control.Parent; p is not null; p = p.Parent)
            if (ReferenceEquals(p, ancestor)) return true;
        return false;
    }

    private static void FixReportsLayout(Form form)
    {
        var tabs = FindAll<TabControl>(form).FirstOrDefault();
        var tab = tabs?.TabPages.Cast<TabPage>().FirstOrDefault(x => x.Text == "Danh sách đã nhập");
        if (tab is null) return;

        var commands = FindAll<GroupBox>(tab).FirstOrDefault(x => x.Text == "Công cụ danh sách");
        if (commands is not null && commands.Tag as string != "v131")
        {
            var actions = FindAll<FlowLayoutPanel>(commands)
                .FirstOrDefault(x => x.Controls.OfType<Button>().Any(b => b.Text == "Làm mới"));
            var status = actions?.Controls.OfType<Label>()
                .FirstOrDefault(x => x.Text.StartsWith("Tổng", StringComparison.OrdinalIgnoreCase));

            if (actions is not null && status is not null)
            {
                actions.Controls.Remove(status);
                commands.Controls.Remove(actions);

                actions.Dock = DockStyle.Fill;
                actions.AutoSize = false;
                actions.Height = 48;
                actions.WrapContents = false;
                actions.AutoScroll = true;
                actions.Padding = new Padding(0, 2, 0, 0);

                status.AutoSize = true;
                status.Padding = new Padding(2, 4, 0, 4);
                status.Margin = new Padding(0);

                var layout = new TableLayoutPanel
                {
                    Dock = DockStyle.Top,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    ColumnCount = 1,
                    RowCount = 2,
                    Padding = new Padding(12, 8, 12, 8)
                };
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.Controls.Add(actions, 0, 0);
                layout.Controls.Add(status, 0, 1);

                commands.AutoSize = true;
                commands.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                commands.Dock = DockStyle.Top;
                commands.Controls.Add(layout);
                commands.Tag = "v131";
            }
        }

        var grid = FindAll<DataGridView>(tab).FirstOrDefault();
        if (grid is not null)
        {
            ConfigureScrollableGrid(grid);
            ApplyReportColumnWidths(grid);
            if (grid.Tag as string != "v131")
            {
                grid.Tag = "v131";
                grid.ColumnAdded += (_, _) =>
                {
                    if (!grid.IsHandleCreated || grid.IsDisposed) return;
                    grid.BeginInvoke((Action)(() => ApplyReportColumnWidths(grid)));
                };
            }
        }
    }

    private static void FixAuditLayout(Form form)
    {
        var tabs = FindAll<TabControl>(form).FirstOrDefault();
        var tab = tabs?.TabPages.Cast<TabPage>().FirstOrDefault(x => x.Text == "Lịch sử");
        if (tab is null) return;

        var box = FindAll<GroupBox>(tab).FirstOrDefault(x => x.Text == "Lịch sử thao tác hệ thống");
        if (box is not null && box.Tag as string != "v131")
        {
            var root = box.Parent as TableLayoutPanel;
            if (root is not null && root.RowStyles.Count > 0)
                root.RowStyles[0] = new RowStyle(SizeType.AutoSize);

            var tools = box.Controls.OfType<FlowLayoutPanel>().FirstOrDefault();
            var status = tools?.Controls.Cast<Control>().LastOrDefault(x => x is Label) as Label;
            if (tools is not null && status is not null)
            {
                tools.Controls.Remove(status);
                box.Controls.Remove(tools);

                tools.Dock = DockStyle.Fill;
                tools.AutoSize = false;
                tools.Height = 48;
                tools.WrapContents = false;
                tools.AutoScroll = true;
                tools.Padding = new Padding(0, 2, 0, 0);

                status.AutoSize = true;
                status.Padding = new Padding(2, 4, 0, 4);
                status.Margin = new Padding(0);

                var layout = new TableLayoutPanel
                {
                    Dock = DockStyle.Top,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    ColumnCount = 1,
                    RowCount = 2,
                    Padding = new Padding(12, 8, 12, 8)
                };
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.Controls.Add(tools, 0, 0);
                layout.Controls.Add(status, 0, 1);

                box.AutoSize = true;
                box.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                box.Dock = DockStyle.Top;
                box.Controls.Add(layout);
                box.Tag = "v131";
            }
        }

        var grid = FindAll<DataGridView>(tab).FirstOrDefault();
        if (grid is not null)
        {
            ConfigureScrollableGrid(grid);
            ApplyAuditColumnWidths(grid);
        }
    }

    private static void ConfigureScrollableGrid(DataGridView grid)
    {
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        grid.ScrollBars = ScrollBars.Both;
        grid.ShowCellToolTips = true;
        grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
        grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
    }

    private static void ApplyReportColumnWidths(DataGridView grid)
    {
        var widths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Date"] = 105,
            ["Time"] = 72,
            ["Shift"] = 78,
            ["Sku"] = 125,
            ["Name"] = 320,
            ["Location"] = 135,
            ["Qty"] = 75,
            ["Base"] = 90,
            ["Version"] = 62,
            ["Status"] = 125,
            ["Editor"] = 130,
            ["Error"] = 360
        };
        foreach (DataGridViewColumn column in grid.Columns)
        {
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            if (widths.TryGetValue(column.Name, out var width)) column.Width = width;
        }
    }

    private static void ApplyAuditColumnWidths(DataGridView grid)
    {
        var widths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Time"] = 165,
            ["User"] = 125,
            ["Action"] = 190,
            ["Device"] = 220,
            ["Session"] = 220,
            ["Details"] = 650
        };
        foreach (DataGridViewColumn column in grid.Columns)
        {
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            if (widths.TryGetValue(column.Name, out var width)) column.Width = width;
        }
    }

    private static IEnumerable<T> FindAll<T>(Control root) where T : Control
    {
        if (root is T self) yield return self;
        foreach (Control child in root.Controls)
        foreach (var found in FindAll<T>(child)) yield return found;
    }
}
