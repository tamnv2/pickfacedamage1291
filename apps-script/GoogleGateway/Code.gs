const CFG = Object.freeze({
  PROJECT_ID: 'pickface-damage-1291',
  FIREBASE_API_KEY: 'AIzaSyD6aKmuZSbcl5HAqnb7fNv_RaD6v4gQABU',
  FIREBASE_DB_URL: 'https://pickface-damage-1291-default-rtdb.asia-southeast1.firebasedatabase.app',
  ROOT_FOLDER_ID: '16jDCy5_Z1X5cKJyNPR1rn_ZqbExQSAeC',
  IMAGE_FOLDER_ID: '1K_lUl_uE4dskR28cVK4iZJrzFUINfvXf',
  LOG_FOLDER_ID: '1Z19VgAAmCN1u7z_xSSztx9IVuq3kFSlK',
  SPREADSHEET_ID: '1Ubm9EhALocUovzVr3UIspdCMtHIPm2NPUw6UjlZcqw4'
});

const LOGIN_ALIAS_PREFIX = 'LOGIN_ALIAS_';
const GATEWAY_VERSION = '1.4.5';
const GATEWAY_CAPABILITIES = Object.freeze(['pull_changes','push_products','pull_products','append_audit','list_audit','delete_audit_range','upload_log','sync_report','get_image','rtdb_proxy']);

const HEADERS = [
  'ID', 'Ngày phát hiện', 'Giờ phát hiện', 'Ca', 'SKU', 'Tên sản phẩm', 'Vị trí phát hiện',
  'Số lượng hư hỏng', 'Base Units', 'Ảnh 1', 'Ảnh 2', 'Ảnh 3', 'Ảnh 4', 'Ảnh 5',
  'Thời gian nhập', 'Thời gian đồng bộ', 'Người tạo', 'Phiên bản', 'Cập nhật lúc', 'Cập nhật bởi'
];

function doPostLegacy_(e) {
  try {
    if (!e || !e.postData || !e.postData.contents) throw new Error('Thiếu request body.');
    const request = JSON.parse(e.postData.contents);
    const action = String(request.action || '');
    const payload = request.payload || {};

    if (action === 'gateway_info') {
      return json_({ ok: true, version: GATEWAY_VERSION, capabilities: GATEWAY_CAPABILITIES });
    }

    if (action === 'login_by_username') {
      return json_(loginByUsername_(String(request.username || ''), String(request.password || '')));
    }

    if (action === 'password_reset_by_username') {
      return json_(passwordResetByUsername_(String(request.username || '')));
    }

    const idToken = String(request.id_token || '');
    const auth = authenticateFirebase_(idToken);

    if (action === 'rtdb_proxy') {
      return json_({ ok: true, ...rtdbProxyV145_(auth, idToken, payload) });
    }

    if (action === 'change_own_email') {
      return json_(changeOwnEmail_(auth, String(payload.current_password || ''), String(payload.new_email || '')));
    }

    if (action === 'sync_login_aliases') {
      requireAdmin_(auth.profile);
      const count = syncLoginAliases_(idToken);
      return json_({ ok: true, count: count });
    }

    if (action === 'verify') {
      verifyFixedResources_();
      if (payload.write_headers === true) { ensureHeaders_(); ensureV140Sheets_(); }
      return json_({ ok: true, uid: auth.uid, username: auth.profile.username || '', version: GATEWAY_VERSION, capabilities: GATEWAY_CAPABILITIES });
    }

    if (action === 'get_image') {
      requireImageReadPermission_(auth.profile);
      return json_({ ok: true, ...getImage_(String(payload.file_id || '')) });
    }

    if (action === 'pull_changes') {
      requireReportReadPermissionV140_(auth.profile);
      return json_({ ok: true, ...pullReportChangesV140_(payload) });
    }

    if (action === 'push_products') {
      requireAdmin_(auth.profile);
      return json_({ ok: true, ...pushProductsV140_(auth, payload) });
    }

    if (action === 'pull_products') {
      return json_({ ok: true, ...pullProductsV140_(payload) });
    }

    if (action === 'append_audit') {
      return json_({ ok: true, ...appendAuditV140_(auth, payload) });
    }

    if (action === 'list_audit') {
      requireAdmin_(auth.profile);
      return json_({ ok: true, ...listAuditV140_(payload) });
    }

    if (action === 'delete_audit_range') {
      requireAdmin_(auth.profile);
      return json_({ ok: true, ...deleteAuditRangeV140_(auth, payload) });
    }

    if (action === 'upload_log') {
      return json_({ ok: true, ...uploadLogV140_(auth, payload) });
    }

    if (action === 'sync_report') {
      requireSyncPermission_(auth.profile);
      const lock = LockService.getScriptLock();
      if (!lock.tryLock(10000)) throw new Error('Hệ thống đang đồng bộ một phiếu khác. Hãy thử lại sau vài giây.');
      try {
        const result = syncReportV140_(payload.report || {}, payload.images || []);
        return json_({ ok: true, ...result });
      } finally {
        lock.releaseLock();
      }
    }

    throw new Error('Action không được hỗ trợ.');
  } catch (err) {
    return json_({ ok: false, error: cleanError_(err) });
  }
}

