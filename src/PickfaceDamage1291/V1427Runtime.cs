using System.Globalization;

namespace PickfaceDamage1291;

internal static class V1427Runtime
{
    public static void Apply(MainForm main)
    {
        try
        {
            PatchBbbgButton(main);
            main.Shown += (_, _) => PatchBbbgButton(main);
            AppLog.Info("V1427_RUNTIME_APPLIED", "Đã áp dụng hộp chọn BBBG chống cắt nút theo DPI v1.4.27.");
        }
        catch (Exception ex)
        {
            AppLog.Exception("V1427_RUNTIME_APPLY_FAILED", ex);
        }
    }

    private static void PatchBbbgButton(MainForm main)
    {
        var current = FindAll<Button>(main).FirstOrDefault(x => Equals(x.Tag, "v1420-export-bbbg"));
        if (current is null) return;
        if (FindAll<Button>(main).Any(x => Equals(x.Tag, "v1427-export-bbbg"))) return;

        var parent = current.Parent;
        if (parent is null) return;
        var index = parent.Controls.GetChildIndex(current);
        current.Visible = false;

        var button = new Button
        {
            Text = "Xuất BBBG Inventory",
            AutoSize = true,
            MinimumSize = new Size(0, 42),
            Tag = "v1427-export-bbbg"
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

            using var select = new BbbgExportSelectionDialogV1427(all);
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
                OverwritePrompt = false,
                CreatePrompt = false,
                CheckFileExists = false,
                FileName = defaultName,
                Title = shifts.Count > 1
                    ? "Chọn tên gốc/vị trí lưu - ứng dụng sẽ tạo riêng từng ca"
                    : "Lưu BBBG Inventory Pickface 1291"
            };
            if (save.ShowDialog(main) != DialogResult.OK || string.IsNullOrWhiteSpace(save.FileName)) return;

            var exportTime = DateTime.Now;
            var outputs = BuildOutputPaths(save.FileName, shifts, exportTime);
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
            NotificationCenter.Show(main,
                $"Đã tạo {exported} file Word BBBG Inventory, tổng {total:N0} dòng dữ liệu thực tế.",
                "Xuất BBBG Inventory",
                MessageBoxIcon.Information);

            AppLog.Info("EXPORT_BBBG_INVENTORY_V1427_DONE", "Đã tạo BBBG Inventory từ dialog DPI-safe.", new Dictionary<string, object?>
            {
                ["entry_date"] = select.SelectedDate.ToString("yyyy-MM-dd"),
                ["shifts"] = string.Join(",", shifts),
                ["file_count"] = exported,
                ["report_count"] = total,
                ["export_time"] = exportTime.ToString("O", CultureInfo.InvariantCulture)
            });
        }
        catch (Exception ex)
        {
            AppLog.Exception("EXPORT_BBBG_INVENTORY_V1427_FAILED", ex);
            NotificationCenter.Show(main, ex.Message, "Không xuất được BBBG Inventory", MessageBoxIcon.Error);
        }
        finally
        {
            button.Enabled = true;
        }
    }

    private static List<(string Shift, string Path)> BuildOutputPaths(string selectedPath, IReadOnlyList<string> shifts, DateTime exportTime)
    {
        var timestamp = exportTime.ToString("HHmmss", CultureInfo.InvariantCulture);
        if (shifts.Count == 1) return [(shifts[0], EnsureUniquePath(selectedPath, timestamp))];

        var directory = Path.GetDirectoryName(selectedPath);
        if (string.IsNullOrWhiteSpace(directory)) directory = Environment.CurrentDirectory;
        var stem = Path.GetFileNameWithoutExtension(selectedPath).Trim();
        if (string.IsNullOrWhiteSpace(stem)) stem = "BBBG Inventory Pickface 1291";
        stem = RemoveTrailingShiftSuffix(stem);

        return shifts.Select(shift =>
        {
            var target = Path.Combine(directory, $"{stem} - {shift}.docx");
            return (shift, EnsureUniquePath(target, timestamp));
        }).ToList();
    }

    private static string RemoveTrailingShiftSuffix(string stem)
    {
        var value = stem.Trim();
        while (true)
        {
            var changed = false;
            foreach (var suffix in new[] { " - Ca 1", " - Ca 2" })
            {
                if (!value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
                value = value[..^suffix.Length].TrimEnd();
                changed = true;
                break;
            }
            if (!changed) return string.IsNullOrWhiteSpace(value) ? "BBBG Inventory Pickface 1291" : value;
        }
    }

    private static string EnsureUniquePath(string path, string timestamp)
    {
        if (!File.Exists(path)) return path;
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory)) directory = Environment.CurrentDirectory;
        var stem = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(extension)) extension = ".docx";

        var timestamped = Path.Combine(directory, $"{stem}_{timestamp}{extension}");
        if (!File.Exists(timestamped)) return timestamped;
        for (var i = 2; i < 10000; i++)
        {
            var candidate = Path.Combine(directory, $"{stem}_{timestamp}_{i}{extension}");
            if (!File.Exists(candidate)) return candidate;
        }
        throw new IOException("Không thể tạo tên file BBBG không trùng sau khi đã thêm hậu tố thời gian xuất.");
    }

    private static async Task<bool> EnsureFreshDataAsync(MainForm main, Label? status)
    {
        if (GoogleService.IsConnected())
        {
            try
            {
                if (status is not null) status.Text = "Đang nhận dữ liệu mới nhất trước khi xuất BBBG Inventory...";
                await CloudSyncService.PullSharedDataAsync(new Progress<string>(s => { if (status is not null) status.Text = s; }));
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Exception("EXPORT_PRE_SYNC_V1427_FAILED", ex);
                return MessageBox.Show(main,
                    "Không nhận được dữ liệu mới nhất từ Google. Nếu tiếp tục, BBBG chỉ dùng dữ liệu hiện có trên máy này.\n\n" + ex.Message + "\n\nTiếp tục?",
                    "Đồng bộ trước khi xuất chưa hoàn tất",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) == DialogResult.Yes;
            }
        }

        return MessageBox.Show(main,
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

internal sealed class BbbgExportSelectionDialogV1427 : Form
{
    private readonly ComboBox _date = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _shift1 = new() { Text = "Ca 1", AutoSize = true, Checked = true };
    private readonly CheckBox _shift2 = new() { Text = "Ca 2", AutoSize = true, Checked = true };

    public BbbgExportSelectionDialogV1427(IReadOnlyList<DamageReport> reports)
    {
        Text = "Chọn dữ liệu xuất BBBG Inventory";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScroll = true;
        Font = new Font("Segoe UI", 10F);
        ClientSize = new Size(560, 390);
        MinimumSize = new Size(580, 430);

        var root = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = 5,
            Width = 540
        };
        for (var i = 0; i < 5; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(new Label
        {
            Text = "Ngày nhập thực tế (chỉ chọn 1 ngày):",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 8)
        }, 0, 0);

        var choices = reports
            .GroupBy(x => x.CreatedAt.ToLocalTime().Date)
            .OrderByDescending(x => x.Key)
            .Select(x => new DateChoice(x.Key, x.Count(r => EqShift(r, "Ca 1")), x.Count(r => EqShift(r, "Ca 2"))))
            .ToList();

        _date.Width = 420;
        foreach (var choice in choices) _date.Items.Add(choice);
        if (_date.Items.Count > 0)
        {
            var today = choices.FindIndex(x => x.Date == DateTime.Today);
            _date.SelectedIndex = today >= 0 ? today : 0;
        }
        root.Controls.Add(_date, 0, 1);

        var shifts = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            WrapContents = true,
            Padding = new Padding(0, 12, 0, 4)
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
        root.Controls.Add(shifts, 0, 2);

        root.Controls.Add(new Label
        {
            Text = "Nếu chọn cả Ca 1 và Ca 2, ứng dụng luôn tạo 2 file Word riêng. Mỗi file chỉ chứa đúng số dòng dữ liệu thực tế của ca đó; trong nội dung biên bản không hiển thị thông tin ca.",
            AutoSize = true,
            MaximumSize = new Size(500, 0),
            ForeColor = Color.DimGray,
            Margin = new Padding(0, 8, 0, 16)
        }, 0, 3);

        var ok = new Button { Text = "Xuất BBBG Inventory", AutoSize = true, MinimumSize = new Size(180, 42) };
        var cancel = new Button { Text = "Hủy", AutoSize = true, MinimumSize = new Size(90, 42), DialogResult = DialogResult.Cancel };
        AppUiStyle.StyleButton(ok, ButtonVisual.Primary);
        AppUiStyle.StyleButton(cancel, ButtonVisual.Normal);
        ok.Click += (_, _) =>
        {
            if (_date.SelectedItem is not DateChoice)
            {
                NotificationCenter.Show(this, "Chọn một ngày cần xuất.", "Xuất BBBG Inventory", MessageBoxIcon.Warning);
                return;
            }
            if (!_shift1.Checked && !_shift2.Checked)
            {
                NotificationCenter.Show(this, "Chọn Ca 1, Ca 2 hoặc cả hai ca.", "Xuất BBBG Inventory", MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        };

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0, 4, 0, 0),
            Padding = new Padding(0, 0, 0, 8)
        };
        actions.Controls.AddRange([ok, cancel]);
        root.Controls.Add(actions, 0, 4);

        Controls.Add(root);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public DateTime SelectedDate => ((DateChoice)_date.SelectedItem!).Date;

    public IReadOnlyList<string> SelectedShifts => new[]
        {
            _shift1.Checked ? "Ca 1" : null,
            _shift2.Checked ? "Ca 2" : null
        }
        .Where(x => x is not null)
        .Cast<string>()
        .ToList();

    public List<DamageReport> Filter(IReadOnlyList<DamageReport> reports, string shift) => reports
        .Where(x => x.CreatedAt.ToLocalTime().Date == SelectedDate.Date && EqShift(x, shift))
        .OrderBy(x => x.OccurredDate)
        .ThenBy(x => x.Hour)
        .ThenBy(x => x.Minute)
        .ThenBy(x => x.CreatedAt)
        .ToList();

    private static bool EqShift(DamageReport report, string shift)
        => string.Equals(report.Shift.Trim(), shift, StringComparison.OrdinalIgnoreCase);

    private sealed record DateChoice(DateTime Date, int Shift1Count, int Shift2Count)
    {
        public override string ToString() => $"{Date:dd/MM/yyyy}  —  Ca 1: {Shift1Count} | Ca 2: {Shift2Count}";
    }
}
