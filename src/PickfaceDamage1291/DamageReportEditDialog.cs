namespace PickfaceDamage1291;

internal sealed class DamageReportEditDialog : Form
{
    private readonly DamageReport _original;
    private readonly TextBox _sku = new();
    private readonly TextBox _name = new();
    private readonly TextBox _base = new();
    private readonly TextBox _location = new();
    private readonly DateTimePicker _date = new();
    private readonly TimeWheelPicker _time = new();
    private readonly ExclusiveShiftPicker _shift = new();
    private readonly NumericUpDown _qty = new();
    private readonly ListBox _imageList = new();
    private readonly PictureBox _preview = new();
    private readonly List<DamageImage> _images;
    private readonly HashSet<string> _hashes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _newFiles = [];
    private bool _saved;

    public DamageReport UpdatedReport { get; private set; }
    public IReadOnlyList<DamageImage> Images => _images;

    public DamageReportEditDialog(DamageReport report, IReadOnlyList<DamageImage> images)
    {
        _original = report;
        UpdatedReport = report;
        _images = images.Select((x, i) => x with { Sequence = i + 1 }).ToList();

        Text = $"Chỉnh sửa phiếu — {report.Sku}";
        Width = 900;
        Height = 820;
        MinimumSize = new Size(760, 680);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10F);

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20) };
        var root = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(0, 0, 14, 18)
        };
        root.Controls.Add(BuildInfoSection(), 0, 0);
        root.Controls.Add(BuildImageSection(), 0, 1);
        root.Controls.Add(BuildAuditNote(), 0, 2);
        root.Controls.Add(BuildActions(), 0, 3);
        scroll.Controls.Add(root);
        Controls.Add(scroll);
        AppUiStyle.StyleAllButtons(this);

        LoadValues();
        FormClosed += (_, _) => CleanupCanceledCopies();
    }

    private Control BuildInfoSection()
    {
        var box = Section("Thông tin hàng hư hỏng");
        var form = FormTable();

        _sku.CharacterCasing = CharacterCasing.Upper;
        EntryInputRules.AttachDigitsOnly(_sku);
        EntryInputRules.AttachLocation(_location);
        _sku.Leave += (_, _) => LookupSku();
        _name.ReadOnly = true;
        _name.BackColor = Color.White;
        _base.ReadOnly = true;
        _base.BackColor = Color.White;
        _date.Format = DateTimePickerFormat.Custom;
        _date.CustomFormat = "dd/MM/yyyy";
        _date.ShowUpDown = false;
        _qty.Minimum = 1;
        _qty.Maximum = 999999999;
        _qty.DecimalPlaces = 0;
        _qty.Increment = 1;

        AddRow(form, "SKU *", _sku);
        AddRow(form, "Tên sản phẩm", _name);
        AddRow(form, "Base Units", _base);
        AddRow(form, "Vị trí phát hiện *", _location);
        AddRow(form, "Ngày phát hiện *", _date);
        AddRow(form, "Giờ phát hiện *", _time);
        AddRow(form, "Ca *", _shift);
        AddRow(form, "Số lượng hư hỏng *", _qty);
        box.Controls.Add(form);
        return box;
    }

    private Control BuildImageSection()
    {
        var box = Section("Hình ảnh — tối đa 5 ảnh");
        var layout = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, ColumnCount = 2, RowCount = 2, Padding = new Padding(12) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 200));

        var actions = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, WrapContents = true };
        var add = new Button { Text = "+ Thêm ảnh", AutoSize = true, MinimumSize = new Size(0, 40), Padding = new Padding(12, 4, 12, 4) };
        var replace = new Button { Text = "Thay ảnh đã chọn", AutoSize = true, MinimumSize = new Size(0, 40), Padding = new Padding(12, 4, 12, 4) };
        var remove = new Button { Text = "Bỏ ảnh đã chọn", AutoSize = true, MinimumSize = new Size(0, 40), Padding = new Padding(12, 4, 12, 4) };
        add.Click += (_, _) => AddImages();
        replace.Click += (_, _) => ReplaceImage();
        remove.Click += (_, _) => RemoveImage();
        actions.Controls.AddRange([add, replace, remove]);
        layout.Controls.Add(actions, 0, 0);
        layout.SetColumnSpan(actions, 2);

        _imageList.Dock = DockStyle.Fill;
        _imageList.SelectedIndexChanged += async (_, _) => await ShowPreviewAsync();
        _preview.Dock = DockStyle.Fill;
        _preview.SizeMode = PictureBoxSizeMode.Zoom;
        _preview.BorderStyle = BorderStyle.FixedSingle;
        _preview.BackColor = Color.White;
        layout.Controls.Add(_imageList, 0, 1);
        layout.Controls.Add(_preview, 1, 1);
        box.Controls.Add(layout);
        return box;
    }

    private static Control BuildAuditNote() => new Label
    {
        AutoSize = true,
        MaximumSize = new Size(820, 0),
        Padding = new Padding(4, 12, 4, 12),
        ForeColor = Color.DimGray,
        Text = "Khi lưu, report_id và thời gian/người tạo gốc được giữ nguyên. Hệ thống tăng phiên bản, ghi người sửa, thời gian sửa và lịch sử trước → sau. Ảnh bị bỏ/thay khỏi phiếu không bị xóa vĩnh viễn khỏi Google Drive."
    };

    private Control BuildActions()
    {
        var panel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 10, 0, 0) };
        var save = new Button { Text = "LƯU THAY ĐỔI", AutoSize = true, Height = 40, Padding = new Padding(14, 0, 14, 0) };
        var cancel = new Button { Text = "Hủy", AutoSize = true, Height = 40, Padding = new Padding(14, 0, 14, 0) };
        save.Click += (_, _) => SaveChanges();
        cancel.Click += (_, _) => Close();
        panel.Controls.AddRange([save, cancel]);
        return panel;
    }

    private void LoadValues()
    {
        _sku.Text = _original.Sku;
        _name.Text = _original.ProductName;
        _base.Text = _original.BaseUnit;
        _location.Text = _original.Location;
        _date.Value = _original.OccurredDate;
        _time.SetValue(_original.Hour, _original.Minute);
        _shift.SetShift(_original.Shift);
        _qty.Value = Math.Max(1, Math.Min(_qty.Maximum, decimal.Truncate(_original.Quantity)));
        foreach (var image in _images)
        {
            if (File.Exists(image.LocalPath))
            {
                try { _hashes.Add(ImageHashService.Sha256File(image.LocalPath)); } catch { }
            }
            else
            {
                var remoteHash = SyncCacheStore.GetImageHash(image.ReportId, image.Sequence);
                if (!string.IsNullOrWhiteSpace(remoteHash)) _hashes.Add(remoteHash);
            }
        }
        RefreshImages();
    }

    private bool LookupSku()
    {
        var sku = ExcelImportService.Clean(_sku.Text).ToUpperInvariant();
        _sku.Text = sku;
        var product = sku.Length == 0 ? null : Database.GetProduct(sku);
        if (product is null)
        {
            _name.Text = sku.Length == 0 ? string.Empty : "Sản phẩm không có trong cơ sở dữ liệu, hãy kiểm tra lại.";
            _name.ForeColor = Color.Firebrick;
            _base.Clear();
            return false;
        }
        _name.ForeColor = SystemColors.WindowText;
        _name.Text = product.ProductName;
        _base.Text = product.BaseUnit;
        return true;
    }

    private void AddImages()
    {
        if (_images.Count >= 5)
        {
            MessageBox.Show(this, "Mỗi phiếu chỉ cho phép tối đa 5 ảnh.", "Giới hạn ảnh");
            return;
        }
        using var dialog = new OpenFileDialog { Filter = "Hình ảnh|*.jpg;*.jpeg;*.png;*.heic;*.webp", Multiselect = true, Title = "Chọn ảnh hàng hư hỏng" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        foreach (var source in dialog.FileNames)
        {
            if (_images.Count >= 5) break;
            string hash;
            try { hash = ImageHashService.Sha256File(source); }
            catch { continue; }
            if (!_hashes.Add(hash))
            {
                MessageBox.Show(this, $"Ảnh này đã có trong phiếu và được bỏ qua:\n{Path.GetFileName(source)}", "Ảnh trùng", MessageBoxButtons.OK, MessageBoxIcon.Information);
                continue;
            }
            var folder = AppPaths.GetDraftImageFolder(_original.ReportId);
            var ext = Path.GetExtension(source).ToLowerInvariant();
            var target = Path.Combine(folder, $"edit_{Guid.NewGuid():N}_{hash[..12]}{ext}");
            File.Copy(source, target, false);
            _newFiles.Add(target);
            _images.Add(new DamageImage(_original.ReportId, _images.Count + 1, target, null, null));
        }
        RefreshImages();
    }

    private void ReplaceImage()
    {
        var index = _imageList.SelectedIndex;
        if (index < 0 || index >= _images.Count)
        {
            MessageBox.Show(this, "Chọn ảnh cần thay.", "Chưa chọn ảnh", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new OpenFileDialog { Filter = "Hình ảnh|*.jpg;*.jpeg;*.png;*.heic;*.webp", Multiselect = false, Title = "Chọn ảnh thay thế" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        string newHash;
        try { newHash = ImageHashService.Sha256File(dialog.FileName); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Không đọc được ảnh", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var old = _images[index];
        string oldHash = string.Empty;
        if (File.Exists(old.LocalPath))
        {
            try { oldHash = ImageHashService.Sha256File(old.LocalPath); } catch { }
        }
        if (string.IsNullOrWhiteSpace(oldHash)) oldHash = SyncCacheStore.GetImageHash(old.ReportId, old.Sequence);

        if (!string.Equals(oldHash, newHash, StringComparison.OrdinalIgnoreCase) && _hashes.Contains(newHash))
        {
            MessageBox.Show(this, "Ảnh thay thế đã tồn tại trong phiếu.", "Ảnh trùng", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var folder = AppPaths.GetDraftImageFolder(_original.ReportId);
        var ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();
        var target = Path.Combine(folder, $"replace_{Guid.NewGuid():N}_{newHash[..12]}{ext}");
        File.Copy(dialog.FileName, target, false);
        _newFiles.Add(target);

        if (!string.IsNullOrWhiteSpace(oldHash)) _hashes.Remove(oldHash);
        _hashes.Add(newHash);
        _images[index] = new DamageImage(_original.ReportId, old.Sequence, target, null, null);
        RefreshImages(index);
    }

    private void RemoveImage()
    {
        var index = _imageList.SelectedIndex;
        if (index < 0 || index >= _images.Count) return;
        var item = _images[index];
        _images.RemoveAt(index);
        string oldHash = string.Empty;
        if (File.Exists(item.LocalPath))
            try { oldHash = ImageHashService.Sha256File(item.LocalPath); } catch { }
        if (string.IsNullOrWhiteSpace(oldHash)) oldHash = SyncCacheStore.GetImageHash(item.ReportId, item.Sequence);
        if (!string.IsNullOrWhiteSpace(oldHash)) _hashes.Remove(oldHash);
        for (var i = 0; i < _images.Count; i++) _images[i] = _images[i] with { Sequence = i + 1 };
        RefreshImages(Math.Min(index, _images.Count - 1));
    }

    private void RefreshImages(int preferredIndex = 0)
    {
        _imageList.Items.Clear();
        foreach (var image in _images)
            _imageList.Items.Add($"Ảnh {image.Sequence}: {Path.GetFileName(image.LocalPath)}{(string.IsNullOrWhiteSpace(image.DriveFileId) ? " (local)" : "")}");
        if (_imageList.Items.Count > 0) _imageList.SelectedIndex = Math.Clamp(preferredIndex, 0, _imageList.Items.Count - 1);
        else { _preview.Image?.Dispose(); _preview.Image = null; }
    }

    private async Task ShowPreviewAsync()
    {
        _preview.Image?.Dispose();
        _preview.Image = null;
        var index = _imageList.SelectedIndex;
        if (index < 0 || index >= _images.Count) return;
        var current = _images[index];
        if (!File.Exists(current.LocalPath) && !string.IsNullOrWhiteSpace(current.DriveFileId) && GoogleService.IsConnected())
        {
            try
            {
                current = await RemoteImageCache.EnsureLocalAsync(current);
                _images[index] = current;
            }
            catch (Exception ex)
            {
                AppLog.Exception("REMOTE_IMAGE_DOWNLOAD_FAILED", ex, new Dictionary<string, object?> { ["report_id"] = current.ReportId, ["sequence"] = current.Sequence });
                return;
            }
        }
        if (!File.Exists(current.LocalPath)) return;
        try { using var image = Image.FromFile(current.LocalPath); _preview.Image = new Bitmap(image); } catch { }
    }

    private void SaveChanges()
    {
        if (!LookupSku())
        {
            _sku.Focus();
            return;
        }
        if (!LocationNormalizer.TryNormalize(_location.Text, out var location, out var error))
        {
            MessageBox.Show(this, error, "Kiểm tra vị trí", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _location.Focus();
            return;
        }
        _location.Text = location;
        if (string.IsNullOrWhiteSpace(_shift.SelectedShift))
        {
            MessageBox.Show(this, "Chưa chọn ca.", "Thiếu thông tin");
            return;
        }

        UpdatedReport = _original with
        {
            OccurredDate = _date.Value.Date,
            Hour = _time.Hour,
            Minute = _time.Minute,
            Shift = _shift.SelectedShift,
            Sku = _sku.Text.Trim(),
            ProductName = _name.Text,
            Location = location,
            Quantity = decimal.Truncate(_qty.Value),
            BaseUnit = _base.Text
        };
        _saved = true;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void CleanupCanceledCopies()
    {
        if (_saved) return;
        foreach (var path in _newFiles)
            try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static GroupBox Section(string title) => new()
    {
        Text = title,
        Dock = DockStyle.Top,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Padding = new Padding(12),
        Margin = new Padding(0, 0, 0, 16),
        Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold)
    };

    private static TableLayoutPanel FormTable()
    {
        var table = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, ColumnCount = 2, RowCount = 0, Padding = new Padding(8), Font = new Font("Segoe UI", 10F) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return table;
    }

    private static void AddRow(TableLayoutPanel table, string label, Control control)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var title = new Label { Text = label, AutoSize = true, Padding = new Padding(0, 8, 12, 14), Margin = new Padding(0, 4, 0, 4) };
        control.Margin = new Padding(3, 4, 3, 12);
        if (control.Dock == DockStyle.None && control is not TimeWheelPicker && control is not ExclusiveShiftPicker) control.Dock = DockStyle.Top;
        table.Controls.Add(title, 0, row);
        table.Controls.Add(control, 1, row);
    }
}
