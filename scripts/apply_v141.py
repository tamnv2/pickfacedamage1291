from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(path):
    return (ROOT / path).read_text(encoding="utf-8")


def write(path, text):
    (ROOT / path).write_text(text, encoding="utf-8")


def replace_once(path, old, new):
    text = read(path)
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"Expected exactly one match in {path}, found {count}: {old[:120]!r}")
    write(path, text.replace(old, new, 1))


# Version.
p = "src/PickfaceDamage1291/PickfaceDamage1291.csproj"
text = read(p)
for old, new in [
    ("<Version>1.4.0</Version>", "<Version>1.4.1</Version>"),
    ("<InformationalVersion>1.4.0</InformationalVersion>", "<InformationalVersion>1.4.1</InformationalVersion>"),
    ("<AssemblyVersion>1.4.0.0</AssemblyVersion>", "<AssemblyVersion>1.4.1.0</AssemblyVersion>"),
    ("<FileVersion>1.4.0.0</FileVersion>", "<FileVersion>1.4.1.0</FileVersion>"),
]:
    if old not in text:
        raise SystemExit(f"Missing version marker: {old}")
    text = text.replace(old, new, 1)
write(p, text)

# Make all network paths honor Windows/system proxy + credentials.
for path, old, new in [
    ("src/PickfaceDamage1291/FirebaseClient.cs",
     "private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };",
     "private static readonly HttpClient Http = NetworkHttpClientFactory.Create(TimeSpan.FromSeconds(30), \"PickfaceDamage1291-Firebase\");"),
    ("src/PickfaceDamage1291/AccountSelfService.cs",
     "private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };",
     "private static readonly HttpClient Http = NetworkHttpClientFactory.Create(TimeSpan.FromSeconds(30), \"PickfaceDamage1291-Account\");"),
    ("src/PickfaceDamage1291/UsernameAuthService.cs",
     "private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };",
     "private static readonly HttpClient Http = NetworkHttpClientFactory.Create(TimeSpan.FromSeconds(20), \"PickfaceDamage1291-UsernameAuth\");"),
]:
    replace_once(path, old, new)

replace_once(
    "src/PickfaceDamage1291/GoogleServiceGateway.cs",
    '''    private static HttpClient CreateClient()\n    {\n        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(4) };\n        client.DefaultRequestHeaders.UserAgent.ParseAdd("PickfaceDamage1291-GoogleGateway");\n        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));\n        return client;\n    }''',
    '''    private static HttpClient CreateClient()\n    {\n        var client = NetworkHttpClientFactory.Create(TimeSpan.FromMinutes(4), "PickfaceDamage1291-GoogleGateway");\n        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));\n        return client;\n    }''')

replace_once(
    "src/PickfaceDamage1291/GoogleGatewayV140.cs",
    '''    private static HttpClient CreateClient()\n    {\n        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(4) };\n        client.DefaultRequestHeaders.UserAgent.ParseAdd("PickfaceDamage1291-SyncV140");\n        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));\n        return client;\n    }''',
    '''    private static HttpClient CreateClient()\n    {\n        var client = NetworkHttpClientFactory.Create(TimeSpan.FromMinutes(4), "PickfaceDamage1291-SyncV141");\n        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));\n        return client;\n    }''')

# Apps Script capability marker lets the EXE distinguish old deployment from permission/network errors.
replace_once(
    "apps-script/GoogleGateway/Code.gs",
    "const LOGIN_ALIAS_PREFIX = 'LOGIN_ALIAS_';",
    "const LOGIN_ALIAS_PREFIX = 'LOGIN_ALIAS_';\nconst GATEWAY_VERSION = '1.4.1';\nconst GATEWAY_CAPABILITIES = Object.freeze(['pull_changes','push_products','pull_products','append_audit','list_audit','delete_audit_range','upload_log','sync_report','get_image']);")

