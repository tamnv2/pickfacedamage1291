namespace PickfaceDamage1291;

internal static class V1419Runtime
{
    public static void Apply(MainForm main)
    {
        try
        {
            PatchExportButtons(main);
            main.Shown += (_, _) => PatchExportButtons(main);
            AppLog.Info("V1419_RUNTIME_APPLIED", "Đã áp dụng Xuất thông tin và Xuất BBBG Inventory v1.4.19.");
        }
        catch (Exception ex)
        {
            AppLog.Exception("V1419_RUNTIME_APPLY_FAILED", ex);
        }
    }

    private static void PatchExportButtons(MainForm main)
    {
        var tab = FindAll<TabControl>(main).FirstOrDefault()?.TabPages.Cast<TabPage>()
            .FirstOrDefault(x => x.Text == "Danh sách đã nhập");
        if (tab is null) return;

        var actions = FindAll<FlowLayoutPanel>(tab)
            .FirstOrDefault(x => x.Controls.OfType<Button>().Any(b => Equals(b.Tag, "v1416-export")));
        if (actions is null || actions.Controls.OfType<Button>().Any(b => Equals(b.Tag, "v1419-export-info"))) return;

        var old = actions.Controls.OfType<Button>().FirstOrDefault(b => Equals(b.Tag, "v1416-export"));
        if (old is null) return;
        var index = actions.Controls.GetChildIndex(old);
        old.Visible = false;

        var info = MakeButton("Xuất thông tin", "v1419-export-info");
        var bbbg = MakeButton("Xuất BBBG Inventory", "v1419-export-bbbg");
        info.Click += async (_, _) => await ExportInformationAsync(main, info);
        bbbg.Click += async (_, _) => await ExportBbbgAsync(main, bbbg);
        actions.Controls.Add(info);
        actions.Controls.Add(bbbg);
        actions.Controls.SetChildIndex(info, Math.Max(0, index));
        actions.Controls.SetChildIndex(bbbg, Math.Max(0, index + 1));
        actions.WrapContents = true;
        actions.Height = 92;

        if (actions.Parent is TableLayoutPanel layout)
        {
            var row = layout.GetRow(actions);
            if (row >= 0 && row < layout.RowStyles.Count)
            {
                layout.RowStyles[row].SizeType = SizeType.Absolute;
                layout.RowStyles[row].Height = 96;
            }
        }
    }

