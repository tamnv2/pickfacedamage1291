namespace PickfaceDamage1291;

internal enum SyncConflictChoice
{
    Cancel,
    UseGoogle,
    KeepLocal
}

internal static class SyncConflictResolver
{
    public static async Task<RemoteReportChange?> FetchRemoteAsync(
        string reportId,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reportId)) return null;

        long cursor = 0;
        for (var pageIndex = 0; pageIndex < 100; pageIndex++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(Math.Clamp(10 + pageIndex * 3, 10, 70));
            var page = await GoogleGatewayV140.PullReportChangesAsync(cursor, 1000, ct);
            var match = page.Changes.FirstOrDefault(x =>
                string.Equals(x.ReportId, reportId, StringComparison.Ordinal));
            if (match is not null)
            {
                progress?.Report(100);
                return match;
            }

            if (!page.HasMore || page.Changes.Count == 0)
                return null;

            var next = page.Changes.Max(x => x.ChangeSeq);
            if (next <= cursor) return null;
            cursor = next;
        }

        return null;
    }

    public static RemoteApplyResult ApplyRemote(RemoteReportChange remote)
    {
        var local = Database.GetReportById(remote.ReportId);
        if (local is not null)
            Database.SetReportStatus(remote.ReportId, "SYNCED");

        var result = SyncCacheStore.ApplyRemoteReport(remote);
        if (result == RemoteApplyResult.Conflict)
            throw new InvalidOperationException("Bản Google chưa thể áp dụng. Xung đột được giữ nguyên để tránh mất dữ liệu.");
        return result;
    }
}

internal sealed class SyncConflictResolutionDialog : Form
{
    public SyncConflictChoice Choice { get; private set; } = SyncConflictChoice.Cancel;

    public SyncConflictResolutionDialog(DamageReport local, RemoteReportChange remote, bool canKeepLocal)
    {
        Text = "Xử lý xung đột đồng bộ";
        Width = 980;
        Height = 650;
        MinimumSize = new Size(820, 560);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = 4
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var intro = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            MaximumSize = new Size(900, 0),
            Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold),
            Text = "Cùng một phiếu đã thay đổi ở hai nơi. Hệ thống không tự ghi đè. So sánh hai bản bên dưới rồi chọn bản cần giữ."
        };
        root.Controls.Add(intro, 0, 0);

        var compare = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 14, 0, 12)
        };
        compare.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        compare.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        compare.Controls.Add(BuildVersionBox("Bản trên máy", FormatLocal(local)), 0, 0);
        compare.Controls.Add(BuildVersionBox(remote.Deleted ? "Bản Google — ĐÃ XÓA" : "Bản Google", FormatRemote(remote)), 1, 0);
        root.Controls.Add(compare, 0, 1);

        var note = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            MaximumSize = new Size(900, 0),
            ForeColor = Color.DimGray,
            Text = canKeepLocal
                ? "Dùng bản Google: thay dữ liệu local bằng bản đang lưu trên Google. Giữ bản trên máy: tăng phiên bản local và ghi đè bản Google sau khi bạn xác nhận."
                : "Bạn có thể xem xung đột nhưng không được ghi đè Google vì phiếu không do tài khoản này tạo. ADMIN hoặc tài khoản tạo phiếu phải xử lý."
        };
        root.Controls.Add(note, 0, 2);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 12, 0, 0)
        };
        var cancel = new Button { Text = "Để xử lý sau", AutoSize = true, MinimumSize = new Size(130, 40) };
        var useGoogle = new Button
        {
            Text = remote.Deleted ? "Chấp nhận xoá theo Google" : "Dùng bản Google",
            AutoSize = true,
            MinimumSize = new Size(150, 40),
            Enabled = canKeepLocal
        };
        var keepLocal = new Button
        {
            Text = "Giữ bản trên máy → ghi lên Google",
            AutoSize = true,
            MinimumSize = new Size(220, 40),
            Enabled = canKeepLocal
        };

        cancel.Click += (_, _) => { Choice = SyncConflictChoice.Cancel; DialogResult = DialogResult.Cancel; Close(); };
        useGoogle.Click += (_, _) => { Choice = SyncConflictChoice.UseGoogle; DialogResult = DialogResult.OK; Close(); };
        keepLocal.Click += (_, _) => { Choice = SyncConflictChoice.KeepLocal; DialogResult = DialogResult.OK; Close(); };

        actions.Controls.Add(cancel);
        actions.Controls.Add(keepLocal);
        actions.Controls.Add(useGoogle);
        root.Controls.Add(actions, 0, 3);

        Controls.Add(root);
        AcceptButton = useGoogle;
        CancelButton = cancel;
    }

    private static GroupBox BuildVersionBox(string title, string text)
    {
        var box = new GroupBox
        {
            Text = title,
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            Margin = new Padding(4)
        };
        var value = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Text = text
        };
        box.Controls.Add(value);
        return box;
    }

    private static string FormatLocal(DamageReport r) =>
        $"Phiên bản: v{r.Version}\r\n" +
        $"SKU: {r.Sku}\r\n" +
        $"Tên sản phẩm: {r.ProductName}\r\n" +
        $"Vị trí: {r.Location}\r\n" +
        $"Thời gian: {r.Hour:00}:{r.Minute:00} {r.OccurredDate:dd/MM/yyyy}\r\n" +
        $"Ca: {r.Shift}\r\n" +
        $"Số lượng: {r.Quantity:0.##} {r.BaseUnit}\r\n" +
        $"Gửi bởi: {r.CreatedBy}\r\n" +
        $"Cập nhật bởi: {r.UpdatedBy ?? r.CreatedBy}\r\n" +
        $"Cập nhật lúc: {(r.UpdatedAt ?? r.CreatedAt):dd/MM/yyyy HH:mm:ss}\r\n" +
        $"Trạng thái: {r.SyncStatus}\r\n" +
        $"Chi tiết xung đột: {r.LastError ?? "-"}";

    private static string FormatRemote(RemoteReportChange r)
    {
        if (r.Deleted)
            return $"Phiên bản: v{r.Version}\r\nTrạng thái: ĐÃ XÓA trên Google\r\n" +
                   $"Xóa bởi: {r.DeletedBy}\r\n" +
                   $"Xóa lúc: {(r.DeletedAt.HasValue ? r.DeletedAt.Value.ToString("dd/MM/yyyy HH:mm:ss") : "-")}";

        return $"Phiên bản: v{r.Version}\r\n" +
               $"SKU: {r.Sku}\r\n" +
               $"Tên sản phẩm: {r.ProductName}\r\n" +
               $"Vị trí: {r.Location}\r\n" +
               $"Thời gian: {r.Hour:00}:{r.Minute:00} {r.OccurredDate:dd/MM/yyyy}\r\n" +
               $"Ca: {r.Shift}\r\n" +
               $"Số lượng: {r.Quantity:0.##} {r.BaseUnit}\r\n" +
               $"Gửi bởi: {r.CreatedBy}\r\n" +
               $"Cập nhật bởi: {(string.IsNullOrWhiteSpace(r.UpdatedBy) ? r.CreatedBy : r.UpdatedBy)}\r\n" +
               $"Cập nhật lúc: {(r.UpdatedAt ?? r.CreatedAt):dd/MM/yyyy HH:mm:ss}\r\n" +
               $"Ảnh: {r.Images.Count:N0}";
    }
}
