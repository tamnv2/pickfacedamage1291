namespace PickfaceDamage1291;

internal static class UiRuntimeFixes
{
    private static bool _googleBootstrapRunning;
    private static bool _entryLayoutApplied;
    private static bool _updateCheckRunning;
    private static UpdateUiState? _updateUi;

    private sealed class UpdateUiState
    {
        public required Label Status { get; init; }
        public required Button Action { get; init; }
        public Label? HeaderVersion { get; init; }
        public ReleaseInfo? Latest { get; set; }
    }

    public static void Attach(Form form)
    {
        var tabs = FindAll<TabControl>(form).FirstOrDefault();
        if (tabs is not null)
        {
            tabs.SelectedIndexChanged += (_, _) =>
            {
                ApplyGoogleSettingsPresentation(form);
                Reflow(form);
            };
        }

        form.Load += (_, _) =>
        {
            ApplyEntryWorkspaceLayout(form);
            SetupUpdaterPanel(form);
            ApplyGoogleSettingsPresentation(form);
            form.BeginInvoke((Action)(() => Reflow(form)));
        };

        form.Resize += (_, _) =>
        {
            if (form.IsHandleCreated && !form.IsDisposed)
                form.BeginInvoke((Action)(() => Reflow(form)));
        };

        form.Shown += async (_, _) =>
        {
            ApplyEntryWorkspaceLayout(form);
            SetupUpdaterPanel(form);
            Reflow(form);
            await BootstrapGoogleAfterLoginAsync(form);
            ApplyGoogleSettingsPresentation(form);
            await CheckForUpdatesAsync(form);
            Reflow(form);
        };
    }

    private static void ApplyEntryWorkspaceLayout(Form form)
    {
        if (_entryLayoutApplied) return;
        var tabs = FindAll<TabControl>(form).FirstOrDefault();
        var tab = tabs?.TabPages.Cast<TabPage>().FirstOrDefault(x => x.Text == "Nhập hư hỏng");
        if (tab is null) return;

        var oldFlow = tab.Controls.OfType<FlowLayoutPanel>().FirstOrDefault();
        if (oldFlow is null) return;
        var groups = oldFlow.Controls.OfType<GroupBox>().ToList();
        var product = groups.FirstOrDefault(x => x.Text.StartsWith("1.", StringComparison.Ordinal));
        var occurrence = groups.FirstOrDefault(x => x.Text.StartsWith("2.", StringComparison.Ordinal));
        var images = groups.FirstOrDefault(x => x.Text.StartsWith("3.", StringComparison.Ordinal));
        var submit = groups.FirstOrDefault(x => x.Text.StartsWith("4.", StringComparison.Ordinal));
        if (product is null || occurrence is null || images is null || submit is null) return;

        oldFlow.Controls.Remove(product);
        oldFlow.Controls.Remove(occurrence);
        oldFlow.Controls.Remove(images);
        oldFlow.Controls.Remove(submit);
        tab.Controls.Clear();
        oldFlow.Dispose();

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 7,
            Panel1MinSize = 400,
            Panel2MinSize = 480,
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
        tab.Controls.Add(split);

        split.HandleCreated += (_, _) => SetInitialSplitter(split);
        split.SizeChanged += (_, _) =>
        {
            if (split.Tag is null) SetInitialSplitter(split);
        };
        _entryLayoutApplied = true;
    }

    private static void SetInitialSplitter(SplitContainer split)
    {
        if (split.Width < 920) return;
        var desired = (int)Math.Round(split.Width * 0.45);
        desired = Math.Max(split.Panel1MinSize, Math.Min(desired, split.Width - split.Panel2MinSize - split.SplitterWidth));
        if (desired > 0) split.SplitterDistance = desired;
        split.Tag = "initialized";
    }

