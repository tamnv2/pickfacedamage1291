using ClosedXML.Excel;
using ClosedXML.Excel.Drawings;

namespace PickfaceDamage1291;

internal sealed record DamageExportResultV148(string Path, int ReportCount, int ImageCount, int MissingImages);

internal sealed class DamageExportSelectionDialogV148 : Form
{
    private readonly CheckedListBox _dates = new();
    private readonly CheckBox _shift1 = new() { Text = "Ca 1", AutoSize = true, Checked = true };
    private readonly CheckBox _shift2 = new() { Text = "Ca 2", AutoSize = true, Checked = true };

    public DamageExportSelectionDialogV148(IReadOnlyList<DamageReport> reports)
    {
        Text = "Chọn phạm vi xuất Excel";
        Width = 560;
        Height = 560;
        MinimumSize = new Size(520, 500);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = 6
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(new Label
        {
            Text = "Chọn một hoặc nhiều ngày nhập thực tế cần xuất:",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 8)
        }, 0, 0);

        _dates.Dock = DockStyle.Fill;
        _dates.CheckOnClick = true;
        _dates.IntegralHeight = false;
        var byDate = reports
            .GroupBy(x => x.CreatedAt.ToLocalTime().Date)
            .OrderByDescending(x => x.Key)
            .Select(x => new DateChoice(x.Key, x.Count()))
            .ToList();
        foreach (var choice in byDate) _dates.Items.Add(choice, choice.Date == DateTime.Today);
        if (_dates.CheckedItems.Count == 0 && _dates.Items.Count > 0) _dates.SetItemChecked(0, true);
        root.Controls.Add(_dates, 0, 1);