function loginByUsername_(username, password) {
  const normalized = normalizeUsername_(username);
  if (!normalized || !password) throw new Error('Tài khoản hoặc mật khẩu không đúng.');

  const email = PropertiesService.getScriptProperties().getProperty(aliasKey_(normalized));
  if (!email) throw new Error('Tài khoản hoặc mật khẩu không đúng.');

  const response = UrlFetchApp.fetch(
    'https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=' + encodeURIComponent(CFG.FIREBASE_API_KEY),
    {
      method: 'post',
      contentType: 'application/json',
      payload: JSON.stringify({ email: email, password: password, returnSecureToken: true }),
      muteHttpExceptions: true
    }
  );
  if (response.getResponseCode() !== 200) throw new Error('Tài khoản hoặc mật khẩu không đúng.');

  const result = JSON.parse(response.getContentText() || '{}');
  const idToken = String(result.idToken || '');
  const auth = authenticateFirebase_(idToken);
  if (normalizeUsername_(auth.profile.username || '') !== normalized)
    throw new Error('Tài khoản hoặc mật khẩu không đúng.');

  return {
    ok: true,
    local_id: String(result.localId || auth.uid || ''),
    id_token: idToken,
    refresh_token: String(result.refreshToken || ''),
    expires_in: String(result.expiresIn || '3600')
  };
}

function passwordResetByUsername_(username) {
  const normalized = normalizeUsername_(username);
  if (!normalized) return { ok: true };
  const email = PropertiesService.getScriptProperties().getProperty(aliasKey_(normalized));
  if (!email) return { ok: true };

  const response = UrlFetchApp.fetch(
    'https://identitytoolkit.googleapis.com/v1/accounts:sendOobCode?key=' + encodeURIComponent(CFG.FIREBASE_API_KEY),
    {
      method: 'post',
      contentType: 'application/json',
      payload: JSON.stringify({ requestType: 'PASSWORD_RESET', email: email }),
      muteHttpExceptions: true
    }
  );
  if (response.getResponseCode() !== 200)
    throw new Error('Không gửi được yêu cầu đặt lại mật khẩu. Hãy thử lại sau.');
  return { ok: true };
}