    private static void RebuildImageSection(GroupBox imageBox)
    {
        var preview = FindAll<PictureBox>(imageBox).FirstOrDefault();
        var list = FindAll<ListBox>(imageBox).FirstOrDefault();
        var actions = FindAll<FlowLayoutPanel>(imageBox).FirstOrDefault(x => x.Controls.OfType<Button>().Any());
        if (preview is null || list is null || actions is null) return;

        foreach (var parent in new Control[] { preview, list, actions })
            parent.Parent?.Controls.Remove(parent);
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

    private static void Reflow(Form form)
    {
        if (form.IsDisposed) return;
        foreach (var flow in FindAll<FlowLayoutPanel>(form)
                     .Where(x => x.AutoScroll && x.FlowDirection == FlowDirection.TopDown && !x.WrapContents))
            FixVerticalStackWidth(flow);
    }

    private static void FixVerticalStackWidth(FlowLayoutPanel panel)
    {
        if (panel.IsDisposed) return;
        var width = panel.ClientSize.Width - panel.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 12;
        if (width < 360) width = 360;

        panel.SuspendLayout();
        try
        {
            foreach (Control child in panel.Controls)
            {
                child.MinimumSize = new Size(width, 0);
                child.MaximumSize = new Size(width, 0);
                child.Width = width;
                if (child is GroupBox box)
                {
                    box.AutoSize = true;
                    box.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                }
            }
        }
        finally
        {
            panel.ResumeLayout(true);
        }
    }

    private static async Task BootstrapGoogleAfterLoginAsync(Form form)
    {
        if (_googleBootstrapRunning || form.IsDisposed) return;
        var session = AppSession.Current;
        if (session is null) return;
        if (session.OfflineMode)
        {
            SetGoogleHeader(form, "Google: offline/local");
            return;
        }

        if (!RuntimeConfigService.IsGoogleGatewayConfigured)
        {
            SetGoogleHeader(form, "Google: chờ cấu hình dùng chung");
            return;
        }

        _googleBootstrapRunning = true;
        try
        {
            SetGoogleHeader(form, "Google: đang kết nối tự động...");
            await GoogleService.VerifyBindingAsync(writeHeaders: true);
            SetGoogleHeader(form, "Google: đã kết nối tự động");

            if (session.Profile.IsAdmin || session.Profile.HasPermission("sync_google") || session.Profile.HasPermission("damage_entry"))
            {
                try { await GoogleService.SyncAllPendingAsync(); } catch { }
            }
        }
        catch (Exception ex)
        {
            SetGoogleHeader(form, "Google: lỗi kết nối");
            if (session.Profile.IsAdmin && !form.IsDisposed)
            {
                MessageBox.Show(
                    form,
                    "Đăng nhập Firebase đã thành công nhưng kết nối Google dùng chung chưa sẵn sàng. Dữ liệu vẫn được lưu local an toàn.\n\n" + ex.Message,
                    "Google chưa sẵn sàng",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
        finally
        {
            _googleBootstrapRunning = false;
        }
    }

    private static void ApplyGoogleSettingsPresentation(Form form)
    {
        var group = FindAll<GroupBox>(form).FirstOrDefault(x => x.Text.Contains("Google Drive", StringComparison.OrdinalIgnoreCase));
        if (group is null) return;
        group.Text = "Google Drive / Google Sheet — kết nối tự động theo tài khoản ứng dụng";

        foreach (var button in FindAll<Button>(group))
        {
            if (button.Text.Contains("Kết nối Google", StringComparison.OrdinalIgnoreCase))
                button.Visible = false;
            else if (button.Text.Contains("Kiểm tra Drive", StringComparison.OrdinalIgnoreCase))
                button.Text = "Kiểm tra kết nối Google";
        }

        var status = FindAll<Label>(group).FirstOrDefault(x =>
            x.Text.Contains("OAuth", StringComparison.OrdinalIgnoreCase) ||
            x.Text.Contains("token", StringComparison.OrdinalIgnoreCase) ||
            x.Text.Contains("laptop này", StringComparison.OrdinalIgnoreCase));
        if (status is not null)
        {
            status.Text = AppSession.Current?.OfflineMode == true
                ? "Đang offline. Phiếu được lưu local và sẽ tự đồng bộ khi có mạng."
                : RuntimeConfigService.IsGoogleGatewayConfigured
                    ? "Đăng nhập Firebase hợp lệ = tự động được kết nối Google theo quyền ứng dụng. Không cần đăng nhập Google riêng trên từng laptop."
                    : "Kết nối Google dùng chung chưa được OWNER triển khai. Không lưu OAuth secret/refresh token trong EXE; phiếu vẫn được lưu local an toàn.";
        }
    }

    private static void SetupUpdaterPanel(Form form)
    {
        if (_updateUi is not null) return;
        var group = FindAll<GroupBox>(form).FirstOrDefault(x => x.Text == "Cập nhật phiên bản");
        if (group is null) return;

        foreach (Control child in group.Controls.Cast<Control>().ToArray()) child.Dispose();
        group.Controls.Clear();
        group.AutoSize = true;
        group.AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(12)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var current = new TextBox { ReadOnly = true, BackColor = Color.White, Dock = DockStyle.Top, Text = VersionUpdateService.CurrentVersionText };
        var status = new Label { AutoSize = true, Text = "Đang kiểm tra bản cập nhật...", Padding = new Padding(0, 8, 0, 8) };
        var location = new TextBox { ReadOnly = true, BackColor = Color.White, Dock = DockStyle.Top, Text = VersionUpdateService.CurrentExecutablePath };
        var actions = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, WrapContents = true };
        var update = new Button { Text = "Kiểm tra cập nhật", AutoSize = true, Height = 36, Padding = new Padding(10, 0, 10, 0) };
        var releases = new Button { Text = "Mở GitHub Releases", AutoSize = true, Height = 36, Padding = new Padding(10, 0, 10, 0) };
        releases.Click += (_, _) => VersionUpdateService.OpenReleasesPage();
        update.Click += async (_, _) => await UpdateActionAsync(form);
        actions.Controls.AddRange([update, releases]);

        AddRow(table, "Phiên bản hiện tại", current);
        AddRow(table, "Trạng thái", status);
        AddRow(table, "File đang chạy", location);
        AddRow(table, "Thao tác", actions);
        group.Controls.Add(table);

        var header = FindAll<Label>(form).FirstOrDefault(x => x.Text == VersionUpdateService.CurrentVersionText);
        _updateUi = new UpdateUiState { Status = status, Action = update, HeaderVersion = header };
    }

    private static void AddRow(TableLayoutPanel table, string label, Control control)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 8, 12, 12) }, 0, row);
        control.Margin = new Padding(3, 4, 3, 10);
        table.Controls.Add(control, 1, row);
    }

    private static async Task CheckForUpdatesAsync(Form form)
    {
        if (_updateCheckRunning || _updateUi is null || form.IsDisposed) return;
        _updateCheckRunning = true;
        try
        {
            _updateUi.Status.Text = "Đang kiểm tra GitHub Release...";
            _updateUi.Action.Enabled = false;
            var release = await VersionUpdateService.GetLatestAsync();
            _updateUi.Latest = release;
            if (release is null)
            {
                _updateUi.Status.Text = "Chưa có GitHub Release hợp lệ.";
                _updateUi.Action.Text = "Kiểm tra lại";
                _updateUi.Action.Enabled = true;
                return;
            }

            if (VersionUpdateService.IsNewer(release))
            {
                _updateUi.Status.Text = $"Có phiên bản mới {release.Tag}.";
                _updateUi.Action.Text = $"Cập nhật lên {release.Tag}";
                _updateUi.Action.Enabled = true;
                if (_updateUi.HeaderVersion is not null)
                    _updateUi.HeaderVersion.Text = $"{VersionUpdateService.CurrentVersionText} • Có bản mới {release.Tag}";
            }
            else
            {
                _updateUi.Status.Text = $"Đang dùng phiên bản mới nhất ({VersionUpdateService.CurrentVersionText}).";
                _updateUi.Action.Text = "Kiểm tra lại";
                _updateUi.Action.Enabled = true;
                if (_updateUi.HeaderVersion is not null)
                    _updateUi.HeaderVersion.Text = VersionUpdateService.CurrentVersionText;
            }
        }
        catch (Exception ex)
        {
            _updateUi.Status.Text = "Không kiểm tra được cập nhật: " + ex.Message;
            _updateUi.Action.Text = "Kiểm tra lại";
            _updateUi.Action.Enabled = true;
        }
        finally
        {
            _updateCheckRunning = false;
        }
    }

    private static async Task UpdateActionAsync(Form form)
    {
        if (_updateUi is null) return;
        if (_updateUi.Latest is null || !VersionUpdateService.IsNewer(_updateUi.Latest))
        {
            await CheckForUpdatesAsync(form);
            if (_updateUi.Latest is null || !VersionUpdateService.IsNewer(_updateUi.Latest)) return;
        }

        var release = _updateUi.Latest;
        var choice = MessageBox.Show(
            form,
            $"Có bản {release.Tag}.\n\nFile đang chạy:\n{VersionUpdateService.CurrentExecutablePath}\n\nYES: tải về và tự ghi đè đúng file này, sau đó ứng dụng tự khởi động lại.\nNO: không ghi đè; chọn nơi tải gói cập nhật xuống.\nCANCEL: bỏ qua.",
            "Cập nhật ứng dụng",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question);

        if (choice == DialogResult.Cancel) return;
        if (choice == DialogResult.No)
        {
            await SavePackageElsewhereAsync(form, release);
            return;
        }

        if (!VersionUpdateService.CanWriteCurrentDirectory(out var reason))
        {
            MessageBox.Show(
                form,
                "Thư mục đang chạy không đủ quyền ghi đè bằng tài khoản Windows hiện tại. Ứng dụng sẽ mở cửa sổ để chọn nơi tải bản cập nhật.\n\n" + reason,
                "Không đủ quyền ghi đè",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            await SavePackageElsewhereAsync(form, release);
            return;
        }

        try
        {
            _updateUi.Action.Enabled = false;
            var progress = new Progress<string>(text => _updateUi.Status.Text = text);
            var script = await VersionUpdateService.PrepareAutoUpdateAsync(release, progress);
            MessageBox.Show(
                form,
                "Đã tải và kiểm tra bản cập nhật. Ứng dụng sẽ thoát, ghi đè file EXE trong đúng thư mục hiện tại và tự mở lại.",
                "Sẵn sàng cập nhật",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            VersionUpdateService.LaunchUpdaterAndExit(script);
        }
        catch (UnauthorizedAccessException ex)
        {
            MessageBox.Show(form, ex.Message, "Không đủ quyền ghi đè", MessageBoxButtons.OK, MessageBoxIcon.Information);
            await SavePackageElsewhereAsync(form, release);
        }
        catch (Exception ex)
        {
            _updateUi.Status.Text = "Cập nhật chưa hoàn tất.";
            MessageBox.Show(form, ex.Message, "Không cập nhật được", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            if (!form.IsDisposed) _updateUi.Action.Enabled = true;
        }
    }

    private static async Task SavePackageElsewhereAsync(Form form, ReleaseInfo release)
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Chọn nơi tải bản cập nhật",
            Filter = "Gói cập nhật ZIP (*.zip)|*.zip",
            FileName = string.IsNullOrWhiteSpace(release.AssetName) ? $"PickfaceDamage1291-{release.Tag}-win-x64.zip" : release.AssetName,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(form) != DialogResult.OK) return;
        try
        {
            if (_updateUi is not null) _updateUi.Action.Enabled = false;
            var progress = new Progress<string>(text => { if (_updateUi is not null) _updateUi.Status.Text = text; });
            await VersionUpdateService.DownloadPackageAsync(release, dialog.FileName, progress);
            if (_updateUi is not null) _updateUi.Status.Text = "Đã tải bản cập nhật về máy.";
            MessageBox.Show(form, "Đã tải bản cập nhật tại:\n" + dialog.FileName, "Tải xuống hoàn tất", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(form, ex.Message, "Không tải được bản cập nhật", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            if (_updateUi is not null) _updateUi.Action.Enabled = true;
        }
    }

    private static void SetGoogleHeader(Control root, string text)
    {
        foreach (var label in FindAll<Label>(root))
        {
            if (label.Text.StartsWith("Google:", StringComparison.OrdinalIgnoreCase))
            {
                label.Text = text;
                return;
            }
        }
    }

    private static IEnumerable<T> FindAll<T>(Control root) where T : Control
    {
        if (root is T self) yield return self;
        foreach (Control child in root.Controls)
        foreach (var found in FindAll<T>(child))
            yield return found;
    }
}
