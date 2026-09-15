namespace PickfaceDamage1291;

internal sealed class ReportDetailForm : Form
{
    private readonly DamageReport _report;
    private readonly List<DamageImage> _images;
    private readonly ListBox _imageList = new();
    private readonly PictureBox _preview = new();
    private readonly Label _imageStatus = new();
    private CancellationTokenSource? _imageLoadCts;

    public ReportDetailForm(DamageReport report, IReadOnlyList<DamageImage> images)
    {
        _report = report;
        _images = images.OrderBy(x => x.Sequence).ToList();

        Text = $"Chi tiết phiếu - {report.Sku}";
        Width = 1040;
        Height = 760;
        MinimumSize = new Size(760, 560);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10F);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(16) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(BuildInfo(), 0, 0);
        root.Controls.Add(BuildImages(), 0, 1);
        Controls.Add(root);

        Load += async (_, _) =>
        {
            PopulateImages();
            if (_imageList.Items.Count > 0)
            {
                _imageList.SelectedIndex = 0;
                await LoadSelectedImageAsync();
            }
        };
        FormClosed += (_, _) =>
        {
            _imageLoadCts?.Cancel();
            _imageLoadCts?.Dispose();
            _preview.Image?.Dispose();
        };
    }

    private Control BuildInfo()
    {
        var box = new GroupBox { Text = "Thông tin phiếu", Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(12) };
        var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 4, Padding = new Padding(8) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        AddPair(table, "SKU", _report.Sku, "Tên sản phẩm", _report.ProductName);
        AddPair(table, "Vị trí", _report.Location, "Số lượng", decimal.Truncate(_report.Quantity).ToString("0") + " " + _report.BaseUnit);
        AddPair(table, "Thời gian phát hiện", $"{_report.Hour:00}:{_report.Minute:00} {_report.OccurredDate:dd/MM/yyyy}", "Ca", _report.Shift);
        AddPair(table, "Người tạo", string.IsNullOrWhiteSpace(_report.CreatedBy) ? "-" : _report.CreatedBy, "Phiên bản", _report.Version.ToString());
        AddPair(table, "Thời gian nhập", _report.CreatedAt.ToString("HH:mm:ss dd/MM/yyyy"), "Thời gian đồng bộ", _report.SyncedAt?.ToString("HH:mm:ss dd/MM/yyyy") ?? "-");
        AddPair(table, "Trạng thái", DisplayStatus(_report.SyncStatus), "Cập nhật bởi", string.IsNullOrWhiteSpace(_report.UpdatedBy) ? "-" : _report.UpdatedBy!);
        if (!string.IsNullOrWhiteSpace(_report.LastError)) AddPair(table, "Lỗi đồng bộ", _report.LastError!, "", "");

        box.Controls.Add(table);
        return box;
    }

    private Control BuildImages()
    {
        var box = new GroupBox { Text = "Hình ảnh", Dock = DockStyle.Fill, Padding = new Padding(12) };
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _imageList.Dock = DockStyle.Fill;
        _imageList.IntegralHeight = false;
        _imageList.SelectedIndexChanged += async (_, _) => await LoadSelectedImageAsync();
        _preview.Dock = DockStyle.Fill;
        _preview.BackColor = Color.White;
        _preview.BorderStyle = BorderStyle.FixedSingle;
        _preview.SizeMode = PictureBoxSizeMode.Zoom;
        _imageStatus.AutoSize = true;
        _imageStatus.Padding = new Padding(4, 8, 4, 4);

        root.Controls.Add(_imageList, 0, 0);
        root.Controls.Add(_preview, 1, 0);
        root.Controls.Add(_imageStatus, 0, 1);
        root.SetColumnSpan(_imageStatus, 2);
        box.Controls.Add(root);
        return box;
    }

    private void PopulateImages()
    {
        _imageList.Items.Clear();
        foreach (var image in _images)
        {
            var source = !string.IsNullOrWhiteSpace(image.DriveFileId) ? "Google Drive" : File.Exists(image.LocalPath) ? "Local" : "Chưa đồng bộ";
            _imageList.Items.Add(new ImageItem(image, $"Ảnh {image.Sequence} • {source}"));
        }
        if (_images.Count == 0) _imageStatus.Text = "Phiếu không có ảnh.";
    }

    private async Task LoadSelectedImageAsync()
    {
        if (_imageList.SelectedItem is not ImageItem item) return;
        _imageLoadCts?.Cancel();
        _imageLoadCts?.Dispose();
        _imageLoadCts = new CancellationTokenSource();
        var ct = _imageLoadCts.Token;

        _preview.Image?.Dispose();
        _preview.Image = null;

        try
        {
            // Show the local copy immediately when available. If the report has a Drive file,
            // fetch the cloud copy too so the detail viewer can still work after local files are moved/removed.
            if (File.Exists(item.Image.LocalPath))
            {
                using var source = Image.FromFile(item.Image.LocalPath);
                _preview.Image = new Bitmap(source);
                _imageStatus.Text = "Đang tải bản lưu trên Google Drive...";
            }
            else
            {
                _imageStatus.Text = "Đang tải ảnh từ Google Drive...";
            }

            if (!string.IsNullOrWhiteSpace(item.Image.DriveFileId) && GoogleService.IsConnected())
            {
                var cloud = await GoogleImageService.DownloadAsync(item.Image.DriveFileId!, ct);
                ct.ThrowIfCancellationRequested();
                using var ms = new MemoryStream(cloud.Bytes);
                using var source = Image.FromStream(ms);
                var bitmap = new Bitmap(source);
                _preview.Image?.Dispose();
                _preview.Image = bitmap;
                _imageStatus.Text = $"Ảnh {item.Image.Sequence} • Google Drive • {cloud.FileName}";
                return;
            }

            if (_preview.Image is not null)
            {
                _imageStatus.Text = $"Ảnh {item.Image.Sequence} • bản local";
                return;
            }

            _imageStatus.Text = GoogleService.IsConnected()
                ? "Không tìm thấy dữ liệu ảnh để hiển thị."
                : "Không có ảnh local và hiện đang offline nên chưa thể tải từ Google Drive.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (_preview.Image is not null)
                _imageStatus.Text = "Đang hiển thị bản local; chưa tải được bản Google: " + ex.Message;
            else
                _imageStatus.Text = "Không hiển thị được ảnh: " + ex.Message;
        }
    }

    private static void AddPair(TableLayoutPanel table, string label1, string value1, string label2, string value2)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(NewLabel(label1, true), 0, row);
        table.Controls.Add(NewValue(value1), 1, row);
        table.Controls.Add(NewLabel(label2, true), 2, row);
        table.Controls.Add(NewValue(value2), 3, row);
    }

    private static Label NewLabel(string text, bool title) => new()
    {
        Text = text,
        AutoSize = true,
        Font = title ? new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold) : new Font("Segoe UI", 9.5F),
        Padding = new Padding(0, 6, 8, 8)
    };

    private static TextBox NewValue(string value) => new()
    {
        Text = value,
        ReadOnly = true,
        BackColor = Color.White,
        BorderStyle = BorderStyle.FixedSingle,
        Dock = DockStyle.Top,
        Margin = new Padding(3, 3, 12, 8)
    };

    private static string DisplayStatus(string status) => status switch
    {
        "SYNCED" => "Đã đồng bộ",
        "SYNCING" => "Đang đồng bộ",
        "ERROR" => "Lỗi đồng bộ",
        "OFFLINE_PENDING" => "Chờ online",
        _ => "Chờ đồng bộ"
    };

    private sealed record ImageItem(DamageImage Image, string Text)
    {
        public override string ToString() => Text;
    }
}
