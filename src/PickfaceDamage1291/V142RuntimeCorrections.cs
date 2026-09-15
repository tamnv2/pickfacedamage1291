namespace PickfaceDamage1291;

internal static class V142RuntimeCorrections
{
    public static void Apply(Form form)
    {
        form.Load += (_, _) => AttachEntryGuard(form);
    }

    private static void AttachEntryGuard(Form form)
    {
        var tabs = FindAll<TabControl>(form).FirstOrDefault();
        var tab = tabs?.TabPages.Cast<TabPage>().FirstOrDefault(x => x.Text == "Nhập hư hỏng");
        if (tab is null) return;

        var productBox = FindAll<GroupBox>(tab).FirstOrDefault(x => x.Text.StartsWith("1.", StringComparison.Ordinal));
        var occurrenceBox = FindAll<GroupBox>(tab).FirstOrDefault(x => x.Text.StartsWith("2.", StringComparison.Ordinal));
        var submitBox = FindAll<GroupBox>(tab).FirstOrDefault(x => x.Text.StartsWith("4.", StringComparison.Ordinal));
        if (productBox is null || occurrenceBox is null || submitBox is null) return;

        var productForm = FindAll<TableLayoutPanel>(productBox).FirstOrDefault(x => x.ColumnCount == 2 && x.RowCount >= 3);
        var occurrenceForm = FindAll<TableLayoutPanel>(occurrenceBox).FirstOrDefault(x => x.ColumnCount == 2 && x.RowCount >= 5);
        var send = FindAll<Button>(submitBox).FirstOrDefault(x => x.Text.Contains("Gửi thông tin hư hỏng", StringComparison.OrdinalIgnoreCase));
        if (productForm is null || occurrenceForm is null || send is null) return;

        var sku = productForm.GetControlFromPosition(1, 0) as TextBox;
        var productName = productForm.GetControlFromPosition(1, 1) as TextBox;
        var baseUnit = productForm.GetControlFromPosition(1, 2) as TextBox;
        var location = occurrenceForm.GetControlFromPosition(1, 0) as TextBox;
        var shift = occurrenceForm.GetControlFromPosition(1, 3) as ExclusiveShiftPicker;
        var quantity = occurrenceForm.GetControlFromPosition(1, 4) as NumericUpDown;
        var status = FindAll<Label>(submitBox).FirstOrDefault(IsEntryStatusLabel);
        if (sku is null || productName is null || baseUnit is null || location is null || shift is null || quantity is null) return;

        var toolTip = new ToolTip { AutoPopDelay = 7000, InitialDelay = 300, ReshowDelay = 150 };

        bool FieldsReady()
        {
            var validProduct = !string.IsNullOrWhiteSpace(sku.Text) &&
                               !string.IsNullOrWhiteSpace(productName.Text) &&
                               !string.IsNullOrWhiteSpace(baseUnit.Text) &&
                               productName.ForeColor != Color.Firebrick;
            var validLocation = LocationNormalizer.TryNormalize(location.Text, out _, out _);
            return validProduct && validLocation && !string.IsNullOrWhiteSpace(shift.SelectedShift) && quantity.Value >= 1;
        }

        bool PermissionReady()
        {
            var parsed = ParseStatusPermission(status?.Text);
            if (parsed.HasValue) return parsed.Value;
            if (AppSession.Current?.Profile.IsAdmin == true) return false;
            return AppSession.OperatorManager?.CanCreateDamage == true;
        }

        void ApplyState()
        {
            if (form.IsDisposed || send.IsDisposed) return;
            var fieldsReady = FieldsReady();
            var permissionReady = PermissionReady();
            send.Enabled = fieldsReady && permissionReady;
            toolTip.SetToolTip(send,
                !fieldsReady
                    ? "Nhập đủ SKU hợp lệ, vị trí, ngày/giờ, ca và số lượng để mở khóa nút Gửi."
                    : !permissionReady
                        ? "Thông tin đã đủ nhưng quyền gửi đang tạm khóa theo trạng thái phiên hiện tại."
                        : string.Empty);
        }

        sku.TextChanged += (_, _) => ApplyState();
        productName.TextChanged += (_, _) => ApplyState();
        baseUnit.TextChanged += (_, _) => ApplyState();
        location.TextChanged += (_, _) => ApplyState();
        shift.SelectedShiftChanged += (_, _) => ApplyState();
        quantity.ValueChanged += (_, _) => ApplyState();
        if (status is not null) status.TextChanged += (_, _) => ApplyState();
        form.FormClosed += (_, _) => toolTip.Dispose();
        ApplyState();
    }

    private static bool IsEntryStatusLabel(Label label)
    {
        var text = label.Text ?? string.Empty;
        return text.Contains("Dữ liệu được lưu", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Sẵn sàng gửi", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Tạm khóa", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("offline", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("quyền nhập", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("active_operator", StringComparison.OrdinalIgnoreCase);
    }

    private static bool? ParseStatusPermission(string? text)
    {
        var value = (text ?? string.Empty).Trim();
        if (value.Contains("Sẵn sàng gửi", StringComparison.OrdinalIgnoreCase)) return true;
        if (value.Contains("quyền nhập dự phòng", StringComparison.OrdinalIgnoreCase)) return true;
        if (value.Contains("Tạm khóa", StringComparison.OrdinalIgnoreCase)) return false;
        if (value.Contains("chưa đọc được trạng thái", StringComparison.OrdinalIgnoreCase)) return false;
        if (value.Contains("ADMIN đã đăng nhập", StringComparison.OrdinalIgnoreCase) && value.Contains("offline", StringComparison.OrdinalIgnoreCase)) return false;
        return null;
    }

    private static IEnumerable<T> FindAll<T>(Control root) where T : Control
    {
        if (root is T self) yield return self;
        foreach (Control child in root.Controls)
        foreach (var found in FindAll<T>(child)) yield return found;
    }
}