replace_once(
    "apps-script/GoogleGateway/Code.gs",
    '''    const action = String(request.action || '');\n    const payload = request.payload || {};\n\n    if (action === 'login_by_username') {''',
    '''    const action = String(request.action || '');\n    const payload = request.payload || {};\n\n    if (action === 'gateway_info') {\n      return json_({ ok: true, version: GATEWAY_VERSION, capabilities: GATEWAY_CAPABILITIES });\n    }\n\n    if (action === 'login_by_username') {''')

replace_once(
    "apps-script/GoogleGateway/Code.gs",
    '''      return json_({ ok: true, uid: auth.uid, username: auth.profile.username || '' });''',
    '''      return json_({ ok: true, uid: auth.uid, username: auth.profile.username || '', version: GATEWAY_VERSION, capabilities: GATEWAY_CAPABILITIES });''')

# Client-side gateway capability handshake and clearer old-deployment diagnostics.
insert_marker = '''    public static async Task<ReportChangePage> PullReportChangesAsync(long afterSeq, int limit = 500, CancellationToken ct = default)'''
insert_code = '''    public static async Task VerifyCapabilitiesAsync(CancellationToken ct = default)\n    {\n        JsonElement root;\n        try\n        {\n            root = await CallAsync("gateway_info", new { }, ct);\n        }\n        catch (InvalidOperationException ex) when (ex.Message.Contains("Action không được hỗ trợ", StringComparison.OrdinalIgnoreCase))\n        {\n            throw new InvalidOperationException("Google Gateway đang chạy deployment cũ hoặc ứng dụng đang trỏ tới URL /exec cũ. Đây không phải lỗi quyền ADMIN. Hãy cập nhật đúng deployment Apps Script hiện hữu.", ex);\n        }\n\n        var version = Str(root, "version");\n        var capabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);\n        if (root.TryGetProperty("capabilities", out var node) && node.ValueKind == JsonValueKind.Array)\n            foreach (var item in node.EnumerateArray())\n                if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString())) capabilities.Add(item.GetString()!);\n\n        var required = new[] { "pull_changes", "push_products", "pull_products", "append_audit", "list_audit", "delete_audit_range", "upload_log", "sync_report", "get_image" };\n        var missing = required.Where(x => !capabilities.Contains(x)).ToArray();\n        if (missing.Length > 0)\n            throw new InvalidOperationException($"Google Gateway {version.DefaultIfEmpty('?', StringComparison.Ordinal)} thiếu chức năng: {string.Join(", ", missing)}. Hãy deploy Code.gs v1.4.1 vào đúng deployment /exec đang dùng.");\n    }\n\n'''
# Avoid a non-existent DefaultIfEmpty overload: normalize after insertion.
text = read("src/PickfaceDamage1291/GoogleGatewayV140.cs")
if text.count(insert_marker) != 1:
    raise SystemExit("Could not find GoogleGatewayV140 insertion marker")
text = text.replace(insert_marker, insert_code + insert_marker, 1)
text = text.replace("$\"Google Gateway {version.DefaultIfEmpty('?', StringComparison.Ordinal)} thiếu chức năng: {string.Join(\", \", missing)}. Hãy deploy Code.gs v1.4.1 vào đúng deployment /exec đang dùng.\"",
                    "$\"Google Gateway {(string.IsNullOrWhiteSpace(version) ? \"?\" : version)} thiếu chức năng: {string.Join(\", \", missing)}. Hãy deploy Code.gs v1.4.1 vào đúng deployment /exec đang dùng.\"")
write("src/PickfaceDamage1291/GoogleGatewayV140.cs", text)

replace_once(
    "src/PickfaceDamage1291/GoogleServiceGateway.cs",
    '''    public static async Task VerifyBindingAsync(bool writeHeaders = false)\n    {\n        var payload = new''',
    '''    public static async Task VerifyBindingAsync(bool writeHeaders = false)\n    {\n        await GoogleGatewayV140.VerifyCapabilitiesAsync();\n        var payload = new''')

