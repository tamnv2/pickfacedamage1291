from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def write(path: str, text: str) -> None:
    (ROOT / path).write_text(text, encoding="utf-8", newline="\n")


def replace_once(path: str, old: str, new: str) -> None:
    text = read(path)
    if old not in text:
        raise SystemExit(f"Expected block not found in {path}: {old[:180]!r}")
    if text.count(old) != 1:
        raise SystemExit(f"Expected exactly one block in {path}, found {text.count(old)}")
    write(path, text.replace(old, new, 1))


def replace_between(path: str, start: str, end: str, replacement: str) -> None:
    text = read(path)
    a = text.find(start)
    if a < 0:
        raise SystemExit(f"Start marker not found in {path}: {start!r}")
    b = text.find(end, a)
    if b < 0:
        raise SystemExit(f"End marker not found in {path}: {end!r}")
    write(path, text[:a] + replacement + text[b:])


# -----------------------------------------------------------------------------
# 1) Responsive button system: width is constrained by the parent immediately,
#    wrapped rows recalculate their own height, and buttons grow with DPI/font.
# -----------------------------------------------------------------------------
replace_between(
    "src/PickfaceDamage1291/V133Runtime.cs",
    "internal static class AppUiStyle\n{\n",
    "internal static class EntryInputRules\n{\n",
    '''internal static class AppUiStyle
{
    private sealed class FlowLayoutState
    {
        public bool Pending;
    }

    private static readonly ConditionalWeakTable<FlowLayoutPanel, FlowLayoutState> ResponsiveFlows = new();

    public static void StyleAllButtons(Control root)
    {
        foreach (var button in FindAll<Button>(root))
            StyleButton(button, Classify(button.Text));
        BindResponsiveFlows(root);
    }

    public static void StyleButton(Button button, ButtonVisual visual)
    {
        button.UseVisualStyleBackColor = false;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.AutoEllipsis = false;

        if (button.Dock != DockStyle.Fill)
        {
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        }
        button.Padding = new Padding(14, 6, 14, 6);
        button.Margin = new Padding(4, 4, 8, 4);
        button.MinimumSize = new Size(button.MinimumSize.Width, 42);

        switch (visual)
        {
            case ButtonVisual.Primary:
                button.BackColor = Color.FromArgb(37, 99, 235);
                button.ForeColor = Color.White;
                button.FlatAppearance.BorderColor = Color.FromArgb(29, 78, 216);
                break;
            case ButtonVisual.Danger:
                button.BackColor = Color.FromArgb(220, 38, 38);
                button.ForeColor = Color.White;
                button.FlatAppearance.BorderColor = Color.FromArgb(185, 28, 28);
                break;
            default:
                button.BackColor = Color.FromArgb(226, 232, 240);
                button.ForeColor = Color.FromArgb(30, 41, 59);
                button.FlatAppearance.BorderColor = Color.FromArgb(148, 163, 184);
                break;
        }
    }

    private static void BindResponsiveFlows(Control root)
    {
        foreach (var flow in FindAll<FlowLayoutPanel>(root)
                     .Where(x => x.Controls.OfType<Button>().Any()))
        {
            if (!ResponsiveFlows.TryGetValue(flow, out var state))
            {
                state = new FlowLayoutState();
                ResponsiveFlows.Add(flow, state);
                flow.SizeChanged += (_, _) => ScheduleFlowReflow(flow, state);
                flow.Layout += (_, _) => ScheduleFlowReflow(flow, state);
                flow.ControlAdded += (_, _) => ScheduleFlowReflow(flow, state);
                flow.ControlRemoved += (_, _) => ScheduleFlowReflow(flow, state);
                flow.HandleCreated += (_, _) => ScheduleFlowReflow(flow, state);
            }

            // AutoSize FlowLayoutPanel can grow horizontally beyond its visible parent.
            // Constrain width with Dock and calculate only the required wrapped height.
            flow.WrapContents = true;
            flow.AutoScroll = false;
            flow.AutoSize = false;
            if (flow.Dock == DockStyle.Fill && flow.Parent is GroupBox)
                flow.Dock = DockStyle.Top;
            ScheduleFlowReflow(flow, state);
        }
    }

    private static void ScheduleFlowReflow(FlowLayoutPanel flow, FlowLayoutState state)
    {
        if (flow.IsDisposed || state.Pending || !flow.IsHandleCreated) return;
        state.Pending = true;
        try
        {
            flow.BeginInvoke((Action)(() =>
            {
                state.Pending = false;
                if (flow.IsDisposed) return;

                flow.PerformLayout();
                var bottom = flow.Padding.Top;
                foreach (Control child in flow.Controls)
                {
                    if (!child.Visible) continue;
                    bottom = Math.Max(bottom, child.Bottom + child.Margin.Bottom);
                }

                var desired = Math.Max(1, bottom + flow.Padding.Bottom + 2);
                if (flow.Height != desired)
                    flow.Height = desired;
                flow.Parent?.PerformLayout();
            }));
        }
        catch
        {
            state.Pending = false;
        }
    }

    private static ButtonVisual Classify(string? text)
    {
        var value = (text ?? string.Empty).Trim();
        if (value.Contains("Xoá", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Xóa", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Đăng xuất", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Bỏ ảnh", StringComparison.OrdinalIgnoreCase))
            return ButtonVisual.Danger;

        if (value.StartsWith("GỬI", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("LƯU", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Đồng bộ", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("Cập nhật lên", StringComparison.OrdinalIgnoreCase) ||
            value is "Đổi mật khẩu" or "Đổi email")
            return ButtonVisual.Primary;

        return ButtonVisual.Normal;
    }

    private static IEnumerable<T> FindAll<T>(Control root) where T : Control
    {
        if (root is T self) yield return self;
        foreach (Control child in root.Controls)
        foreach (var item in FindAll<T>(child))
            yield return item;
    }
}

'''
)

