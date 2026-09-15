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


# Version
p = "src/PickfaceDamage1291/PickfaceDamage1291.csproj"
text = read(p)
for old, new in [
    ("<Version>1.3.7</Version>", "<Version>1.4.0</Version>"),
    ("<InformationalVersion>1.3.7</InformationalVersion>", "<InformationalVersion>1.4.0</InformationalVersion>"),
    ("<AssemblyVersion>1.3.7.0</AssemblyVersion>", "<AssemblyVersion>1.4.0.0</AssemblyVersion>"),
    ("<FileVersion>1.3.7.0</FileVersion>", "<FileVersion>1.4.0.0</FileVersion>"),
]:
    if old not in text:
        raise SystemExit(f"Missing version marker {old}")
    text = text.replace(old, new, 1)
write(p, text)

# Database: conflicts must not be retried automatically.
replace_once(
    "src/PickfaceDamage1291/Database.cs",
    'cmd.CommandText = $"SELECT {ReportColumns} FROM damage_reports WHERE sync_status<>'SYNCED' ORDER BY created_at";',
    'cmd.CommandText = $"SELECT {ReportColumns} FROM damage_reports WHERE sync_status IN (\'PENDING\',\'ERROR\',\'OFFLINE_PENDING\',\'SYNCING\') ORDER BY created_at";')

# Audit transport: keep Firebase Auth/active_operator, move business audit rows to Google Sheet.
replace_once(
    "src/PickfaceDamage1291/FirebaseClient.cs",
    '''    private static async Task UploadAuditEntryAsync(FirebaseSession session, FirebaseAuditEntry entry, CancellationToken ct)\n    {\n        using var response = await Http.PutAsync(DbUrl($"audit/{entry.EventId}", session.IdToken), JsonContent(entry), ct);\n        var body = await response.Content.ReadAsStringAsync(ct);\n        if (!response.IsSuccessStatusCode)\n            throw new InvalidOperationException(ToFriendlyDatabaseError(response.StatusCode, body));\n    }''',
    '''    private static async Task UploadAuditEntryAsync(FirebaseSession session, FirebaseAuditEntry entry, CancellationToken ct)\n    {\n        await GoogleGatewayV140.AppendAuditAsync(entry, ct);\n    }''')

# Main list/status/manual sync/product catalog publish.
replace_once(
    "src/PickfaceDamage1291/MainFormV3.cs",
    '''        "ERROR" => "Lỗi đồng bộ",\n        "OFFLINE_PENDING" => "Chờ online",\n        _ => "Chờ đồng bộ"''',
    '''        "ERROR" => "Lỗi đồng bộ",\n        "OFFLINE_PENDING" => "Chờ online",\n        "CONFLICT" => "Xung đột",\n        _ => "Chờ đồng bộ"''')

replace_once(
    "src/PickfaceDamage1291/MainFormV3.cs",
    '''    private async Task SyncPendingAsync()\n    {\n        if (!GoogleService.IsConnected())\n        {\n            MessageBox.Show(this, GoogleService.NeedsReconnect() ? "Phiên Google cũ cần kết nối lại. Vào Cài đặt → Kết nối Google." : "Chưa kết nối Google trên laptop này. Vào Cài đặt → Kết nối Google.", "Chưa kết nối");\n            return;\n        }\n        try\n        {\n            _reportStatus.Text = "Đang đồng bộ...";\n            var progress = new Progress<string>(s => _reportStatus.Text = s);\n            await GoogleService.SyncAllPendingAsync(progress);\n            MessageBox.Show(this, "Đã xử lý hàng đợi đồng bộ.", "Hoàn tất");\n        }\n        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Đồng bộ chưa hoàn tất", MessageBoxButtons.OK, MessageBoxIcon.Warning); }\n        finally { RefreshReports(); RefreshSettings(); }\n    }''',
    '''    private async Task SyncPendingAsync()\n    {\n        if (!GoogleService.IsConnected())\n        {\n            MessageBox.Show(this, "Chưa kết nối Google dùng chung. Dữ liệu local vẫn được giữ an toàn.", "Chưa kết nối");\n            return;\n        }\n        try\n        {\n            _reportStatus.Text = "Đang đồng bộ hai chiều...";\n            var progress = new Progress<string>(s => _reportStatus.Text = s);\n            var summary = await CloudSyncService.SyncNowAsync(progress);\n            MessageBox.Show(this,\n                $"Đồng bộ hoàn tất.\\n\\nNhận phiếu: {summary.ReportsPulled:N0}\\nGửi phiếu: {summary.ReportsPushed:N0}\\nXoá nhận từ máy khác: {summary.ReportsDeleted:N0}\\nSKU nhận: {summary.ProductsPulled:N0}\\nXung đột: {summary.ReportConflicts:N0}",\n                "Hoàn tất", MessageBoxButtons.OK, MessageBoxIcon.Information);\n        }\n        catch (Exception ex)\n        {\n            AppLog.Exception("MANUAL_SYNC_FAILED", ex);\n            MessageBox.Show(this, ex.Message, "Đồng bộ chưa hoàn tất", MessageBoxButtons.OK, MessageBoxIcon.Warning);\n        }\n        finally { RefreshReports(); RefreshProducts(); RefreshSettings(); }\n    }''')