# Pull-only path for view-only users and export; never pushes another user's pending local rows.
marker = '''    public static async Task<int> PushLocalProductCatalogAsync(IProgress<string>? progress = null, CancellationToken ct = default)'''
code = '''    public static async Task<(int ReportsPulled, int ReportsDeleted, int ProductsPulled)> PullSharedDataAsync(IProgress<string>? progress = null, CancellationToken ct = default)\n    {\n        if (!GoogleService.IsConnected())\n            throw new InvalidOperationException("Cần kết nối online để nhận dữ liệu dùng chung.");\n\n        await Gate.WaitAsync(ct);\n        try\n        {\n            SyncCacheStore.Initialize();\n            progress?.Report("Đang nhận thay đổi phiếu từ Google...");\n            var reports = await PullReportsAsync(progress, ct);\n            progress?.Report("Đang nhận danh mục SKU dùng chung...");\n            var products = await PullProductsAsync(progress, ct);\n            return (reports.Applied, reports.Deleted, products.Pulled);\n        }\n        finally\n        {\n            Gate.Release();\n        }\n    }\n\n'''
text = read("src/PickfaceDamage1291/CloudSyncService.cs")
if text.count(marker) != 1:
    raise SystemExit("CloudSyncService insertion marker not found")
write("src/PickfaceDamage1291/CloudSyncService.cs", text.replace(marker, code + marker, 1))

# Startup: users with view-only permission pull only; ADMIN/damage-entry/sync users do full two-way sync.
replace_once(
    "src/PickfaceDamage1291/UiRuntimeFixesV2.cs",
    '''            if (session.Profile.IsAdmin || session.Profile.HasPermission("sync_google") || session.Profile.HasPermission("damage_entry") || session.Profile.HasPermission("view_reports"))\n            {\n                try\n                {\n                    var progress = new Progress<string>(text => SetGoogleHeader(form, "Google: " + text));\n                    await CloudSyncService.SyncNowAsync(progress);\n                    SetGoogleHeader(form, "Google: đã đồng bộ đa máy");\n                }\n                catch (Exception syncEx)\n                {\n                    AppLog.Exception("STARTUP_SYNC_FAILED", syncEx);\n                    SetGoogleHeader(form, "Google: đã kết nối • đồng bộ chưa hoàn tất");\n                }\n            }''',
    '''            if (session.Profile.IsAdmin || session.Profile.HasPermission("sync_google") || session.Profile.HasPermission("damage_entry") || session.Profile.HasPermission("view_reports"))\n            {\n                try\n                {\n                    var progress = new Progress<string>(text => SetGoogleHeader(form, "Google: " + text));\n                    if (session.Profile.IsAdmin || session.Profile.HasPermission("sync_google") || session.Profile.HasPermission("damage_entry"))\n                        await CloudSyncService.SyncNowAsync(progress);\n                    else\n                        await CloudSyncService.PullSharedDataAsync(progress);\n                    SetGoogleHeader(form, "Google: đã đồng bộ đa máy");\n                }\n                catch (Exception syncEx)\n                {\n                    AppLog.Exception("STARTUP_SYNC_FAILED", syncEx);\n                    SetGoogleHeader(form, "Google: đã kết nối • đồng bộ chưa hoàn tất");\n                }\n            }''')

# ADMIN is never demoted on transient active_operator connectivity; only damage-entry send is fail-closed.
replace_once(
    "src/PickfaceDamage1291/SessionUiBinder.cs",
    '''        if (session.OfflineMode)\n        {\n            send.Enabled = false;\n            if (status is not null) status.Text = "Tạm khóa gửi — ADMIN đang offline nên chưa thể xác minh quyền nhập.";\n            return;\n        }''',
    '''        if (session.OfflineMode)\n        {\n            send.Enabled = false;\n            if (status is not null) status.Text = "ADMIN đã đăng nhập. Chỉ tạm khóa nút Gửi vì đang offline nên chưa đọc được trạng thái USER đang nhập.";\n            return;\n        }''')