function changeOwnEmail_(auth, currentPassword, newEmail) {
  const oldEmail = String(auth.firebase_email || auth.profile.email || '').trim();
  const username = normalizeUsername_(auth.profile.username || '');
  newEmail = String(newEmail || '').trim().toLowerCase();
  if (!oldEmail || !username) throw new Error('Không xác định được thông tin tài khoản hiện tại.');
  if (!currentPassword) throw new Error('Chưa nhập mật khẩu hiện tại.');
  if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(newEmail)) throw new Error('Email mới không hợp lệ.');
  if (oldEmail.toLowerCase() === newEmail.toLowerCase()) throw new Error('Email mới đang trùng email hiện tại.');

  const signIn = UrlFetchApp.fetch(
    'https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=' + encodeURIComponent(CFG.FIREBASE_API_KEY),
    {
      method: 'post',
      contentType: 'application/json',
      payload: JSON.stringify({ email: oldEmail, password: currentPassword, returnSecureToken: true }),
      muteHttpExceptions: true
    }
  );
  if (signIn.getResponseCode() !== 200) throw new Error('Mật khẩu hiện tại không đúng.');
  const signed = JSON.parse(signIn.getContentText() || '{}');
  if (String(signed.localId || '') !== String(auth.uid || '')) throw new Error('Không xác minh được tài khoản hiện tại.');

  const update = UrlFetchApp.fetch(
    'https://identitytoolkit.googleapis.com/v1/accounts:update?key=' + encodeURIComponent(CFG.FIREBASE_API_KEY),
    {
      method: 'post',
      contentType: 'application/json',
      payload: JSON.stringify({ idToken: String(signed.idToken || ''), email: newEmail, returnSecureToken: true }),
      muteHttpExceptions: true
    }
  );
  if (update.getResponseCode() !== 200) {
    const body = JSON.parse(update.getContentText() || '{}');
    const code = body && body.error ? String(body.error.message || '') : '';
    if (code === 'EMAIL_EXISTS') throw new Error('Email mới đã được sử dụng bởi tài khoản khác.');
    throw new Error('Firebase không chấp nhận email mới.');
  }

  const updated = JSON.parse(update.getContentText() || '{}');
  const newIdToken = String(updated.idToken || '');
  if (!newIdToken) throw new Error('Firebase không trả phiên mới sau khi đổi email.');

  const oldProfile = JSON.parse(JSON.stringify(auth.profile || {}));
  const newProfile = JSON.parse(JSON.stringify(oldProfile));
  newProfile.email = newEmail;

  try {
    const profileWrite = UrlFetchApp.fetch(
      CFG.FIREBASE_DB_URL + '/users/' + encodeURIComponent(auth.uid) + '.json?auth=' + encodeURIComponent(newIdToken),
      {
        method: 'put',
        contentType: 'application/json',
        payload: JSON.stringify(newProfile),
        muteHttpExceptions: true
      }
    );
    if (profileWrite.getResponseCode() !== 200) throw new Error('Không cập nhật được hồ sơ tài khoản.');

    PropertiesService.getScriptProperties().setProperty(aliasKey_(username), newEmail);
    return {
      ok: true,
      local_id: String(updated.localId || auth.uid || ''),
      email: newEmail,
      id_token: newIdToken,
      refresh_token: String(updated.refreshToken || ''),
      expires_in: String(updated.expiresIn || '3600')
    };
  } catch (err) {
    try {
      const rollback = UrlFetchApp.fetch(
        'https://identitytoolkit.googleapis.com/v1/accounts:update?key=' + encodeURIComponent(CFG.FIREBASE_API_KEY),
        {
          method: 'post',
          contentType: 'application/json',
          payload: JSON.stringify({ idToken: newIdToken, email: oldEmail, returnSecureToken: true }),
          muteHttpExceptions: true
        }
      );
      const rollbackBody = JSON.parse(rollback.getContentText() || '{}');
      const rollbackToken = String(rollbackBody.idToken || '');
      if (rollback.getResponseCode() === 200 && rollbackToken) {
        UrlFetchApp.fetch(
          CFG.FIREBASE_DB_URL + '/users/' + encodeURIComponent(auth.uid) + '.json?auth=' + encodeURIComponent(rollbackToken),
          {
            method: 'put',
            contentType: 'application/json',
            payload: JSON.stringify(oldProfile),
            muteHttpExceptions: true
          }
        );
      }
      PropertiesService.getScriptProperties().setProperty(aliasKey_(username), oldEmail);
    } catch (_) {}
    throw new Error('Không thể hoàn tất đổi email. Hệ thống đã cố gắng khôi phục email cũ. ' + cleanError_(err));
  }
}