        var dateActions = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            WrapContents = true,
            Padding = new Padding(0, 6, 0, 4)
        };
        var allDates = new Button { Text = "Chọn tất cả ngày", AutoSize = true };
        var clearDates = new Button { Text = "Bỏ chọn ngày", AutoSize = true };
        AppUiStyle.StyleButton(allDates, ButtonVisual.Normal);
        AppUiStyle.StyleButton(clearDates, ButtonVisual.Normal);
        allDates.Click += (_, _) =>
        {
            for (var i = 0; i < _dates.Items.Count; i++) _dates.SetItemChecked(i, true);
        };
        clearDates.Click += (_, _) =>
        {
            for (var i = 0; i < _dates.Items.Count; i++) _dates.SetItemChecked(i, false);
        };
        dateActions.Controls.AddRange([allDates, clearDates]);
        root.Controls.Add(dateActions, 0, 2);

        var shifts = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            WrapContents = true,
            Padding = new Padding(0, 7, 0, 4)
        };
        shifts.Controls.Add(new Label
        {
            Text = "Ca ghi nhận:",
            AutoSize = true,
            Padding = new Padding(0, 5, 12, 0),
            Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold)
        });
        _shift1.Margin = new Padding(2, 5, 14, 4);
        _shift2.Margin = new Padding(2, 5, 14, 4);
        shifts.Controls.AddRange([_shift1, _shift2]);
        root.Controls.Add(shifts, 0, 3);

        root.Controls.Add(new Label
        {
            Text = "Lọc ngày ở đây dùng thời gian nhập thực tế. Trong file Excel, cột Thời gian phát hiện vẫn giữ đúng ngày/giờ phát hiện hư hỏng đã ghi nhận.",
            AutoSize = true,
            MaximumSize = new Size(490, 0),
            ForeColor = Color.DimGray,
            Margin = new Padding(0, 4, 0, 10)
        }, 0, 4);

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 5, 0, 0)
        };
        var ok = new Button { Text = "Xuất Excel", AutoSize = true };
        var cancel = new Button { Text = "Hủy", AutoSize = true, DialogResult = DialogResult.Cancel };
        AppUiStyle.StyleButton(ok, ButtonVisual.Primary);
        AppUiStyle.StyleButton(cancel, ButtonVisual.Normal);
        ok.Click += (_, _) =>
        {
            if (_dates.CheckedItems.Count == 0)
            {
                NotificationCenter.Show(this, "Chọn ít nhất một ngày nhập thực tế.", "Xuất Excel", MessageBoxIcon.Warning);
                return;
            }
            if (!_shift1.Checked && !_shift2.Checked)
            {
                NotificationCenter.Show(this, "Chọn Ca 1, Ca 2 hoặc cả hai ca.", "Xuất Excel", MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        };
        actions.Controls.AddRange([ok, cancel]);
        root.Controls.Add(actions, 0, 5);

        Controls.Add(root);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public List<DamageReport> Filter(IReadOnlyList<DamageReport> reports)
    {
        var selectedDates = _dates.CheckedItems.Cast<DateChoice>().Select(x => x.Date).ToHashSet();
        return reports
            .Where(x => selectedDates.Contains(x.CreatedAt.ToLocalTime().Date))
            .Where(x =>
                (_shift1.Checked && string.Equals(x.Shift.Trim(), "Ca 1", StringComparison.OrdinalIgnoreCase)) ||
                (_shift2.Checked && string.Equals(x.Shift.Trim(), "Ca 2", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(x => x.OccurredDate)
            .ThenBy(x => x.Hour)
            .ThenBy(x => x.Minute)
            .ThenBy(x => x.CreatedAt)
            .ToList();
    }

    private sealed record DateChoice(DateTime Date, int Count)
    {
        public override string ToString() => $"{Date:dd/MM/yyyy}  —  {Count:N0} phiếu";
    }
}

internal static class DamageReportExportServiceV148
{
    private const int ImageColumn = 8;
    private const int CanvasWidthPx = 345;
    private const int CanvasHeightPx = 190;

    public static async Task<DamageExportResultV148?> ExportAsync(
        IWin32Window owner,
        IReadOnlyList<DamageReport> selectedReports,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var reports = selectedReports
            .OrderBy(x => x.OccurredDate)
            .ThenBy(x => x.Hour)
            .ThenBy(x => x.Minute)
            .ThenBy(x => x.CreatedAt)
            .ToList();

        if (reports.Count == 0)
        {
            NotificationCenter.Show(owner as Form, "Không có dữ liệu phù hợp để xuất Excel.", "Xuất Excel", MessageBoxIcon.Information);
            return null;
        }

        var firstDetected = DetectionTime(reports[0]);
        var lastDetected = DetectionTime(reports[^1]);
        var oneDetectionDate = firstDetected.Date == lastDetected.Date;
        var fileDateText = oneDetectionDate
            ? firstDetected.ToString("ddMMyyyy")
            : $"{firstDetected:ddMMyyyy}-{lastDetected:ddMMyyyy}";
        var titleDateText = oneDetectionDate
            ? $"NGÀY {firstDetected:dd/MM/yyyy}"
            : $"TỪ {firstDetected:dd/MM/yyyy} ĐẾN {lastDetected:dd/MM/yyyy}";

        using var save = new SaveFileDialog
        {
            Filter = "Excel Workbook (*.xlsx)|*.xlsx",
            DefaultExt = "xlsx",
            AddExtension = true,
            FileName = oneDetectionDate
                ? $"Thông tin hàng hư hỏng Pickface 1291 ngày {fileDateText}.xlsx"
                : $"Thông tin hàng hư hỏng Pickface 1291 từ {fileDateText}.xlsx",
            Title = "Lưu danh sách hàng hỏng Pickface 1291"
        };
        if (save.ShowDialog(owner) != DialogResult.OK) return null;

        progress?.Report("Đang chuẩn bị dữ liệu xuất Excel...");
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Hàng hỏng Pickface 1291");
        ConfigureSheet(ws);

        ws.Range(1, 1, 1, 8).Merge();
        var title = ws.Cell(1, 1);
        title.Value = $"DANH SÁCH HÀNG HỎNG PICKFACE 1291 - {titleDateText}";
        title.Style.Font.Bold = true;
        title.Style.Font.FontSize = 16;
        title.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        title.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Row(1).Height = 30;

        var headers = new[] { "STT", "SKU", "Tên sản phẩm", "Vị trí phát hiện", "Thời gian phát hiện", "Số lượng", "Base Units", "Hình ảnh" };
        for (var col = 1; col <= headers.Length; col++)
        {
            var cell = ws.Cell(2, col);
            cell.Value = headers[col - 1];
            cell.Style.Font.Bold = true;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
        }
        ws.Row(2).Height = 28;

        var totalImages = 0;
        var missingImages = 0;
        for (var i = 0; i < reports.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var report = reports[i];
            var row = i + 3;
            progress?.Report($"Đang xuất {i + 1:N0}/{reports.Count:N0}: SKU {report.Sku}");

            ws.Row(row).Height = 150;
            ws.Cell(row, 1).Value = i + 1;
            ws.Cell(row, 2).Value = report.Sku;
            ws.Cell(row, 3).Value = report.ProductName;
            ws.Cell(row, 4).Value = report.Location;
            ws.Cell(row, 5).Value = DetectionTime(report);
            ws.Cell(row, 5).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";
            ws.Cell(row, 6).Value = decimal.Truncate(report.Quantity);
            ws.Cell(row, 7).Value = report.BaseUnit;

            var rowRange = ws.Range(row, 1, row, 8);
            rowRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            rowRange.Style.Alignment.WrapText = true;
            ws.Range(row, 1, row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Range(row, 4, row, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var reportImages = Database.GetImages(report.ReportId)
                .OrderBy(x => x.Sequence)
                .Take(5)
                .ToList();
            var resolved = new List<string>(reportImages.Count);
            for (var imageIndex = 0; imageIndex < reportImages.Count; imageIndex++)
            {
                ct.ThrowIfCancellationRequested();
                var current = reportImages[imageIndex];
                if (!File.Exists(current.LocalPath) && !string.IsNullOrWhiteSpace(current.DriveFileId) && GoogleService.IsConnected())
                {
                    try
                    {
                        progress?.Report($"Đang tải ảnh {imageIndex + 1:N0}/{reportImages.Count:N0} cho SKU {report.Sku}...");
                        current = await RemoteImageCache.EnsureLocalAsync(current, ct);
                    }
                    catch (Exception ex)
                    {
                        missingImages++;
                        AppLog.Exception("EXPORT_IMAGE_DOWNLOAD_FAILED", ex, new Dictionary<string, object?>
                        {
                            ["report_id"] = report.ReportId,
                            ["sku"] = report.Sku,
                            ["sequence"] = current.Sequence,
                            ["drive_file_id"] = current.DriveFileId
                        });
                        continue;
                    }
                }

                if (File.Exists(current.LocalPath))
                {
                    resolved.Add(current.LocalPath);
                    AppLog.Info("EXPORT_IMAGE_RESOLVED", "Đã ghép ảnh đúng phiếu để xuất Excel.", new Dictionary<string, object?>
                    {
                        ["report_id"] = report.ReportId,
                        ["sku"] = report.Sku,
                        ["sequence"] = current.Sequence,
                        ["has_drive_file"] = !string.IsNullOrWhiteSpace(current.DriveFileId)
                    });
                }
                else
                {
                    missingImages++;
                }
            }

            progress?.Report($"Đang chèn ảnh vào Excel cho SKU {report.Sku}...");
            var added = AddPictures(ws, row, resolved, report.ReportId, report.Sku);
            totalImages += added;
            missingImages += Math.Max(0, resolved.Count - added);
            if (resolved.Count == 0 && reportImages.Count > 0)
            {
                ws.Cell(row, ImageColumn).Value = "Không tải được hình ảnh khi xuất.";
                ws.Cell(row, ImageColumn).Style.Font.FontColor = XLColor.DarkRed;
            }
        }

        var used = ws.Range(1, 1, reports.Count + 2, 8);
        used.Style.Border.TopBorder = XLBorderStyleValues.Thin;
        used.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        used.Style.Border.LeftBorder = XLBorderStyleValues.Thin;
        used.Style.Border.RightBorder = XLBorderStyleValues.Thin;
        used.Style.Font.FontName = "Arial";
        ws.SheetView.FreezeRows(2);
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.FitToPages(1, 0);
        ws.PageSetup.Margins.Top = 0.35;
        ws.PageSetup.Margins.Bottom = 0.35;
        ws.PageSetup.Margins.Left = 0.25;
        ws.PageSetup.Margins.Right = 0.25;
        ws.PageSetup.CenterHorizontally = true;
        ws.PageSetup.PrintAreas.Add(1, 1, reports.Count + 2, 8);

        progress?.Report("Đang ghi file Excel...");
        wb.SaveAs(save.FileName);
        progress?.Report("Đã xuất Excel.");
        return new DamageExportResultV148(save.FileName, reports.Count, totalImages, missingImages);
    }

    private static DateTime DetectionTime(DamageReport report)
        => report.OccurredDate.Date
            .AddHours(Math.Clamp(report.Hour, 0, 23))
            .AddMinutes(Math.Clamp(report.Minute, 0, 59));

    private static void ConfigureSheet(IXLWorksheet ws)
    {
        ws.Column(1).Width = 7;
        ws.Column(2).Width = 16;
        ws.Column(3).Width = 42;
        ws.Column(4).Width = 22;
        ws.Column(5).Width = 24;
        ws.Column(6).Width = 12;
        ws.Column(7).Width = 15;
        ws.Column(8).Width = 50;
        ws.Style.Font.FontName = "Arial";
        ws.Style.Font.FontSize = 11;
    }

    private static int AddPictures(IXLWorksheet ws, int row, IReadOnlyList<string> paths, string reportId, string sku)
    {
        var valid = paths.Take(5).Where(File.Exists).ToList();
        if (valid.Count == 0) return 0;

        var slots = BuildSlots(valid.Count);
        var added = 0;
        for (var i = 0; i < valid.Count; i++)
        {
            try
            {
                var picture = ws.AddPicture(valid[i]);
                var slot = slots[i];
                var sourceW = Math.Max(1, picture.OriginalWidth);
                var sourceH = Math.Max(1, picture.OriginalHeight);
                var scale = Math.Min(slot.Width / (double)sourceW, slot.Height / (double)sourceH);
                scale = Math.Min(1d, Math.Max(0.02d, scale));
                var width = Math.Max(1, (int)Math.Floor(sourceW * scale));
                var height = Math.Max(1, (int)Math.Floor(sourceH * scale));
                var x = slot.X + Math.Max(0, (slot.Width - width) / 2);
                var y = slot.Y + Math.Max(0, (slot.Height - height) / 2);

                picture.WithPlacement(XLPicturePlacement.Move);
                picture.WithSize(width, height);
                picture.MoveTo(ws.Cell(row, ImageColumn), x, y);
                added++;
            }
            catch (Exception ex)
            {
                AppLog.Exception("EXPORT_IMAGE_EMBED_FAILED", ex, new Dictionary<string, object?>
                {
                    ["report_id"] = reportId,
                    ["sku"] = sku,
                    ["image_index"] = i + 1,
                    ["extension"] = Path.GetExtension(valid[i])
                });
            }
        }
        return added;
    }

    private static List<ImageSlot> BuildSlots(int count)
    {
        const int gap = 6;
        const int pad = 6;
        var width = CanvasWidthPx - pad * 2;
        var height = CanvasHeightPx - pad * 2;
        var result = new List<ImageSlot>();
        if (count == 1)
        {
            result.Add(new ImageSlot(pad, pad, width, height));
            return result;
        }
        if (count == 2)
        {
            var slotWidth = (width - gap) / 2;
            result.Add(new ImageSlot(pad, pad, slotWidth, height));
            result.Add(new ImageSlot(pad + slotWidth + gap, pad, slotWidth, height));
            return result;
        }

        var rowHeight = (height - gap) / 2;
        var topCount = count == 5 ? 3 : 2;
        var bottomCount = count - topCount;
        AddCenteredRow(result, pad, pad, width, rowHeight, topCount, gap);
        if (bottomCount > 0)
            AddCenteredRow(result, pad, pad + rowHeight + gap, width, rowHeight, bottomCount, gap);
        return result;
    }

    private static void AddCenteredRow(List<ImageSlot> target, int x, int y, int width, int height, int count, int gap)
    {
        var slotWidth = count <= 1 ? Math.Min(width, 160) : (width - gap * (count - 1)) / count;
        var total = slotWidth * count + gap * (count - 1);
        var start = x + Math.Max(0, (width - total) / 2);
        for (var i = 0; i < count; i++)
            target.Add(new ImageSlot(start + i * (slotWidth + gap), y, slotWidth, height));
    }

    private sealed record ImageSlot(int X, int Y, int Width, int Height);
}