replace_once(
    "src/PickfaceDamage1291/SessionUiBinder.cs",
    '''                var snapshot = await FirebaseClient.GetActiveOperatorSnapshotAsync(session);''',
    '''                var snapshot = await NetworkHttpClientFactory.RetryAsync(() => FirebaseClient.GetActiveOperatorSnapshotAsync(session), attempts: 3);''')

replace_once(
    "src/PickfaceDamage1291/SessionUiBinder.cs",
    '''            catch\n            {\n                if (form.IsDisposed) return;\n                void Work()\n                {\n                    send.Enabled = false;\n                    if (status is not null) status.Text = "Tạm khóa gửi — chưa xác minh được quyền nhập. Hãy kiểm tra kết nối.";\n                }\n                if (form.InvokeRequired) form.BeginInvoke((Action)Work); else Work();\n            }''',
    '''            catch (Exception ex)\n            {\n                AppLog.Exception("ADMIN_OPERATOR_VERIFY_FAILED", ex);\n                if (form.IsDisposed) return;\n                void Work()\n                {\n                    send.Enabled = false;\n                    if (status is not null) status.Text = "ADMIN vẫn có đầy đủ quyền quản trị. Chỉ tạm khóa nút Gửi vì chưa đọc được trạng thái active_operator từ Firebase.";\n                }\n                if (form.InvokeRequired) form.BeginInvoke((Action)Work); else Work();\n            }''')

# Reports UI: owner can edit own reports; ADMIN can edit all; export is real.
replace_once("src/PickfaceDamage1291/MainFormV3.cs", 'var edit = NewButton("Chỉnh sửa phiếu (ADMIN)");', 'var edit = NewButton("Chỉnh sửa phiếu");')
replace_once("src/PickfaceDamage1291/MainFormV3.cs", 'var export = NewButton("Xuất Excel — sẽ cập nhật logic");', 'var export = NewButton("Xuất Excel");')
replace_once("src/PickfaceDamage1291/MainFormV3.cs", 'export.Click += (_, _) => MessageBox.Show(this, "Chức năng xuất Excel đang giữ chỗ và sẽ cập nhật theo logic OWNER chốt sau.", "Thông báo");', 'export.Click += async (_, _) => await ExportReportsAsync();')
replace_once("src/PickfaceDamage1291/MainFormV3.cs", '        edit.Visible = isAdmin;\n        selectAll.Visible = isAdmin;', '        edit.Visible = true;\n        selectAll.Visible = isAdmin;')

replace_once(
    "src/PickfaceDamage1291/MainFormV3.cs",
    '''        if (session is null || !session.Profile.IsAdmin)\n        {\n            MessageBox.Show(this, "Chỉ ADMIN được chỉnh sửa phiếu đã nhập.", "Không có quyền");\n            return;\n        }''',
    '''        if (session is null)\n        {\n            MessageBox.Show(this, "Chưa đăng nhập ứng dụng.", "Không có phiên");\n            return;\n        }''')

replace_once(
    "src/PickfaceDamage1291/MainFormV3.cs",
    '''        var before = Database.GetReportById(id);\n        if (before is null) { MessageBox.Show(this, "Không tìm thấy phiếu local.", "Dữ liệu đã thay đổi"); return; }\n        var beforeImages = Database.GetImages(id);''',
    '''        var before = Database.GetReportById(id);\n        if (before is null) { MessageBox.Show(this, "Không tìm thấy phiếu local.", "Dữ liệu đã thay đổi"); return; }\n        var ownsReport = !string.IsNullOrWhiteSpace(before.CreatedBy) && string.Equals(before.CreatedBy, session.Profile.Username, StringComparison.OrdinalIgnoreCase);\n        if (!session.Profile.IsAdmin && !ownsReport)\n        {\n            MessageBox.Show(this, $"Phiếu này do tài khoản {before.CreatedBy.DefaultIfEmpty('-')} tạo. USER chỉ được sửa phiếu do chính mình tạo; ADMIN được sửa mọi phiếu.", "Không có quyền sửa", MessageBoxButtons.OK, MessageBoxIcon.Warning);\n            return;\n        }\n        var beforeImages = Database.GetImages(id);''')