replace_once(
    "src/PickfaceDamage1291/MainFormV3.cs",
    '''            Cursor = Cursors.WaitCursor;\n            await Task.Run(() => Database.ApplyImport(preview));\n            Cursor = Cursors.Default;\n            if (AppSession.Current is { } session)\n                try { await FirebaseClient.AppendAuditAsync(session, "SKU_IMPORT", new { file = Path.GetFileName(dialog.FileName), total = preview.TotalRows, unique = preview.UniqueSkus, added = preview.NewProducts.Count, conflicts = preview.Conflicts.Count }); } catch { }\n            MessageBox.Show(this, "Đã cập nhật danh mục SKU local. SKU cũ không có trong file mới KHÔNG bị xoá.", "Hoàn tất");\n            RefreshProducts();''',
    '''            Cursor = Cursors.WaitCursor;\n            await Task.Run(() => Database.ApplyImport(preview));\n            SyncCacheStore.MarkProductsDirty();\n            Cursor = Cursors.Default;\n            var catalogSynced = false;\n            if (AppSession.Current is { } session)\n            {\n                try { await FirebaseClient.AppendAuditAsync(session, "SKU_IMPORT", new { file = Path.GetFileName(dialog.FileName), total = preview.TotalRows, unique = preview.UniqueSkus, added = preview.NewProducts.Count, conflicts = preview.Conflicts.Count }); } catch { }\n                if (session.Profile.IsAdmin && GoogleService.IsConnected())\n                {\n                    try\n                    {\n                        await CloudSyncService.PushLocalProductCatalogAsync(new Progress<string>(s => _productCount.Text = s));\n                        catalogSynced = true;\n                    }\n                    catch (Exception syncEx)\n                    {\n                        AppLog.Exception("PRODUCT_CATALOG_PUSH_FAILED", syncEx);\n                    }\n                }\n            }\n            MessageBox.Show(this,\n                catalogSynced\n                    ? "Đã cập nhật danh mục SKU local và Google Sheet dùng chung. SKU cũ không có trong file mới KHÔNG bị xoá."\n                    : "Đã cập nhật danh mục SKU local. Thay đổi đang được giữ để ADMIN đồng bộ lên Google khi online. SKU cũ không có trong file mới KHÔNG bị xoá.",\n                "Hoàn tất");\n            RefreshProducts();''')

# More accurate direct-entry handling for central duplicate/conflict.
replace_once(
    "src/PickfaceDamage1291/MainFormV3.cs",
    '''                catch (Exception ex)\n                {\n                    MessageBox.Show(this, "Phiếu đã được lưu an toàn trên laptop nhưng chưa đồng bộ được Google.\\n\\n" + ex.Message, "Đã lưu local - chưa đồng bộ", MessageBoxButtons.OK, MessageBoxIcon.Warning);\n                }''',
    '''                catch (DuplicateReportException ex)\n                {\n                    SyncCacheStore.RemoveLocalDuplicate(report.ReportId, ex.ExistingReportId);\n                    MessageBox.Show(this, ex.Message + "\\n\\nBản ghi local vừa tạo đã được loại để không phát sinh dữ liệu trùng.", "Không tạo phiếu trùng", MessageBoxButtons.OK, MessageBoxIcon.Information);\n                }\n                catch (SyncConflictException ex)\n                {\n                    MessageBox.Show(this, ex.Message, "Xung đột đồng bộ", MessageBoxButtons.OK, MessageBoxIcon.Warning);\n                }\n                catch (Exception ex)\n                {\n                    MessageBox.Show(this, "Phiếu đã được lưu an toàn trên laptop nhưng chưa đồng bộ được Google.\\n\\n" + ex.Message, "Đã lưu local - chưa đồng bộ", MessageBoxButtons.OK, MessageBoxIcon.Warning);\n                }''')

# Startup sync: pull -> push -> pull, not only push local queue.
replace_once(
    "src/PickfaceDamage1291/UiRuntimeFixesV2.cs",
    '''            if (session.Profile.IsAdmin || session.Profile.HasPermission("sync_google") || session.Profile.HasPermission("damage_entry"))\n            {\n                try { await GoogleService.SyncAllPendingAsync(); } catch { }\n            }''',
    '''            if (session.Profile.IsAdmin || session.Profile.HasPermission("sync_google") || session.Profile.HasPermission("damage_entry") || session.Profile.HasPermission("view_reports"))\n            {\n                try\n                {\n                    var progress = new Progress<string>(text => SetGoogleHeader(form, "Google: " + text));\n                    await CloudSyncService.SyncNowAsync(progress);\n                    SetGoogleHeader(form, "Google: đã đồng bộ đa máy");\n                }\n                catch (Exception syncEx)\n                {\n                    AppLog.Exception("STARTUP_SYNC_FAILED", syncEx);\n                    SetGoogleHeader(form, "Google: đã kết nối • đồng bộ chưa hoàn tất");\n                }\n            }''')

# GoogleService: understand duplicate/version conflict and retain central metadata.
replace_once(
    "src/PickfaceDamage1291/GoogleServiceGateway.cs",
    '''                var root = await CallGatewayAsync("sync_report", payload);\n                progress?.Report(88);\n                ValidateGatewayResult(report, localImages, root);''',
    '''                var root = await CallGatewayAsync("sync_report", payload);\n                if (root.TryGetProperty("duplicate", out var duplicateNode) && duplicateNode.ValueKind == JsonValueKind.True)\n                {\n                    var existingId = root.TryGetProperty("duplicate_report_id", out var duplicateIdNode) ? duplicateIdNode.GetString() ?? string.Empty : string.Empty;\n                    throw new DuplicateReportException(existingId);\n                }\n                if (root.TryGetProperty("conflict", out var conflictNode) && conflictNode.ValueKind == JsonValueKind.True)\n                {\n                    var remoteVersion = root.TryGetProperty("remote_version", out var remoteVersionNode) && remoteVersionNode.TryGetInt32(out var parsedVersion) ? parsedVersion : 0;\n                    var message = $"Phiếu đã thay đổi trên máy khác. Local v{report.Version}, Google v{remoteVersion}. Hệ thống không ghi đè tự động.";\n                    SyncCacheStore.MarkConflict(report.ReportId, message);\n                    throw new SyncConflictException(message);\n                }\n                progress?.Report(88);\n                ValidateGatewayResult(report, localImages, root);''')