replace_once(
    "src/PickfaceDamage1291/MainFormV3.cs",
    '''    private static Button NewButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Height = 34,
        Padding = new Padding(8, 0, 8, 0),
        Margin = new Padding(3, 3, 8, 3)
    };
''',
    '''    private static Button NewButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        MinimumSize = new Size(0, 42),
        Padding = new Padding(14, 6, 14, 6),
        Margin = new Padding(4, 4, 8, 4)
    };
'''
)

# -----------------------------------------------------------------------------
# 2) ADMIN multi-select/delete with password re-authentication.
# -----------------------------------------------------------------------------
replace_between(
    "src/PickfaceDamage1291/MainFormV3.cs",
    "    private TabPage BuildReportsTab()\n    {\n",
    "    private TabPage BuildProductsTab()\n    {\n",
    '''    private TabPage BuildReportsTab()
    {
        var tab = NewTab("Danh sách đã nhập");
        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), RowCount = 2, ColumnCount = 1 };
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var commands = NewSection("Công cụ danh sách");
        commands.Dock = DockStyle.Top;
        var top = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, Padding = new Padding(12), WrapContents = true };
        var refresh = NewButton("Làm mới");
        var sync = NewButton("Đồng bộ lại");
        var edit = NewButton("Chỉnh sửa phiếu (ADMIN)");
        var selectAll = NewButton("Chọn tất cả");
        var clearSelection = NewButton("Bỏ chọn");
        var delete = NewButton("Xoá phiếu đã chọn (ADMIN)");
        var export = NewButton("Xuất Excel — sẽ cập nhật logic");
        var isAdmin = AppSession.Current?.Profile.IsAdmin == true;

        refresh.Click += (_, _) => RefreshReports();
        sync.Click += async (_, _) => await SyncPendingAsync();
        edit.Click += async (_, _) => await EditSelectedReportAsync();
        selectAll.Click += (_, _) =>
        {
            _reportGrid.ClearSelection();
            foreach (DataGridViewRow row in _reportGrid.Rows) row.Selected = true;
        };
        clearSelection.Click += (_, _) => _reportGrid.ClearSelection();
        delete.Click += async (_, _) => await DeleteSelectedReportsAsync();
        export.Click += (_, _) => MessageBox.Show(this, "Chức năng xuất Excel đang giữ chỗ và sẽ cập nhật theo logic OWNER chốt sau.", "Thông báo");

        edit.Visible = isAdmin;
        selectAll.Visible = isAdmin;
        clearSelection.Visible = isAdmin;
        delete.Visible = isAdmin;
        _reportStatus.AutoSize = true;
        _reportStatus.Padding = new Padding(18, 9, 0, 0);
        top.Controls.AddRange([refresh, sync, edit, selectAll, clearSelection, delete, export, _reportStatus]);
        commands.Controls.Add(top);

        ConfigureGrid(_reportGrid);
        _reportGrid.MultiSelect = true;
        _reportGrid.KeyDown += (_, e) =>
        {
            if (!e.Control || e.KeyCode != Keys.A) return;
            foreach (DataGridViewRow row in _reportGrid.Rows) row.Selected = true;
            e.Handled = true;
            e.SuppressKeyPress = true;
        };

        outer.Controls.Add(commands, 0, 0);
        outer.Controls.Add(_reportGrid, 0, 1);
        tab.Controls.Add(outer);
        return tab;
    }

'''
)

