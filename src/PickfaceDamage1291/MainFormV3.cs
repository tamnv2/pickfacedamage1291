using System.Diagnostics;

namespace PickfaceDamage1291;

internal sealed class MainForm : Form
{
    private readonly TabControl _tabs = new();
    private readonly Label _headerCloud = new();
    private readonly Label _headerVersion = new();

    private readonly TextBox _sku = new();
    private readonly TextBox _productName = new();
    private readonly TextBox _location = new();
    private readonly DateTimePicker _date = new();
    private readonly TimeWheelPicker _time = new();
    private readonly ExclusiveShiftPicker _shift = new();
    private readonly NumericUpDown _quantity = new();
    private readonly TextBox _baseUnit = new();
    private readonly ListBox _images = new();
    private readonly PictureBox _imagePreview = new();
    private readonly Button _send = new();
    private readonly Label _entryStatus = new();
    private bool _skuValid;
    private string _draftId = Guid.NewGuid().ToString("D");
    private readonly List<string> _draftImages = [];
    private readonly HashSet<string> _draftImageHashes = new(StringComparer.OrdinalIgnoreCase);

    private readonly DataGridView _reportGrid = new();
    private readonly Label _reportStatus = new();
    private readonly DataGridView _productGrid = new();
    private readonly TextBox _productSearch = new();
    private readonly Label _productCount = new();

    private readonly Label _googleStatus = new();
    private readonly Label _firebaseStatus = new();
    private readonly Label _versionStatus = new();
    private readonly Button _updateButton = new();
    private ReleaseInfo? _availableRelease;

