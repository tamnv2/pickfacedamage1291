using ClosedXML.Excel;

namespace PickfaceDamage1291;

internal static class V1416Runtime
{
    public static void Apply(MainForm main)
    {
        try
        {
            ReplaceExcelExportButton(main);
            main.Shown += (_, _) => ReplaceExcelExportButton(main);
            AppLog.Info("V1416_RUNTIME_APPLIED", "Đã áp dụng định dạng Excel v1.4.16.");
        }
        catch (Exception ex)
        {
            AppLog.Exception("V1416_RUNTIME_APPLY_FAILED", ex);
        }
    }

    private static void ReplaceExcelExportButton(MainForm main)
    {
        var tabs = FindAll<TabControl>(main).FirstOrDefault();
        var tab = tabs?.TabPages.Cast<TabPage>()
            .FirstOrDefault(x => string.Equals(x.Text, "Danh sách đã nhập", StringComparison.Ordinal));
        if (tab is null) return;

        var actions = FindAll<FlowLayoutPanel>(tab)
            .FirstOrDefault(x => x.Controls.OfType<Button>().Any(b => string.Equals(b.Text, "Xuất Excel", StringComparison.Ordinal)));
        if (actions is null) return;
        if (actions.Controls.OfType<Button>().Any(x => Equals(x.Tag, "v1416-export"))) return;

        var old = actions.Controls.OfType<Button>()
            .FirstOrDefault(x => Equals(x.Tag, "v148-export"))
            ?? actions.Controls.OfType<Button>().FirstOrDefault(x => string.Equals(x.Text, "Xuất Excel", StringComparison.Ordinal) && x.Visible);
        if (old is null) return;

        var index = actions.Controls.GetChildIndex(old);
        old.Visible = false;

        var export = new Button
        {
            Text = "Xuất Excel",
            AutoSize = true,
            MinimumSize = new Size(0, 42),
            Tag = "v1416-export"
        };
        AppUiStyle.StyleButton(export, ButtonVisual.Normal);
        export.Click += async (_, _) => await ExportAsync(main, export);
        actions.Controls.Add(export);
        actions.Controls.SetChildIndex(export, Math.Max(0, index));
    }