replace_once(
    "src/PickfaceDamage1291/MainFormV3.cs",
    "        var rows = Database.GetReports();\n",
    "        var rows = Database.GetReports(int.MaxValue);\n"
)

replace_once(
    "src/PickfaceDamage1291/MainFormV3.cs",
    '''        var report = new DamageReport(
            _draftId, _date.Value.Date, _time.Hour, _time.Minute, _shift.SelectedShift,
            _sku.Text.Trim(), _productName.Text, location, decimal.Truncate(_quantity.Value), _baseUnit.Text,
            DateTime.Now, "PENDING", null, null,
            AppSession.Current?.Profile.Username ?? string.Empty, 1, null, null);

        _send.Enabled = false;
''',
    '''        var report = new DamageReport(
            _draftId, _date.Value.Date, _time.Hour, _time.Minute, _shift.SelectedShift,
            _sku.Text.Trim(), _productName.Text, location, decimal.Truncate(_quantity.Value), _baseUnit.Text,
            DateTime.Now, "PENDING", null, null,
            AppSession.Current?.Profile.Username ?? string.Empty, 1, null, null);

        var duplicateId = Database.FindExactDuplicateReport(report, _draftImages);
        if (!string.IsNullOrWhiteSpace(duplicateId))
        {
            MessageBox.Show(
                this,
                "Toàn bộ thông tin và hình ảnh của phiếu này trùng hoàn toàn với một phiếu đã có. Hệ thống không tạo thêm bản ghi trùng.\n\nID phiếu đã có: " + duplicateId,
                "Phát hiện phiếu trùng",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        _send.Enabled = false;
'''
)

insert_marker = "    private static string DisplayStatus(string status) => status switch\n"
text = read("src/PickfaceDamage1291/MainFormV3.cs")
if insert_marker not in text:
    raise SystemExit("DisplayStatus marker not found")
