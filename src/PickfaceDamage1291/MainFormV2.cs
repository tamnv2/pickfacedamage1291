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
    private readonly NumericUpDown _hour = new();
    private readonly NumericUpDown _minute = new();
    private readonly ComboBox _shift = new();
    private readonly NumericUpDown _quantity = new();
    private readonly TextBox _baseUnit = new();
    private readonly ListBox _images = new();
    private readonly PictureBox _imagePreview = new();
    private readonly Button _send = new();
    private readonly Label _entryStatus = new();
    private bool _skuValid;
    private string _draftId = Guid.NewGuid().ToString("D");
    private readonly List<string> _draftImages = [];

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
        Width = 1240;
        Height = 820;
        MinimumSize = new Size(1040, 700);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = SystemColors.Control
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(BuildHeader(), 0, 0);

        _tabs.Dock = DockStyle.Fill;
        _tabs.Padding = new Point(18, 7);
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
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 12, 20, 10),
            ColumnCount = 3,
            BackColor = Color.White
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));

        var title = new Label
        {
            Text = "CẬP NHẬT HƯ HỎNG PICKFACE 1291",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 16F, FontStyle.Bold),
            Padding = new Padding(0, 6, 0, 0)
        };
        _headerCloud.AutoSize = true;
        _headerCloud.TextAlign = ContentAlignment.MiddleRight;
        _headerCloud.Dock = DockStyle.Fill;
        _headerVersion.AutoSize = true;
        _headerVersion.TextAlign = ContentAlignment.MiddleRight;
        _headerVersion.Dock = DockStyle.Fill;

        panel.Controls.Add(title, 0, 0);
        panel.Controls.Add(_headerCloud, 1, 0);
        panel.Controls.Add(_headerVersion, 2, 0);
        return panel;
    }

    private TabPage BuildDamageTab()
    {
        var tab = NewTab("Nhập hư hỏng");
        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 2,
            RowCount = 1
        };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));

        var left = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(0, 0, 14, 0)
        };
        left.SizeChanged += (_, _) => ResizeFlowChildren(left);

        left.Controls.Add(BuildProductSection());
        left.Controls.Add(BuildOccurrenceSection());
        left.Controls.Add(BuildImagesSection());
        left.Controls.Add(BuildSubmitSection());

        var previewBox = new GroupBox
        {
            Text = "Xem trước ảnh",
            Dock = DockStyle.Fill,
            Padding = new Padding(14)
        };
        _imagePreview.Dock = DockStyle.Fill;
        _imagePreview.SizeMode = PictureBoxSizeMode.Zoom;
        _imagePreview.BorderStyle = BorderStyle.FixedSingle;
        _imagePreview.BackColor = Color.White;
        previewBox.Controls.Add(_imagePreview);

        outer.Controls.Add(left, 0, 0);
        outer.Controls.Add(previewBox, 1, 0);
        tab.Controls.Add(outer);
        return tab;
    }

    private GroupBox BuildProductSection()
    {
        var box = NewSection("1. Thông tin sản phẩm");
        var form = NewFormTable(180);

        _sku.CharacterCasing = CharacterCasing.Upper;
        _sku.Margin = new Padding(3, 4, 3, 10);
        _sku.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            LookupSku();
            e.SuppressKeyPress = true;
        };
        _sku.Leave += (_, _) =>
        {
            if (_sku.TextLength > 0) LookupSku();
        };

        _productName.ReadOnly = true;
        _productName.BackColor = Color.White;
        _baseUnit.ReadOnly = true;
        _baseUnit.BackColor = Color.White;

        AddFormRow(form, "SKU *", _sku, 54);
        AddFormRow(form, "Tên sản phẩm", _productName, 54);
        AddFormRow(form, "Base Units", _baseUnit, 54);
        box.Controls.Add(form);
        return box;
    }

    private GroupBox BuildOccurrenceSection()
    {
        var box = NewSection("2. Thông tin phát hiện hư hỏng");
        var form = NewFormTable(180);

        _date.Format = DateTimePickerFormat.Custom;
        _date.CustomFormat = "dd/MM/yyyy";
        _date.ShowUpDown = false;

        _hour.Minimum = 0;
        _hour.Maximum = 23;
        _hour.Width = 74;
        _hour.TextAlign = HorizontalAlignment.Center;
        _minute.Minimum = 0;
        _minute.Maximum = 59;
        _minute.Width = 74;
        _minute.TextAlign = HorizontalAlignment.Center;

        var time = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            WrapContents = false,
            Margin = new Padding(0)
        };
        time.Controls.Add(_hour);
        time.Controls.Add(new Label { Text = ":", AutoSize = true, Font = new Font("Segoe UI", 14F, FontStyle.Bold), Padding = new Padding(7, 1, 7, 0) });
        time.Controls.Add(_minute);
        time.Controls.Add(new Label { Text = "  (HH : mm)", AutoSize = true, Padding = new Padding(8, 7, 0, 0) });

        _shift.DropDownStyle = ComboBoxStyle.DropDownList;
        _shift.Items.AddRange(["Ca 1", "Ca 2"]);
        _shift.SelectedIndexChanged += (_, _) => SaveLastShift();

        _quantity.Minimum = 1;
        _quantity.Maximum = 999999999;
        _quantity.DecimalPlaces = 0;
        _quantity.Increment = 1;
        _quantity.ThousandsSeparator = true;

        AddFormRow(form, "Vị trí phát hiện *", _location, 54);
        AddFormRow(form, "Ngày phát hiện *", _date, 54);
        AddFormRow(form, "Giờ phát hiện *", time, 54);
        AddFormRow(form, "Ca *", _shift, 54);
        AddFormRow(form, "Số lượng hư hỏng *", _quantity, 54);
        box.Controls.Add(form);
        return box;
    }

    private GroupBox BuildImagesSection()
    {
        var box = NewSection("3. Hình ảnh hiện trạng — tối đa 5 ảnh");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 10, 12, 12),
            ColumnCount = 1,
            RowCount = 2
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 135));

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        var addImage = new Button { Text = "+ Thêm ảnh", AutoSize = true, Height = 34, Padding = new Padding(8, 0, 8, 0) };
        var removeImage = new Button { Text = "Xoá ảnh đã chọn", AutoSize = true, Height = 34, Padding = new Padding(8, 0, 8, 0) };
        addImage.Click += (_, _) => AddImages();
        removeImage.Click += (_, _) => RemoveSelectedImage();
        buttons.Controls.AddRange([addImage, removeImage]);

        _images.Dock = DockStyle.Fill;
        _images.SelectedIndexChanged += (_, _) => ShowSelectedImage();
        layout.Controls.Add(buttons, 0, 0);
        layout.Controls.Add(_images, 0, 1);
        box.Controls.Add(layout);
        box.Height = 240;
        return box;
    }

    private GroupBox BuildSubmitSection()
    {
        var box = NewSection("4. Lưu và đồng bộ");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 10, 12, 12),
            ColumnCount = 1,
            RowCount = 2
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _send.Text = "GỬI THÔNG TIN HƯ HỎNG";
        _send.Height = 42;
        _send.Dock = DockStyle.Top;
        _send.Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold);
        _send.Click += async (_, _) => await SaveAndSendAsync();

        _entryStatus.AutoSize = true;
        _entryStatus.MaximumSize = new Size(760, 0);
        _entryStatus.Text = "Dữ liệu được lưu trên laptop trước. Khi mất mạng hoặc Google tạm lỗi, phiếu được giữ local để đồng bộ lại khi online.";
        _entryStatus.Padding = new Padding(0, 7, 0, 0);

        layout.Controls.Add(_send, 0, 0);
        layout.Controls.Add(_entryStatus, 0, 1);
        box.Controls.Add(layout);
        box.Height = 145;
        return box;
    }

    private TabPage BuildReportsTab()
    {
        var tab = NewTab("Danh sách đã nhập");
        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), RowCount = 2, ColumnCount = 1 };
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var commands = NewSection("Công cụ danh sách");
        commands.Dock = DockStyle.Fill;
        var top = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12, 12, 12, 8), WrapContents = false };
        var refresh = NewButton("Làm mới");
        var sync = NewButton("Đồng bộ lại");
        var export = NewButton("Xuất Excel — sẽ cập nhật logic");
        refresh.Click += (_, _) => RefreshReports();
        sync.Click += async (_, _) => await SyncPendingAsync();
        export.Click += (_, _) => MessageBox.Show(this, "Chức năng xuất Excel đang giữ chỗ và sẽ cập nhật theo logic OWNER chốt sau.", "Thông báo");
        _reportStatus.AutoSize = true;
        _reportStatus.Padding = new Padding(18, 9, 0, 0);
        top.Controls.AddRange([refresh, sync, export, _reportStatus]);
        commands.Controls.Add(top);

        ConfigureGrid(_reportGrid);
        outer.Controls.Add(commands, 0, 0);
        outer.Controls.Add(_reportGrid, 0, 1);
        tab.Controls.Add(outer);
        return tab;
    }

    private TabPage BuildProductsTab()
    {
        var tab = NewTab("Danh mục SKU");
        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), RowCount = 2, ColumnCount = 1 };
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var tools = NewSection("Cập nhật và tìm kiếm danh mục SKU");
        tools.Dock = DockStyle.Fill;
        var top = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12, 12, 12, 8), WrapContents = false };
        var import = NewButton("Cập nhật từ Excel");
        import.Click += async (_, _) => await ImportProductsAsync();
        _productSearch.Width = 360;
        _productSearch.PlaceholderText = "Tìm SKU hoặc tên sản phẩm...";
        _productSearch.Margin = new Padding(14, 3, 3, 3);
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
        var scroll = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(18)
        };
        scroll.SizeChanged += (_, _) => ResizeFlowChildren(scroll);

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
        _googleStatus.MaximumSize = new Size(800, 0);

        var connect = NewButton("Kết nối Google");
        var verify = NewButton("Kiểm tra Drive / Sheet");
        var openDrive = NewButton("Mở thư mục Google Drive");
        connect.Click += async (_, _) => await ConnectGoogleAsync();
        verify.Click += async (_, _) => await VerifyGoogleAsync();
        openDrive.Click += (_, _) => OpenDriveFolder();
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        actions.Controls.AddRange([connect, verify, openDrive]);

        AddFormRow(panel, "Google Cloud project", ReadOnlyText(CloudConfig.GoogleCloudProjectId), 52);
        AddFormRow(panel, "Drive root", ReadOnlyText("CẬP NHẬT HƯ HỎNG PICKFACE — " + CloudConfig.DriveRootFolderId), 52);
        AddFormRow(panel, "Trạng thái", _googleStatus, 62);
        AddFormRow(panel, "Thao tác", actions, 58);
        box.Controls.Add(panel);
        return box;
    }

    private GroupBox BuildFirebaseSettingsSection()
    {
        var box = NewSection("Tài khoản / phiên làm việc — Firebase Spark");
        var panel = NewFormTable(220);
        _firebaseStatus.AutoSize = true;
        _firebaseStatus.MaximumSize = new Size(800, 0);
        AddFormRow(panel, "Project", ReadOnlyText(CloudConfig.GoogleCloudProjectId), 52);
        AddFormRow(panel, "Trạng thái", _firebaseStatus, 66);
        AddFormRow(panel, "Ghi chú", new Label
        {
            AutoSize = true,
            MaximumSize = new Size(800, 0),
            Text = "Firebase chỉ dùng Authentication + Realtime Database theo Spark/no-cost. Không tự nâng Blaze, không tự gắn billing. Sau khi OWNER hoàn tất cấu hình Firebase, login/role/active_operator/offline lease sẽ được kích hoạt ở build tiếp theo."
        }, 88);
        box.Controls.Add(panel);
        return box;
    }

    private GroupBox BuildUpdateSection()
    {
        var box = NewSection("Cập nhật phiên bản");
        var panel = NewFormTable(220);
        _versionStatus.AutoSize = true;
        _versionStatus.MaximumSize = new Size(800, 0);
        _updateButton.Text = "Kiểm tra cập nhật";
        _updateButton.AutoSize = true;
        _updateButton.Padding = new Padding(8, 0, 8, 0);
        _updateButton.Click += async (_, _) => await UpdateButtonAsync();
        var openReleases = NewButton("Mở GitHub Releases");
        openReleases.Click += (_, _) => VersionUpdateService.OpenReleasesPage();
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        actions.Controls.AddRange([_updateButton, openReleases]);
        AddFormRow(panel, "Phiên bản hiện tại", ReadOnlyText(VersionUpdateService.CurrentVersionText), 52);
        AddFormRow(panel, "Trạng thái", _versionStatus, 72);
        AddFormRow(panel, "Thao tác", actions, 58);
        box.Controls.Add(panel);
        return box;
    }

    private GroupBox BuildLocalDataSection()
    {
        var box = NewSection("Dữ liệu local trên laptop");
        var panel = NewFormTable(220);
        var openData = NewButton("Mở thư mục dữ liệu local");
        openData.Click += (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.Root) { UseShellExecute = true });
        AddFormRow(panel, "Thư mục", ReadOnlyText(AppPaths.Root), 60);
        AddFormRow(panel, "Thao tác", openData, 56);
        AddFormRow(panel, "Nguyên tắc", new Label
        {
            AutoSize = true,
            MaximumSize = new Size(800, 0),
            Text = "SQLite, ảnh pending và token thực tế chỉ nằm trên máy người dùng; không commit dữ liệu thực tế hoặc credential vào repository public."
        }, 76);
        box.Controls.Add(panel);
        return box;
    }

    private static TabPage NewTab(string title) => new(title) { BackColor = Color.WhiteSmoke };

    private static GroupBox NewSection(string title) => new()
    {
        Text = title,
        Width = 760,
        Height = 220,
        Margin = new Padding(0, 0, 0, 16),
        Padding = new Padding(10),
        Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold)
    };

    private static TableLayoutPanel NewFormTable(int labelWidth)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 10, 12, 10),
            ColumnCount = 2,
            RowCount = 0,
            Font = new Font("Segoe UI", 10F)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelWidth));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return panel;
    }

    private static TextBox ReadOnlyText(string value) => new()
    {
        Text = value,
        ReadOnly = true,
        BackColor = Color.White,
        Dock = DockStyle.Top
    };

    private static Button NewButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Height = 34,
        Padding = new Padding(8, 0, 8, 0),
        Margin = new Padding(3, 3, 7, 3)
    };

    private static void ResizeFlowChildren(FlowLayoutPanel panel)
    {
        var width = Math.Max(400, panel.ClientSize.Width - panel.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 8);
        foreach (Control child in panel.Controls)
            child.Width = width;
    }

    private static void ConfigureGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.BackgroundColor = Color.White;
        grid.BorderStyle = BorderStyle.Fixed3D;
        grid.RowTemplate.Height = 34;
        grid.ColumnHeadersHeight = 38;
        grid.EnableHeadersVisualStyles = true;
    }

    private static void AddFormRow(TableLayoutPanel panel, string label, Control control, int height)
    {
        var row = panel.RowCount++;
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        var title = new Label
        {
            Text = label,
            AutoSize = true,
            Padding = new Padding(0, 7, 12, 0),
            Font = new Font("Segoe UI", 10F)
        };
        control.Margin = new Padding(3, 3, 3, 9);
        if (control is not FlowLayoutPanel && control.Dock == DockStyle.None) control.Dock = DockStyle.Top;
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

        using var dialog = new OpenFileDialog
        {
            Filter = "Hình ảnh|*.jpg;*.jpeg;*.png;*.heic;*.webp",
            Multiselect = true,
            Title = "Chọn ảnh hàng hư hỏng"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var available = 5 - _draftImages.Count;
        foreach (var source in dialog.FileNames.Take(available))
        {
            var folder = AppPaths.GetDraftImageFolder(_draftId);
            var ext = Path.GetExtension(source).ToLowerInvariant();
            var target = Path.Combine(folder, $"{_draftImages.Count + 1:00}{ext}");
            File.Copy(source, target, true);
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
        try { if (File.Exists(path)) File.Delete(path); } catch { }
        _imagePreview.Image?.Dispose();
        _imagePreview.Image = null;
        RefreshImageList();
    }

    private void RefreshImageList()
    {
        _images.Items.Clear();
        for (var i = 0; i < _draftImages.Count; i++)
            _images.Items.Add($"Ảnh {i + 1}: {Path.GetFileName(_draftImages[i])}");
        if (_images.Items.Count > 0) _images.SelectedIndex = 0;
    }

    private void ShowSelectedImage()
    {
        _imagePreview.Image?.Dispose();
        _imagePreview.Image = null;
        var idx = _images.SelectedIndex;
        if (idx < 0 || idx >= _draftImages.Count || !File.Exists(_draftImages[idx])) return;
        try
        {
            using var source = Image.FromFile(_draftImages[idx]);
            _imagePreview.Image = new Bitmap(source);
        }
        catch { }
    }

    private async Task SaveAndSendAsync()
    {
        LookupSku();
        if (!_skuValid)
        {
            _sku.Focus();
            return;
        }
        if (string.IsNullOrWhiteSpace(_location.Text))
        {
            MessageBox.Show(this, "Chưa nhập vị trí phát hiện hư hỏng.", "Thiếu thông tin");
            _location.Focus();
            return;
        }
        if (_shift.SelectedIndex < 0)
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
            _draftId,
            _date.Value.Date,
            (int)_hour.Value,
            (int)_minute.Value,
            _shift.SelectedItem!.ToString()!,
            _sku.Text.Trim(),
            _productName.Text,
            ExcelImportService.Clean(_location.Text),
            decimal.Truncate(_quantity.Value),
            _baseUnit.Text,
            DateTime.Now,
            "PENDING",
            null,
            null);

        _send.Enabled = false;
        try
        {
            Database.SaveDamageReport(report, _draftImages);
            var synced = false;
            if (GoogleService.IsConnected())
            {
                try
                {
                    _entryStatus.Text = "Đã lưu local. Đang đồng bộ Google...";
                    await GoogleService.SyncReportAsync(report);
                    synced = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this,
                        "Phiếu đã được lưu an toàn trên laptop nhưng chưa đồng bộ được Google.\n\n" + ex.Message,
                        "Đã lưu local - chưa đồng bộ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

            if (synced)
                MessageBox.Show(this, "Đã lưu và đồng bộ Google thành công.", "Hoàn tất", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else if (!GoogleService.IsConnected())
                MessageBox.Show(this, "Đã lưu trên laptop. Google chưa kết nối hoặc đang offline nên phiếu đang chờ đồng bộ.", "Đã lưu local", MessageBoxButtons.OK, MessageBoxIcon.Information);

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
        _sku.Clear();
        _skuValid = false;
        _productName.Clear();
        _productName.ForeColor = SystemColors.WindowText;
        _location.Clear();
        _baseUnit.Clear();
        _date.Value = DateTime.Today;
        _hour.Value = DateTime.Now.Hour;
        _minute.Value = DateTime.Now.Minute;
        _quantity.Value = 1;

        var lastShift = SettingsStore.Load().LastShift;
        var shiftIndex = _shift.Items.IndexOf(lastShift);
        _shift.SelectedIndex = shiftIndex >= 0 ? shiftIndex : 0;

        _images.Items.Clear();
        _imagePreview.Image?.Dispose();
        _imagePreview.Image = null;
        _sku.Focus();
    }

    private void SaveLastShift()
    {
        if (_shift.SelectedItem is null) return;
        var settings = SettingsStore.Load();
        settings.LastShift = _shift.SelectedItem.ToString() ?? "Ca 1";
        SettingsStore.Save(settings);
    }

    private void RefreshReports()
    {
        var rows = Database.GetReports();
        _reportGrid.Columns.Clear();
        _reportGrid.Rows.Clear();
        foreach (var (name, header, weight) in new[]
        {
            ("Date","Ngày",60), ("Time","Giờ",45), ("Shift","Ca",45), ("Sku","SKU",65),
            ("Name","Tên sản phẩm",150), ("Location","Vị trí",80), ("Qty","SL",45), ("Base","Base",45),
            ("Status","Trạng thái",70), ("Error","Lỗi",120)
        })
            _reportGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = header, FillWeight = weight });

        foreach (var r in rows)
            _reportGrid.Rows.Add(r.OccurredDate.ToString("dd/MM/yyyy"), $"{r.Hour:00}:{r.Minute:00}", r.Shift, r.Sku, r.ProductName, r.Location, decimal.Truncate(r.Quantity).ToString("0"), r.BaseUnit, DisplayStatus(r.SyncStatus), r.LastError ?? "");

        var pending = rows.Count(x => x.SyncStatus != "SYNCED");
        _reportStatus.Text = $"Tổng {rows.Count:N0} | Chờ/lỗi đồng bộ: {pending:N0}";
    }

    private static string DisplayStatus(string status) => status switch
    {
        "SYNCED" => "Đã đồng bộ",
        "SYNCING" => "Đang đồng bộ",
        "ERROR" => "Lỗi đồng bộ",
        "OFFLINE_PENDING" => "Chờ online",
        _ => "Chờ đồng bộ"
    };

    private async Task SyncPendingAsync()
    {
        if (!GoogleService.IsConnected())
        {
            MessageBox.Show(this, "Chưa kết nối Google trên laptop này. Vào Cài đặt > Kết nối Google.", "Chưa kết nối");
            return;
        }

        try
        {
            _reportStatus.Text = "Đang đồng bộ...";
            var progress = new Progress<string>(s => _reportStatus.Text = s);
            await GoogleService.SyncAllPendingAsync(progress);
            MessageBox.Show(this, "Đã xử lý hàng đợi đồng bộ.", "Hoàn tất");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Đồng bộ chưa hoàn tất", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            RefreshReports();
            RefreshSettings();
        }
    }

    private async Task ImportProductsAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Excel Workbook (*.xlsx)|*.xlsx",
            Title = "Chọn file REPORT_BIN_INVENTORY"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            Cursor = Cursors.WaitCursor;
            var preview = await Task.Run(() => ExcelImportService.Analyze(dialog.FileName));
            Cursor = Cursors.Default;

            var sourceWarning = preview.SourceConflicts.Count == 0
                ? string.Empty
                : $"\n\nCó {preview.SourceConflicts.Count} SKU mâu thuẫn ngay trong file nguồn; các SKU này sẽ KHÔNG được nhập và cần kiểm tra file nguồn.";
            var summary =
                $"Tổng dòng dữ liệu: {preview.TotalRows:N0}\n" +
                $"SKU hợp lệ duy nhất: {preview.UniqueSkus:N0}\n" +
                $"Dòng trùng cùng SKU/Tên/Base: {preview.DuplicateRows:N0}\n" +
                $"SKU mới: {preview.NewProducts.Count:N0}\n" +
                $"Không thay đổi: {preview.UnchangedProducts.Count:N0}\n" +
                $"SKU thay đổi cần Owner quyết định: {preview.Conflicts.Count:N0}\n" +
                $"Dòng/SKU lỗi: {preview.InvalidRows:N0}" + sourceWarning;

            if (MessageBox.Show(this, summary + "\n\nTiếp tục?", "Kiểm tra file trước khi cập nhật", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
                return;

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
            Cursor = Cursors.Default;
            MessageBox.Show(this, "Đã cập nhật danh mục SKU local thành công. SKU cũ không có trong file mới KHÔNG bị xoá.", "Hoàn tất");
            RefreshProducts();
        }
        catch (Exception ex)
        {
            Cursor = Cursors.Default;
            MessageBox.Show(this, ex.Message, "Không thể cập nhật danh mục SKU", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
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
        foreach (var p in rows)
            _productGrid.Rows.Add(p.Sku, p.ProductName, p.BaseUnit, p.LastSeenAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"), p.SourceFile);
        _productCount.Text = $"Database: {Database.GetProductCount():N0} SKU" + (rows.Count >= 1000 ? " | hiển thị tối đa 1.000 dòng" : "");
    }

    private async Task ConnectGoogleAsync()
    {
        if (!GoogleService.IsClientConfigured())
        {
            MessageBox.Show(this,
                "Build hiện tại chưa có Google OAuth Client ID. Không cần file JSON nữa. OWNER chỉ cần cung cấp Client ID của Desktop OAuth trong project pickface-damage-1291 để đưa vào build tiếp theo.",
                "Thiếu Google OAuth Client ID", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            _googleStatus.Text = "Đang chờ đăng nhập Google trên trình duyệt...";
            await GoogleService.AuthorizeAsync();
            RefreshSettings();
            MessageBox.Show(this, "Kết nối Google và xác minh Drive cố định thành công.", "Hoàn tất");
        }
        catch (Exception ex)
        {
            RefreshSettings();
            MessageBox.Show(this, ex.Message, "Không kết nối được Google", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task VerifyGoogleAsync()
    {
        if (!GoogleService.IsConnected())
        {
            MessageBox.Show(this, "Chưa kết nối Google trên laptop này.", "Chưa kết nối");
            return;
        }

        try
        {
            _googleStatus.Text = "Đang xác minh đúng Drive root / thư mục ảnh / Google Sheet...";
            await GoogleService.ProvisionAsync();
            RefreshSettings();
            MessageBox.Show(this, "Đã xác minh đúng phạm vi Drive được OWNER cho phép.", "Google đã sẵn sàng");
        }
        catch (Exception ex)
        {
            RefreshSettings();
            MessageBox.Show(this, ex.Message, "Không thể xác minh Google", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RefreshSettings()
    {
        var connected = GoogleService.IsConnected();
        var clientReady = GoogleService.IsClientConfigured();
        _googleStatus.Text = !clientReady
            ? "Chưa cấu hình OAuth Client ID — build không dùng file JSON."
            : connected
                ? "Đã kết nối Google trên laptop này. Drive root được khóa theo ID cố định."
                : "OAuth Client ID đã có, nhưng laptop này chưa đăng nhập Google.";

        var firebaseReady = !string.IsNullOrWhiteSpace(CloudConfig.FirebaseApiKey) && !string.IsNullOrWhiteSpace(CloudConfig.FirebaseDatabaseUrl);
        _firebaseStatus.Text = firebaseReady
            ? "Đã có public Firebase client config; chờ/đang triển khai Auth + active_operator."
            : "Chưa có Firebase API key / Realtime Database URL của project pickface-damage-1291.";

        _versionStatus.Text = _availableRelease is null
            ? "Chưa kiểm tra bản mới."
            : VersionUpdateService.IsNewer(_availableRelease)
                ? $"Có bản mới {_availableRelease.Tag}."
                : $"Đang dùng bản mới nhất ({VersionUpdateService.CurrentVersionText}).";
        _updateButton.Text = _availableRelease is not null && VersionUpdateService.IsNewer(_availableRelease)
            ? $"Cập nhật lên {_availableRelease.Tag}"
            : "Kiểm tra cập nhật";

        _headerCloud.Text = connected ? "Google: đã kết nối" : "Google: local/offline";
        _headerVersion.Text = VersionUpdateService.CurrentVersionText;
    }

    private void OpenDriveFolder()
    {
        Process.Start(new ProcessStartInfo($"https://drive.google.com/drive/folders/{CloudConfig.DriveRootFolderId}") { UseShellExecute = true });
    }

    private async Task UpdateButtonAsync()
    {
        try
        {
            _updateButton.Enabled = false;
            if (_availableRelease is null || !VersionUpdateService.IsNewer(_availableRelease))
            {
                _versionStatus.Text = "Đang kiểm tra GitHub Release...";
                _availableRelease = await VersionUpdateService.GetLatestAsync();
                if (_availableRelease is null)
                {
                    _versionStatus.Text = "Chưa có GitHub Release hợp lệ để cập nhật tự động.";
                    return;
                }
                RefreshSettings();
                if (!VersionUpdateService.IsNewer(_availableRelease))
                {
                    MessageBox.Show(this, "Ứng dụng đang ở phiên bản mới nhất.", "Cập nhật phiên bản");
                    return;
                }

                var notes = string.IsNullOrWhiteSpace(_availableRelease.Notes) ? "Không có ghi chú phát hành." : _availableRelease.Notes;
                if (notes.Length > 2500) notes = notes[..2500] + "...";
                if (MessageBox.Show(this,
                        $"Có phiên bản {_availableRelease.Tag}.\n\n{notes}\n\nTải và cập nhật ngay?",
                        "Có bản cập nhật", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
                    return;
            }

            var progress = new Progress<string>(text => _versionStatus.Text = text);
            var script = await VersionUpdateService.DownloadAndPrepareAsync(_availableRelease, progress);
            _versionStatus.Text = "Đã tải xong. Ứng dụng sẽ đóng, thay phiên bản và mở lại.";
            MessageBox.Show(this,
                "Bản cập nhật đã tải xong. Ứng dụng sẽ đóng và mở lại tự động. Nếu Windows/EDR chặn việc thay EXE, bản hiện tại vẫn được giữ để cập nhật thủ công từ GitHub Releases.",
                "Sẵn sàng cập nhật", MessageBoxButtons.OK, MessageBoxIcon.Information);
            VersionUpdateService.LaunchUpdaterAndExit(script);
        }
        catch (Exception ex)
        {
            _versionStatus.Text = "Cập nhật tự động chưa hoàn tất.";
            MessageBox.Show(this,
                ex.Message + "\n\nCó thể dùng nút Mở GitHub Releases để tải thủ công.",
                "Không thể tự cập nhật", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _updateButton.Enabled = true;
        }
    }

    private void RefreshActiveTab()
    {
        if (_tabs.SelectedIndex == 1) RefreshReports();
        else if (_tabs.SelectedIndex == 2) RefreshProducts();
        else if (_tabs.SelectedIndex == 3) RefreshSettings();
    }
}