replace_once(
    "src/PickfaceDamage1291/GoogleServiceGateway.cs",
    '''                progress?.Report(96);\n                Database.SetReportStatus(report.ReportId, "SYNCED");\n                progress?.Report(100);\n            }\n            catch (Exception ex)\n            {\n                Database.SetReportStatus(report.ReportId, "ERROR", ex.Message);\n                throw;\n            }''',
    '''                progress?.Report(96);\n                var changeSeq = root.TryGetProperty("change_seq", out var changeSeqNode) && changeSeqNode.TryGetInt64(out var seqValue) ? seqValue : 0;\n                var fingerprint = root.TryGetProperty("fingerprint", out var fingerprintNode) ? fingerprintNode.GetString() ?? string.Empty : string.Empty;\n                if (changeSeq > 0) SyncCacheStore.MarkSyncedMetadata(report.ReportId, changeSeq, fingerprint);\n                else Database.SetReportStatus(report.ReportId, "SYNCED");\n                progress?.Report(100);\n            }\n            catch (DuplicateReportException)\n            {\n                throw;\n            }\n            catch (SyncConflictException)\n            {\n                throw;\n            }\n            catch (Exception ex)\n            {\n                Database.SetReportStatus(report.ReportId, "ERROR", ex.Message);\n                throw;\n            }''')

replace_once(
    "src/PickfaceDamage1291/GoogleServiceGateway.cs",
    '''            var root = await CallGatewayAsync("sync_report", payload);\n            var returnedId = root.TryGetProperty("report_id", out var idNode) ? idNode.GetString() ?? string.Empty : string.Empty;''',
    '''            var root = await CallGatewayAsync("sync_report", payload);\n            if (root.TryGetProperty("conflict", out var conflictNode) && conflictNode.ValueKind == JsonValueKind.True)\n            {\n                var remoteVersion = root.TryGetProperty("remote_version", out var versionNode) && versionNode.TryGetInt32(out var parsedVersion) ? parsedVersion : 0;\n                throw new SyncConflictException($"Không thể xoá vì phiếu trên Google đã lên phiên bản {remoteVersion}. Hãy đồng bộ lại trước.");\n            }\n            var returnedId = root.TryGetProperty("report_id", out var idNode) ? idNode.GetString() ?? string.Empty : string.Empty;''')

# Background queue messages for duplicate/conflict.
replace_once(
    "src/PickfaceDamage1291/BackgroundSyncCoordinator.cs",
    '''            catch (Exception ex)\n            {\n                Publish(new BackgroundSyncState(reportId, report.Sku, 100, $"Đồng bộ SKU {report.Sku} chưa thành công.", Math.Max(0, QueueCount - 1), true, true, true));\n                NotificationCenter.Show(null, $"Phiếu SKU {report.Sku} vẫn được giữ trên máy. {ex.Message}", "Chưa đồng bộ được", MessageBoxIcon.Warning);\n            }''',
    '''            catch (DuplicateReportException ex)\n            {\n                SyncCacheStore.RemoveLocalDuplicate(reportId, ex.ExistingReportId);\n                Publish(new BackgroundSyncState(reportId, report.Sku, 100, $"SKU {report.Sku} trùng dữ liệu trung tâm.", Math.Max(0, QueueCount - 1), true, true, false));\n                NotificationCenter.Show(null, ex.Message, "Không tạo phiếu trùng", MessageBoxIcon.Information);\n            }\n            catch (SyncConflictException ex)\n            {\n                Publish(new BackgroundSyncState(reportId, report.Sku, 100, $"SKU {report.Sku} có xung đột phiên bản.", Math.Max(0, QueueCount - 1), true, true, true));\n                NotificationCenter.Show(null, ex.Message, "Xung đột đồng bộ", MessageBoxIcon.Warning);\n            }\n            catch (Exception ex)\n            {\n                Publish(new BackgroundSyncState(reportId, report.Sku, 100, $"Đồng bộ SKU {report.Sku} chưa thành công.", Math.Max(0, QueueCount - 1), true, true, true));\n                NotificationCenter.Show(null, $"Phiếu SKU {report.Sku} vẫn được giữ trên máy. {ex.Message}", "Chưa đồng bộ được", MessageBoxIcon.Warning);\n            }''')

# Lazy download remote images for admin edit/view.
replace_once(
    "src/PickfaceDamage1291/DamageReportEditDialog.cs",
    '''        _imageList.SelectedIndexChanged += (_, _) => ShowPreview();''',
    '''        _imageList.SelectedIndexChanged += async (_, _) => await ShowPreviewAsync();''')

replace_once(
    "src/PickfaceDamage1291/DamageReportEditDialog.cs",
    '''        foreach (var image in _images)\n            if (File.Exists(image.LocalPath))\n                try { _hashes.Add(ImageHashService.Sha256File(image.LocalPath)); } catch { }''',
    '''        foreach (var image in _images)\n        {\n            if (File.Exists(image.LocalPath))\n            {\n                try { _hashes.Add(ImageHashService.Sha256File(image.LocalPath)); } catch { }\n            }\n            else\n            {\n                var remoteHash = SyncCacheStore.GetImageHash(image.ReportId, image.Sequence);\n                if (!string.IsNullOrWhiteSpace(remoteHash)) _hashes.Add(remoteHash);\n            }\n        }''')

replace_once(
    "src/PickfaceDamage1291/DamageReportEditDialog.cs",
    '''    private void ShowPreview()\n    {\n        _preview.Image?.Dispose();\n        _preview.Image = null;\n        var index = _imageList.SelectedIndex;\n        if (index < 0 || index >= _images.Count || !File.Exists(_images[index].LocalPath)) return;\n        try { using var image = Image.FromFile(_images[index].LocalPath); _preview.Image = new Bitmap(image); } catch { }\n    }''',
    '''    private async Task ShowPreviewAsync()\n    {\n        _preview.Image?.Dispose();\n        _preview.Image = null;\n        var index = _imageList.SelectedIndex;\n        if (index < 0 || index >= _images.Count) return;\n        var current = _images[index];\n        if (!File.Exists(current.LocalPath) && !string.IsNullOrWhiteSpace(current.DriveFileId) && GoogleService.IsConnected())\n        {\n            try\n            {\n                current = await RemoteImageCache.EnsureLocalAsync(current);\n                _images[index] = current;\n            }\n            catch (Exception ex)\n            {\n                AppLog.Exception("REMOTE_IMAGE_DOWNLOAD_FAILED", ex, new Dictionary<string, object?> { ["report_id"] = current.ReportId, ["sequence"] = current.Sequence });\n                return;\n            }\n        }\n        if (!File.Exists(current.LocalPath)) return;\n        try { using var image = Image.FromFile(current.LocalPath); _preview.Image = new Bitmap(image); } catch { }\n    }''')