delete_method = '''    private async Task DeleteSelectedReportsAsync()
    {
        var session = AppSession.Current;
        if (session is null || !session.Profile.IsAdmin)
        {
            MessageBox.Show(this, "Chỉ ADMIN được xoá phiếu đã nhập.", "Không có quyền", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var ids = _reportGrid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(row => Convert.ToString(row.Cells["ReportId"].Value) ?? string.Empty)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (ids.Count == 0)
        {
            MessageBox.Show(this, "Chọn ít nhất một phiếu cần xoá. Có thể dùng Ctrl/Shift hoặc nút Chọn tất cả.", "Chưa chọn phiếu", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!GoogleService.IsConnected())
        {
            MessageBox.Show(
                this,
                "Xoá phiếu cần kết nối Google để ghi dấu đã xoá trước, tránh dữ liệu quay lại ở lần đồng bộ sau.",
                "Chưa thể xoá an toàn",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var reports = ids.Select(Database.GetReportById).Where(x => x is not null).Cast<DamageReport>().ToList();
        if (reports.Count == 0)
        {
            RefreshReports();
            MessageBox.Show(this, "Các phiếu đã chọn không còn tồn tại trên máy.", "Dữ liệu đã thay đổi");
            return;
        }

        var confirm = MessageBox.Show(
            this,
            $"Xoá {reports.Count:N0} phiếu đã chọn khỏi danh sách?\n\nGoogle sẽ được ghi dấu đã xoá để dữ liệu không tự xuất hiện lại khi đồng bộ. Hành động này được ghi vào lịch sử.",
            "Xác nhận xoá phiếu",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        using var passwordDialog = new AdminPasswordConfirmDialog(reports.Count);
        if (passwordDialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            UseWaitCursor = true;
            await AccountSelfService.VerifyCurrentPasswordAsync(session, passwordDialog.Password);
        }
        catch (Exception ex)
        {
            UseWaitCursor = false;
            MessageBox.Show(this, ex.Message, "Không xác minh được mật khẩu", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        AppPaths.BackupDatabase();
        var deleted = new List<DamageReport>();
        var failures = new List<string>();
        try
        {
            foreach (var report in reports)
            {
                try
                {
                    await GoogleService.MarkReportDeletedAsync(report, session.Profile.Username);
                    if (Database.DeleteDamageReport(report.ReportId, session.Profile.Username, remoteTombstoned: true))
                        deleted.Add(report);
                }
                catch (Exception ex)
                {
                    failures.Add($"{report.Sku} / {report.ReportId[..Math.Min(8, report.ReportId.Length)]}: {ex.Message}");
                }
            }

            if (deleted.Count > 0)
            {
                try
                {
                    await FirebaseClient.AppendAuditAsync(session, "DAMAGE_REPORTS_DELETED", new
                    {
                        count = deleted.Count,
                        report_ids = deleted.Take(100).Select(x => x.ReportId).ToArray(),
                        ids_truncated = deleted.Count > 100,
                        skus = deleted.Take(100).Select(x => x.Sku).ToArray(),
                        google_tombstone = true
                    });
                }
                catch { }
            }
        }
        finally
        {
            UseWaitCursor = false;
            RefreshReports();
        }

        if (failures.Count == 0)
        {
            MessageBox.Show(this, $"Đã xoá {deleted.Count:N0} phiếu. Google đã được ghi dấu xoá để chống khôi phục lại dữ liệu cũ.", "Đã xoá", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var detail = string.Join("\n", failures.Take(8));
        if (failures.Count > 8) detail += $"\n... và {failures.Count - 8:N0} phiếu khác.";
        MessageBox.Show(
            this,
            $"Đã xoá {deleted.Count:N0}/{reports.Count:N0} phiếu. Các phiếu lỗi vẫn được giữ nguyên để tránh mất dữ liệu không đồng bộ.\n\n{detail}",
            "Xoá chưa hoàn tất",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

'''
text = text.replace(insert_marker, delete_method + insert_marker, 1)
write("src/PickfaceDamage1291/MainFormV3.cs", text)