function syncLoginAliases_(idToken) {
  const response = UrlFetchApp.fetch(
    CFG.FIREBASE_DB_URL + '/users.json?auth=' + encodeURIComponent(idToken),
    { method: 'get', muteHttpExceptions: true }
  );
  if (response.getResponseCode() !== 200) throw new Error('Không đọc được danh sách tài khoản để đồng bộ đăng nhập.');
  const users = JSON.parse(response.getContentText() || '{}') || {};
  const updates = {};
  let count = 0;
  Object.keys(users).forEach(uid => {
    const profile = users[uid] || {};
    const username = normalizeUsername_(profile.username || '');
    const email = String(profile.email || '').trim();
    if (!username || !email) return;
    updates[aliasKey_(username)] = email;
    count++;
  });
  if (count > 0) PropertiesService.getScriptProperties().setProperties(updates, false);
  return count;
}

function normalizeUsername_(value) {
  return String(value || '').trim().toLowerCase();
}

function aliasKey_(normalizedUsername) {
  const bytes = Utilities.computeDigest(
    Utilities.DigestAlgorithm.SHA_256,
    normalizedUsername,
    Utilities.Charset.UTF_8
  );
  const hex = bytes.map(b => ((b + 256) % 256).toString(16).padStart(2, '0')).join('');
  return LOGIN_ALIAS_PREFIX + hex;
}

function authenticateFirebase_(idToken) {
  if (!idToken) throw new Error('Phiên Firebase không hợp lệ.');
  const lookup = UrlFetchApp.fetch(
    'https://identitytoolkit.googleapis.com/v1/accounts:lookup?key=' + encodeURIComponent(CFG.FIREBASE_API_KEY),
    {
      method: 'post',
      contentType: 'application/json',
      payload: JSON.stringify({ idToken: idToken }),
      muteHttpExceptions: true
    }
  );
  if (lookup.getResponseCode() !== 200) throw new Error('Phiên đăng nhập Firebase đã hết hạn hoặc không hợp lệ.');
  const lookupJson = JSON.parse(lookup.getContentText() || '{}');
  const user = lookupJson.users && lookupJson.users[0];
  if (!user || !user.localId) throw new Error('Không xác định được UID Firebase.');

  const profileResponse = UrlFetchApp.fetch(
    CFG.FIREBASE_DB_URL + '/users/' + encodeURIComponent(user.localId) + '.json?auth=' + encodeURIComponent(idToken),
    { method: 'get', muteHttpExceptions: true }
  );
  if (profileResponse.getResponseCode() !== 200) throw new Error('Không đọc được hồ sơ tài khoản ứng dụng.');
  const profile = JSON.parse(profileResponse.getContentText() || 'null');
  if (!profile || profile.active !== true) throw new Error('Tài khoản chưa được cấp quyền hoặc đã bị khóa.');
  if (String(profile.uid || user.localId) !== String(user.localId)) throw new Error('UID hồ sơ không khớp Firebase Authentication.');
  return { uid: user.localId, profile: profile, firebase_email: String(user.email || profile.email || '') };
}

