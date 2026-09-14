using System.Diagnostics;

namespace PickfaceDamage1291;

internal sealed class MainForm : Form
{
    private readonly TabControl _tabs = new();

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
    private string _draftId = Guid.NewGuid().ToString("D");
    private readonly List<string> _draftImages = [];

    private readonly DataGridView _reportGrid = new();
    private readonly Label _reportStatus = new();
    private readonly DataGridView _productGrid = new();
    private readonly TextBox _productSearch = new();
    private readonly Label _productCount = new();

    private readonly Label _googleStatus = new();
    private readonly Label _oauthPath = new();

    public MainForm()
    {
        Text = "Cập nhật hư hỏng Pickface 1291";
        Width = 1180;
        Height = 760;
        MinimumSize = new Size(980, 650);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10F);

        _tabs.Dock = DockStyle.Fill;
        _tabs.TabPages.Add(BuildDamageTab());
        _tabs.TabPages.Add(BuildReportsTab());
        _tabs.TabPages.Add(BuildProductsTab());
        _tabs.TabPages.Add(BuildSettingsTab());
        _tabs.SelectedIndexChanged += (_, _) => RefreshActiveTab();
        Controls.Add(_tabs);

        ResetDraft();
        RefreshProducts();
        RefreshReports();
        RefreshSettings();
    }

    private TabPage BuildDamageTab()
    {
        var tab = new TabPage("Nhập hư hỏng");
        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 2,
            RowCount = 1
        };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));

        var form = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 185));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _sku.CharacterCasing = CharacterCasing.Upper;
        _sku.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { LookupSku(showError: true); e.SuppressKeyPress = true; } };
        _sku.Leave += (_, _) => { if (_sku.TextLength > 0) LookupSku(showError: false); };
        _productName.ReadOnly = true;
        _baseUnit.ReadOnly = true;
        _date.Format = DateTimePickerFormat.Custom;
        _date.CustomFormat = "dd/MM/yyyy";
        _hour.Minimum = 0; _hour.Maximum = 23; _hour.Width = 60;
        _minute.Minimum = 0; _minute.Maximum = 59; _minute.Width = 60;
        _shift.DropDownStyle = ComboBoxStyle.DropDownList;
        _shift.Items.AddRange(["Ca 1", "Ca 2"]);
        _quantity.Minimum = 0.001M; _quantity.Maximum = 999999999M; _quantity.DecimalPlaces = 3; _quantity.Increment = 1;

        AddFormRow(form, "SKU *", _sku);
        AddFormRow(form, "Tên sản phẩm", _productName);
        AddFormRow(form, "Vị trí phát hiện *", _location);
        AddFormRow(form, "Ngày phát hiện *", _date);

        var time = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        time.Controls.Add(_hour);
        time.Controls.Add(new Label { Text = ":", AutoSize = true, Padding = new Padding(3, 6, 3, 0) });
        time.Controls.Add(_minute);
        AddFormRow(form, "Giờ phát hiện *", time);
        AddFormRow(form, "Ca *", _shift);
        AddFormRow(form, "Số lượng hư hỏng *", _quantity);
        AddFormRow(form, "Base Units", _baseUnit);

        var imageButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        var addImage = new Button { Text = "+ Thêm ảnh", AutoSize = true };
        var removeImage = new Button { Text = "Xoá ảnh đã chọn", AutoSize = true };
        addImage.Click += (_, _) => AddImages();
        removeImage.Click += (_, _) => RemoveSelectedImage();
        imageButtons.Controls.AddRange([addImage, removeImage]);
        AddFormRow(form, "Ảnh (tối đa 5)", imageButtons);

        _images.Dock = DockStyle.Fill;
        _images.Height = 125;
        _images.SelectedIndexChanged += (_, _) => ShowSelectedImage();
        AddFormRow(form, "Ảnh đã chọn", _images, 130);

        _send.Text = "GỬI";
        _send.Height = 44;
        _send.Dock = DockStyle.Top;
        _send.Click += async (_, _) => await SaveAndSendAsync();
        AddFormRow(form, string.Empty, _send, 58);

        var note = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            MaximumSize = new Size(720, 0),
            Text = "Dữ liệu luôn được lưu trên laptop trước. Nếu Google hoặc mạng lỗi, phiếu vẫn được giữ local và có thể đồng bộ lại sau."
        };
        AddFormRow(form, string.Empty, note, 70);

        _imagePreview.Dock = DockStyle.Fill;
        _imagePreview.SizeMode = PictureBoxSizeMode.Zoom;
        _imagePreview.BorderStyle = BorderStyle.FixedSingle;
        outer.Controls.Add(form, 0, 0);
        outer.Controls.Add(_imagePreview, 1, 0);
        tab.Controls.Add(outer);
        return tab;
    }

    private TabPage BuildReportsTab()
    {
        var tab = new TabPage("Danh sách đã nhập");
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 52, Padding = new Padding(8) };
        var refresh = new Button { Text = "Làm mới", AutoSize = true };
        var sync = new Button { Text = "Đồng bộ lại", AutoSize = true };
        var export = new Button { Text = "Xuất Excel (sẽ cập nhật logic)", AutoSize = true };
        refresh.Click += (_, _) => RefreshReports();
        sync.Click += async (_, _) => await SyncPendingAsync();
        export.Click += (_, _) => MessageBox.Show(this, "Chức năng xuất Excel được giữ chỗ và sẽ cập nhật logic ở phiên bản sau.", "Thông báo");
        _reportStatus.AutoSize = true;
        _reportStatus.Padding = new Padding(12, 7, 0, 0);
        top.Controls.AddRange([refresh, sync, export, _reportStatus]);

        ConfigureGrid(_reportGrid);
        tab.Controls.Add(_reportGrid);
        tab.Controls.Add(top);
        return tab;
    }

    private TabPage BuildProductsTab()
    {
        var tab = new TabPage("Danh mục SKU");
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 56, Padding = new Padding(8) };
        var import = new Button { Text = "Cập nhật từ Excel", AutoSize = true };
        import.Click += async (_, _) => await ImportProductsAsync();
        _productSearch.Width = 300;
        _productSearch.PlaceholderText = "Tìm SKU hoặc tên sản phẩm...";
        _productSearch.TextChanged += (_, _) => RefreshProducts();
        _productCount.AutoSize = true;
        _productCount.Padding = new Padding(12, 7, 0, 0);
        top.Controls.AddRange([import, _productSearch, _productCount]);

        ConfigureGrid(_productGrid);
        tab.Controls.Add(_productGrid);
        tab.Controls.Add(top);
        return tab;
    }

    private TabPage BuildSettingsTab()
    {
        var tab = new TabPage("Cài đặt");
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Padding = new Padding(20),
            AutoSize = true,
            ColumnCount = 2
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _oauthPath.AutoSize = true;
        _oauthPath.MaximumSize = new Size(800, 0);
        _googleStatus.AutoSize = true;

        var chooseOAuth = new Button { Text = "Chọn OAuth Client JSON", AutoSize = true };
        var connect = new Button { Text = "Kết nối Google", AutoSize = true };
        var provision = new Button { Text = "Khởi tạo / kiểm tra Drive & Sheet", AutoSize = true };
        var openData = new Button { Text = "Mở thư mục dữ liệu local", AutoSize = true };
        var openDrive = new Button { Text = "Mở thư mục Google Drive", AutoSize = true };
        chooseOAuth.Click += (_, _) => ChooseOAuthJson();
        connect.Click += async (_, _) => await ConnectGoogleAsync();
        provision.Click += async (_, _) => await ProvisionGoogleAsync();
        openData.Click += (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.Root) { UseShellExecute = true });
        openDrive.Click += (_, _) => OpenDriveFolder();

        AddSettingsRow(panel, "OAuth Client", _oauthPath);
        AddSettingsRow(panel, string.Empty, chooseOAuth);
        AddSettingsRow(panel, "Trạng thái Google", _googleStatus);
        AddSettingsRow(panel, string.Empty, connect);
        AddSettingsRow(panel, string.Empty, provision);
        AddSettingsRow(panel, "Dữ liệu ứng dụng", new Label { Text = AppPaths.Root, AutoSize = true, MaximumSize = new Size(800, 0) });
        AddSettingsRow(panel, string.Empty, openData);
        AddSettingsRow(panel, string.Empty, openDrive);
        AddSettingsRow(panel, "Lưu ý", new Label
        {
            AutoSize = true,
            MaximumSize = new Size(800, 0),
            Text = "Repository GitHub là public nhưng OAuth JSON, Google token, SQLite, ảnh và dữ liệu thực tế chỉ nằm trong LocalAppData của Windows; không được đưa lên GitHub."
        });
        tab.Controls.Add(panel);
        return tab;
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
    }

    private static void AddFormRow(TableLayoutPanel panel, string label, Control control, int height = 44)
    {
        var row = panel.RowCount++;
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        panel.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 7, 8, 0) }, 0, row);
        control.Dock = control is FlowLayoutPanel ? DockStyle.Fill : DockStyle.Top;
        panel.Controls.Add(control, 1, row);
    }

    private static void AddSettingsRow(TableLayoutPanel panel, string label, Control control)
    {
        var row = panel.RowCount++;
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 7, 8, 12) }, 0, row);
        control.Margin = new Padding(3, 3, 3, 12);
        panel.Controls.Add(control, 1, row);
    }

    private void LookupSku(bool showError)
    {
        var sku = ExcelImportService.Clean(_sku.Text).ToUpperInvariant();
        _sku.Text = sku;
        var product = sku.Length == 0 ? null : Database.GetProduct(sku);
        if (product is null)
        {
            _productName.Clear();
            _baseUnit.Clear();
            if (showError && sku.Length > 0)
                MessageBox.Show(this, "Không tìm thấy SKU trong danh mục local. Hãy kiểm tra SKU hoặc cập nhật danh mục SKU trước.", "Không tìm thấy SKU", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
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
        LookupSku(showError: true);
        if (_productName.TextLength == 0 || _baseUnit.TextLength == 0) return;
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

        var report = new DamageReport(
            _draftId,
            _date.Value.Date,
            (int)_hour.Value,
            (int)_minute.Value,
            _shift.SelectedItem!.ToString()!,
            _sku.Text.Trim(),
            _productName.Text,
            ExcelImportService.Clean(_location.Text),
            _quantity.Value,
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
                MessageBox.Show(this, "Đã lưu trên laptop. Google chưa được kết nối nên phiếu đang chờ đồng bộ.", "Đã lưu local", MessageBoxButtons.OK, MessageBoxIcon.Information);

            ResetDraft();
            RefreshReports();
        }
        finally
        {
            _send.Enabled = true;
        }
    }

    private void ResetDraft()
    {
        _draftId = Guid.NewGuid().ToString("D");
        _draftImages.Clear();
        _sku.Clear();
        _productName.Clear();
        _location.Clear();
        _baseUnit.Clear();
        _date.Value = DateTime.Today;
        _hour.Value = DateTime.Now.Hour;
        _minute.Value = DateTime.Now.Minute;
        _quantity.Value = 1;
        if (_shift.Items.Count > 0) _shift.SelectedIndex = 0;
        _images.Items.Clear();
        _imagePreview.Image?.Dispose();
        _imagePreview.Image = null;
        _sku.Focus();
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
            _reportGrid.Rows.Add(r.OccurredDate.ToString("dd/MM/yyyy"), $"{r.Hour:00}:{r.Minute:00}", r.Shift, r.Sku, r.ProductName, r.Location, r.Quantity.ToString("0.###"), r.BaseUnit, DisplayStatus(r.SyncStatus), r.LastError ?? "");
        var pending = rows.Count(x => x.SyncStatus != "SYNCED");
        _reportStatus.Text = $"Tổng {rows.Count:N0} | Chờ/lỗi đồng bộ: {pending:N0}";
    }

    private static string DisplayStatus(string status) => status switch
    {
        "SYNCED" => "Đã đồng bộ",
        "SYNCING" => "Đang đồng bộ",
        "ERROR" => "Lỗi đồng bộ",
        _ => "Chờ đồng bộ"
    };

    private async Task SyncPendingAsync()
    {
        if (!GoogleService.IsConnected())
        {
            MessageBox.Show(this, "Chưa kết nối Google. Vào Cài đặt để kết nối trước.", "Chưa kết nối");
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
        _productCount.Text = $"Database: {Database.GetProductCount():N0} SKU" + (rows.Count >= 1000 ? " | đang hiển thị tối đa 1.000 dòng" : "");
    }

    private void ChooseOAuthJson()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Google OAuth JSON (*.json)|*.json",
            Title = "Chọn OAuth Client JSON loại Desktop app"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var local = Path.Combine(AppPaths.Data, "oauth_client.json");
            File.Copy(dialog.FileName, local, true);
            var s = SettingsStore.Load();
            s.OAuthClientJsonPath = local;
            s.AccessToken = string.Empty;
            s.RefreshToken = string.Empty;
            s.AccessTokenExpiresUtc = DateTime.MinValue;
            SettingsStore.Save(s);
            RefreshSettings();
            MessageBox.Show(this, "Đã lưu cấu hình OAuth vào thư mục dữ liệu local. File này không được đưa lên GitHub.", "Đã lưu");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Không thể lưu OAuth JSON", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ConnectGoogleAsync()
    {
        var s = SettingsStore.Load();
        if (string.IsNullOrWhiteSpace(s.OAuthClientJsonPath) || !File.Exists(s.OAuthClientJsonPath))
        {
            MessageBox.Show(this, "Hãy chọn OAuth Client JSON trước.", "Thiếu cấu hình");
            return;
        }
        try
        {
            _googleStatus.Text = "Đang chờ đăng nhập Google trên trình duyệt...";
            await GoogleService.AuthorizeAsync(s.OAuthClientJsonPath);
            RefreshSettings();
            MessageBox.Show(this, "Kết nối Google thành công.", "Hoàn tất");
        }
        catch (Exception ex)
        {
            RefreshSettings();
            MessageBox.Show(this, ex.Message, "Không kết nối được Google", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ProvisionGoogleAsync()
    {
        try
        {
            _googleStatus.Text = "Đang khởi tạo/kiểm tra Google Drive và Sheet...";
            await GoogleService.ProvisionAsync();
            RefreshSettings();
            MessageBox.Show(this,
                "Đã sẵn sàng:\n- CẬP NHẬT HƯ HỎNG PICKFACE\n- Ảnh hàng hư hỏng\n- Cập nhật thông tin hư hỏng pickface 1291",
                "Google đã sẵn sàng");
        }
        catch (Exception ex)
        {
            RefreshSettings();
            MessageBox.Show(this, ex.Message, "Không thể khởi tạo Google", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RefreshSettings()
    {
        var s = SettingsStore.Load();
        _oauthPath.Text = string.IsNullOrWhiteSpace(s.OAuthClientJsonPath) ? "Chưa chọn" : s.OAuthClientJsonPath;
        var connected = GoogleService.IsConnected();
        var provisioned = !string.IsNullOrWhiteSpace(s.RootFolderId) && !string.IsNullOrWhiteSpace(s.ImageFolderId) && !string.IsNullOrWhiteSpace(s.SpreadsheetId);
        _googleStatus.Text = $"{(connected ? "Đã kết nối Google" : "Chưa kết nối Google")} | {(provisioned ? "Drive/Sheet đã cấu hình" : "Drive/Sheet chưa cấu hình")}";
    }

    private void OpenDriveFolder()
    {
        var id = SettingsStore.Load().RootFolderId;
        if (string.IsNullOrWhiteSpace(id))
        {
            MessageBox.Show(this, "Chưa có thư mục Google Drive. Hãy khởi tạo trước.", "Chưa cấu hình");
            return;
        }
        Process.Start(new ProcessStartInfo($"https://drive.google.com/drive/folders/{id}") { UseShellExecute = true });
    }

    private void RefreshActiveTab()
    {
        if (_tabs.SelectedIndex == 1) RefreshReports();
        else if (_tabs.SelectedIndex == 2) RefreshProducts();
        else if (_tabs.SelectedIndex == 3) RefreshSettings();
    }
}
