using System.Diagnostics;
using System.Reflection;

namespace PickfaceDamage1291;

internal static class V132Runtime
{
    private static string? _storageMoveTarget;

    public static void Apply(MainForm form)
    {
        ReplaceSubmitButton(form);
        AttachBackgroundProgress(form);
        AttachReportDetails(form);
        CleanUserFacingPresentation(form);
        AddStorageMoveAction(form);

        form.Load += (_, _) => SchedulePresentationRefresh(form);
        form.Shown += (_, _) => SchedulePresentationRefresh(form);

        var tabs = FindAll<TabControl>(form).FirstOrDefault();
        if (tabs is not null)
            tabs.SelectedIndexChanged += (_, _) => SchedulePresentationRefresh(form);

        BackgroundSyncCoordinator.StateChanged += state =>
        {
            if (form.IsDisposed) return;
            void Work()
            {
                if (state.Completed) InvokePrivate(form, "RefreshReports");
            }
            if (form.InvokeRequired) form.BeginInvoke((Action)Work); else Work();
        };
    }

    public static string? ConsumeStorageMoveTarget()
    {
        var value = _storageMoveTarget;
        _storageMoveTarget = null;
        return value;
    }

    private static void SchedulePresentationRefresh(MainForm form)
    {
        if (!form.IsHandleCreated || form.IsDisposed) return;
        form.BeginInvoke((Action)(() =>
        {
            if (form.IsDisposed) return;
            CleanUserFacingPresentation(form);
            AttachBackgroundProgress(form);
            AddStorageMoveAction(form);
        }));
    }

    private static void ReplaceSubmitButton(MainForm form)
    {
        var oldButton = GetField<Button>(form, "_send");
        if (oldButton is null || oldButton.Tag as string == "v132-replaced") return;
        var parent = oldButton.Parent as TableLayoutPanel;
        if (parent is null) return;

        var pos = parent.GetPositionFromControl(oldButton);
        var colSpan = parent.GetColumnSpan(oldButton);
        var rowSpan = parent.GetRowSpan(oldButton);
        var replacement = new Button
        {
            Text = "GỬI THÔNG TIN HƯ HỎNG",
            Height = Math.Max(42, oldButton.Height),
            Dock = DockStyle.Top,
            Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold),
            Margin = oldButton.Margin,
            Tag = "v132-submit"
        };
        replacement.Click += async (_, _) => await EntryBackgroundSubmitter.SubmitAsync(form, replacement, oldButton);

        parent.Controls.Remove(oldButton);
        oldButton.Tag = "v132-replaced";
        parent.Controls.Add(replacement, pos.Column, pos.Row);
        parent.SetColumnSpan(replacement, colSpan);
        parent.SetRowSpan(replacement, rowSpan);