// Restricted RTDB relay for networks that allow Google Apps Script but block the Firebase RTDB host.
// It never uses an admin credential. The caller's Firebase ID token is forwarded to RTDB, therefore
// the existing Firebase Security Rules remain the final authority for every read/write.
function rtdbProxyV145_(auth, idToken, payload) {
  const method = String(payload.method || 'GET').toUpperCase();
  const path = String(payload.path || '').replace(/^\/+|\/+$/g, '');
  const role = String(auth.profile.role || '').toLowerCase();
  let targetPath = '';

  if (path === '.info/serverTimeOffset' && method === 'GET') {
    targetPath = '.info/serverTimeOffset';
  } else if (path === 'active_operator' && (method === 'GET' || method === 'PUT' || method === 'DELETE')) {
    if ((method === 'PUT' || method === 'DELETE') && role !== 'user')
      throw new Error('ADMIN không được ghi active_operator.');
    targetPath = 'active_operator';
  } else if (path === 'users' && method === 'GET') {
    requireAdmin_(auth.profile);
    targetPath = 'users';
  } else {
    const userMatch = path.match(/^users\/([^/]+)$/);
    if (!userMatch || (method !== 'GET' && method !== 'PUT'))
      throw new Error('RTDB relay từ chối path/method ngoài danh sách cho phép.');
    let uid = '';
    try { uid = decodeURIComponent(userMatch[1]); } catch (_) { throw new Error('UID RTDB không hợp lệ.'); }
    if (!uid || (uid !== String(auth.uid || '') && role !== 'admin'))
      throw new Error('Không có quyền truy cập hồ sơ RTDB này.');
    targetPath = 'users/' + encodeURIComponent(uid);
  }

  const headers = {};
  if (payload.want_etag === true) headers['X-Firebase-ETag'] = 'true';
  const ifMatch = String(payload.if_match || '').trim();
  if (ifMatch) headers['if-match'] = ifMatch;

  const options = {
    method: method.toLowerCase(),
    muteHttpExceptions: true,
    headers: headers
  };
  if (method === 'PUT') {
    const body = String(payload.body || 'null');
    if (body.length > 256 * 1024) throw new Error('RTDB relay body vượt 256 KB.');
    try { JSON.parse(body); } catch (_) { throw new Error('RTDB relay body không phải JSON hợp lệ.'); }
    options.contentType = 'application/json';
    options.payload = body;
  }

  const response = UrlFetchApp.fetch(
    CFG.FIREBASE_DB_URL + '/' + targetPath + '.json?auth=' + encodeURIComponent(idToken),
    options
  );
  const allHeaders = response.getAllHeaders() || {};
  const etag = String(allHeaders.ETag || allHeaders.Etag || allHeaders.etag || '');
  return {
    http_status: response.getResponseCode(),
    body: response.getContentText() || '',
    etag: etag
  };
}

function requireAdmin_(profile) {
  if (String(profile.role || '').toLowerCase() !== 'admin')
    throw new Error('Chỉ ADMIN được đồng bộ danh sách tài khoản đăng nhập.');
}

function requireSyncPermission_(profile) {
  if (String(profile.role || '').toLowerCase() === 'admin') return;
  const p = profile.permissions || {};
  if (p.damage_entry === true || p.sync_google === true) return;
  throw new Error('Tài khoản không có quyền đồng bộ thông tin hư hỏng.');
}

function requireImageReadPermission_(profile) {
  if (String(profile.role || '').toLowerCase() === 'admin') return;
  const p = profile.permissions || {};
  if (p.view_reports === true || p.damage_entry === true) return;
  throw new Error('Tài khoản không có quyền xem ảnh phiếu hư hỏng.');
}

function verifyFixedResources_() {
  DriveApp.getFolderById(CFG.ROOT_FOLDER_ID).getName();
  const imageFolder = DriveApp.getFolderById(CFG.IMAGE_FOLDER_ID);
  assertHasParent_(imageFolder, CFG.ROOT_FOLDER_ID, 'thư mục Ảnh hàng hư hỏng');
  const sheetFile = DriveApp.getFileById(CFG.SPREADSHEET_ID);
  assertHasParent_(sheetFile, CFG.ROOT_FOLDER_ID, 'Google Sheet hư hỏng');
  SpreadsheetApp.openById(CFG.SPREADSHEET_ID).getId();
}