# GoogleGatewayV140: expose existing get_image action.
replace_once(
    "src/PickfaceDamage1291/GoogleGatewayV140.cs",
    '''    public static async Task UploadLogAsync(string fileName, byte[] bytes, CancellationToken ct = default)\n    {''',
    '''    public static async Task<(byte[] Bytes, string MimeType, string Name)> GetImageAsync(string fileId, CancellationToken ct = default)\n    {\n        if (string.IsNullOrWhiteSpace(fileId)) throw new ArgumentException("Thiếu mã ảnh Drive.", nameof(fileId));\n        var root = await CallAsync("get_image", new { file_id = fileId }, ct);\n        var data = Str(root, "data_base64");\n        if (string.IsNullOrWhiteSpace(data)) throw new InvalidOperationException("Google không trả dữ liệu ảnh.");\n        return (Convert.FromBase64String(data), Str(root, "mime_type"), Str(root, "name"));\n    }\n\n    public static async Task UploadLogAsync(string fileName, byte[] bytes, CancellationToken ct = default)\n    {''')

# Clarify audit UI now reads Google Sheet, while diagnostic files are a separate Logs tab.
p = "src/PickfaceDamage1291/AuditControl.cs"
text = read(p)
text = text.replace("Xóa logs theo ngày", "Xóa lịch sử theo ngày")
text = text.replace("Xóa toàn bộ logs từ", "Xóa toàn bộ lịch sử từ")
text = text.replace(" logs trong phạm vi đã chọn.", " bản ghi lịch sử trong phạm vi đã chọn.")
text = text.replace(" logs\";", " bản ghi\";")
text = text.replace("Nếu vừa cập nhật Security Rules, OWNER cần Publish rules mới.", "Kiểm tra kết nối Google Sheet gateway nếu lỗi tiếp diễn.")
write(p, text)

# Apps Script V1.4 routes and sync storage.
p = "apps-script/GoogleGateway/Code.gs"
text = read(p)
if "V140_SYNC_BLOCK" in text:
    raise SystemExit("V140 Apps Script block already exists")
text = text.replace(
    "  IMAGE_FOLDER_ID: '1K_lUl_uE4dskR28cVK4iZJrzFUINfvXf',\n  SPREADSHEET_ID:",
    "  IMAGE_FOLDER_ID: '1K_lUl_uE4dskR28cVK4iZJrzFUINfvXf',\n  LOG_FOLDER_ID: '1Z19VgAAmCN1u7z_xSSztx9IVuq3kFSlK',\n  SPREADSHEET_ID:", 1)
text = text.replace(
    "      if (payload.write_headers === true) ensureHeaders_();",
    "      if (payload.write_headers === true) { ensureHeaders_(); ensureV140Sheets_(); }", 1)
route_marker = '''    if (action === 'sync_report') {\n      requireSyncPermission_(auth.profile);'''
routes = '''    if (action === 'pull_changes') {\n      requireReportReadPermissionV140_(auth.profile);\n      return json_({ ok: true, ...pullReportChangesV140_(payload) });\n    }\n\n    if (action === 'push_products') {\n      requireAdmin_(auth.profile);\n      return json_({ ok: true, ...pushProductsV140_(auth, payload) });\n    }\n\n    if (action === 'pull_products') {\n      return json_({ ok: true, ...pullProductsV140_(payload) });\n    }\n\n    if (action === 'append_audit') {\n      return json_({ ok: true, ...appendAuditV140_(auth, payload) });\n    }\n\n    if (action === 'list_audit') {\n      requireAdmin_(auth.profile);\n      return json_({ ok: true, ...listAuditV140_(payload) });\n    }\n\n    if (action === 'delete_audit_range') {\n      requireAdmin_(auth.profile);\n      return json_({ ok: true, ...deleteAuditRangeV140_(auth, payload) });\n    }\n\n    if (action === 'upload_log') {\n      return json_({ ok: true, ...uploadLogV140_(auth, payload) });\n    }\n\n''' + route_marker
if route_marker not in text:
    raise SystemExit("Apps Script route marker missing")
text = text.replace(route_marker, routes, 1)
text = text.replace("const result = syncReport_(payload.report || {}, payload.images || []);", "const result = syncReportV140_(payload.report || {}, payload.images || []);", 1)

