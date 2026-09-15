using ClosedXML.Excel;
using ClosedXML.Excel.Drawings;

namespace PickfaceDamage1291;

internal sealed record DamageExportResult(string Path, int ReportCount, int ImageCount, int MissingImages);

internal static class DamageReportExportService
{
    private const int ImageColumn = 8;
    private const int CanvasWidthPx = 345;
    private const int CanvasHeightPx = 190;

    public static async Task<DamageExportResult?> ExportAsync(IWin32Window owner, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var reports = Database.GetReports(int.MaxValue)
            .OrderBy(x => x.OccurredDate)
            .ThenBy(x => x.Hour)
            .ThenBy(x => x.Minute)
            .ThenBy(x => x.CreatedAt)
            .ToList();

        if (reports.Count == 0)
        {
            MessageBox.Show(owner, "Chưa có dữ liệu để xuất Excel.", "Xuất Excel", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        using var save = new SaveFileDialog
        {
            Filter = "Excel Workbook (*.xlsx)|*.xlsx",
            DefaultExt = "xlsx",
            AddExtension = true,
            FileName = $"Thông tin hàng hư hỏng Pickface 1291 ngày {DateTime.Now:MMddyyyy}.xlsx",
            Title = "Lưu danh sách hàng hỏng Pickface 1291"
        };
        if (save.ShowDialog(owner) != DialogResult.OK) return null;

        progress?.Report("Đang chuẩn bị dữ liệu xuất Excel...");
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Hàng hỏng Pickface 1291");
        ConfigureSheet(ws);

        ws.Range(1, 1, 1, 8).Merge();
        var title = ws.Cell(1, 1);
        title.Value = $"DANH SÁCH HÀNG HỎNG PICKFACE 1291 -  NGÀY {DateTime.Now:dd/MM/yyyy}";
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

            // 150 pt is about 200 px at 96 DPI. The image canvas below is intentionally
            // smaller so every picture stays inside the visual bounds of this report row.
            ws.Row(row).Height = 150;
            ws.Cell(row, 1).Value = i + 1;
            ws.Cell(row, 2).Value = report.Sku;
            ws.Cell(row, 3).Value = report.ProductName;
            ws.Cell(row, 4).Value = report.Location;
            ws.Cell(row, 5).Value = $"{report.OccurredDate:dd/MM/yyyy} {report.Hour:00}:{report.Minute:00}";
            ws.Cell(row, 6).Value = decimal.Truncate(report.Quantity);
            ws.Cell(row, 7).Value = report.BaseUnit;

            var rowRange = ws.Range(row, 1, row, 8);
            rowRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            rowRange.Style.Alignment.WrapText = true;
            ws.Range(row, 1, row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Range(row, 4, row, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // Images are always resolved from this report_id only, then ordered by the stored
            // sequence. A Drive image can therefore never be placed into another SKU's row.
            var reportImages = Database.GetImages(report.ReportId)
                .OrderBy(x => x.Sequence)
                .Take(5)
                .ToList();
            var resolved = new List<string>(reportImages.Count);
            for (var imageIndex = 0; imageIndex < reportImages.Count; imageIndex++)
            {
                ct.ThrowIfCancellationRequested();
                var image = reportImages[imageIndex];
                var current = image;
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
                            ["sequence"] = image.Sequence,
                            ["drive_file_id"] = image.DriveFileId
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
                        ["sequence"] = image.Sequence,
                        ["has_drive_file"] = !string.IsNullOrWhiteSpace(image.DriveFileId)
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
        return new DamageExportResult(save.FileName, reports.Count, totalImages, missingImages);
    }

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

                // Floor rather than round so an image can never exceed its assigned slot by
                // a rounding pixel. This guarantees that every picture remains inside column H
                // and the report row that belongs to this SKU.
                var width = Math.Max(1, (int)Math.Floor(sourceW * scale));
                var height = Math.Max(1, (int)Math.Floor(sourceH * scale));
                var x = slot.X + Math.Max(0, (slot.Width - width) / 2);
                var y = slot.Y + Math.Max(0, (slot.Height - height) / 2);

                // Keep a one-cell anchor (Move) with an explicit pixel size. Converting the same
                // picture to MoveAndSize after MoveTo produced malformed drawing anchors that
                // Microsoft Excel repaired from /xl/drawings/drawing1.xml in v1.4.2/v1.4.3.
                // One-cell anchors are stable in ClosedXML 0.102 and still keep each picture tied
                // to the image cell of the correct report row.
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
        var w = CanvasWidthPx - pad * 2;
        var h = CanvasHeightPx - pad * 2;
        var result = new List<ImageSlot>();
        if (count == 1)
        {
            result.Add(new ImageSlot(pad, pad, w, h));
            return result;
        }
        if (count == 2)
        {
            var sw = (w - gap) / 2;
            result.Add(new ImageSlot(pad, pad, sw, h));
            result.Add(new ImageSlot(pad + sw + gap, pad, sw, h));
            return result;
        }

        var rowH = (h - gap) / 2;
        var topCount = count == 5 ? 3 : 2;
        var bottomCount = count - topCount;
        AddCenteredRow(result, pad, pad, w, rowH, topCount, gap);
        if (bottomCount > 0)
            AddCenteredRow(result, pad, pad + rowH + gap, w, rowH, bottomCount, gap);
        return result;
    }

    private static void AddCenteredRow(List<ImageSlot> target, int x, int y, int width, int height, int count, int gap)
    {
        var slotW = count <= 1 ? Math.Min(width, 160) : (width - gap * (count - 1)) / count;
        var total = slotW * count + gap * (count - 1);
        var start = x + Math.Max(0, (width - total) / 2);
        for (var i = 0; i < count; i++)
            target.Add(new ImageSlot(start + i * (slotW + gap), y, slotW, height));
    }

    private sealed record ImageSlot(int X, int Y, int Width, int Height);
}