function assertHasParent_(driveItem, parentId, label) {
  const parents = driveItem.getParents();
  while (parents.hasNext()) {
    if (parents.next().getId() === parentId) return;
  }
  throw new Error(label + ' nằm ngoài phạm vi dữ liệu được phép.');
}

function ensureHeaders_() {
  const sheet = getDataSheet_();
  const current = sheet.getRange(1, 1, 1, HEADERS.length).getValues()[0];
  let differs = false;
  for (let i = 0; i < HEADERS.length; i++) {
    if (String(current[i] || '') !== HEADERS[i]) { differs = true; break; }
  }
  if (differs) sheet.getRange(1, 1, 1, HEADERS.length).setValues([HEADERS]);
}

function getImage_(fileId) {
  if (!fileId) throw new Error('Thiếu mã file ảnh.');
  const file = DriveApp.getFileById(fileId);
  assertHasParent_(file, CFG.IMAGE_FOLDER_ID, 'Ảnh hư hỏng');
  const blob = file.getBlob();
  return {
    file_id: fileId,
    name: file.getName(),
    mime_type: blob.getContentType() || 'application/octet-stream',
    data_base64: Utilities.base64Encode(blob.getBytes())
  };
}

function syncReport_(report, images) {
  verifyFixedResources_();
  ensureHeaders_();
  const reportId = String(report.report_id || '').trim();
  const sku = String(report.sku || '').trim();
  if (!reportId || !sku) throw new Error('Phiếu thiếu report_id hoặc SKU.');

  const sheet = getDataSheet_();
  let row = findReportRow_(sheet, reportId);
  const isNewRow = !row;
  if (!row) {
    row = Math.max(2, sheet.getLastRow() + 1);
  }

  const existingLinks = isNewRow
    ? ['', '', '', '', '']
    : sheet.getRange(row, 10, 1, 5).getValues()[0].map(v => String(v || ''));
  const links = existingLinks.slice(0, 5);
  const imageResults = [];
  const imageFolder = DriveApp.getFolderById(CFG.IMAGE_FOLDER_ID);
  const requestedImages = (images || []).slice().sort((a, b) => Number(a.sequence || 0) - Number(b.sequence || 0));

  requestedImages.forEach(image => {
    const sequence = Number(image.sequence || 0);
    if (sequence < 1 || sequence > 5) throw new Error('Thứ tự ảnh không hợp lệ: ' + sequence + '.');
    let fileId = String(image.drive_file_id || '');
    let url = String(image.drive_link || '');

    if (!fileId && links[sequence - 1]) {
      url = links[sequence - 1];
      fileId = extractDriveFileId_(url);
    }

    if (!fileId) {
      const data = String(image.data_base64 || '');
      if (!data) throw new Error('Ảnh ' + sequence + ' chưa có dữ liệu để tải lên.');
      const bytes = Utilities.base64Decode(data);
      const mime = String(image.mime_type || 'application/octet-stream');
      const name = buildImageName_(report, sequence, mime, String(image.original_name || ''));
      const file = imageFolder.createFile(Utilities.newBlob(bytes, mime, name));
      fileId = file.getId();
      url = file.getUrl();
    }

    if (!fileId || !url) throw new Error('Ảnh ' + sequence + ' chưa có file/link Drive hợp lệ sau đồng bộ.');
    links[sequence - 1] = url;
    imageResults.push({ sequence: sequence, file_id: fileId, url: url });
  });

  if (imageResults.length !== requestedImages.length)
    throw new Error('Số ảnh đồng bộ không khớp số ảnh được gửi.');

  const now = new Date();
  const values = [
    reportId,
    displayDate_(report.occurred_date),
    pad2_(report.hour) + ':' + pad2_(report.minute),
    String(report.shift || ''),
    sku,
    String(report.product_name || ''),
    String(report.location || ''),
    Number(report.quantity || 0),
    String(report.base_unit || ''),
    links[0], links[1], links[2], links[3], links[4],
    String(report.created_at || ''),
    Utilities.formatDate(now, Session.getScriptTimeZone(), 'HH:mm:ss dd/MM/yyyy'),
    String(report.created_by || ''),
    Number(report.version || 1),
    String(report.updated_at || ''),
    String(report.updated_by || '')
  ];

  sheet.getRange(row, 1, 1, HEADERS.length).setValues([values]);
  SpreadsheetApp.flush();

  const saved = sheet.getRange(row, 1, 1, 14).getDisplayValues()[0];
  if (String(saved[0] || '') !== reportId || String(saved[4] || '') !== sku)
    throw new Error('Kiểm tra sau ghi thất bại: ID/SKU trên Google Sheet không khớp phiếu gửi lên.');
  requestedImages.forEach(image => {
    const sequence = Number(image.sequence || 0);
    if (!String(saved[8 + sequence] || ''))
      throw new Error('Kiểm tra sau ghi thất bại: thiếu link Ảnh ' + sequence + ' trên Google Sheet.');
  });

  return {
    report_id: reportId,
    row: row,
    image_count: imageResults.length,
    images: imageResults
  };
}