block = r'''

// V140_SYNC_BLOCK — Google Sheet is the shared business source of truth; SQLite is local cache/outbox.
const REPORT_HEADERS_V140 = HEADERS.concat([
  'Change Seq', 'Đã xoá', 'Fingerprint',
  'Ảnh Hash 1', 'Ảnh Hash 2', 'Ảnh Hash 3', 'Ảnh Hash 4', 'Ảnh Hash 5',
  'Deleted At', 'Deleted By'
]);
const PRODUCT_HEADERS_V140 = ['SKU','Tên sản phẩm','Base Units','First Seen','Last Seen','Source File','Change Seq','Updated By'];
const AUDIT_HEADERS_V140 = ['Event ID','Server Time','Client Time','UID','Username','Device ID','Session ID','Action','Details JSON'];

function ensureV140Sheets_() {
  ensureReportHeadersV140_();
  getOrCreateSheetV140_('SKU_Catalog', PRODUCT_HEADERS_V140);
  getOrCreateSheetV140_('AuditHistory', AUDIT_HEADERS_V140);
  const logs = DriveApp.getFolderById(CFG.LOG_FOLDER_ID);
  assertHasParent_(logs, CFG.ROOT_FOLDER_ID, 'thư mục Logs');
}

function ensureReportHeadersV140_() {
  const sheet = getDataSheet_();
  sheet.getRange(1, 1, 1, REPORT_HEADERS_V140.length).setValues([REPORT_HEADERS_V140]);
  const last = sheet.getLastRow();
  if (last < 2) return;
  const rows = sheet.getRange(2, 1, last - 1, REPORT_HEADERS_V140.length).getValues();
  let counter = getCounterV140_('REPORT_CHANGE_SEQ_V140', sheet, 21);
  let changed = false;
  rows.forEach(row => {
    if (!String(row[0] || '').trim()) return;
    if (!Number(row[20] || 0)) { row[20] = ++counter; changed = true; }
    if (String(row[4] || '') === '__DELETED__' && row[21] !== true) { row[21] = true; changed = true; }
  });
  if (changed) {
    sheet.getRange(2, 1, rows.length, REPORT_HEADERS_V140.length).setValues(rows);
    PropertiesService.getScriptProperties().setProperty('REPORT_CHANGE_SEQ_V140', String(counter));
    SpreadsheetApp.flush();
  }
}

function getOrCreateSheetV140_(name, headers) {
  const ss = SpreadsheetApp.openById(CFG.SPREADSHEET_ID);
  let sheet = ss.getSheetByName(name);
  if (!sheet) sheet = ss.insertSheet(name);
  sheet.getRange(1, 1, 1, headers.length).setValues([headers]);
  return sheet;
}

function requireReportReadPermissionV140_(profile) {
  if (String(profile.role || '').toLowerCase() === 'admin') return;
  const p = profile.permissions || {};
  if (p.view_reports === true || p.damage_entry === true || p.sync_google === true) return;
  throw new Error('Tài khoản không có quyền đồng bộ danh sách hư hỏng.');
}

function syncReportV140_(report, images) {
  verifyFixedResources_();
  ensureReportHeadersV140_();
  const reportId = String(report.report_id || '').trim();
  const sku = String(report.sku || '').trim();
  if (!reportId || !sku) throw new Error('Phiếu thiếu report_id hoặc SKU.');
  const deleted = report.deleted === true || sku === '__DELETED__';
  const sheet = getDataSheet_();
  let row = findReportRow_(sheet, reportId);
  const isNew = !row;
  if (!row) row = Math.max(2, sheet.getLastRow() + 1);

  const existing = isNew ? new Array(REPORT_HEADERS_V140.length).fill('') : sheet.getRange(row, 1, 1, REPORT_HEADERS_V140.length).getValues()[0];
  const existingVersion = Number(existing[17] || 1);
  const incomingVersion = Math.max(1, Number(report.version || 1));
  const existingDeleted = existing[21] === true || String(existing[4] || '') === '__DELETED__';
  const oldLinks = existing.slice(9, 14).map(v => String(v || ''));
  const oldHashes = existing.slice(23, 28).map(v => String(v || ''));

  const requested = (images || []).slice().sort((a,b) => Number(a.sequence || 0) - Number(b.sequence || 0));
  const prepared = requested.map(image => {
    const sequence = Number(image.sequence || 0);
    if (sequence < 1 || sequence > 5) throw new Error('Thứ tự ảnh không hợp lệ: ' + sequence + '.');
    let fileId = String(image.drive_file_id || '');
    let url = String(image.drive_link || '');
    if (!fileId && oldLinks[sequence - 1]) {
      url = oldLinks[sequence - 1];
      fileId = extractDriveFileId_(url);
    }
    let hash = String(image.sha256 || oldHashes[sequence - 1] || '').toLowerCase();
    const data = String(image.data_base64 || '');
    if (!hash && data) hash = sha256BytesV140_(Utilities.base64Decode(data));
    if (!hash && fileId) {
      try {
        const file = DriveApp.getFileById(fileId);
        assertHasParent_(file, CFG.IMAGE_FOLDER_ID, 'Ảnh hư hỏng');
        hash = sha256BytesV140_(file.getBlob().getBytes());
      } catch (_) {}
    }
    return { image:image, sequence:sequence, fileId:fileId, url:url, hash:hash, data:data };
  });

  const imageHashes = ['', '', '', '', ''];
  prepared.forEach(x => imageHashes[x.sequence - 1] = x.hash);
  const fingerprint = deleted ? '' : buildFingerprintV140_(report, imageHashes.filter(Boolean));

  if (!deleted && fingerprint) {
    const duplicate = findFingerprintRowV140_(sheet, fingerprint, reportId);
    if (duplicate) {
      return {
        report_id: reportId,
        row: duplicate.row,
        duplicate: true,
        duplicate_report_id: duplicate.reportId,
        fingerprint: fingerprint,
        change_seq: Number(duplicate.values[20] || 0),
        images: []
      };
    }
  }

  if (!isNew) {
    if (existingVersion > incomingVersion) {
      return { report_id:reportId, row:row, conflict:true, remote_version:existingVersion, change_seq:Number(existing[20] || 0), fingerprint:String(existing[22] || ''), images:[] };
    }
    if (existingVersion === incomingVersion) {
      const existingFingerprint = String(existing[22] || '');
      if (existingFingerprint && ((!deleted && existingFingerprint !== fingerprint) || existingDeleted !== deleted)) {
        return { report_id:reportId, row:row, conflict:true, remote_version:existingVersion, change_seq:Number(existing[20] || 0), fingerprint:existingFingerprint, images:[] };
      }
      if (existingFingerprint || existingDeleted === deleted) {
        return {
          report_id:reportId,
          row:row,
          change_seq:Number(existing[20] || 0),
          fingerprint:existingFingerprint || fingerprint,
          images:existingImagesResultV140_(existing)
        };
      }
    }
  }

  const links = ['', '', '', '', ''];
  const imageResults = [];
  const folder = DriveApp.getFolderById(CFG.IMAGE_FOLDER_ID);
  prepared.forEach(x => {
    let fileId = x.fileId;
    let url = x.url;
    let hash = x.hash;
    if (fileId) {
      const file = DriveApp.getFileById(fileId);
      assertHasParent_(file, CFG.IMAGE_FOLDER_ID, 'Ảnh hư hỏng');
      if (!url) url = file.getUrl();
      if (!hash) hash = sha256BytesV140_(file.getBlob().getBytes());
    } else {
      if (!x.data) throw new Error('Ảnh ' + x.sequence + ' chưa có dữ liệu để tải lên.');
      const bytes = Utilities.base64Decode(x.data);
      const mime = String(x.image.mime_type || 'application/octet-stream');
      const name = buildImageName_(report, x.sequence, mime, String(x.image.original_name || ''));
      const file = folder.createFile(Utilities.newBlob(bytes, mime, name));
      fileId = file.getId();
      url = file.getUrl();
      if (!hash) hash = sha256BytesV140_(bytes);
    }
    links[x.sequence - 1] = url;
    imageHashes[x.sequence - 1] = hash;
    imageResults.push({ sequence:x.sequence, file_id:fileId, url:url, sha256:hash });
  });

  const finalFingerprint = deleted ? '' : buildFingerprintV140_(report, imageHashes.filter(Boolean));
  const changeSeq = nextCounterV140_('REPORT_CHANGE_SEQ_V140', sheet, 21);
  const now = new Date();
  const values = [
    reportId,
    deleted ? '' : displayDate_(report.occurred_date),
    deleted ? '' : pad2_(report.hour) + ':' + pad2_(report.minute),
    deleted ? '' : String(report.shift || ''),
    deleted ? '__DELETED__' : sku,
    deleted ? 'ĐÃ XÓA' : String(report.product_name || ''),
    deleted ? '' : String(report.location || ''),
    deleted ? 0 : Number(report.quantity || 0),
    deleted ? '' : String(report.base_unit || ''),
    links[0],links[1],links[2],links[3],links[4],
    String(report.created_at || existing[14] || ''),
    Utilities.formatDate(now, Session.getScriptTimeZone(), 'HH:mm:ss dd/MM/yyyy'),
    String(report.created_by || existing[16] || ''),
    incomingVersion,
    String(report.updated_at || ''),
    String(report.updated_by || ''),
    changeSeq,
    deleted,
    finalFingerprint,
    imageHashes[0],imageHashes[1],imageHashes[2],imageHashes[3],imageHashes[4],
    deleted ? now.toISOString() : '',
    deleted ? String(report.updated_by || '') : ''
  ];
  sheet.getRange(row, 1, 1, REPORT_HEADERS_V140.length).setValues([values]);
  SpreadsheetApp.flush();
  return { report_id:reportId, row:row, change_seq:changeSeq, fingerprint:finalFingerprint, images:imageResults };
}

function pullReportChangesV140_(payload) {
  ensureReportHeadersV140_();
  const sheet = getDataSheet_();
  const after = Math.max(0, Number(payload.after_seq || 0));
  const limit = Math.max(1, Math.min(1000, Number(payload.limit || 500)));
  const last = sheet.getLastRow();
  if (last < 2) return { changes:[], latest_seq:getCounterV140_('REPORT_CHANGE_SEQ_V140', sheet, 21), has_more:false };
  const rows = sheet.getRange(2, 1, last - 1, REPORT_HEADERS_V140.length).getValues();
  const matches = [];
  rows.forEach((values, idx) => {
    const seq = Number(values[20] || 0);
    if (!String(values[0] || '').trim() || seq <= after) return;
    matches.push({ values:values, row:idx + 2, seq:seq });
  });
  matches.sort((a,b) => a.seq - b.seq);
  const page = matches.slice(0, limit).map(x => rowToChangeV140_(x.values));
  return {
    changes: page,
    latest_seq: getCounterV140_('REPORT_CHANGE_SEQ_V140', sheet, 21),
    has_more: matches.length > limit
  };
}

function rowToChangeV140_(v) {
  const time = String(v[2] || '').split(':');
  const images = [];
  for (let i = 0; i < 5; i++) {
    const url = String(v[9 + i] || '');
    const hash = String(v[23 + i] || '');
    if (!url && !hash) continue;
    images.push({ sequence:i + 1, url:url, file_id:extractDriveFileId_(url), sha256:hash });
  }
  return {
    change_seq:Number(v[20] || 0),
    deleted:v[21] === true || String(v[4] || '') === '__DELETED__',
    fingerprint:String(v[22] || ''),
    deleted_at:asIsoV140_(v[28]),
    deleted_by:String(v[29] || ''),
    report:{
      report_id:String(v[0] || ''),
      occurred_date:isoDateV140_(v[1]),
      hour:Number(time[0] || 0), minute:Number(time[1] || 0),
      shift:String(v[3] || ''), sku:String(v[4] || ''), product_name:String(v[5] || ''),
      location:String(v[6] || ''), quantity:Number(v[7] || 0), base_unit:String(v[8] || ''),
      created_at:asIsoV140_(v[14]), created_by:String(v[16] || ''), version:Math.max(1,Number(v[17] || 1)),
      updated_at:asIsoV140_(v[18]), updated_by:String(v[19] || '')
    },
    images:images
  };
}

function pushProductsV140_(auth, payload) {
  const sheet = getOrCreateSheetV140_('SKU_Catalog', PRODUCT_HEADERS_V140);
  const products = Array.isArray(payload.products) ? payload.products.slice(0, 1000) : [];
  const last = sheet.getLastRow();
  const rows = last >= 2 ? sheet.getRange(2,1,last-1,PRODUCT_HEADERS_V140.length).getValues() : [];
  const index = {};
  rows.forEach((row,i) => { const sku=String(row[0]||'').trim(); if (sku) index[sku]=i; });
  let counter = getCounterV140_('PRODUCT_CHANGE_SEQ_V140', sheet, 7);
  let changed = 0;
  products.forEach(p => {
    const sku=String(p.sku||'').trim(); if (!sku) return;
    const candidate=[sku,String(p.product_name||''),String(p.base_unit||''),String(p.first_seen_at||''),String(p.last_seen_at||''),String(p.source_file||'')];
    const i=index[sku];
    if (i === undefined) {
      const row=candidate.concat([++counter,String(auth.profile.username||'')]);
      index[sku]=rows.length; rows.push(row); changed++; return;
    }
    const current=rows[i];
    let differs=false;
    for(let c=0;c<6;c++) if(String(current[c]||'')!==String(candidate[c]||'')){ differs=true; break; }
    if (!differs) return;
    rows[i]=candidate.concat([++counter,String(auth.profile.username||'')]); changed++;
  });
  if (rows.length) sheet.getRange(2,1,rows.length,PRODUCT_HEADERS_V140.length).setValues(rows);
  PropertiesService.getScriptProperties().setProperty('PRODUCT_CHANGE_SEQ_V140',String(counter));
  SpreadsheetApp.flush();
  return { changed:changed, latest_seq:counter, server_count:rows.length };
}

function pullProductsV140_(payload) {
  const sheet=getOrCreateSheetV140_('SKU_Catalog',PRODUCT_HEADERS_V140);
  const after=Math.max(0,Number(payload.after_seq||0));
  const limit=Math.max(1,Math.min(1000,Number(payload.limit||500)));
  const last=sheet.getLastRow();
  if(last<2) return {changes:[],latest_seq:getCounterV140_('PRODUCT_CHANGE_SEQ_V140',sheet,7),has_more:false,server_count:0};
  const rows=sheet.getRange(2,1,last-1,PRODUCT_HEADERS_V140.length).getValues();
  const matches=rows.filter(r=>String(r[0]||'').trim() && Number(r[6]||0)>after).sort((a,b)=>Number(a[6]||0)-Number(b[6]||0));
  const changes=matches.slice(0,limit).map(r=>({
    sku:String(r[0]||''), product_name:String(r[1]||''), base_unit:String(r[2]||''),
    first_seen_at:asIsoV140_(r[3]), last_seen_at:asIsoV140_(r[4]), source_file:String(r[5]||''), change_seq:Number(r[6]||0)
  }));
  return {changes:changes,latest_seq:getCounterV140_('PRODUCT_CHANGE_SEQ_V140',sheet,7),has_more:matches.length>limit,server_count:rows.filter(r=>String(r[0]||'').trim()).length};
}

function appendAuditV140_(auth, payload) {
  const sheet=getOrCreateSheetV140_('AuditHistory',AUDIT_HEADERS_V140);
  const eventId=String(payload.event_id||'').trim() || Utilities.getUuid().replace(/-/g,'');
  if(sheet.getLastRow()>=2){
    const found=sheet.getRange(2,1,sheet.getLastRow()-1,1).createTextFinder(eventId).matchEntireCell(true).findNext();
    if(found) return {stored:false,duplicate:true,event_id:eventId,server_time:Number(sheet.getRange(found.getRow(),2).getValue()||0)};
  }
  const serverTime=Date.now();
  let details='';
  try{ details=JSON.stringify(payload.details===undefined?null:payload.details); }catch(_){ details='null'; }
  if(details.length>20000) details=details.substring(0,20000)+'...';
  sheet.appendRow([
    eventId,serverTime,String(payload.client_time||''),String(auth.uid||''),String(auth.profile.username||''),
    safeTextV140_(payload.device_id,200),safeTextV140_(payload.session_id,200),safeTextV140_(payload.action,200),details
  ]);
  return {stored:true,duplicate:false,event_id:eventId,server_time:serverTime};
}

function listAuditV140_(payload) {
  const sheet=getOrCreateSheetV140_('AuditHistory',AUDIT_HEADERS_V140);
  const beforeRaw=payload.before_server_time_exclusive;
  const before=beforeRaw===null||beforeRaw===undefined||beforeRaw===''?Number.MAX_SAFE_INTEGER:Number(beforeRaw);
  const limit=Math.max(1,Math.min(100,Number(payload.limit||100)));
  const last=sheet.getLastRow();
  if(last<2) return {entries:[],has_more:false,next_before_server_time:null};
  const rows=sheet.getRange(2,1,last-1,AUDIT_HEADERS_V140.length).getValues();
  const matches=rows.filter(r=>Number(r[1]||0)<before).sort((a,b)=>Number(b[1]||0)-Number(a[1]||0));
  const page=matches.slice(0,limit).map(r=>{
    let details=null; try{details=JSON.parse(String(r[8]||'null'));}catch(_){details=String(r[8]||'');}
    return {event_id:String(r[0]||''),server_time:Number(r[1]||0),client_time:String(r[2]||''),uid:String(r[3]||''),username:String(r[4]||''),device_id:String(r[5]||''),session_id:String(r[6]||''),action:String(r[7]||''),details:details};
  });
  return {entries:page,has_more:matches.length>limit,next_before_server_time:(matches.length>limit&&page.length)?Number(page[page.length-1].server_time||0):null};
}

function deleteAuditRangeV140_(auth,payload) {
  const from=Number(payload.from_server_time||0), to=Number(payload.to_server_time||0);
  if(!from||!to||from>to) throw new Error('Khoảng ngày xóa không hợp lệ.');
  const sheet=getOrCreateSheetV140_('AuditHistory',AUDIT_HEADERS_V140);
  const last=sheet.getLastRow();
  if(last<2) return {deleted_count:0};
  const rows=sheet.getRange(2,1,last-1,AUDIT_HEADERS_V140.length).getValues();
  const kept=[]; let deleted=0;
  rows.forEach(r=>{
    const t=Number(r[1]||0), action=String(r[7]||'');
    if(t>=from&&t<=to&&action!=='AUDIT_LOGS_DELETED') deleted++; else kept.push(r);
  });
  sheet.getRange(2,1,last-1,AUDIT_HEADERS_V140.length).clearContent();
  if(kept.length) sheet.getRange(2,1,kept.length,AUDIT_HEADERS_V140.length).setValues(kept);
  appendAuditV140_(auth,{event_id:Utilities.getUuid().replace(/-/g,''),device_id:'server',session_id:'',action:'AUDIT_LOGS_DELETED',client_time:new Date().toISOString(),details:{from_server_time:from,to_server_time:to,deleted_count:deleted}});
  SpreadsheetApp.flush();
  return {deleted_count:deleted};
}

function uploadLogV140_(auth,payload) {
  const folder=DriveApp.getFolderById(CFG.LOG_FOLDER_ID);
  assertHasParent_(folder,CFG.ROOT_FOLDER_ID,'thư mục Logs');
  const data=String(payload.data_base64||'');
  if(!data) throw new Error('File log không có dữ liệu.');
  let bytes=Utilities.base64Decode(data);
  if(bytes.length>8*1024*1024) throw new Error('File log vượt giới hạn 8 MB.');
  let text=Utilities.newBlob(bytes).getDataAsString('UTF-8');
  text=sanitizeLogV140_(text);
  bytes=Utilities.newBlob(text,'text/plain').getBytes();
  const stamp=Utilities.formatDate(new Date(),Session.getScriptTimeZone(),'yyyyMMdd_HHmmss');
  const user=sanitizeFilePart_(String(auth.profile.username||'user')) || 'user';
  const device=sanitizeFilePart_(String(payload.device_id||'device')).substring(0,24) || 'device';
  let original=sanitizeFilePart_(String(payload.file_name||'app.log')) || 'app.log';
  if(!original.toLowerCase().endsWith('.log')) original += '.log';
  const name=(stamp+'_'+user+'_'+device+'_'+original).substring(0,220);
  const file=folder.createFile(Utilities.newBlob(bytes,'text/plain',name));
  return {file_id:file.getId(),url:file.getUrl(),name:file.getName(),size:bytes.length};
}

function sanitizeLogV140_(text) {
  let v=String(text||'');
  v=v.replace(/(authorization\s*[:=]\s*bearer\s+)[^\s,;]+/ig,'$1<redacted>');
  v=v.replace(/([?&](?:auth|key|token|id_token|refresh_token|access_token)=)[^&\s]+/ig,'$1<redacted>');
  v=v.replace(/("?(?:password|passwd|pwd|secret|credential|id_token|refresh_token|access_token|authorization|cookie)"?\s*[:=]\s*"?)[^",;\s}]+/ig,'$1<redacted>');
  v=v.replace(/eyJ[a-zA-Z0-9_-]{10,}\.[a-zA-Z0-9_-]{10,}\.[a-zA-Z0-9_-]{10,}/g,'<redacted-jwt>');
  return v;
}

function findFingerprintRowV140_(sheet,fingerprint,excludeId) {
  const last=sheet.getLastRow(); if(last<2) return null;
  const rows=sheet.getRange(2,1,last-1,REPORT_HEADERS_V140.length).getValues();
  for(let i=0;i<rows.length;i++){
    const r=rows[i];
    if(String(r[0]||'')===String(excludeId||'')) continue;
    if(r[21]===true||String(r[4]||'')==='__DELETED__') continue;
    if(String(r[22]||'')===fingerprint) return {row:i+2,reportId:String(r[0]||''),values:r};
  }
  return null;
}

function buildFingerprintV140_(report, hashes) {
  const core=[
    String(report.occurred_date||''),pad2_(report.hour)+':'+pad2_(report.minute),String(report.shift||''),String(report.sku||''),
    String(report.product_name||''),String(report.location||''),String(Number(report.quantity||0)),String(report.base_unit||''),
    (hashes||[]).map(x=>String(x||'').toLowerCase()).sort()
  ];
  return sha256TextV140_(JSON.stringify(core));
}

function existingImagesResultV140_(row) {
  const out=[];
  for(let i=0;i<5;i++){
    const url=String(row[9+i]||''), hash=String(row[23+i]||'');
    if(!url&&!hash) continue;
    out.push({sequence:i+1,file_id:extractDriveFileId_(url),url:url,sha256:hash});
  }
  return out;
}

function sha256TextV140_(text) {
  const bytes=Utilities.computeDigest(Utilities.DigestAlgorithm.SHA_256,String(text||''),Utilities.Charset.UTF_8);
  return bytes.map(b=>((b+256)%256).toString(16).padStart(2,'0')).join('');
}
function sha256BytesV140_(bytes) {
  const digest=Utilities.computeDigest(Utilities.DigestAlgorithm.SHA_256,bytes);
  return digest.map(b=>((b+256)%256).toString(16).padStart(2,'0')).join('');
}
function getCounterV140_(key,sheet,col) {
  const props=PropertiesService.getScriptProperties();
  let current=Number(props.getProperty(key)||0);
  if(!current && sheet.getLastRow()>=2){
    const vals=sheet.getRange(2,col,sheet.getLastRow()-1,1).getValues();
    vals.forEach(r=>{current=Math.max(current,Number(r[0]||0));});
    props.setProperty(key,String(current));
  }
  return current;
}
function nextCounterV140_(key,sheet,col) {
  const next=getCounterV140_(key,sheet,col)+1;
  PropertiesService.getScriptProperties().setProperty(key,String(next));
  return next;
}
function isoDateV140_(value) {
  if(value instanceof Date) return Utilities.formatDate(value,Session.getScriptTimeZone(),'yyyy-MM-dd');
  const s=String(value||'');
  if(/^\d{4}-\d{2}-\d{2}$/.test(s)) return s;
  const m=s.match(/^(\d{2})\/(\d{2})\/(\d{4})$/); return m?m[3]+'-'+m[2]+'-'+m[1]:s;
}
function asIsoV140_(value) {
  if(value instanceof Date) return value.toISOString();
  return String(value||'');
}
function safeTextV140_(value,max) { const s=String(value||''); return s.length>(max||1000)?s.substring(0,max||1000):s; }
'''
text += block
write(p, text)

print("v1.4.0 patch applied")