# Fix DefaultIfEmpty placeholder above without LINQ misuse.
text = read("src/PickfaceDamage1291/MainFormV3.cs")
text = text.replace('$"Phiếu này do tài khoản {before.CreatedBy.DefaultIfEmpty(\'-\')} tạo. USER chỉ được sửa phiếu do chính mình tạo; ADMIN được sửa mọi phiếu."',
                    '$"Phiếu này do tài khoản {(string.IsNullOrWhiteSpace(before.CreatedBy) ? "-" : before.CreatedBy)} tạo. USER chỉ được sửa phiếu do chính mình tạo; ADMIN được sửa mọi phiếu."')
write("src/PickfaceDamage1291/MainFormV3.cs", text)

replace_once(
    "src/PickfaceDamage1291/MainFormV3.cs",
    '''MessageBox.Show(this, "Phiếu local đã sửa nhưng chưa ghi được audit Firebase. Phiếu vẫn chờ đồng bộ Google.\\n\\n" + auditEx.Message, "Cảnh báo audit", MessageBoxButtons.OK, MessageBoxIcon.Warning);''',
    '''MessageBox.Show(this, "Phiếu local đã sửa nhưng lịch sử thay đổi chưa gửi được Google Sheet. Phiếu vẫn chờ đồng bộ Google.\\n\\n" + auditEx.Message, "Cảnh báo lịch sử", MessageBoxButtons.OK, MessageBoxIcon.Warning);''')

sync_marker = '''    private async Task SyncPendingAsync()'''
export_method = '''    private async Task ExportReportsAsync()\n    {\n        try\n        {\n            if (GoogleService.IsConnected())\n            {\n                try\n                {\n                    _reportStatus.Text = "Đang nhận dữ liệu mới nhất trước khi xuất Excel...";\n                    await CloudSyncService.PullSharedDataAsync(new Progress<string>(s => _reportStatus.Text = s));\n                    RefreshReports();\n                }\n                catch (Exception ex)\n                {\n                    AppLog.Exception("EXPORT_PRE_SYNC_FAILED", ex);\n                    var answer = MessageBox.Show(this,\n                        "Không nhận được dữ liệu mới nhất từ Google trước khi xuất. Nếu tiếp tục, file Excel chỉ phản ánh dữ liệu hiện có trên máy này.\\n\\n" + ex.Message + "\\n\\nTiếp tục xuất dữ liệu local?",\n                        "Đồng bộ trước khi xuất chưa hoàn tất", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);\n                    if (answer != DialogResult.Yes) return;\n                }\n            }\n            else\n            {\n                var answer = MessageBox.Show(this,\n                    "Ứng dụng đang offline/chưa kết nối Google. File Excel chỉ phản ánh dữ liệu hiện có trên máy này. Tiếp tục?",\n                    "Xuất dữ liệu local", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);\n                if (answer != DialogResult.Yes) return;\n            }\n\n            var progress = new Progress<string>(s => _reportStatus.Text = s);\n            var result = await DamageReportExportService.ExportAsync(this, progress);\n            if (result is null) return;\n            _reportStatus.Text = $"Đã xuất {result.ReportCount:N0} phiếu.";\n            var imageNote = result.MissingImages > 0 ? $"\\nKhông tải/nhúng được: {result.MissingImages:N0} ảnh." : string.Empty;\n            MessageBox.Show(this, $"Đã xuất {result.ReportCount:N0} phiếu và {result.ImageCount:N0} ảnh.\\n\\n{result.Path}{imageNote}", "Xuất Excel hoàn tất", MessageBoxButtons.OK, MessageBoxIcon.Information);\n        }\n        catch (Exception ex)\n        {\n            AppLog.Exception("EXPORT_EXCEL_FAILED", ex);\n            MessageBox.Show(this, ex.Message, "Không xuất được Excel", MessageBoxButtons.OK, MessageBoxIcon.Error);\n        }\n        finally\n        {\n            RefreshReports();\n        }\n    }\n\n'''
text = read("src/PickfaceDamage1291/MainFormV3.cs")
if text.count(sync_marker) != 1:
    raise SystemExit("Sync marker not found")