        var mirror = new System.Windows.Forms.Timer { Interval = 350 };
        mirror.Tick += (_, _) =>
        {
            if (form.IsDisposed)
            {
                mirror.Stop();
                mirror.Dispose();
                return;
            }
            if (replacement.Tag as string != "v132-saving") replacement.Enabled = oldButton.Enabled;
        };
        mirror.Start();
    }

    private static void AttachBackgroundProgress(MainForm form)
    {
        var tabs = FindAll<TabControl>(form).FirstOrDefault();
        var tab = tabs?.TabPages.Cast<TabPage>().FirstOrDefault(x => x.Text == "Nhập hư hỏng");
        var split = tab?.Controls.OfType<SplitContainer>().FirstOrDefault();
        if (split is null) return;
        if (FindAll<BackgroundSyncProgressControl>(split.Panel2).Any()) return;

        var imageBox = FindAll<GroupBox>(split.Panel2).FirstOrDefault(x => x.Text.Contains("Hình ảnh", StringComparison.OrdinalIgnoreCase));
        if (imageBox is null) return;
        imageBox.Parent?.Controls.Remove(imageBox);

        var host = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(0),
            Margin = new Padding(0),
            Tag = "v132-image-host"
        };
        host.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        host.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        imageBox.Dock = DockStyle.Fill;
        host.Controls.Add(imageBox, 0, 0);
        host.Controls.Add(new BackgroundSyncProgressControl(), 0, 1);
        split.Panel2.Controls.Add(host);
        host.BringToFront();
    }

    private static void ApplyResponsiveEntryLayout(MainForm form)
    {
        var tabs = FindAll<TabControl>(form).FirstOrDefault();
        var tab = tabs?.TabPages.Cast<TabPage>().FirstOrDefault(x => x.Text == "Nhập hư hỏng");
        var split = tab?.Controls.OfType<SplitContainer>().FirstOrDefault();
        if (split is null || split.Width <= 0 || split.Height <= 0) return;

        var compact = tab!.ClientSize.Width < 1380;
        try
        {
            split.Panel1MinSize = 0;
            split.Panel2MinSize = 0;
            var desiredOrientation = compact ? Orientation.Horizontal : Orientation.Vertical;
            if (split.Orientation != desiredOrientation) split.Orientation = desiredOrientation;

            if (compact)
            {
                var minBottom = Math.Min(260, Math.Max(120, split.Height / 3));
                var desired = Math.Clamp((int)Math.Round(split.Height * 0.55), 240, Math.Max(241, split.Height - minBottom - split.SplitterWidth));
                if (desired > 0 && desired < split.Height - split.SplitterWidth) split.SplitterDistance = desired;
                var bottom = split.Height - split.SplitterWidth - split.SplitterDistance;
                split.Panel1MinSize = Math.Min(220, Math.Max(0, split.SplitterDistance));
                split.Panel2MinSize = Math.Min(220, Math.Max(0, bottom));
            }
            else
            {
                var desired = Math.Clamp((int)Math.Round(split.Width * 0.45), 460, Math.Max(461, split.Width - 520 - split.SplitterWidth));
                if (desired > 0 && desired < split.Width - split.SplitterWidth) split.SplitterDistance = desired;
                var right = split.Width - split.SplitterWidth - split.SplitterDistance;
                split.Panel1MinSize = Math.Min(380, Math.Max(0, split.SplitterDistance));
                split.Panel2MinSize = Math.Min(420, Math.Max(0, right));
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            split.Panel1MinSize = 0;
            split.Panel2MinSize = 0;
        }

        var left = split.Panel1.Controls.OfType<FlowLayoutPanel>().FirstOrDefault();
        if (left is not null)
        {
            var width = Math.Max(360, left.ClientSize.Width - left.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 8);
            foreach (Control child in left.Controls)
            {
                child.MinimumSize = Size.Empty;
                child.MaximumSize = Size.Empty;
                child.Width = width;
            }
        }
    }

    private static void CleanUserFacingPresentation(MainForm form)
    {
        var tabs = FindAll<TabControl>(form).FirstOrDefault();
        if (tabs is null) return;

        var settings = tabs.TabPages.Cast<TabPage>().FirstOrDefault(x => x.Text == "Cài đặt");
        var account = tabs.TabPages.Cast<TabPage>().FirstOrDefault(x => x.Text == "Tài khoản");
        if (settings is not null)
        {
            foreach (var table in FindAll<TableLayoutPanel>(settings)) HideRows(table, "Ghi chú", "Nguyên tắc");
            foreach (var box in FindAll<GroupBox>(settings))
            {
                if (box.Text.Contains("Google Drive", StringComparison.OrdinalIgnoreCase)) box.Text = "Google Drive / Google Sheet";
                if (box.Text.Contains("Firebase", StringComparison.OrdinalIgnoreCase)) box.Text = "Tài khoản / phiên làm việc";
                if (box.Text.Contains("Dữ liệu local", StringComparison.OrdinalIgnoreCase)) box.Text = "Dữ liệu trên máy";
            }
            foreach (var label in FindAll<Label>(settings))
            {
                if (label.Text.Contains("OWNER", StringComparison.OrdinalIgnoreCase) ||
                    label.Text.Contains("OAuth", StringComparison.OrdinalIgnoreCase) ||
                    label.Text.Contains("refresh token", StringComparison.OrdinalIgnoreCase) ||
                    label.Text.Contains("Firebase hợp lệ", StringComparison.OrdinalIgnoreCase))
                {
                    label.Text = AppSession.Current?.OfflineMode == true
                        ? "Đang offline. Dữ liệu sẽ tự đồng bộ khi có mạng."
                        : GoogleService.IsConnected() ? "Đã kết nối tự động." : "Chưa kết nối.";
                }
            }
        }

        if (account is not null)
            foreach (var table in FindAll<TableLayoutPanel>(account)) HideRows(table, "Ghi chú", "Nguyên tắc");

        foreach (var button in FindAll<Button>(form))
        {
            if (button.Text.Contains("Xuất Excel", StringComparison.OrdinalIgnoreCase) && button.Text.Contains("cập nhật logic", StringComparison.OrdinalIgnoreCase))
                button.Visible = false;
        }
    }

    private static void HideRows(TableLayoutPanel table, params string[] labels)
    {
        foreach (Control control in table.Controls)
        {
            if (control is not Label label) continue;
            if (!labels.Any(x => string.Equals(label.Text.Trim(), x, StringComparison.OrdinalIgnoreCase))) continue;
            var pos = table.GetPositionFromControl(label);
            foreach (Control rowControl in table.Controls.Cast<Control>().Where(x => table.GetRow(x) == pos.Row).ToList())
                rowControl.Visible = false;
            if (pos.Row >= 0 && pos.Row < table.RowStyles.Count) table.RowStyles[pos.Row].Height = 0;
        }
    }

    private static void AddStorageMoveAction(MainForm form)
    {
        var tabs = FindAll<TabControl>(form).FirstOrDefault();
        var settings = tabs?.TabPages.Cast<TabPage>().FirstOrDefault(x => x.Text == "Cài đặt");
        var box = settings is null ? null : FindAll<GroupBox>(settings).FirstOrDefault(x => x.Text is "Dữ liệu trên máy" or "Dữ liệu local trên laptop");
        if (box is null || FindAll<Button>(box).Any(x => x.Tag as string == "v132-storage")) return;
        var table = FindAll<TableLayoutPanel>(box).FirstOrDefault();
        if (table is null) return;

        var button = new Button { Text = "Thay đổi nơi lưu trữ", AutoSize = true, Height = 36, Padding = new Padding(10, 0, 10, 0), Tag = "v132-storage" };
        button.Click += (_, _) => RequestStorageMove(form);
        AddRow(table, "Chuyển dữ liệu", button);
    }

    private static void RequestStorageMove(MainForm form)
    {
        if (BackgroundSyncCoordinator.IsBusy || BackgroundSyncCoordinator.QueueCount > 0)
        {
            NotificationCenter.Show(form, "Đang có tiến trình đồng bộ nền. Hãy chờ đồng bộ xong rồi đổi nơi lưu trữ.", "Chưa thể chuyển", MessageBoxIcon.Warning);
            return;
        }

        using var dialog = new FolderBrowserDialog
        {
            Description = "Chọn thư mục mới. Ứng dụng sẽ tạo thư mục PickfaceDamage1291 bên trong.",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(Path.GetDirectoryName(AppPaths.Root)) ? Path.GetDirectoryName(AppPaths.Root)! : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog(form) != DialogResult.OK) return;

        var target = LocalStorageManager.BuildTargetRoot(dialog.SelectedPath);
        var confirm = MessageBox.Show(
            form,
            $"Chuyển toàn bộ dữ liệu local sang:\n{target}\n\nỨng dụng sẽ đóng, sao chép và kiểm tra toàn vẹn dữ liệu rồi tự mở lại. Tiếp tục?",
            "Xác nhận chuyển nơi lưu trữ",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        _storageMoveTarget = target;
        form.Close();
    }

    private static void AttachReportDetails(MainForm form)
    {
        var grid = GetField<DataGridView>(form, "_reportGrid");
        if (grid is null || grid.Tag as string == "v132-details") return;
        grid.Tag = "v132-details";
        grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count) return;
            var row = grid.Rows[e.RowIndex];
            var id = Convert.ToString(row.Cells["ReportId"].Value) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(id)) return;
            var report = Database.GetReportById(id);
            if (report is null)
            {
                NotificationCenter.Show(form, "Không tìm thấy phiếu trong dữ liệu local.", "Chi tiết phiếu", MessageBoxIcon.Warning);
                return;
            }
            using var detail = new ReportDetailForm(report, Database.GetImages(id));
            detail.ShowDialog(form);
        };
    }

    private static void AddRow(TableLayoutPanel table, string label, Control control)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 8, 12, 12) }, 0, row);
        control.Margin = new Padding(3, 4, 3, 10);
        table.Controls.Add(control, 1, row);
    }

    private static T? GetField<T>(object target, string name) where T : class
        => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target) as T;

    internal static object? GetFieldValue(object target, string name)
        => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target);

    internal static void InvokePrivate(object target, string name)
        => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(target, null);

    private static IEnumerable<T> FindAll<T>(Control root) where T : Control
    {
        if (root is T self) yield return self;
        foreach (Control child in root.Controls)
        foreach (var found in FindAll<T>(child)) yield return found;
    }
}

