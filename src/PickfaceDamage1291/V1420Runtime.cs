using DocumentFormat.OpenXml.Packaging;

namespace PickfaceDamage1291;

internal static class V1420Runtime
{
    public static void Apply(MainForm main)
    {
        try
        {
            PatchBbbgButton(main);
            main.Shown += (_, _) => PatchBbbgButton(main);
            AppLog.Info("V1420_RUNTIME_APPLIED", "Đã áp dụng sửa xuất BBBG Inventory v1.4.20.");
        }
        catch (Exception ex)
        {
            AppLog.Exception("V1420_RUNTIME_APPLY_FAILED", ex);
        }
    }

    private static void PatchBbbgButton(MainForm main)
    {
        var old = FindAll<Button>(main).FirstOrDefault(x => Equals(x.Tag, "v1419-export-bbbg"));
        if (old is null) return;
        if (FindAll<Button>(main).Any(x => Equals(x.Tag, "v1420-export-bbbg"))) return;

        var parent = old.Parent;
        if (parent is null) return;
        var index = parent.Controls.GetChildIndex(old);
        old.Visible = false;

        var button = new Button
        {
            Text = "Xuất BBBG Inventory",
            AutoSize = true,
            MinimumSize = new Size(0, 42),
            Tag = "v1420-export-bbbg"
        };
        AppUiStyle.StyleButton(button, ButtonVisual.Normal);
        button.Click += async (_, _) => await ExportBbbgAsync(main, button);
        parent.Controls.Add(button);
        parent.Controls.SetChildIndex(button, Math.Max(0, index));
    }