    private static async Task ExportAsync(MainForm main, Button button)
    {
        var status = FindAll<Label>(main)
            .FirstOrDefault(x => x.Name == "_reportStatus")
            ?? GetField<Label>(main, "_reportStatus");

        try
        {
            button.Enabled = false;
            if (GoogleService.IsConnected())
            {
                try
                {
                    if (status is not null) status.Text = "Đang nhận dữ liệu mới nhất trước khi xuất Excel...";
                    await CloudSyncService.PullSharedDataAsync(new Progress<string>(s =>
                    {
                        if (status is not null) status.Text = s;
                    }));
                }
                catch (Exception ex)
                {
                    AppLog.Exception("EXPORT_PRE_SYNC_V1416_FAILED", ex);
                    if (MessageBox.Show(
                            main,
                            "Không nhận được dữ liệu mới nhất từ Google. Nếu tiếp tục, file Excel chỉ phản ánh dữ liệu hiện có trên máy này.\n\n" + ex.Message + "\n\nTiếp tục?",
                            "Đồng bộ trước khi xuất chưa hoàn tất",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning) != DialogResult.Yes) return;
                }
            }
            else if (MessageBox.Show(
                         main,
                         "Ứng dụng đang offline/chưa kết nối Google. File Excel chỉ phản ánh dữ liệu hiện có trên máy này. Tiếp tục?",
                         "Xuất dữ liệu local",
                         MessageBoxButtons.YesNo,
                         MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            var all = Database.GetReports(int.MaxValue);
            if (all.Count == 0)
            {
                NotificationCenter.Show(main, "Chưa có dữ liệu để xuất Excel.", "Xuất Excel", MessageBoxIcon.Information);
                return;
            }

            using var select = new DamageExportSelectionDialogV148(all);
            if (select.ShowDialog(main) != DialogResult.OK) return;
            var selected = select.Filter(all);
            if (selected.Count == 0)
            {
                NotificationCenter.Show(main, "Không có phiếu phù hợp ngày/ca đã chọn.", "Xuất Excel", MessageBoxIcon.Warning);
                return;
            }

            var progress = new Progress<string>(s =>
            {
                if (status is not null) status.Text = s;
            });
            var result = await DamageReportExportServiceV1416.ExportAsync(
                main,
                selected,
                select.SelectedDates,
                select.SelectedShiftLabel,
                progress);
            if (result is null) return;

            if (status is not null) status.Text = $"Đã xuất {result.ReportCount:N0} phiếu.";
            var imageNote = result.MissingImages > 0 ? $" Không tải/nhúng được {result.MissingImages:N0} ảnh." : string.Empty;
            NotificationCenter.Show(main,
                $"Đã xuất {result.ReportCount:N0} phiếu và {result.ImageCount:N0} ảnh.{imageNote}",
                "Xuất Excel",
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppLog.Exception("EXPORT_EXCEL_V1416_FAILED", ex);
            NotificationCenter.Show(main, ex.Message, "Không xuất được Excel", MessageBoxIcon.Error);
        }
        finally
        {
            button.Enabled = true;
        }
    }

    private static T? GetField<T>(object owner, string name) where T : class
        => owner.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(owner) as T;

    private static IEnumerable<T> FindAll<T>(Control root) where T : Control
    {
        if (root is T self) yield return self;
        foreach (Control child in root.Controls)
        foreach (var found in FindAll<T>(child)) yield return found;
    }
}

internal static class DamageReportExportServiceV1416
{
    public static async Task<DamageExportResultV148?> ExportAsync(
        IWin32Window owner,
        IReadOnlyList<DamageReport> selectedReports,
        IReadOnlyList<DateTime> selectedEntryDates,
        string selectedShiftLabel,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var result = await DamageReportExportServiceV148.ExportAsync(
            owner,
            selectedReports,
            selectedEntryDates,
            selectedShiftLabel,
            progress,
            ct);
        if (result is null) return null;

        progress?.Report("Đang hoàn thiện định dạng Excel...");
        ApplyOwnerFormat(result.Path, selectedReports, ct);
        progress?.Report("Đã hoàn thiện định dạng Excel.");
        return result;
    }

    private static void ApplyOwnerFormat(string path, IReadOnlyList<DamageReport> selectedReports, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var reports = selectedReports
            .OrderBy(x => x.OccurredDate)
            .ThenBy(x => x.Hour)
            .ThenBy(x => x.Minute)
            .ThenBy(x => x.CreatedAt)
            .ToList();
        var lastRow = reports.Count + 2;
        const int shiftColumn = 9;

        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheet(1);

        // Expand title from A:H to A:I while keeping the image canvas in column H.
        ws.Range(1, 1, 1, 8).Unmerge();
        ws.Range(1, 1, 1, shiftColumn).Merge();

        ws.Cell(2, shiftColumn).Value = "Ca ghi nhận";
        ws.Cell(2, shiftColumn).Style.Fill.BackgroundColor = XLColor.LightGray;
        ws.Cell(2, shiftColumn).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(2, shiftColumn).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        for (var i = 0; i < reports.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var row = i + 3;
            ws.Cell(row, shiftColumn).Value = reports[i].Shift;
            ws.Cell(row, shiftColumn).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, shiftColumn).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Cell(row, shiftColumn).Style.Alignment.WrapText = true;
        }

        ws.Column(shiftColumn).Width = 16;

        // OWNER format: Aptos 14, all bold. Only the list title is Aptos 25.
        var full = ws.Range(1, 1, lastRow, shiftColumn);
        full.Style.Font.FontName = "Aptos";
        full.Style.Font.FontSize = 14;
        full.Style.Font.Bold = true;

        var title = ws.Range(1, 1, 1, shiftColumn);
        title.Style.Font.FontName = "Aptos";
        title.Style.Font.FontSize = 25;
        title.Style.Font.Bold = true;
        title.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        title.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Row(1).Height = 42;

        // Gridlines are hidden. Borders start at the header row; the merged title row has none.
        ws.ShowGridLines = false;
        ClearBorders(full);
        var content = ws.Range(2, 1, lastRow, shiftColumn);
        content.Style.Border.TopBorder = XLBorderStyleValues.Thin;
        content.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        content.Style.Border.LeftBorder = XLBorderStyleValues.Thin;
        content.Style.Border.RightBorder = XLBorderStyleValues.Thin;

        ws.PageSetup.PrintAreas.Clear();
        ws.PageSetup.PrintAreas.Add(1, 1, lastRow, shiftColumn);
        wb.Save();

        AppLog.Info("EXPORT_EXCEL_V1416_FORMAT_APPLIED", "Đã áp dụng định dạng Excel theo yêu cầu OWNER.", new Dictionary<string, object?>
        {
            ["rows"] = reports.Count,
            ["font"] = "Aptos",
            ["font_size"] = 14,
            ["title_size"] = 25,
            ["shift_column"] = shiftColumn,
            ["gridlines"] = false
        });
    }

    private static void ClearBorders(IXLRange range)
    {
        // ClosedXML 0.102 does not expose InsideHorizontal/InsideVertical on IXLBorder.
        // Clear every cell explicitly so the title row is guaranteed border-free and the
        // content border can then be applied cleanly from row 2 downward.
        foreach (var cell in range.Cells())
        {
            cell.Style.Border.TopBorder = XLBorderStyleValues.None;
            cell.Style.Border.BottomBorder = XLBorderStyleValues.None;
            cell.Style.Border.LeftBorder = XLBorderStyleValues.None;
            cell.Style.Border.RightBorder = XLBorderStyleValues.None;
        }
    }
}