    public MainForm()
    {
        Text = "Cập nhật hư hỏng Pickface 1291";
        Width = 1280;
        Height = 860;
        MinimumSize = new Size(980, 680);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10F);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = SystemColors.Control };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(BuildHeader(), 0, 0);

        _tabs.Dock = DockStyle.Fill;
        _tabs.Padding = new Point(16, 7);
        _tabs.TabPages.Add(BuildDamageTab());
        _tabs.TabPages.Add(BuildReportsTab());
        _tabs.TabPages.Add(BuildProductsTab());
        _tabs.TabPages.Add(BuildSettingsTab());
        _tabs.SelectedIndexChanged += (_, _) => RefreshActiveTab();
        root.Controls.Add(_tabs, 0, 1);
        Controls.Add(root);

        ResetDraft();
        RefreshProducts();
        RefreshReports();
        RefreshSettings();
    }

    private Control BuildHeader()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 10, 20, 8), ColumnCount = 3, BackColor = Color.White };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));
        panel.Controls.Add(new Label
        {
            Text = "CẬP NHẬT HƯ HỎNG PICKFACE 1291",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 16F, FontStyle.Bold),
            Padding = new Padding(0, 7, 0, 0)
        }, 0, 0);
        _headerCloud.Dock = DockStyle.Fill;
        _headerCloud.TextAlign = ContentAlignment.MiddleRight;
        _headerVersion.Dock = DockStyle.Fill;
        _headerVersion.TextAlign = ContentAlignment.MiddleRight;
        panel.Controls.Add(_headerCloud, 1, 0);
        panel.Controls.Add(_headerVersion, 2, 0);
        return panel;
    }

    private TabPage BuildDamageTab()
    {
        var tab = NewTab("Nhập hư hỏng");
        var scroll = NewScrollFlow();
        scroll.Controls.Add(BuildProductSection());
        scroll.Controls.Add(BuildOccurrenceSection());
        scroll.Controls.Add(BuildImagesSection());
        scroll.Controls.Add(BuildSubmitSection());
        tab.Controls.Add(scroll);
        return tab;
    }

    private GroupBox BuildProductSection()
    {
        var box = NewSection("1. Thông tin sản phẩm");
        var form = NewFormTable(190);
        _sku.CharacterCasing = CharacterCasing.Upper;
        _sku.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            LookupSku();
            e.SuppressKeyPress = true;
        };
        _sku.Leave += (_, _) => { if (_sku.TextLength > 0) LookupSku(); };
        _productName.ReadOnly = true;
        _productName.BackColor = Color.White;
        _baseUnit.ReadOnly = true;
        _baseUnit.BackColor = Color.White;
        AddFormRow(form, "SKU *", _sku);
        AddFormRow(form, "Tên sản phẩm", _productName);
        AddFormRow(form, "Base Units", _baseUnit);
        box.Controls.Add(form);
        return box;
    }

    private GroupBox BuildOccurrenceSection()
    {
        var box = NewSection("2. Thông tin phát hiện hư hỏng");
        var form = NewFormTable(190);
        _date.Format = DateTimePickerFormat.Custom;
        _date.CustomFormat = "dd/MM/yyyy";
        _date.ShowUpDown = false;
        _location.Leave += (_, _) =>
        {
            if (LocationNormalizer.TryNormalize(_location.Text, out var value, out _)) _location.Text = value;
        };
        _shift.SelectedShiftChanged += (_, _) => SaveLastShift();
        _quantity.Minimum = 1;
        _quantity.Maximum = 999999999;
        _quantity.DecimalPlaces = 0;
        _quantity.Increment = 1;
        _quantity.ThousandsSeparator = true;
        AddFormRow(form, "Vị trí phát hiện *", _location);
        AddFormRow(form, "Ngày phát hiện *", _date);
        AddFormRow(form, "Giờ phát hiện *", _time);
        AddFormRow(form, "Ca *", _shift);
        AddFormRow(form, "Số lượng hư hỏng *", _quantity);
        box.Controls.Add(form);
        return box;
    }

    private GroupBox BuildImagesSection()
    {
        var box = NewSection("3. Hình ảnh hiện trạng — tối đa 5 ảnh");
        var layout = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, Padding = new Padding(12), ColumnCount = 2, RowCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
        var actions = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, WrapContents = true };
        var add = NewButton("+ Thêm ảnh");
        var remove = NewButton("Xoá ảnh đã chọn");
        add.Click += (_, _) => AddImages();
        remove.Click += (_, _) => RemoveSelectedImage();
        actions.Controls.AddRange([add, remove]);
        layout.Controls.Add(actions, 0, 0);
        layout.SetColumnSpan(actions, 2);
        _images.Dock = DockStyle.Fill;
        _images.SelectedIndexChanged += (_, _) => ShowSelectedImage();
        _imagePreview.Dock = DockStyle.Fill;
        _imagePreview.SizeMode = PictureBoxSizeMode.Zoom;
        _imagePreview.BorderStyle = BorderStyle.FixedSingle;
        _imagePreview.BackColor = Color.White;
        layout.Controls.Add(_images, 0, 1);
        layout.Controls.Add(_imagePreview, 1, 1);
        box.Controls.Add(layout);
        return box;
    }

    private GroupBox BuildSubmitSection()
    {
        var box = NewSection("4. Lưu và đồng bộ");
        var form = NewFormTable(190);

        _send.Text = "Gửi thông tin hư hỏng";
        _send.AutoSize = true;
        _send.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _send.MinimumSize = new Size(0, 40);
        _send.Padding = new Padding(14, 4, 14, 4);
        _send.Click += async (_, _) => await SaveAndSendAsync();

        _entryStatus.AutoSize = true;
        _entryStatus.MaximumSize = new Size(1000, 0);
        _entryStatus.Text = "Dữ liệu được lưu trên máy trước và tự đồng bộ khi kết nối sẵn sàng.";
        _entryStatus.Padding = new Padding(0, 7, 0, 7);

        AddFormRow(form, "Thao tác", _send);
        AddFormRow(form, "Trạng thái", _entryStatus);
        box.Controls.Add(form);
        return box;
    }

    private TabPage BuildReportsTab()
    {
        var tab = NewTab("Danh sách đã nhập");
        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), RowCount = 2, ColumnCount = 1 };
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var commands = NewSection("Công cụ danh sách");
        commands.Dock = DockStyle.Top;
        var top = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, Padding = new Padding(12), WrapContents = true };
        var refresh = NewButton("Làm mới");
        var sync = NewButton("Đồng bộ lại");
        var edit = NewButton("Chỉnh sửa phiếu (ADMIN)");
        var selectAll = NewButton("Chọn tất cả");
        var clearSelection = NewButton("Bỏ chọn");
        var delete = NewButton("Xoá phiếu đã chọn (ADMIN)");
        var export = NewButton("Xuất Excel — sẽ cập nhật logic");
        var isAdmin = AppSession.Current?.Profile.IsAdmin == true;

        refresh.Click += (_, _) => RefreshReports();
        sync.Click += async (_, _) => await SyncPendingAsync();
        edit.Click += async (_, _) => await EditSelectedReportAsync();
        selectAll.Click += (_, _) =>
        {
            _reportGrid.ClearSelection();
            foreach (DataGridViewRow row in _reportGrid.Rows) row.Selected = true;
        };
        clearSelection.Click += (_, _) => _reportGrid.ClearSelection();
        delete.Click += async (_, _) => await DeleteSelectedReportsAsync();
        export.Click += (_, _) => MessageBox.Show(this, "Chức năng xuất Excel đang giữ chỗ và sẽ cập nhật theo logic OWNER chốt sau.", "Thông báo");

        edit.Visible = isAdmin;
        selectAll.Visible = isAdmin;
        clearSelection.Visible = isAdmin;
        delete.Visible = isAdmin;
        _reportStatus.AutoSize = true;
        _reportStatus.Padding = new Padding(18, 9, 0, 0);
        top.Controls.AddRange([refresh, sync, edit, selectAll, clearSelection, delete, export, _reportStatus]);
        commands.Controls.Add(top);

        ConfigureGrid(_reportGrid);
        _reportGrid.MultiSelect = true;
        _reportGrid.KeyDown += (_, e) =>
        {
            if (!e.Control || e.KeyCode != Keys.A) return;
            foreach (DataGridViewRow row in _reportGrid.Rows) row.Selected = true;
            e.Handled = true;
            e.SuppressKeyPress = true;
        };

        outer.Controls.Add(commands, 0, 0);
        outer.Controls.Add(_reportGrid, 0, 1);
        tab.Controls.Add(outer);
        return tab;
    }

    private TabPage BuildProductsTab()
    {
        var tab = NewTab("Danh mục SKU");
        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), RowCount = 2, ColumnCount = 1 };
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var tools = NewSection("Cập nhật và tìm kiếm danh mục SKU");
        tools.Dock = DockStyle.Top;
        var top = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, Padding = new Padding(12), WrapContents = true };
        var import = NewButton("Cập nhật từ Excel");
        import.Click += async (_, _) => await ImportProductsAsync();
        _productSearch.Width = 360;
        _productSearch.PlaceholderText = "Tìm SKU hoặc tên sản phẩm...";
        _productSearch.Margin = new Padding(14, 5, 3, 3);
        _productSearch.TextChanged += (_, _) => RefreshProducts();
        _productCount.AutoSize = true;
        _productCount.Padding = new Padding(18, 9, 0, 0);
        top.Controls.AddRange([import, _productSearch, _productCount]);
        tools.Controls.Add(top);
        ConfigureGrid(_productGrid);
        outer.Controls.Add(tools, 0, 0);
        outer.Controls.Add(_productGrid, 0, 1);
        tab.Controls.Add(outer);
        return tab;
    }

    private TabPage BuildSettingsTab()
    {
        var tab = NewTab("Cài đặt");
        var scroll = NewScrollFlow();
        scroll.Controls.Add(BuildGoogleSettingsSection());
        scroll.Controls.Add(BuildFirebaseSettingsSection());
        scroll.Controls.Add(BuildUpdateSection());
        scroll.Controls.Add(BuildLocalDataSection());
        tab.Controls.Add(scroll);
        return tab;
    }

    private GroupBox BuildGoogleSettingsSection()
    {
        var box = NewSection("Google Drive / Google Sheet — phạm vi cố định");
        var panel = NewFormTable(220);
        _googleStatus.AutoSize = true;
        _googleStatus.MaximumSize = new Size(950, 0);
        var connect = NewButton("Kết nối Google");
        var verify = NewButton("Kiểm tra Drive / Sheet");
        var openDrive = NewButton("Mở thư mục Google Drive");
        connect.Click += async (_, _) => await ConnectGoogleAsync();
        verify.Click += async (_, _) => await VerifyGoogleAsync();
        openDrive.Click += (_, _) => OpenDriveFolder();
        var actions = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, WrapContents = true };
        actions.Controls.AddRange([connect, verify, openDrive]);
        AddFormRow(panel, "Google Cloud project", ReadOnlyText(CloudConfig.GoogleCloudProjectId));
        AddFormRow(panel, "Drive root", ReadOnlyText("CẬP NHẬT HƯ HỎNG PICKFACE — " + CloudConfig.DriveRootFolderId));
        AddFormRow(panel, "Trạng thái", _googleStatus);
        AddFormRow(panel, "Thao tác", actions);
        box.Controls.Add(panel);
        return box;
    }

    private GroupBox BuildFirebaseSettingsSection()
    {
        var box = NewSection("Tài khoản / phiên làm việc — Firebase Spark");
        var panel = NewFormTable(220);
        _firebaseStatus.AutoSize = true;
        _firebaseStatus.MaximumSize = new Size(950, 0);
        AddFormRow(panel, "Project", ReadOnlyText(CloudConfig.GoogleCloudProjectId));
        AddFormRow(panel, "Trạng thái", _firebaseStatus);
        AddFormRow(panel, "Ghi chú", new Label
        {
            AutoSize = true,
            MaximumSize = new Size(950, 0),
            Text = "Firebase chỉ dùng Authentication + Realtime Database theo Spark/no-cost. USER nhập hư hỏng dùng active_operator realtime + lease offline 30 phút; ADMIN không chiếm/kick USER. Không tự nâng Blaze hoặc gắn billing."
        });
        box.Controls.Add(panel);
        return box;
    }

    private GroupBox BuildUpdateSection()
    {
        var box = NewSection("Cập nhật phiên bản");
        var panel = NewFormTable(220);
        _versionStatus.AutoSize = true;
        _versionStatus.MaximumSize = new Size(950, 0);
        _updateButton.Text = "Kiểm tra cập nhật";
        _updateButton.AutoSize = true;
        _updateButton.Padding = new Padding(8, 0, 8, 0);
        _updateButton.Click += async (_, _) => await UpdateButtonAsync();
        var open = NewButton("Mở GitHub Releases");
        open.Click += (_, _) => VersionUpdateService.OpenReleasesPage();
        var actions = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, WrapContents = true };
        actions.Controls.AddRange([_updateButton, open]);
        AddFormRow(panel, "Phiên bản hiện tại", ReadOnlyText(VersionUpdateService.CurrentVersionText));
        AddFormRow(panel, "Trạng thái", _versionStatus);
        AddFormRow(panel, "Thao tác", actions);
        box.Controls.Add(panel);
        return box;
    }

    private GroupBox BuildLocalDataSection()
    {
        var box = NewSection("Dữ liệu local trên laptop");
        var panel = NewFormTable(220);
        var open = NewButton("Mở thư mục dữ liệu local");
        open.Click += (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.Root) { UseShellExecute = true });
        AddFormRow(panel, "Thư mục", ReadOnlyText(AppPaths.Root));
        AddFormRow(panel, "Thao tác", open);
        AddFormRow(panel, "Nguyên tắc", new Label
        {
            AutoSize = true,
            MaximumSize = new Size(950, 0),
            Text = "SQLite, ảnh pending và token thực tế chỉ nằm trên laptop người dùng. Dữ liệu/credential thực tế không được commit vào repository public."
        });
        box.Controls.Add(panel);
        return box;
    }

    private FlowLayoutPanel NewScrollFlow()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(18)
        };
        panel.SizeChanged += (_, _) => ResizeFlowChildren(panel);
        return panel;
    }

    private static TabPage NewTab(string title) => new(title) { BackColor = Color.WhiteSmoke };

    private static GroupBox NewSection(string title) => new()
    {
        Text = title,
        Width = 900,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Margin = new Padding(0, 0, 0, 18),
        Padding = new Padding(12),
        Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold)
    };

    private static TableLayoutPanel NewFormTable(int labelWidth)
    {
        var panel = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Padding = new Padding(12, 10, 12, 10),
            ColumnCount = 2,
            RowCount = 0,
            Font = new Font("Segoe UI", 10F)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelWidth));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return panel;
    }

    private static TextBox ReadOnlyText(string value) => new() { Text = value, ReadOnly = true, BackColor = Color.White, Dock = DockStyle.Top };

    private static Button NewButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        MinimumSize = new Size(0, 42),
        Padding = new Padding(14, 6, 14, 6),
        Margin = new Padding(4, 4, 8, 4)
    };

    private static void ResizeFlowChildren(FlowLayoutPanel panel)
    {
        var width = Math.Max(520, panel.ClientSize.Width - panel.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 12);
        foreach (Control child in panel.Controls) child.Width = width;
    }

    private static void ConfigureGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.BackgroundColor = Color.White;
        grid.BorderStyle = BorderStyle.Fixed3D;
        grid.RowTemplate.Height = 34;
        grid.ColumnHeadersHeight = 38;
    }

    private static void AddFormRow(TableLayoutPanel panel, string label, Control control)
    {
        var row = panel.RowCount++;
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var title = new Label { Text = label, AutoSize = true, Padding = new Padding(0, 8, 12, 14), Margin = new Padding(0, 4, 0, 4), Font = new Font("Segoe UI", 10F) };
        control.Margin = new Padding(3, 4, 3, 12);
        if (control.Dock == DockStyle.None && control is not FlowLayoutPanel && control is not TimeWheelPicker && control is not ExclusiveShiftPicker) control.Dock = DockStyle.Top;
        panel.Controls.Add(title, 0, row);
        panel.Controls.Add(control, 1, row);
    }

    private void LookupSku()
    {
        var sku = ExcelImportService.Clean(_sku.Text).ToUpperInvariant();
        _sku.Text = sku;
        var product = sku.Length == 0 ? null : Database.GetProduct(sku);
        if (product is null)
        {
            _skuValid = false;
            _productName.Text = sku.Length == 0 ? string.Empty : "Sản phẩm không có trong cơ sở dữ liệu, hãy kiểm tra lại.";
            _productName.ForeColor = Color.Firebrick;
            _baseUnit.Clear();
            return;
        }
        _skuValid = true;
        _productName.ForeColor = SystemColors.WindowText;
        _productName.Text = product.ProductName;
        _baseUnit.Text = product.BaseUnit;
    }

    private void AddImages()
    {
        if (_draftImages.Count >= 5)
        {
            MessageBox.Show(this, "Mỗi phiếu chỉ cho phép tối đa 5 ảnh.", "Giới hạn ảnh");
            return;
        }
        using var dialog = new OpenFileDialog { Filter = "Hình ảnh|*.jpg;*.jpeg;*.png;*.heic;*.webp", Multiselect = true, Title = "Chọn ảnh hàng hư hỏng" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        foreach (var source in dialog.FileNames)
        {
            if (_draftImages.Count >= 5) break;
            string hash;
            try { hash = ImageHashService.Sha256File(source); }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Không đọc được ảnh {Path.GetFileName(source)}: {ex.Message}", "Lỗi ảnh", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                continue;
            }
            if (!_draftImageHashes.Add(hash))
            {
                MessageBox.Show(this, $"Ảnh này đã được thêm và sẽ bỏ qua:\n{Path.GetFileName(source)}", "Ảnh trùng", MessageBoxButtons.OK, MessageBoxIcon.Information);
                continue;
            }
            var folder = AppPaths.GetDraftImageFolder(_draftId);
            var ext = Path.GetExtension(source).ToLowerInvariant();
            var target = Path.Combine(folder, $"{Guid.NewGuid():N}_{hash[..12]}{ext}");
            File.Copy(source, target, false);
            _draftImages.Add(target);
        }
        RefreshImageList();
    }

    private void RemoveSelectedImage()
    {
        var index = _images.SelectedIndex;
        if (index < 0 || index >= _draftImages.Count) return;
        var path = _draftImages[index];
        _draftImages.RemoveAt(index);
        try { if (File.Exists(path)) _draftImageHashes.Remove(ImageHashService.Sha256File(path)); } catch { }
        try { if (File.Exists(path)) File.Delete(path); } catch { }
        _imagePreview.Image?.Dispose();
        _imagePreview.Image = null;
        RefreshImageList();
    }

    private void RefreshImageList()
    {
        _images.Items.Clear();
        for (var i = 0; i < _draftImages.Count; i++) _images.Items.Add($"Ảnh {i + 1}: {Path.GetFileName(_draftImages[i])}");
        if (_images.Items.Count > 0) _images.SelectedIndex = 0;
    }

    private void ShowSelectedImage()
    {
        _imagePreview.Image?.Dispose();
        _imagePreview.Image = null;
        var idx = _images.SelectedIndex;
        if (idx < 0 || idx >= _draftImages.Count || !File.Exists(_draftImages[idx])) return;
        try { using var source = Image.FromFile(_draftImages[idx]); _imagePreview.Image = new Bitmap(source); } catch { }
    }

    private async Task SaveAndSendAsync()
    {
        LookupSku();
        if (!_skuValid) { _sku.Focus(); return; }
        if (!LocationNormalizer.TryNormalize(_location.Text, out var location, out var locationError))
        {
            MessageBox.Show(this, locationError, "Kiểm tra vị trí", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _location.Focus();
            return;
        }
        _location.Text = location;
        if (string.IsNullOrWhiteSpace(_shift.SelectedShift))
        {
            MessageBox.Show(this, "Chưa chọn ca.", "Thiếu thông tin");
            return;
        }
        if (_quantity.Value < 1)
        {
            MessageBox.Show(this, "Số lượng hư hỏng phải là số nguyên lớn hơn 0.", "Thiếu thông tin");
            return;
        }

        var report = new DamageReport(
            _draftId, _date.Value.Date, _time.Hour, _time.Minute, _shift.SelectedShift,
            _sku.Text.Trim(), _productName.Text, location, decimal.Truncate(_quantity.Value), _baseUnit.Text,
            DateTime.Now, "PENDING", null, null,
            AppSession.Current?.Profile.Username ?? string.Empty, 1, null, null);

        var duplicateId = Database.FindExactDuplicateReport(report, _draftImages);
        if (!string.IsNullOrWhiteSpace(duplicateId))
        {
            MessageBox.Show(
                this,
                "Toàn bộ thông tin và hình ảnh của phiếu này trùng hoàn toàn với một phiếu đã có. Hệ thống không tạo thêm bản ghi trùng.\n\nID phiếu đã có: " + duplicateId,
                "Phát hiện phiếu trùng",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        _send.Enabled = false;
        try
        {
            Database.SaveDamageReport(report, _draftImages);
            try
            {
                if (AppSession.Current is { } session)
                    await FirebaseClient.AppendAuditAsync(session, "DAMAGE_REPORT_CREATED", new
                    {
                        report_id = report.ReportId,
                        sku = report.Sku,
                        location = report.Location,
                        quantity = decimal.Truncate(report.Quantity),
                        shift = report.Shift,
                        image_count = _draftImages.Count
                    }, AppSession.OperatorManager?.SessionId);
            }
            catch { }

            var synced = false;
            if (GoogleService.IsConnected())
            {
                try
                {
                    _entryStatus.Text = "Đã lưu local. Đang đồng bộ Google...";
                    await GoogleService.SyncReportAsync(report);
                    synced = true;
                }
                catch (DuplicateReportException ex)
                {
                    SyncCacheStore.RemoveLocalDuplicate(report.ReportId, ex.ExistingReportId);
                    MessageBox.Show(this, ex.Message + "\n\nBản ghi local vừa tạo đã được loại để không phát sinh dữ liệu trùng.", "Không tạo phiếu trùng", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (SyncConflictException ex)
                {
                    MessageBox.Show(this, ex.Message, "Xung đột đồng bộ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Phiếu đã được lưu an toàn trên laptop nhưng chưa đồng bộ được Google.\n\n" + ex.Message, "Đã lưu local - chưa đồng bộ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            if (synced)
                MessageBox.Show(this, "Đã lưu và đồng bộ Google thành công.", "Hoàn tất", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else if (!GoogleService.IsConnected())
                MessageBox.Show(this, "Đã lưu trên laptop. Google chưa kết nối/đang offline nên phiếu chờ đồng bộ.", "Đã lưu local", MessageBoxButtons.OK, MessageBoxIcon.Information);
            ResetDraft();
            RefreshReports();
            RefreshSettings();
        }
        finally
        {
            _send.Enabled = true;
            _entryStatus.Text = "Dữ liệu được lưu trên laptop trước. Khi mất mạng hoặc Google tạm lỗi, phiếu được giữ local để đồng bộ lại khi online.";
        }
    }

    private void ResetDraft()
    {
        _draftId = Guid.NewGuid().ToString("D");
        _draftImages.Clear();
        _draftImageHashes.Clear();
        _sku.Clear();
        _skuValid = false;
        _productName.Clear();
        _productName.ForeColor = SystemColors.WindowText;
        _location.Clear();
        _baseUnit.Clear();
        _date.Value = DateTime.Today;
        _time.SetValue(DateTime.Now.Hour, DateTime.Now.Minute);
        _quantity.Value = 1;
        var settings = SettingsStore.Load();
        var key = AppSession.Current?.Uid ?? "local";
        var last = settings.LastShiftByUser is not null && settings.LastShiftByUser.TryGetValue(key, out var saved) ? saved : settings.LastShift;
        _shift.SetShift(last);
        _images.Items.Clear();
        _imagePreview.Image?.Dispose();
        _imagePreview.Image = null;
        _sku.Focus();
    }

    private void SaveLastShift()
    {
        var selected = _shift.SelectedShift;
        if (string.IsNullOrWhiteSpace(selected)) return;
        var settings = SettingsStore.Load();
        settings.LastShiftByUser ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        settings.LastShiftByUser[AppSession.Current?.Uid ?? "local"] = selected;
        settings.LastShift = selected;
        SettingsStore.Save(settings);
    }

    private void RefreshReports()
    {
        var rows = Database.GetReports(int.MaxValue);
        _reportGrid.Columns.Clear();
        _reportGrid.Rows.Clear();
        _reportGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ReportId", HeaderText = "ID", Visible = false });
        foreach (var (name, header, weight) in new[]
        {
            ("Date","Ngày",60), ("Time","Giờ",45), ("Shift","Ca",45), ("Sku","SKU",65),
            ("Name","Tên sản phẩm",150), ("Location","Vị trí",85), ("Qty","SL",45), ("Base","Base",45),
            ("Version","Ver",38), ("Status","Trạng thái",70), ("Editor","Cập nhật bởi",70), ("Error","Lỗi",110)
        }) _reportGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = header, FillWeight = weight });
        foreach (var r in rows)
            _reportGrid.Rows.Add(r.ReportId, r.OccurredDate.ToString("dd/MM/yyyy"), $"{r.Hour:00}:{r.Minute:00}", r.Shift, r.Sku, r.ProductName, r.Location, decimal.Truncate(r.Quantity).ToString("0"), r.BaseUnit, r.Version, DisplayStatus(r.SyncStatus), r.UpdatedBy ?? r.CreatedBy, r.LastError ?? "");
        var pending = rows.Count(x => x.SyncStatus != "SYNCED");
        _reportStatus.Text = $"Tổng {rows.Count:N0} | Chờ/lỗi đồng bộ: {pending:N0}";
    }

    private async Task EditSelectedReportAsync()
    {
        var session = AppSession.Current;
        if (session is null || !session.Profile.IsAdmin)
        {
            MessageBox.Show(this, "Chỉ ADMIN được chỉnh sửa phiếu đã nhập.", "Không có quyền");
            return;
        }
        if (_reportGrid.SelectedRows.Count == 0)
        {
            MessageBox.Show(this, "Chọn một phiếu cần chỉnh sửa.", "Chưa chọn phiếu");
            return;
        }
        var id = Convert.ToString(_reportGrid.SelectedRows[0].Cells["ReportId"].Value) ?? string.Empty;
        var before = Database.GetReportById(id);
        if (before is null) { MessageBox.Show(this, "Không tìm thấy phiếu local.", "Dữ liệu đã thay đổi"); return; }
        var beforeImages = Database.GetImages(id);
        using var dialog = new DamageReportEditDialog(before, beforeImages);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var updated = Database.UpdateDamageReport(dialog.UpdatedReport, dialog.Images, session.Profile.Username);
            try
            {
                await FirebaseClient.AppendAuditAsync(session, "DAMAGE_REPORT_UPDATED", new
                {
                    report_id = id,
                    before = new { before.OccurredDate, before.Hour, before.Minute, before.Shift, before.Sku, before.ProductName, before.Location, before.Quantity, before.BaseUnit, before.Version, images = beforeImages.Count },
                    after = new { updated.OccurredDate, updated.Hour, updated.Minute, updated.Shift, updated.Sku, updated.ProductName, updated.Location, updated.Quantity, updated.BaseUnit, updated.Version, images = dialog.Images.Count }
                });
            }
            catch (Exception auditEx)
            {
                MessageBox.Show(this, "Phiếu local đã sửa nhưng chưa ghi được audit Firebase. Phiếu vẫn chờ đồng bộ Google.\n\n" + auditEx.Message, "Cảnh báo audit", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            if (GoogleService.IsConnected())
            {
                try { await GoogleService.SyncReportAsync(updated); }
                catch (Exception ex) { MessageBox.Show(this, "Đã lưu thay đổi local, nhưng Google chưa đồng bộ.\n\n" + ex.Message, "Chờ đồng bộ", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
            RefreshReports();
            MessageBox.Show(this, $"Đã lưu thay đổi. Phiên bản phiếu: {updated.Version}.", "Đã cập nhật", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Không sửa được phiếu", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task DeleteSelectedReportsAsync()
    {
        var session = AppSession.Current;
        if (session is null || !session.Profile.IsAdmin)
        {
            MessageBox.Show(this, "Chỉ ADMIN được xoá phiếu đã nhập.", "Không có quyền", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var ids = _reportGrid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(row => Convert.ToString(row.Cells["ReportId"].Value) ?? string.Empty)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (ids.Count == 0)
        {
            MessageBox.Show(this, "Chọn ít nhất một phiếu cần xoá. Có thể dùng Ctrl/Shift hoặc nút Chọn tất cả.", "Chưa chọn phiếu", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!GoogleService.IsConnected())
        {
            MessageBox.Show(
                this,
                "Xoá phiếu cần kết nối Google để ghi dấu đã xoá trước, tránh dữ liệu quay lại ở lần đồng bộ sau.",
                "Chưa thể xoá an toàn",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var reports = ids.Select(Database.GetReportById).Where(x => x is not null).Cast<DamageReport>().ToList();
        if (reports.Count == 0)
        {
            RefreshReports();
            MessageBox.Show(this, "Các phiếu đã chọn không còn tồn tại trên máy.", "Dữ liệu đã thay đổi");
            return;
        }

        var confirm = MessageBox.Show(
            this,
            $"Xoá {reports.Count:N0} phiếu đã chọn khỏi danh sách?\n\nGoogle sẽ được ghi dấu đã xoá để dữ liệu không tự xuất hiện lại khi đồng bộ. Hành động này được ghi vào lịch sử.",
            "Xác nhận xoá phiếu",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        using var passwordDialog = new AdminPasswordConfirmDialog(reports.Count);
        if (passwordDialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            UseWaitCursor = true;
            await AccountSelfService.VerifyCurrentPasswordAsync(session, passwordDialog.Password);
        }
        catch (Exception ex)
        {
            UseWaitCursor = false;
            MessageBox.Show(this, ex.Message, "Không xác minh được mật khẩu", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        AppPaths.BackupDatabase();
        var deleted = new List<DamageReport>();
        var failures = new List<string>();
        try
        {
            foreach (var report in reports)
            {
                try
                {
                    await GoogleService.MarkReportDeletedAsync(report, session.Profile.Username);
                    if (Database.DeleteDamageReport(report.ReportId, session.Profile.Username, remoteTombstoned: true))
                        deleted.Add(report);
                }
                catch (Exception ex)
                {
                    failures.Add($"{report.Sku} / {report.ReportId[..Math.Min(8, report.ReportId.Length)]}: {ex.Message}");
                }
            }

            if (deleted.Count > 0)
            {
                try
                {
                    await FirebaseClient.AppendAuditAsync(session, "DAMAGE_REPORTS_DELETED", new
                    {
                        count = deleted.Count,
                        report_ids = deleted.Take(100).Select(x => x.ReportId).ToArray(),
                        ids_truncated = deleted.Count > 100,
                        skus = deleted.Take(100).Select(x => x.Sku).ToArray(),
                        google_tombstone = true
                    });
                }
                catch { }
            }
        }
        finally
        {
            UseWaitCursor = false;
            RefreshReports();
        }

        if (failures.Count == 0)
        {
            MessageBox.Show(this, $"Đã xoá {deleted.Count:N0} phiếu. Google đã được ghi dấu xoá để chống khôi phục lại dữ liệu cũ.", "Đã xoá", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var detail = string.Join("\n", failures.Take(8));
        if (failures.Count > 8) detail += $"\n... và {failures.Count - 8:N0} phiếu khác.";
        MessageBox.Show(
            this,
            $"Đã xoá {deleted.Count:N0}/{reports.Count:N0} phiếu. Các phiếu lỗi vẫn được giữ nguyên để tránh mất dữ liệu không đồng bộ.\n\n{detail}",
            "Xoá chưa hoàn tất",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    private static string DisplayStatus(string status) => status switch
    {
        "SYNCED" => "Đã đồng bộ",
        "SYNCING" => "Đang đồng bộ",
        "ERROR" => "Lỗi đồng bộ",
        "OFFLINE_PENDING" => "Chờ online",
        "CONFLICT" => "Xung đột",
        _ => "Chờ đồng bộ"
    };

    private async Task SyncPendingAsync()
    {
        if (!GoogleService.IsConnected())
        {
            MessageBox.Show(this, "Chưa kết nối Google dùng chung. Dữ liệu local vẫn được giữ an toàn.", "Chưa kết nối");
            return;
        }
        try
        {
            _reportStatus.Text = "Đang đồng bộ hai chiều...";
            var progress = new Progress<string>(s => _reportStatus.Text = s);
            var summary = await CloudSyncService.SyncNowAsync(progress);
            MessageBox.Show(this,
                $"Đồng bộ hoàn tất.\n\nNhận phiếu: {summary.ReportsPulled:N0}\nGửi phiếu: {summary.ReportsPushed:N0}\nXoá nhận từ máy khác: {summary.ReportsDeleted:N0}\nSKU nhận: {summary.ProductsPulled:N0}\nXung đột: {summary.ReportConflicts:N0}",
                "Hoàn tất", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppLog.Exception("MANUAL_SYNC_FAILED", ex);
            MessageBox.Show(this, ex.Message, "Đồng bộ chưa hoàn tất", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { RefreshReports(); RefreshProducts(); RefreshSettings(); }
    }

    private async Task ImportProductsAsync()
    {
        using var dialog = new OpenFileDialog { Filter = "Excel Workbook (*.xlsx)|*.xlsx", Title = "Chọn file REPORT_BIN_INVENTORY" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            Cursor = Cursors.WaitCursor;
            var preview = await Task.Run(() => ExcelImportService.Analyze(dialog.FileName));
            Cursor = Cursors.Default;
            var sourceWarning = preview.SourceConflicts.Count == 0 ? string.Empty : $"\n\nCó {preview.SourceConflicts.Count} SKU mâu thuẫn ngay trong file nguồn; các SKU này sẽ KHÔNG được nhập.";
            var summary = $"Tổng dòng dữ liệu: {preview.TotalRows:N0}\nSKU hợp lệ duy nhất: {preview.UniqueSkus:N0}\nDòng trùng cùng SKU/Tên/Base: {preview.DuplicateRows:N0}\nSKU mới: {preview.NewProducts.Count:N0}\nKhông thay đổi: {preview.UnchangedProducts.Count:N0}\nSKU thay đổi cần Owner quyết định: {preview.Conflicts.Count:N0}\nDòng/SKU lỗi: {preview.InvalidRows:N0}" + sourceWarning;
            if (MessageBox.Show(this, summary + "\n\nTiếp tục?", "Kiểm tra file trước khi cập nhật", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes) return;
            if (preview.Conflicts.Count > 0)
            {
                using var conflicts = new ConflictResolutionForm(preview.Conflicts);
                if (conflicts.ShowDialog(this) != DialogResult.OK) return;
            }
            if (preview.SourceConflicts.Count > 0)
            {
                var detail = string.Join("\n", preview.SourceConflicts.Take(20));
                if (preview.SourceConflicts.Count > 20) detail += $"\n... và {preview.SourceConflicts.Count - 20} SKU khác.";
                MessageBox.Show(this, "Các SKU sau bị bỏ qua vì file nguồn tự mâu thuẫn:\n\n" + detail, "Cảnh báo file nguồn", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            Cursor = Cursors.WaitCursor;
            await Task.Run(() => Database.ApplyImport(preview));
            SyncCacheStore.MarkProductsDirty();
            Cursor = Cursors.Default;
            var catalogSynced = false;
            if (AppSession.Current is { } session)
            {
                try { await FirebaseClient.AppendAuditAsync(session, "SKU_IMPORT", new { file = Path.GetFileName(dialog.FileName), total = preview.TotalRows, unique = preview.UniqueSkus, added = preview.NewProducts.Count, conflicts = preview.Conflicts.Count }); } catch { }
                if (session.Profile.IsAdmin && GoogleService.IsConnected())
                {
                    try
                    {
                        await CloudSyncService.PushLocalProductCatalogAsync(new Progress<string>(s => _productCount.Text = s));
                        catalogSynced = true;
                    }
                    catch (Exception syncEx)
                    {
                        AppLog.Exception("PRODUCT_CATALOG_PUSH_FAILED", syncEx);
                    }
                }
            }
            MessageBox.Show(this,
                catalogSynced
                    ? "Đã cập nhật danh mục SKU local và Google Sheet dùng chung. SKU cũ không có trong file mới KHÔNG bị xoá."
                    : "Đã cập nhật danh mục SKU local. Thay đổi đang được giữ để ADMIN đồng bộ lên Google khi online. SKU cũ không có trong file mới KHÔNG bị xoá.",
                "Hoàn tất");
            RefreshProducts();
        }
        catch (Exception ex) { Cursor = Cursors.Default; MessageBox.Show(this, ex.Message, "Không thể cập nhật danh mục SKU", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void RefreshProducts()
    {
        var rows = Database.GetProducts(_productSearch.Text, 1000);
        _productGrid.Columns.Clear();
        _productGrid.Rows.Clear();
        _productGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Sku", HeaderText = "SKU", FillWeight = 70 });
        _productGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Tên sản phẩm", FillWeight = 210 });
        _productGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Base", HeaderText = "Base Units", FillWeight = 60 });
        _productGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Seen", HeaderText = "Lần gần nhất thấy", FillWeight = 90 });
        _productGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Source", HeaderText = "File nguồn", FillWeight = 120 });
        foreach (var p in rows) _productGrid.Rows.Add(p.Sku, p.ProductName, p.BaseUnit, p.LastSeenAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"), p.SourceFile);
        _productCount.Text = $"Database: {Database.GetProductCount():N0} SKU" + (rows.Count >= 1000 ? " | hiển thị tối đa 1.000 dòng" : "");
    }

    private async Task ConnectGoogleAsync()
    {
        if (!GoogleService.IsClientConfigured())
        {
            MessageBox.Show(this, "Build hiện tại chưa có Google OAuth Desktop Client ID.", "Thiếu cấu hình", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            _googleStatus.Text = "Đang chờ đăng nhập Google trên trình duyệt...";
            await GoogleService.AuthorizeAsync();
            RefreshSettings();
            MessageBox.Show(this, "Kết nối Google, kiểm tra refresh token và xác minh Drive cố định thành công.", "Hoàn tất");
        }
        catch (Exception ex) { RefreshSettings(); MessageBox.Show(this, ex.Message, "Không kết nối được Google", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private async Task VerifyGoogleAsync()
    {
        if (!GoogleService.IsConnected())
        {
            MessageBox.Show(this, GoogleService.NeedsReconnect() ? "Phiên Google cũ không còn tương thích. Bấm Kết nối Google để đăng nhập lại." : "Chưa kết nối Google trên laptop này.", "Chưa kết nối");
            return;
        }
        try
        {
            _googleStatus.Text = "Đang xác minh Drive root / thư mục ảnh / Google Sheet...";
            await GoogleService.ProvisionAsync();
            RefreshSettings();
            MessageBox.Show(this, "Đã xác minh đúng phạm vi Drive và cập nhật header Sheet.", "Google đã sẵn sàng");
        }
        catch (Exception ex) { RefreshSettings(); MessageBox.Show(this, ex.Message, "Không thể xác minh Google", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void RefreshSettings()
    {
        var connected = GoogleService.IsConnected();
        var clientReady = GoogleService.IsClientConfigured();
        var reconnect = GoogleService.NeedsReconnect();
        _googleStatus.Text = !clientReady
            ? "Chưa cấu hình OAuth Desktop Client ID."
            : reconnect
                ? "Có phiên Google cũ/khác Client ID. Cần bấm Kết nối Google và cấp quyền lại một lần."
                : connected
                    ? "Đã kết nối Google và token thuộc đúng Desktop Client hiện tại. Drive root khóa theo ID cố định."
                    : "OAuth Desktop Client ID đã có, laptop này chưa kết nối Google.";
        var firebaseReady = !string.IsNullOrWhiteSpace(CloudConfig.FirebaseApiKey) && !string.IsNullOrWhiteSpace(CloudConfig.FirebaseDatabaseUrl);
        _firebaseStatus.Text = firebaseReady
            ? $"Đã cấu hình Firebase. Phiên hiện tại: {AppSession.Current?.Profile.Username ?? "-"} ({AppSession.Current?.Profile.Role.ToUpperInvariant() ?? "-"})."
            : "Chưa có Firebase API key / Realtime Database URL của project pickface-damage-1291.";
        _versionStatus.Text = _availableRelease is null ? "Chưa kiểm tra bản mới." : VersionUpdateService.IsNewer(_availableRelease) ? $"Có bản mới {_availableRelease.Tag}." : $"Đang dùng bản mới nhất ({VersionUpdateService.CurrentVersionText}).";
        _updateButton.Text = _availableRelease is not null && VersionUpdateService.IsNewer(_availableRelease) ? $"Cập nhật lên {_availableRelease.Tag}" : "Kiểm tra cập nhật";
        _headerCloud.Text = connected ? "Google: đã kết nối" : reconnect ? "Google: cần kết nối lại" : "Google: local/offline";
        _headerVersion.Text = VersionUpdateService.CurrentVersionText;
    }

    private void OpenDriveFolder() => Process.Start(new ProcessStartInfo($"https://drive.google.com/drive/folders/{CloudConfig.DriveRootFolderId}") { UseShellExecute = true });

    private async Task UpdateButtonAsync()
    {
        try
        {
            _updateButton.Enabled = false;
            if (_availableRelease is null || !VersionUpdateService.IsNewer(_availableRelease))
            {
                _versionStatus.Text = "Đang kiểm tra GitHub Release...";
                _availableRelease = await VersionUpdateService.GetLatestAsync();
                if (_availableRelease is null) { _versionStatus.Text = "Chưa có GitHub Release hợp lệ để cập nhật tự động."; return; }
                RefreshSettings();
                if (!VersionUpdateService.IsNewer(_availableRelease)) { MessageBox.Show(this, "Ứng dụng đang ở phiên bản mới nhất.", "Cập nhật phiên bản"); return; }
                var notes = string.IsNullOrWhiteSpace(_availableRelease.Notes) ? "Không có ghi chú phát hành." : _availableRelease.Notes;
                if (notes.Length > 2500) notes = notes[..2500] + "...";
                if (MessageBox.Show(this, $"Có phiên bản {_availableRelease.Tag}.\n\n{notes}\n\nTải và cập nhật ngay?", "Có bản cập nhật", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes) return;
            }
            var progress = new Progress<string>(text => _versionStatus.Text = text);
            var script = await VersionUpdateService.DownloadAndPrepareAsync(_availableRelease, progress);
            _versionStatus.Text = "Đã tải xong. Ứng dụng sẽ đóng, thay phiên bản và mở lại.";
            MessageBox.Show(this, "Bản cập nhật đã tải xong. Ứng dụng sẽ đóng và mở lại tự động. Nếu Windows/EDR chặn thay EXE, bản hiện tại vẫn được giữ.", "Sẵn sàng cập nhật", MessageBoxButtons.OK, MessageBoxIcon.Information);
            VersionUpdateService.LaunchUpdaterAndExit(script);
        }
        catch (Exception ex)
        {
            _versionStatus.Text = "Cập nhật tự động chưa hoàn tất.";
            MessageBox.Show(this, ex.Message + "\n\nCó thể dùng nút Mở GitHub Releases để tải thủ công.", "Không thể tự cập nhật", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { _updateButton.Enabled = true; }
    }

    private void RefreshActiveTab()
    {
        var text = _tabs.SelectedTab?.Text;
        if (text == "Danh sách đã nhập") RefreshReports();
        else if (text == "Danh mục SKU") RefreshProducts();
        else if (text == "Cài đặt") RefreshSettings();
    }
}