# -----------------------------------------------------------------------------
# Database: exact duplicate detection + durable local delete tombstones.
# -----------------------------------------------------------------------------
replace_once(
    "src/PickfaceDamage1291/Database.cs",
    '''CREATE TABLE IF NOT EXISTS damage_images (
    report_id TEXT NOT NULL,
    sequence INTEGER NOT NULL,
    local_path TEXT NOT NULL,
    drive_file_id TEXT,
    drive_link TEXT,
    PRIMARY KEY(report_id, sequence),
    FOREIGN KEY(report_id) REFERENCES damage_reports(report_id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_damage_created ON damage_reports(created_at DESC);
''',
    '''CREATE TABLE IF NOT EXISTS damage_images (
    report_id TEXT NOT NULL,
    sequence INTEGER NOT NULL,
    local_path TEXT NOT NULL,
    drive_file_id TEXT,
    drive_link TEXT,
    PRIMARY KEY(report_id, sequence),
    FOREIGN KEY(report_id) REFERENCES damage_reports(report_id) ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS deleted_reports (
    report_id TEXT PRIMARY KEY,
    deleted_at TEXT NOT NULL,
    deleted_by TEXT NOT NULL,
    remote_tombstoned INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS idx_damage_created ON damage_reports(created_at DESC);
'''
)

marker = "    public static void SetReportStatus(string reportId, string status, string? error = null)\n"
text = read("src/PickfaceDamage1291/Database.cs")
if marker not in text:
    raise SystemExit("Database SetReportStatus marker not found")
db_methods = r'''    public static string? FindExactDuplicateReport(DamageReport candidate, IReadOnlyList<string> candidateImagePaths)
    {
        var candidateHashes = HashImages(candidateImagePaths);
        if (candidateHashes is null) return null;

        var ids = new List<string>();
        using (var cn = Open())
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = """
SELECT report_id
FROM damage_reports
WHERE occurred_date=$date
  AND occurred_hour=$h
  AND occurred_minute=$m
  AND shift=$shift
  AND sku=$sku
  AND product_name=$name
  AND location=$loc
  AND quantity=$qty
  AND base_unit=$base
ORDER BY created_at DESC
""";
            cmd.Parameters.AddWithValue("$date", candidate.OccurredDate.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("$h", candidate.Hour);
            cmd.Parameters.AddWithValue("$m", candidate.Minute);
            cmd.Parameters.AddWithValue("$shift", candidate.Shift);
            cmd.Parameters.AddWithValue("$sku", candidate.Sku);
            cmd.Parameters.AddWithValue("$name", candidate.ProductName);
            cmd.Parameters.AddWithValue("$loc", candidate.Location);
            cmd.Parameters.AddWithValue("$qty", candidate.Quantity.ToString(CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$base", candidate.BaseUnit);
            using var r = cmd.ExecuteReader();
            while (r.Read()) ids.Add(r.GetString(0));
        }

        foreach (var id in ids)
        {
            var existing = GetImages(id).Select(x => x.LocalPath).ToList();
            var existingHashes = HashImages(existing);
            if (existingHashes is null) continue;
            if (candidateHashes.SequenceEqual(existingHashes, StringComparer.OrdinalIgnoreCase))
                return id;
        }
        return null;
    }

    private static List<string>? HashImages(IReadOnlyList<string> paths)
    {
        var hashes = new List<string>(paths.Count);
        foreach (var path in paths)
        {
            if (!File.Exists(path)) return null;
            try { hashes.Add(ImageHashService.Sha256File(path)); }
            catch { return null; }
        }
        hashes.Sort(StringComparer.OrdinalIgnoreCase);
        return hashes;
    }

    public static bool DeleteDamageReport(string reportId, string deletedBy, bool remoteTombstoned)
    {
        if (string.IsNullOrWhiteSpace(reportId)) return false;
        var imagePaths = new List<string>();
        var deleted = false;

        using (var cn = Open())
        using (var tx = cn.BeginTransaction())
        {
            using (var images = cn.CreateCommand())
            {
                images.Transaction = tx;
                images.CommandText = "SELECT local_path FROM damage_images WHERE report_id=$id";
                images.Parameters.AddWithValue("$id", reportId);
                using var r = images.ExecuteReader();
                while (r.Read()) imagePaths.Add(r.GetString(0));
            }

            using (var tombstone = cn.CreateCommand())
            {
                tombstone.Transaction = tx;
                tombstone.CommandText = """
INSERT INTO deleted_reports(report_id,deleted_at,deleted_by,remote_tombstoned)
VALUES($id,$at,$by,$remote)
ON CONFLICT(report_id) DO UPDATE SET
 deleted_at=excluded.deleted_at,
 deleted_by=excluded.deleted_by,
 remote_tombstoned=excluded.remote_tombstoned
""";
                tombstone.Parameters.AddWithValue("$id", reportId);
                tombstone.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O"));
                tombstone.Parameters.AddWithValue("$by", deletedBy);
                tombstone.Parameters.AddWithValue("$remote", remoteTombstoned ? 1 : 0);
                tombstone.ExecuteNonQuery();
            }

            using (var del = cn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM damage_reports WHERE report_id=$id";
                del.Parameters.AddWithValue("$id", reportId);
                deleted = del.ExecuteNonQuery() == 1;
            }
            tx.Commit();
        }

        if (!deleted) return false;

        var root = Path.GetFullPath(AppPaths.PendingImages).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var path in imagePaths)
        {
            try
            {
                var full = Path.GetFullPath(path);
                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                if (File.Exists(full)) File.Delete(full);
                var parent = Path.GetDirectoryName(full);
                if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any())
                    Directory.Delete(parent, false);
            }
            catch
            {
                // DB deletion remains authoritative. Orphan local files can be cleaned later.
            }
        }
        return true;
    }

'''
text = text.replace(marker, db_methods + marker, 1)
write("src/PickfaceDamage1291/Database.cs", text)