function findReportRow_(sheet, reportId) {
  const last = sheet.getLastRow();
  if (last < 2) return 0;
  const match = sheet.getRange(2, 1, last - 1, 1).createTextFinder(reportId).matchEntireCell(true).findNext();
  return match ? match.getRow() : 0;
}

function getDataSheet_() {
  const ss = SpreadsheetApp.openById(CFG.SPREADSHEET_ID);
  const sheets = ss.getSheets();
  if (!sheets.length) throw new Error('Google Sheet không có tab dữ liệu.');
  return sheets[0];
}

function buildImageName_(report, sequence, mime, originalName) {
  const date = String(report.occurred_date || '').replace(/-/g, '');
  const time = pad2_(report.hour) + '-' + pad2_(report.minute);
  const parts = [
    date,
    time,
    String(report.shift || ''),
    String(report.sku || ''),
    String(report.product_name || ''),
    String(report.location || ''),
    'SL-' + String(report.quantity || ''),
    String(sequence).padStart(2, '0')
  ];
  const base = parts.map(sanitizeFilePart_).filter(Boolean).join('_').substring(0, 180);
  return base + extensionFor_(mime, originalName);
}

function sanitizeFilePart_(value) {
  return String(value || '').replace(/[\\/:*?"<>|\r\n]+/g, '-').replace(/\s+/g, ' ').trim();
}

function extensionFor_(mime, originalName) {
  const lower = String(originalName || '').toLowerCase();
  const m = lower.match(/\.(jpg|jpeg|png|webp|heic)$/);
  if (m) return '.' + m[1];
  if (mime === 'image/png') return '.png';
  if (mime === 'image/webp') return '.webp';
  if (mime === 'image/heic') return '.heic';
  return '.jpg';
}

function extractDriveFileId_(url) {
  const m = String(url || '').match(/\/d\/([^/]+)/);
  if (m) return m[1];
  const q = String(url || '').match(/[?&]id=([^&]+)/);
  return q ? decodeURIComponent(q[1]) : '';
}

function displayDate_(isoDate) {
  const p = String(isoDate || '').split('-');
  return p.length === 3 ? p[2] + '/' + p[1] + '/' + p[0] : String(isoDate || '');
}

function pad2_(value) {
  return String(Number(value || 0)).padStart(2, '0');
}

function cleanError_(err) {
  const text = err && err.message ? err.message : String(err || 'Lỗi không xác định.');
  return text.length > 1000 ? text.substring(0, 1000) + '...' : text;
}

function json_(value) {
  return ContentService.createTextOutput(JSON.stringify(value)).setMimeType(ContentService.MimeType.JSON);
}

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