write("src/PickfaceDamage1291/MainFormV3.cs", text.replace(sync_marker, export_method + sync_marker, 1))

# Edit dialog: add explicit replace-image action and make the toolbar fully auto-sized.
replace_once(
    "src/PickfaceDamage1291/DamageReportEditDialog.cs",
    '''        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(12) };\n        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));\n        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));\n        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));\n        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 180));\n\n        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };\n        var add = new Button { Text = "+ Thêm ảnh", AutoSize = true, Height = 34 };\n        var remove = new Button { Text = "Bỏ ảnh đã chọn", AutoSize = true, Height = 34 };\n        add.Click += (_, _) => AddImages();\n        remove.Click += (_, _) => RemoveImage();\n        actions.Controls.AddRange([add, remove]);''',
    '''        var layout = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, ColumnCount = 2, RowCount = 2, Padding = new Padding(12) };\n        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));\n        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));\n        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));\n        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 200));\n\n        var actions = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, WrapContents = true };\n        var add = new Button { Text = "+ Thêm ảnh", AutoSize = true, MinimumSize = new Size(0, 40), Padding = new Padding(12, 4, 12, 4) };\n        var replace = new Button { Text = "Thay ảnh đã chọn", AutoSize = true, MinimumSize = new Size(0, 40), Padding = new Padding(12, 4, 12, 4) };\n        var remove = new Button { Text = "Bỏ ảnh đã chọn", AutoSize = true, MinimumSize = new Size(0, 40), Padding = new Padding(12, 4, 12, 4) };\n        add.Click += (_, _) => AddImages();\n        replace.Click += (_, _) => ReplaceImage();\n        remove.Click += (_, _) => RemoveImage();\n        actions.Controls.AddRange([add, replace, remove]);''')

replace_once(
    "src/PickfaceDamage1291/DamageReportEditDialog.cs",
    '''        Text = "Khi lưu, report_id và thời gian/người tạo gốc được giữ nguyên. Hệ thống tăng phiên bản, ghi ADMIN sửa, thời gian sửa và audit trước → sau. Ảnh bị bỏ khỏi phiếu không bị xóa vĩnh viễn khỏi Google Drive."''',
    '''        Text = "Khi lưu, report_id và thời gian/người tạo gốc được giữ nguyên. Hệ thống tăng phiên bản, ghi người sửa, thời gian sửa và lịch sử trước → sau. Ảnh bị bỏ/thay khỏi phiếu không bị xóa vĩnh viễn khỏi Google Drive."''')