    private static Button MakeButton(string text, string tag)
    {
        var button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(0, 42), Tag = tag };
        AppUiStyle.StyleButton(button, ButtonVisual.Normal);
        return button;
    }

    private static async Task ExportInformationAsync(MainForm main, Button button)
    {
        var status = Field<Label>(main, "_reportStatus");
        try
        {
            button.Enabled = false;
            if (!await EnsureFreshDataAsync(main, status, "xuất thông tin")) return;

            var all = Database.GetReports(int.MaxValue);
            if (all.Count == 0)
            {
                NotificationCenter.Show(main, "Chưa có dữ liệu để xuất.", "Xuất thông tin", MessageBoxIcon.Information);
                return;
            }

            using var select = new DamageExportSelectionDialogV148(all);
            RelabelInformationDialog(select);
            if (select.ShowDialog(main) != DialogResult.OK) return;

            var selected = select.Filter(all);
            if (selected.Count == 0)
            {
                NotificationCenter.Show(main, "Không có phiếu phù hợp ngày/ca đã chọn.", "Xuất thông tin", MessageBoxIcon.Warning);
                return;
            }

            var result = await DamageReportExportServiceV1416.ExportAsync(
                main,
                selected,
                select.SelectedDates,
                select.SelectedShiftLabel,
                new Progress<string>(s => { if (status is not null) status.Text = s; }));
            if (result is null) return;

            if (status is not null) status.Text = $"Đã xuất {result.ReportCount:N0} phiếu.";
            var note = result.MissingImages > 0 ? $" Không tải/nhúng được {result.MissingImages:N0} ảnh." : string.Empty;
            NotificationCenter.Show(main,
                $"Đã xuất {result.ReportCount:N0} phiếu và {result.ImageCount:N0} ảnh.{note}",
                "Xuất thông tin",
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppLog.Exception("EXPORT_INFORMATION_V1419_FAILED", ex);
            NotificationCenter.Show(main, ex.Message, "Không xuất được thông tin", MessageBoxIcon.Error);
        }
        finally
        {
            button.Enabled = true;
        }
    }

    private static void RelabelInformationDialog(Form dialog)
    {
        dialog.Text = "Chọn phạm vi xuất thông tin";
        foreach (var button in FindAll<Button>(dialog))
        {
            if (string.Equals(button.Text, "Xuất Excel", StringComparison.Ordinal))
                button.Text = "Xuất thông tin";
        }
    }

    private static async Task ExportBbbgAsync(MainForm main, Button button)
    {
        var status = Field<Label>(main, "_reportStatus");
        try
        {
            button.Enabled = false;
            if (!await EnsureFreshDataAsync(main, status, "xuất BBBG Inventory")) return;

            var all = Database.GetReports(int.MaxValue);
            if (all.Count == 0)
            {
                NotificationCenter.Show(main, "Chưa có dữ liệu để xuất biên bản.", "Xuất BBBG Inventory", MessageBoxIcon.Information);
                return;
            }

            using var select = new BbbgExportSelectionDialogV1419(all);
            if (select.ShowDialog(main) != DialogResult.OK) return;

            using var folder = new FolderBrowserDialog
            {
                Description = "Chọn thư mục lưu BBBG Inventory. Chọn cả 2 ca sẽ luôn tạo 2 file Word riêng.",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };
            if (folder.ShowDialog(main) != DialogResult.OK || string.IsNullOrWhiteSpace(folder.SelectedPath)) return;

            var exported = 0;
            var total = 0;
            foreach (var shift in select.SelectedShifts)
            {
                var reports = select.Filter(all, shift);
                if (status is not null) status.Text = $"Đang tạo BBBG {shift}: {reports.Count:N0} phiếu...";

                var name = $"BBBG Inventory Pickface 1291 ngày {select.SelectedDate:ddMMyyyy} - {shift}.docx";
                var path = UniquePath(folder.SelectedPath, name);
                await Task.Run(() => BbbgInventoryWordExporterV1419.Export(path, reports));
                exported++;
                total += reports.Count;
            }

            if (status is not null) status.Text = $"Đã xuất {exported} file BBBG, {total:N0} phiếu.";
            NotificationCenter.Show(main,
                $"Đã tạo {exported} file Word BBBG Inventory, tổng {total:N0} dòng dữ liệu thực tế.",
                "Xuất BBBG Inventory",
                MessageBoxIcon.Information);

            AppLog.Info("EXPORT_BBBG_INVENTORY_V1419_DONE", "Đã tạo BBBG Inventory.", new Dictionary<string, object?>
            {
                ["entry_date"] = select.SelectedDate.ToString("yyyy-MM-dd"),
                ["shifts"] = string.Join(",", select.SelectedShifts),
                ["file_count"] = exported,
                ["report_count"] = total
            });
        }
        catch (Exception ex)
        {
            AppLog.Exception("EXPORT_BBBG_INVENTORY_V1419_FAILED", ex);
            NotificationCenter.Show(main, ex.Message, "Không xuất được BBBG Inventory", MessageBoxIcon.Error);
        }
        finally
        {
            button.Enabled = true;
        }
    }

    private static async Task<bool> EnsureFreshDataAsync(MainForm main, Label? status, string operation)
    {
        if (GoogleService.IsConnected())
        {
            try
            {
                if (status is not null) status.Text = $"Đang nhận dữ liệu mới nhất trước khi {operation}...";
                await CloudSyncService.PullSharedDataAsync(new Progress<string>(s => { if (status is not null) status.Text = s; }));
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Exception("EXPORT_PRE_SYNC_V1419_FAILED", ex, new Dictionary<string, object?> { ["operation"] = operation });
                return MessageBox.Show(main,
                    $"Không nhận được dữ liệu mới nhất từ Google. Nếu tiếp tục, {operation} chỉ dùng dữ liệu hiện có trên máy này.\n\n{ex.Message}\n\nTiếp tục?",
                    "Đồng bộ trước khi xuất chưa hoàn tất",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) == DialogResult.Yes;
            }
        }

        return MessageBox.Show(main,
            $"Ứng dụng đang offline/chưa kết nối Google. {operation} chỉ dùng dữ liệu hiện có trên máy này. Tiếp tục?",
            "Xuất dữ liệu local",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) == DialogResult.Yes;
    }

    private static string UniquePath(string folder, string fileName)
    {
        var path = Path.Combine(folder, fileName);
        if (!File.Exists(path)) return path;

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (var i = 2; i < 10000; i++)
        {
            path = Path.Combine(folder, $"{stem} ({i}){ext}");
            if (!File.Exists(path)) return path;
        }
        throw new IOException("Không thể tạo tên file BBBG không trùng.");
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

internal sealed class BbbgExportSelectionDialogV1419 : Form
{
    private readonly ComboBox _date = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _shift1 = new() { Text = "Ca 1", AutoSize = true, Checked = true };
    private readonly CheckBox _shift2 = new() { Text = "Ca 2", AutoSize = true, Checked = true };

    public BbbgExportSelectionDialogV1419(IReadOnlyList<DamageReport> reports)
    {
        Text = "Chọn dữ liệu xuất BBBG Inventory";
        Width = 560;
        Height = 330;
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
            RowCount = 5
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
            .Select(x => new DateChoice(
                x.Key,
                x.Count(r => EqShift(r, "Ca 1")),
                x.Count(r => EqShift(r, "Ca 2"))))
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
            Margin = new Padding(0, 8, 0, 12)
        }, 0, 3);

        var ok = new Button { Text = "Xuất BBBG Inventory", AutoSize = true };
        var cancel = new Button { Text = "Hủy", AutoSize = true, DialogResult = DialogResult.Cancel };
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
            WrapContents = false
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

    private sealed record DateChoice(DateTime Date, int Shift1, int Shift2)
    {
        public override string ToString() => $"{Date:dd/MM/yyyy}  —  Ca 1: {Shift1:N0} | Ca 2: {Shift2:N0}";
    }
}