    private static async Task ExportBbbgAsync(MainForm main, Button button)
    {
        var status = Field<Label>(main, "_reportStatus");
        try
        {
            button.Enabled = false;
            if (!await EnsureFreshDataAsync(main, status)) return;

            var all = Database.GetReports(int.MaxValue);
            if (all.Count == 0)
            {
                NotificationCenter.Show(main, "Chưa có dữ liệu để xuất biên bản.", "Xuất BBBG Inventory", MessageBoxIcon.Information);
                return;
            }

            using var select = new BbbgExportSelectionDialogV1419(all);
            if (select.ShowDialog(main) != DialogResult.OK) return;

            var shifts = select.SelectedShifts.ToList();
            var defaultName = shifts.Count == 1
                ? $"BBBG Inventory Pickface 1291 ngày {select.SelectedDate:ddMMyyyy} - {shifts[0]}.docx"
                : $"BBBG Inventory Pickface 1291 ngày {select.SelectedDate:ddMMyyyy}.docx";

            using var save = new SaveFileDialog
            {
                Filter = "Word Document (*.docx)|*.docx",
                DefaultExt = "docx",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = defaultName,
                Title = "Lưu BBBG Inventory Pickface 1291"
            };
            if (save.ShowDialog(main) != DialogResult.OK || string.IsNullOrWhiteSpace(save.FileName)) return;

            var outputs = BuildOutputPaths(save.FileName, shifts);
            var existing = outputs.Where(x => File.Exists(x.Path)).Select(x => Path.GetFileName(x.Path)).ToList();
            if (existing.Count > 0)
            {
                var confirm = MessageBox.Show(
                    main,
                    "Các file sau đã tồn tại và sẽ được ghi đè:\n\n" + string.Join("\n", existing) + "\n\nTiếp tục?",
                    "Xác nhận ghi đè BBBG Inventory",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (confirm != DialogResult.Yes) return;
            }

            var exported = 0;
            var total = 0;
            foreach (var output in outputs)
            {
                var reports = select.Filter(all, output.Shift);
                if (status is not null) status.Text = $"Đang tạo BBBG {output.Shift}: {reports.Count:N0} phiếu...";

                await Task.Run(() => BbbgInventoryWordExporterV1420.Export(output.Path, reports));
                exported++;
                total += reports.Count;
            }

            if (status is not null) status.Text = $"Đã xuất {exported} file BBBG, {total:N0} phiếu.";
            NotificationCenter.Show(
                main,
                $"Đã tạo {exported} file Word BBBG Inventory, tổng {total:N0} dòng dữ liệu thực tế.",
                "Xuất BBBG Inventory",
                MessageBoxIcon.Information);

            AppLog.Info("EXPORT_BBBG_INVENTORY_V1420_DONE", "Đã tạo BBBG Inventory bằng hộp thoại lưu file.", new Dictionary<string, object?>
            {
                ["entry_date"] = select.SelectedDate.ToString("yyyy-MM-dd"),
                ["shifts"] = string.Join(",", shifts),
                ["file_count"] = exported,
                ["report_count"] = total
            });
        }
        catch (Exception ex)
        {
            AppLog.Exception("EXPORT_BBBG_INVENTORY_V1420_FAILED", ex);
            NotificationCenter.Show(main, ex.Message, "Không xuất được BBBG Inventory", MessageBoxIcon.Error);
        }
        finally
        {
            button.Enabled = true;
        }
    }

    private static List<(string Shift, string Path)> BuildOutputPaths(string selectedPath, IReadOnlyList<string> shifts)
    {
        if (shifts.Count == 1)
            return [(shifts[0], selectedPath)];

        var directory = Path.GetDirectoryName(selectedPath);
        if (string.IsNullOrWhiteSpace(directory)) directory = Environment.CurrentDirectory;
        var stem = Path.GetFileNameWithoutExtension(selectedPath).Trim();
        if (string.IsNullOrWhiteSpace(stem)) stem = "BBBG Inventory Pickface 1291";

        return shifts
            .Select(shift => (shift, Path.Combine(directory, $"{stem} - {shift}.docx")))
            .ToList();
    }

    private static async Task<bool> EnsureFreshDataAsync(MainForm main, Label? status)
    {
        if (GoogleService.IsConnected())
        {
            try
            {
                if (status is not null) status.Text = "Đang nhận dữ liệu mới nhất trước khi xuất BBBG Inventory...";
                await CloudSyncService.PullSharedDataAsync(new Progress<string>(s =>
                {
                    if (status is not null) status.Text = s;
                }));
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Exception("EXPORT_PRE_SYNC_V1420_FAILED", ex);
                return MessageBox.Show(
                    main,
                    "Không nhận được dữ liệu mới nhất từ Google. Nếu tiếp tục, BBBG chỉ dùng dữ liệu hiện có trên máy này.\n\n" + ex.Message + "\n\nTiếp tục?",
                    "Đồng bộ trước khi xuất chưa hoàn tất",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) == DialogResult.Yes;
            }
        }

        return MessageBox.Show(
            main,
            "Ứng dụng đang offline/chưa kết nối Google. BBBG chỉ dùng dữ liệu hiện có trên máy này. Tiếp tục?",
            "Xuất dữ liệu local",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) == DialogResult.Yes;
    }

    private static T? Field<T>(object owner, string name) where T : class
        => owner.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(owner) as T;

    private static IEnumerable<T> FindAll<T>(Control root) where T : Control
    {
        if (root is T self) yield return self;
        foreach (Control child in root.Controls)
        foreach (var found in FindAll<T>(child)) yield return found;
    }
}

internal static class BbbgInventoryWordExporterV1420
{
    public static void Export(string finalPath, IReadOnlyList<DamageReport> reports)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(finalPath);
        ArgumentNullException.ThrowIfNull(reports);

        var directory = Path.GetDirectoryName(finalPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(
            string.IsNullOrWhiteSpace(directory) ? Environment.CurrentDirectory : directory,
            $".{Path.GetFileNameWithoutExtension(finalPath)}.{Guid.NewGuid():N}.tmp.docx");

        try
        {
            BbbgInventoryWordExporterV1419.Export(tempPath, reports);

            using (var verify = WordprocessingDocument.Open(tempPath, false))
            {
                if (verify.MainDocumentPart?.Document?.Body is null)
                    throw new InvalidDataException("File Word BBBG tạo ra không có nội dung hợp lệ.");
            }

            File.Move(tempPath, finalPath, true);
            AppLog.Info("EXPORT_BBBG_INVENTORY_V1420_VERIFIED", "File Word BBBG đã được kiểm tra cấu trúc trước khi lưu.", new Dictionary<string, object?>
            {
                ["file_name"] = Path.GetFileName(finalPath),
                ["report_count"] = reports.Count
            });
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch { }
        }
    }
}
