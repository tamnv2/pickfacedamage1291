using System.Reflection;

namespace PickfaceDamage1291;

/// <summary>
/// v1.4.7 correction layer for entry validation/layout. It intentionally runs after V146Runtime
/// so this layer is the final authority for the Send button state and the visible wording/layout.
/// </summary>
internal static class V147Runtime
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly HashSet<Form> PatchedEditDialogs = [];
    private static EventHandler? _idleHandler;

    public static void Apply(MainForm main)
    {
        try
        {
            FixMainEntryTextAndLayout(main);
            // v1.4.9: do not attach the legacy entry gate. V148Runtime is the sole submit-state authority.
            StyleButtons(main);
            AttachEditDialogFixes(main);
            UsageDashboardV147.Attach(main);

            main.Shown += (_, _) =>
            {
                FixMainEntryTextAndLayout(main);
                FixCaText(main);
                StyleButtons(main);
                // v1.4.9: submit validation is reevaluated by V148Runtime.
            };

            AppLog.Info("V147_RUNTIME_APPLIED", "Đã áp dụng sửa validation/layout v1.4.7.");
        }
        catch (Exception ex)
        {
            AppLog.Exception("V147_RUNTIME_APPLY_FAILED", ex);
        }
    }

    private static EntryGate AttachAuthoritativeEntryGate(MainForm main)
    {
        var sku = Field<TextBox>(main, "_sku");
        var productName = Field<TextBox>(main, "_productName");
        var location = Field<TextBox>(main, "_location");
        var shift = Field<ExclusiveShiftPicker>(main, "_shift");
        var quantity = Field<NumericUpDown>(main, "_quantity");
        var baseUnit = Field<TextBox>(main, "_baseUnit");
        var send = Field<Button>(main, "_send");
        var entryStatus = Field<Label>(main, "_entryStatus");

        var tabs = Field<TabControl>(main, "_tabs");
        var entryTab = tabs.TabPages.Cast<TabPage>().First(x => string.Equals(x.Text, "Nhập hư hỏng", StringComparison.Ordinal));
        var productBox = FindAll<GroupBox>(entryTab).First(x => x.Text.StartsWith("1.", StringComparison.Ordinal));
        var submitBox = FindAll<GroupBox>(entryTab).First(x => x.Text.StartsWith("4.", StringComparison.Ordinal));
        var overrideCheck = FindAll<CheckBox>(productBox)
            .FirstOrDefault(x => x.Text.Contains("Thay đổi Base Units", StringComparison.OrdinalIgnoreCase));
        var overrideSelect = FindAll<ComboBox>(productBox).FirstOrDefault();
        var requiredNote = FindAll<Label>(submitBox)
            .FirstOrDefault(x => x.ForeColor == Color.Firebrick || x.Text.Contains("Chưa nhập đủ", StringComparison.OrdinalIgnoreCase));

        if (requiredNote is null)
        {
            requiredNote = new Label
            {
                AutoSize = true,
                ForeColor = Color.Firebrick,
                MaximumSize = new Size(1000, 0),
                Margin = new Padding(12, 0, 12, 12)
            };
            var submitTable = FindAll<TableLayoutPanel>(submitBox).FirstOrDefault(x => x.ColumnCount == 2);
            if (submitTable is not null)
            {
                var row = submitTable.RowCount++;
                submitTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                submitTable.Controls.Add(requiredNote, 0, row);
                submitTable.SetColumnSpan(requiredNote, 2);
            }
        }

        var gate = new EntryGate(main, sku, productName, location, shift, quantity, baseUnit, send, entryStatus, overrideCheck, overrideSelect, requiredNote);
        gate.Attach();
        return gate;
    }

    private static void FixMainEntryTextAndLayout(MainForm main)
    {
        var tabs = Field<TabControl>(main, "_tabs");
        var entryTab = tabs.TabPages.Cast<TabPage>().FirstOrDefault(x => string.Equals(x.Text, "Nhập hư hỏng", StringComparison.Ordinal));
        if (entryTab is null) return;

        FixCaText(entryTab);

        foreach (var box in FindAll<GroupBox>(entryTab))
        {
            foreach (var table in FindAll<TableLayoutPanel>(box).Where(x => x.ColumnCount == 2))
                AlignTwoColumnForm(table);
        }

        var send = Field<Button>(main, "_send");
        var entryStatus = Field<Label>(main, "_entryStatus");
        var submitBox = FindAll<GroupBox>(entryTab).FirstOrDefault(x => x.Text.StartsWith("4.", StringComparison.Ordinal));
        var submitTable = submitBox is null ? null : FindAll<TableLayoutPanel>(submitBox).FirstOrDefault(x => x.ColumnCount == 2);
        if (submitTable is not null)
        {
            var sendPos = submitTable.GetPositionFromControl(send);
            if (sendPos.Row >= 0)
            {
                var sendLabel = submitTable.GetControlFromPosition(0, sendPos.Row);
                if (sendLabel is not null && !ReferenceEquals(sendLabel, send)) sendLabel.Visible = false;
                submitTable.SetColumn(send, 0);
                submitTable.SetColumnSpan(send, 2);
                send.Dock = DockStyle.Top;
                send.AutoSize = false;
                send.Height = 44;
                send.MinimumSize = new Size(0, 44);
                send.Margin = new Padding(12, 8, 12, 14);
                send.TextAlign = ContentAlignment.MiddleCenter;
            }

            var statusPos = submitTable.GetPositionFromControl(entryStatus);
            if (statusPos.Row >= 0)
            {
                var statusLabel = submitTable.GetControlFromPosition(0, statusPos.Row);
                if (statusLabel is not null && !ReferenceEquals(statusLabel, entryStatus)) statusLabel.Visible = false;
                submitTable.SetColumn(entryStatus, 0);
                submitTable.SetColumnSpan(entryStatus, 2);
                entryStatus.Margin = new Padding(12, 2, 12, 10);
                entryStatus.Padding = Padding.Empty;
                entryStatus.MaximumSize = new Size(1200, 0);
            }

            foreach (var note in FindAll<Label>(submitBox).Where(x => x.ForeColor == Color.Firebrick || x.Text.Contains("Chưa nhập đủ", StringComparison.OrdinalIgnoreCase)))
            {
                var pos = submitTable.GetPositionFromControl(note);
                if (pos.Row < 0) continue;
                submitTable.SetColumn(note, 0);
                submitTable.SetColumnSpan(note, 2);
                note.Margin = new Padding(12, 0, 12, 10);
                note.Padding = Padding.Empty;
                note.MaximumSize = new Size(1200, 0);
            }
        }

        var reportGrid = Field<DataGridView>(main, "_reportGrid");
        if (reportGrid.Columns.Contains("Shift")) reportGrid.Columns["Shift"]!.HeaderText = "Ca ghi nhận";
    }

    private static void AlignTwoColumnForm(TableLayoutPanel table)
    {
        if (table.ColumnStyles.Count >= 2)
        {
            table.ColumnStyles[0].SizeType = SizeType.Absolute;
            table.ColumnStyles[0].Width = 220;
            table.ColumnStyles[1].SizeType = SizeType.Percent;
            table.ColumnStyles[1].Width = 100;
        }

        for (var row = 0; row < table.RowCount; row++)
        {
            if (row < table.RowStyles.Count) table.RowStyles[row].SizeType = SizeType.AutoSize;

            if (table.GetControlFromPosition(0, row) is Label label)
            {
                label.AutoSize = true;
                label.MaximumSize = new Size(210, 0);
                label.Padding = Padding.Empty;
                label.Margin = new Padding(0, 4, 14, 9);
                label.Anchor = AnchorStyles.Left;
                label.TextAlign = ContentAlignment.MiddleLeft;
            }

            var control = table.GetControlFromPosition(1, row);
            if (control is null) continue;
            control.Margin = new Padding(3, 4, 3, 9);

            switch (control)
            {
                case TextBox:
                case DateTimePicker:
                case NumericUpDown:
                case ComboBox:
                    control.Dock = DockStyle.None;
                    control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
                    break;
                case CheckBox:
                case TimeWheelPicker:
                case ExclusiveShiftPicker:
                    control.Dock = DockStyle.None;
                    control.Anchor = AnchorStyles.Left;
                    break;
                case Label valueLabel:
                    valueLabel.Anchor = AnchorStyles.Left;
                    valueLabel.MaximumSize = new Size(900, 0);
                    break;
            }
        }

        table.PerformLayout();
    }

    private static void StyleButtons(Control root)
    {
        foreach (var button in FindAll<Button>(root))
        {
            if (button.Text.Contains("Gửi thông tin hư hỏng", StringComparison.OrdinalIgnoreCase)) continue;
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.MinimumSize = new Size(button.MinimumSize.Width, Math.Max(40, button.MinimumSize.Height));
            button.Padding = new Padding(14, 5, 14, 5);
            button.Margin = new Padding(5, 5, 9, 8);
            button.TextAlign = ContentAlignment.MiddleCenter;
        }

        foreach (var flow in FindAll<FlowLayoutPanel>(root))
        {
            if (string.Equals(flow.Tag as string, "audit-actions-fixed-height", StringComparison.Ordinal))
                continue;

            if (flow.Dock == DockStyle.Top || flow.AutoSize)
            {
                flow.AutoSize = true;
                flow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                var p = flow.Padding;
                flow.Padding = new Padding(p.Left, Math.Max(6, p.Top), p.Right, Math.Max(8, p.Bottom));
            }
        }
    }

    private static void AttachEditDialogFixes(MainForm main)
    {
        _idleHandler = (_, _) =>
        {
            foreach (Form form in Application.OpenForms)
            {
                if (form is not DamageReportEditDialog dialog || PatchedEditDialogs.Contains(dialog)) continue;
                PatchedEditDialogs.Add(dialog);
                dialog.FormClosed += (_, _) => PatchedEditDialogs.Remove(dialog);
                try
                {
                    FixCaText(dialog);
                    foreach (var table in FindAll<TableLayoutPanel>(dialog).Where(x => x.ColumnCount == 2)) AlignTwoColumnForm(table);
                    StyleButtons(dialog);
                }
                catch (Exception ex)
                {
                    AppLog.Exception("V147_EDIT_LAYOUT_FAILED", ex);
                }
            }
        };
        Application.Idle += _idleHandler;
        main.FormClosed += (_, _) =>
        {
            if (_idleHandler is not null) Application.Idle -= _idleHandler;
            _idleHandler = null;
            PatchedEditDialogs.Clear();
        };
    }

    private static void FixCaText(Control root)
    {
        foreach (var label in FindAll<Label>(root))
            if (label.Text.Contains("Ca ghi nhân", StringComparison.Ordinal))
                label.Text = label.Text.Replace("Ca ghi nhân", "Ca ghi nhận", StringComparison.Ordinal);

        foreach (var grid in FindAll<DataGridView>(root))
            foreach (DataGridViewColumn column in grid.Columns)
                if (column.HeaderText.Contains("Ca ghi nhân", StringComparison.Ordinal))
                    column.HeaderText = column.HeaderText.Replace("Ca ghi nhân", "Ca ghi nhận", StringComparison.Ordinal);
    }

    private static T Field<T>(object owner, string name) where T : class
    {
        var field = owner.GetType().GetField(name, PrivateInstance)
                    ?? throw new MissingFieldException(owner.GetType().Name, name);
        return field.GetValue(owner) as T
               ?? throw new InvalidOperationException($"Field {owner.GetType().Name}.{name} không phải {typeof(T).Name}.");
    }

    private static IEnumerable<T> FindAll<T>(Control root) where T : Control
    {
        if (root is T self) yield return self;
        foreach (Control child in root.Controls)
        foreach (var found in FindAll<T>(child)) yield return found;
    }

    private sealed class EntryGate
    {
        private readonly MainForm _main;
        private readonly TextBox _sku;
        private readonly TextBox _productName;
        private readonly TextBox _location;
        private readonly ExclusiveShiftPicker _shift;
        private readonly NumericUpDown _quantity;
        private readonly TextBox _baseUnit;
        private readonly Button _send;
        private readonly Label _status;
        private readonly CheckBox? _overrideCheck;
        private readonly ComboBox? _overrideSelect;
        private readonly Label _requiredNote;
        private bool _settingEnabled;
        private bool _normalizingBase;

        public EntryGate(
            MainForm main,
            TextBox sku,
            TextBox productName,
            TextBox location,
            ExclusiveShiftPicker shift,
            NumericUpDown quantity,
            TextBox baseUnit,
            Button send,
            Label status,
            CheckBox? overrideCheck,
            ComboBox? overrideSelect,
            Label requiredNote)
        {
            _main = main;
            _sku = sku;
            _productName = productName;
            _location = location;
            _shift = shift;
            _quantity = quantity;
            _baseUnit = baseUnit;
            _send = send;
            _status = status;
            _overrideCheck = overrideCheck;
            _overrideSelect = overrideSelect;
            _requiredNote = requiredNote;
        }

        public void Attach()
        {
            _sku.TextChanged += (_, _) => Reevaluate();
            _productName.TextChanged += (_, _) => Reevaluate();
            _location.TextChanged += (_, _) => Reevaluate();
            _shift.SelectedShiftChanged += (_, _) => Reevaluate();
            _quantity.ValueChanged += (_, _) => Reevaluate();
            _baseUnit.TextChanged += (_, _) => Reevaluate();
            if (_overrideCheck is not null) _overrideCheck.CheckedChanged += (_, _) => Reevaluate();
            if (_overrideSelect is not null) _overrideSelect.SelectedIndexChanged += (_, _) => Reevaluate();
            _status.TextChanged += (_, _) => Reevaluate();

            _send.MouseDown += (_, _) => Reevaluate();
            _send.EnabledChanged += (_, _) =>
            {
                if (_settingEnabled || _main.IsDisposed) return;
                try
                {
                    _main.BeginInvoke((Action)(() =>
                    {
                        if (!_main.IsDisposed) Reevaluate();
                    }));
                }
                catch { }
            };

            Reevaluate();
        }

        public void Reevaluate()
        {
            if (_main.IsDisposed || _send.IsDisposed) return;

            var missing = new List<string>();
            var skuText = ExcelImportService.Clean(_sku.Text).ToUpperInvariant();
            var product = skuText.Length == 0 ? null : Database.GetProduct(skuText);
            if (product is null)
            {
                missing.Add("SKU hợp lệ");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(_productName.Text) || _productName.ForeColor == Color.Firebrick)
                    missing.Add("Tên sản phẩm");

                if (_overrideCheck?.Checked == true)
                {
                    var selected = _overrideSelect?.SelectedItem as string;
                    if (string.IsNullOrWhiteSpace(selected))
                    {
                        missing.Add("Base Units thực tế hư hỏng");
                    }
                    else if (!_normalizingBase && !string.Equals(_baseUnit.Text.Trim(), selected.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        _normalizingBase = true;
                        try { _baseUnit.Text = selected.Trim(); }
                        finally { _normalizingBase = false; }
                    }
                }
                else
                {
                    var master = (product.BaseUnit ?? string.Empty).Trim();
                    if (master.Length == 0)
                    {
                        missing.Add("Base Units SKU");
                    }
                    else if (!_normalizingBase && !string.Equals(_baseUnit.Text.Trim(), master, StringComparison.OrdinalIgnoreCase))
                    {
                        _normalizingBase = true;
                        try { _baseUnit.Text = master; }
                        finally { _normalizingBase = false; }
                    }
                }
            }

            if (!LocationNormalizer.TryNormalize(_location.Text, out _, out _))
                missing.Add("Vị trí phát hiện hư hỏng thực tế");
            if (_quantity.Value < 1) missing.Add("Số lượng hư hỏng");
            if (string.IsNullOrWhiteSpace(_shift.SelectedShift)) missing.Add("Ca ghi nhận");

            var complete = missing.Count == 0;
            _requiredNote.Visible = !complete;
            _requiredNote.Text = complete
                ? string.Empty
                : "Chưa nhập đủ thông tin bắt buộc nên không thể gửi. Thiếu: " + string.Join(", ", missing.Distinct(StringComparer.CurrentCultureIgnoreCase)) + ".";

            var allowedBySession = IsAllowedBySession();
            SetEnabled(complete && allowedBySession);
        }

        private bool IsAllowedBySession()
        {
            var text = (_status.Text ?? string.Empty).Trim();
            if (text.Contains("Sẵn sàng gửi", StringComparison.OrdinalIgnoreCase)) return true;
            if (text.Contains("quyền nhập dự phòng", StringComparison.OrdinalIgnoreCase)) return true;
            if (text.Contains("Tạm khóa", StringComparison.OrdinalIgnoreCase)) return false;
            if (text.Contains("chưa đọc được trạng thái", StringComparison.OrdinalIgnoreCase)) return false;
            if (text.Contains("không có quyền", StringComparison.OrdinalIgnoreCase)) return false;
            if (text.Contains("offline", StringComparison.OrdinalIgnoreCase) && AppSession.Current?.Profile.IsAdmin == true) return false;

            if (AppSession.Current?.Profile.IsAdmin == true)
                return text.Contains("không có USER", StringComparison.OrdinalIgnoreCase) || _send.Enabled;

            return AppSession.OperatorManager?.CanCreateDamage == true;
        }

        private void SetEnabled(bool enabled)
        {
            if (_send.Enabled == enabled) return;
            _settingEnabled = true;
            try { _send.Enabled = enabled; }
            finally { _settingEnabled = false; }
        }
    }
}