remove_marker = '''    private void RemoveImage()'''
replace_method = '''    private void ReplaceImage()\n    {\n        var index = _imageList.SelectedIndex;\n        if (index < 0 || index >= _images.Count)\n        {\n            MessageBox.Show(this, "Chọn ảnh cần thay.", "Chưa chọn ảnh", MessageBoxButtons.OK, MessageBoxIcon.Information);\n            return;\n        }\n\n        using var dialog = new OpenFileDialog { Filter = "Hình ảnh|*.jpg;*.jpeg;*.png;*.heic;*.webp", Multiselect = false, Title = "Chọn ảnh thay thế" };\n        if (dialog.ShowDialog(this) != DialogResult.OK) return;\n\n        string newHash;\n        try { newHash = ImageHashService.Sha256File(dialog.FileName); }\n        catch (Exception ex)\n        {\n            MessageBox.Show(this, ex.Message, "Không đọc được ảnh", MessageBoxButtons.OK, MessageBoxIcon.Warning);\n            return;\n        }\n\n        var old = _images[index];\n        string oldHash = string.Empty;\n        if (File.Exists(old.LocalPath))\n        {\n            try { oldHash = ImageHashService.Sha256File(old.LocalPath); } catch { }\n        }\n        if (string.IsNullOrWhiteSpace(oldHash)) oldHash = SyncCacheStore.GetImageHash(old.ReportId, old.Sequence);\n\n        if (!string.Equals(oldHash, newHash, StringComparison.OrdinalIgnoreCase) && _hashes.Contains(newHash))\n        {\n            MessageBox.Show(this, "Ảnh thay thế đã tồn tại trong phiếu.", "Ảnh trùng", MessageBoxButtons.OK, MessageBoxIcon.Information);\n            return;\n        }\n\n        var folder = AppPaths.GetDraftImageFolder(_original.ReportId);\n        var ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();\n        var target = Path.Combine(folder, $"replace_{Guid.NewGuid():N}_{newHash[..12]}{ext}");\n        File.Copy(dialog.FileName, target, false);\n        _newFiles.Add(target);\n\n        if (!string.IsNullOrWhiteSpace(oldHash)) _hashes.Remove(oldHash);\n        _hashes.Add(newHash);\n        _images[index] = new DamageImage(_original.ReportId, old.Sequence, target, null, null);\n        RefreshImages(index);\n    }\n\n'''
text = read("src/PickfaceDamage1291/DamageReportEditDialog.cs")
if text.count(remove_marker) != 1:
    raise SystemExit("RemoveImage marker not found")
write("src/PickfaceDamage1291/DamageReportEditDialog.cs", text.replace(remove_marker, replace_method + remove_marker, 1))

replace_once(
    "src/PickfaceDamage1291/DamageReportEditDialog.cs",
    '''        if (File.Exists(item.LocalPath))\n            try { _hashes.Remove(ImageHashService.Sha256File(item.LocalPath)); } catch { }\n        for (var i = 0; i < _images.Count; i++) _images[i] = _images[i] with { Sequence = i + 1 };\n        RefreshImages();''',
    '''        string oldHash = string.Empty;\n        if (File.Exists(item.LocalPath))\n            try { oldHash = ImageHashService.Sha256File(item.LocalPath); } catch { }\n        if (string.IsNullOrWhiteSpace(oldHash)) oldHash = SyncCacheStore.GetImageHash(item.ReportId, item.Sequence);\n        if (!string.IsNullOrWhiteSpace(oldHash)) _hashes.Remove(oldHash);\n        for (var i = 0; i < _images.Count; i++) _images[i] = _images[i] with { Sequence = i + 1 };\n        RefreshImages(Math.Min(index, _images.Count - 1));''')

replace_once(
    "src/PickfaceDamage1291/DamageReportEditDialog.cs",
    '''    private void RefreshImages()\n    {\n        _imageList.Items.Clear();\n        foreach (var image in _images)\n            _imageList.Items.Add($"Ảnh {image.Sequence}: {Path.GetFileName(image.LocalPath)}{(string.IsNullOrWhiteSpace(image.DriveFileId) ? " (local)" : "")}");\n        if (_imageList.Items.Count > 0) _imageList.SelectedIndex = 0;\n        else { _preview.Image?.Dispose(); _preview.Image = null; }\n    }''',
    '''    private void RefreshImages(int preferredIndex = 0)\n    {\n        _imageList.Items.Clear();\n        foreach (var image in _images)\n            _imageList.Items.Add($"Ảnh {image.Sequence}: {Path.GetFileName(image.LocalPath)}{(string.IsNullOrWhiteSpace(image.DriveFileId) ? " (local)" : "")}");\n        if (_imageList.Items.Count > 0) _imageList.SelectedIndex = Math.Clamp(preferredIndex, 0, _imageList.Items.Count - 1);\n        else { _preview.Image?.Dispose(); _preview.Image = null; }\n    }''')

print("v1.4.1 patch applied")
