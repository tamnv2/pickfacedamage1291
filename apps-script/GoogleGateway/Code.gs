const CFG = Object.freeze({
  PROJECT_ID: 'pickface-damage-1291',
  FIREBASE_API_KEY: 'AIzaSyD6aKmuZSbcl5HAqnb7fNv_RaD6v4gQABU',
  FIREBASE_DB_URL: 'https://pickface-damage-1291-default-rtdb.asia-southeast1.firebasedatabase.app',
  ROOT_FOLDER_ID: '16jDCy5_Z1X5cKJyNPR1rn_ZqbExQSAeC',
  IMAGE_FOLDER_ID: '1K_lUl_uE4dskR28cVK4iZJrzFUINfvXf',
  SPREADSHEET_ID: '1Ubm9EhALocUovzVr3UIspdCMtHIPm2NPUw6UjlZcqw4'
});

const LOGIN_ALIAS_PREFIX = 'LOGIN_ALIAS_';

const HEADERS = [
  'ID', 'Ngày phát hiện', 'Giờ phát hiện', 'Ca', 'SKU', 'Tên sản phẩm', 'Vị trí phát hiện',
  'Số lượng hư hỏng', 'Base Units', 'Ảnh 1', 'Ảnh 2', 'Ảnh 3', 'Ảnh 4', 'Ảnh 5',
  'Thời gian nhập', 'Thời gian đồng bộ', 'Người tạo', 'Phiên bản', 'Cập nhật lúc', 'Cập nhật bởi'
];

function doPost(e) {
  try {
    if (!e || !e.postData || !e.postData.contents) throw new Error('Thiếu request body.');
    const request = JSON.parse(e.postData.contents);
    const action = String(request.action || '');
    const payload = request.payload || {};

    if (action === 'login_by_username') {
      return json_(loginByUsername_(String(request.username || ''), String(request.password || '')));
    }

    if (action === 'password_reset_by_username') {
      return json_(passwordResetByUsername_(String(request.username || '')));
    }

    const idToken = String(request.id_token || '');
    const auth = authenticateFirebase_(idToken);

    if (action === 'sync_login_aliases') {
      requireAdmin_(auth.profile);
      const count = syncLoginAliases_(idToken);
      return json_({ ok: true, count: count });
    }

    if (action === 'verify') {
      verifyFixedResources_();
      if (payload.write_headers === true) ensureHeaders_();
      return json_({ ok: true, uid: auth.uid, username: auth.profile.username || '' });
    }

    if (action === 'get_image') {
      requireImageReadPermission_(auth.profile);
      return json_({ ok: true, ...getImage_(String(payload.file_id || '')) });
    }

    if (action === 'sync_report') {
      requireSyncPermission_(auth.profile);
      const lock = LockService.getScriptLock();
      if (!lock.tryLock(10000)) throw new Error('Hệ thống đang đồng bộ một phiếu khác. Hãy thử lại sau vài giây.');
      try {
        const result = syncReport_(payload.report || {}, payload.images || []);
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
  return { uid: user.localId, profile: profile };
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