# -----------------------------------------------------------------------------
# Password re-authentication and compact password confirmation dialog.
# -----------------------------------------------------------------------------
replace_once(
    "src/PickfaceDamage1291/AccountSelfService.cs",
    '''    public static async Task ChangePasswordAsync(
''',
    '''    public static async Task VerifyCurrentPasswordAsync(
        FirebaseSession session,
        string currentPassword,
        CancellationToken ct = default)
    {
        EnsureOnline(session);
        if (string.IsNullOrWhiteSpace(currentPassword))
            throw new InvalidOperationException("Chưa nhập mật khẩu hiện tại.");

        var reauthenticated = await UsernameAuthService.SignInAsync(session.Profile.Username, currentPassword, ct);
        if (!string.Equals(reauthenticated.Uid, session.Uid, StringComparison.Ordinal))
            throw new InvalidOperationException("Không xác minh được tài khoản hiện tại.");
    }

    public static async Task ChangePasswordAsync(
'''
)

marker = "internal sealed class ChangePasswordDialog : Form\n"
text = read("src/PickfaceDamage1291/AccountSelfService.cs")
if marker not in text:
    raise SystemExit("ChangePasswordDialog marker not found")
password_dialog = '''internal sealed class AdminPasswordConfirmDialog : Form
{
    private readonly TextBox _password = new() { UseSystemPasswordChar = true };
    public string Password => _password.Text;

    public AdminPasswordConfirmDialog(int reportCount)
    {
        Text = "Xác nhận mật khẩu ADMIN";
        Width = 500;
        Height = 225;
        MinimumSize = new Size(460, 210);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10F);

        var table = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 2, RowCount = 0 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var note = new Label
        {
            Text = $"Đang xác nhận xoá {reportCount:N0} phiếu. Mật khẩu không được lưu lại.",
            AutoSize = true,
            MaximumSize = new Size(420, 0),
            ForeColor = Color.DimGray
        };
        AddRow(table, "", note);
        AddRow(table, "Mật khẩu hiện tại", _password);

        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var ok = new Button { Text = "Xác nhận xoá", AutoSize = true };
        var cancel = new Button { Text = "Hủy", AutoSize = true, DialogResult = DialogResult.Cancel };
        AppUiStyle.StyleButton(ok, ButtonVisual.Danger);
        AppUiStyle.StyleButton(cancel, ButtonVisual.Normal);
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_password.Text))
            {
                MessageBox.Show(this, "Nhập mật khẩu hiện tại của tài khoản ADMIN.", "Thiếu mật khẩu", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        };
        actions.Controls.AddRange([ok, cancel]);
        AddRow(table, "", actions);
        Controls.Add(table);
        AcceptButton = ok;
        CancelButton = cancel;
        Shown += (_, _) => _password.Focus();
    }

    private static void AddRow(TableLayoutPanel table, string label, Control control)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 8, 12, 12) }, 0, row);
        control.Dock = control is FlowLayoutPanel ? DockStyle.Top : DockStyle.Fill;
        control.Margin = new Padding(3, 3, 3, 12);
        table.Controls.Add(control, 1, row);
    }
}

'''
text = text.replace(marker, password_dialog + marker, 1)
write("src/PickfaceDamage1291/AccountSelfService.cs", text)