internal static class EntryBackgroundSubmitter
{
    public static Task SubmitAsync(MainForm form, Button submitButton, Button permissionSource)
    {
        V132Runtime.InvokePrivate(form, "LookupSku");
        if (V132Runtime.GetFieldValue(form, "_skuValid") is not bool skuValid || !skuValid)
        {
            (V132Runtime.GetFieldValue(form, "_sku") as TextBox)?.Focus();
            return Task.CompletedTask;
        }

        var sku = (TextBox)V132Runtime.GetFieldValue(form, "_sku")!;
        var productName = (TextBox)V132Runtime.GetFieldValue(form, "_productName")!;
        var locationBox = (TextBox)V132Runtime.GetFieldValue(form, "_location")!;
        var date = (DateTimePicker)V132Runtime.GetFieldValue(form, "_date")!;
        var time = (TimeWheelPicker)V132Runtime.GetFieldValue(form, "_time")!;
        var shift = (ExclusiveShiftPicker)V132Runtime.GetFieldValue(form, "_shift")!;
        var quantity = (NumericUpDown)V132Runtime.GetFieldValue(form, "_quantity")!;
        var baseUnit = (TextBox)V132Runtime.GetFieldValue(form, "_baseUnit")!;
        var draftId = Convert.ToString(V132Runtime.GetFieldValue(form, "_draftId")) ?? Guid.NewGuid().ToString("D");
        var draftImages = (List<string>)V132Runtime.GetFieldValue(form, "_draftImages")!;

        if (!LocationNormalizer.TryNormalize(locationBox.Text, out var location, out var locationError))
        {
            NotificationCenter.Show(form, locationError, "Kiểm tra vị trí", MessageBoxIcon.Warning);
            locationBox.Focus();
            return Task.CompletedTask;
        }
        locationBox.Text = location;
        if (string.IsNullOrWhiteSpace(shift.SelectedShift))
        {
            NotificationCenter.Show(form, "Chưa chọn ca.", "Thiếu thông tin", MessageBoxIcon.Warning);
            return Task.CompletedTask;
        }
        if (quantity.Value < 1)
        {
            NotificationCenter.Show(form, "Số lượng hư hỏng phải lớn hơn 0.", "Thiếu thông tin", MessageBoxIcon.Warning);
            return Task.CompletedTask;
        }

        var report = new DamageReport(
            draftId,
            date.Value.Date,
            time.Hour,
            time.Minute,
            shift.SelectedShift,
            sku.Text.Trim(),
            productName.Text,
            location,
            decimal.Truncate(quantity.Value),
            baseUnit.Text,
            DateTime.Now,
            GoogleService.IsConnected() ? "PENDING" : "OFFLINE_PENDING",
            null,
            null,
            AppSession.Current?.Profile.Username ?? string.Empty,
            1,
            null,
            null);

        submitButton.Tag = "v132-saving";
        submitButton.Enabled = false;
        try
        {
            Database.SaveDamageReport(report, draftImages.ToList());
            var imageCount = draftImages.Count;
            var session = AppSession.Current;
            if (session is not null)
            {
                _ = FirebaseClient.AppendAuditAsync(session, "DAMAGE_REPORT_CREATED", new
                {
                    report_id = report.ReportId,
                    sku = report.Sku,
                    location = report.Location,
                    quantity = decimal.Truncate(report.Quantity),
                    shift = report.Shift,
                    image_count = imageCount
                }, AppSession.OperatorManager?.SessionId);
            }

            V132Runtime.InvokePrivate(form, "ResetDraft");
            V132Runtime.InvokePrivate(form, "RefreshReports");
            V132Runtime.InvokePrivate(form, "RefreshSettings");

            if (GoogleService.IsConnected())
            {
                BackgroundSyncCoordinator.Enqueue(report.ReportId);
                NotificationCenter.Show(form, "Đã lưu phiếu trên máy. Có thể nhập phiếu tiếp theo; đồng bộ Google đang chạy nền.", "Đã lưu", MessageBoxIcon.Information);
            }
            else
            {
                NotificationCenter.Show(form, "Đã lưu phiếu trên máy. Phiếu sẽ chờ đồng bộ khi có kết nối.", "Đã lưu", MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            NotificationCenter.Show(form, "Không lưu được phiếu: " + ex.Message, "Lỗi lưu dữ liệu", MessageBoxIcon.Error);
        }
        finally
        {
            submitButton.Tag = "v132-submit";
            submitButton.Enabled = permissionSource.Enabled;
        }
        return Task.CompletedTask;
    }
}

internal sealed class BackgroundSyncProgressControl : UserControl
{
    private readonly Label _label = new();
    private readonly ProgressBar _bar = new();
    private readonly System.Windows.Forms.Timer _idleTimer = new() { Interval = 2200 };

    public BackgroundSyncProgressControl()
    {
        Dock = DockStyle.Fill;
        Height = 58;
        Padding = new Padding(4, 6, 4, 4);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        _label.Text = "Đồng bộ nền: sẵn sàng";
        _label.AutoSize = true;
        _bar.Dock = DockStyle.Fill;
        _bar.Minimum = 0;
        _bar.Maximum = 100;
        layout.Controls.Add(_label, 0, 0);
        layout.Controls.Add(_bar, 0, 1);
        Controls.Add(layout);

        BackgroundSyncCoordinator.StateChanged += OnStateChanged;
        _idleTimer.Tick += (_, _) =>
        {
            _idleTimer.Stop();
            _bar.Value = 0;
            _label.Text = "Đồng bộ nền: sẵn sàng";
        };
        Disposed += (_, _) =>
        {
            BackgroundSyncCoordinator.StateChanged -= OnStateChanged;
            _idleTimer.Dispose();
        };
    }

    private void OnStateChanged(BackgroundSyncState state)
    {
        if (IsDisposed) return;
        void Work()
        {
            if (IsDisposed) return;
            _idleTimer.Stop();
            _bar.Value = Math.Clamp(state.Percent, 0, 100);
            var queue = state.QueueCount > 1 ? $" • {state.QueueCount - 1} phiếu chờ" : string.Empty;
            _label.Text = $"Đồng bộ nền: {state.Message} {Math.Clamp(state.Percent, 0, 100)}%{queue}";
            if (state.Completed && !state.IsBusy && state.QueueCount == 0) _idleTimer.Start();
        }
        if (InvokeRequired) BeginInvoke((Action)Work); else Work();
    }
}