# -----------------------------------------------------------------------------
# Google tombstone write. Uses existing sync_report gateway action, therefore no
# Apps Script redeploy is required. A single mutation gate prevents a pending
# background write from overwriting a delete marker on the same client.
# -----------------------------------------------------------------------------
replace_between(
    "src/PickfaceDamage1291/GoogleServiceGateway.cs",
    "    public static async Task SyncReportAsync(DamageReport report, IProgress<int>? progress = null)\n    {\n",
    "    private static void ValidateGatewayResult(DamageReport report, IReadOnlyList<DamageImage> localImages, JsonElement root)\n",
    '''    private static readonly SemaphoreSlim MutationGate = new(1, 1);

    public static async Task SyncReportAsync(DamageReport report, IProgress<int>? progress = null)
    {
        EnsureGatewayReady();
        await MutationGate.WaitAsync();
        try
        {
            try
            {
                progress?.Report(5);
                Database.SetReportStatus(report.ReportId, "SYNCING");
                var localImages = Database.GetImages(report.ReportId);
                var imagePayload = new List<object>();

                var imageIndex = 0;
                foreach (var image in localImages.OrderBy(x => x.Sequence))
                {
                    string? base64 = null;
                    string? mimeType = null;
                    string? originalName = null;
                    if (string.IsNullOrWhiteSpace(image.DriveFileId))
                    {
                        if (!File.Exists(image.LocalPath))
                            throw new FileNotFoundException("Không tìm thấy ảnh trên máy để đồng bộ.", image.LocalPath);
                        var bytes = await File.ReadAllBytesAsync(image.LocalPath);
                        base64 = Convert.ToBase64String(bytes);
                        mimeType = GetMimeType(image.LocalPath);
                        originalName = Path.GetFileName(image.LocalPath);
                    }

                    imagePayload.Add(new
                    {
                        sequence = image.Sequence,
                        drive_file_id = image.DriveFileId ?? string.Empty,
                        drive_link = image.DriveLink ?? string.Empty,
                        original_name = originalName ?? string.Empty,
                        mime_type = mimeType ?? string.Empty,
                        data_base64 = base64 ?? string.Empty
                    });

                    imageIndex++;
                    var imageProgress = localImages.Count == 0 ? 30 : 10 + (int)Math.Round(25d * imageIndex / localImages.Count);
                    progress?.Report(imageProgress);
                }

                if (localImages.Count == 0) progress?.Report(35);

                var payload = new
                {
                    report = new
                    {
                        report_id = report.ReportId,
                        occurred_date = report.OccurredDate.ToString("yyyy-MM-dd"),
                        hour = report.Hour,
                        minute = report.Minute,
                        shift = report.Shift,
                        sku = report.Sku,
                        product_name = report.ProductName,
                        location = report.Location,
                        quantity = decimal.Truncate(report.Quantity),
                        base_unit = report.BaseUnit,
                        created_at = report.CreatedAt.ToString("O"),
                        created_by = report.CreatedBy,
                        version = report.Version,
                        updated_at = report.UpdatedAt?.ToString("O") ?? string.Empty,
                        updated_by = report.UpdatedBy ?? string.Empty
                    },
                    images = imagePayload
                };

                progress?.Report(45);
                var root = await CallGatewayAsync("sync_report", payload);
                progress?.Report(88);
                ValidateGatewayResult(report, localImages, root);

                if (root.TryGetProperty("images", out var imagesNode) && imagesNode.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in imagesNode.EnumerateArray())
                    {
                        var sequence = item.TryGetProperty("sequence", out var seqNode) ? seqNode.GetInt32() : 0;
                        var fileId = item.TryGetProperty("file_id", out var fileNode) ? fileNode.GetString() ?? string.Empty : string.Empty;
                        var link = item.TryGetProperty("url", out var urlNode) ? urlNode.GetString() ?? string.Empty : string.Empty;
                        if (sequence > 0 && !string.IsNullOrWhiteSpace(fileId) && !string.IsNullOrWhiteSpace(link))
                            Database.UpdateImageDrive(report.ReportId, sequence, fileId, link);
                    }
                }

                progress?.Report(96);
                Database.SetReportStatus(report.ReportId, "SYNCED");
                progress?.Report(100);
            }
            catch (Exception ex)
            {
                Database.SetReportStatus(report.ReportId, "ERROR", ex.Message);
                throw;
            }
        }
        finally
        {
            MutationGate.Release();
        }
    }

    public static async Task MarkReportDeletedAsync(DamageReport report, string deletedBy)
    {
        EnsureGatewayReady();
        await MutationGate.WaitAsync();
        try
        {
            var now = DateTime.Now;
            var payload = new
            {
                report = new
                {
                    report_id = report.ReportId,
                    occurred_date = string.Empty,
                    hour = 0,
                    minute = 0,
                    shift = string.Empty,
                    sku = "__DELETED__",
                    product_name = "ĐÃ XÓA",
                    location = string.Empty,
                    quantity = 0,
                    base_unit = string.Empty,
                    created_at = report.CreatedAt.ToString("O"),
                    created_by = report.CreatedBy,
                    version = Math.Max(1, report.Version) + 1,
                    updated_at = now.ToString("O"),
                    updated_by = deletedBy
                },
                images = Array.Empty<object>()
            };

            var root = await CallGatewayAsync("sync_report", payload);
            var returnedId = root.TryGetProperty("report_id", out var idNode) ? idNode.GetString() ?? string.Empty : string.Empty;
            var row = root.TryGetProperty("row", out var rowNode) && rowNode.TryGetInt32(out var rowValue) ? rowValue : 0;
            if (!string.Equals(returnedId, report.ReportId, StringComparison.Ordinal) || row < 2)
                throw new InvalidOperationException("Google chưa xác nhận ghi dấu xoá cho phiếu.");
        }
        finally
        {
            MutationGate.Release();
        }
    }

'''
)

# -----------------------------------------------------------------------------
# Release metadata: current live is 1.3.6, therefore this batch becomes 1.3.7.
# -----------------------------------------------------------------------------
replace_once(
    "src/PickfaceDamage1291/PickfaceDamage1291.csproj",
    '''    <Version>1.3.6</Version>
    <InformationalVersion>1.3.6</InformationalVersion>
    <AssemblyVersion>1.3.6.0</AssemblyVersion>
    <FileVersion>1.3.6.0</FileVersion>
''',
    '''    <Version>1.3.7</Version>
    <InformationalVersion>1.3.7</InformationalVersion>
    <AssemblyVersion>1.3.7.0</AssemblyVersion>
    <FileVersion>1.3.7.0</FileVersion>
'''
)

print("v1.3.7 responsive buttons + admin delete + exact duplicate guard patch applied")
