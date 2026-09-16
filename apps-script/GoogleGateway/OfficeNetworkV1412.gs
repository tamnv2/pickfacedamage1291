const OFFICE_NETWORK_VERSION_V1412 = '1.4.12';

function firebaseRefreshSessionV1412_(sessionRefresh) {
  sessionRefresh = String(sessionRefresh || '').trim();
  if (!sessionRefresh) throw new Error('Thiếu thông tin làm mới phiên. Hãy đăng nhập lại.');

  const response = UrlFetchApp.fetch(
    'https://securetoken.googleapis.com/v1/token?key=' + encodeURIComponent(CFG.FIREBASE_API_KEY),
    {
      method: 'post',
      contentType: 'application/x-www-form-urlencoded',
      payload: {
        grant_type: 'refresh_token',
        refresh_token: sessionRefresh
      },
      muteHttpExceptions: true
    }
  );

  if (response.getResponseCode() !== 200) {
    const body = JSON.parse(response.getContentText() || '{}');
    const code = body && body.error ? String(body.error.message || '') : '';
    if (/TOKEN_EXPIRED|INVALID_REFRESH_TOKEN|USER_DISABLED|USER_NOT_FOUND/i.test(code))
      throw new Error('Phiên đăng nhập đã hết hiệu lực. Hãy đăng nhập lại.');
    throw new Error('Không làm mới được phiên Firebase qua Google Gateway.');
  }

  const body = JSON.parse(response.getContentText() || '{}');
  const idToken = String(body.id_token || '');
  if (!idToken) throw new Error('Firebase không trả ID token mới.');

  return {
    id_token: idToken,
    session_refresh: String(body.refresh_token || sessionRefresh),
    user_id: String(body.user_id || ''),
    expires_in: String(body.expires_in || '3600'),
    transport: 'google_gateway'
  };
}

function firebaseAdminCreateUserV1412_(auth, adminIdToken, payload) {
  requireAdmin_(auth.profile);

  const email = String(payload.email || '').trim().toLowerCase();
  const username = normalizeUsername_(payload.username || '');
  const displayName = String(payload.display_name || '').trim();
  const requestedPermissions = payload.permissions && typeof payload.permissions === 'object'
    ? payload.permissions
    : {};

  if (!username || !displayName || !email)
    throw new Error('Nhập đủ tên tài khoản, họ tên và email.');
  if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email))
    throw new Error('Email không hợp lệ.');
  if (username.length > 80 || displayName.length > 120 || email.length > 200)
    throw new Error('Thông tin USER vượt giới hạn cho phép.');

  const usersResponse = UrlFetchApp.fetch(
    CFG.FIREBASE_DB_URL + '/users.json?auth=' + encodeURIComponent(adminIdToken),
    { method: 'get', muteHttpExceptions: true }
  );
  if (usersResponse.getResponseCode() !== 200)
    throw new Error('Không kiểm tra được danh sách USER hiện tại.');

  const users = JSON.parse(usersResponse.getContentText() || '{}') || {};
  Object.keys(users).forEach(uid => {
    const profile = users[uid] || {};
    if (normalizeUsername_(profile.username || '') === username)
      throw new Error('Tên tài khoản đã tồn tại.');
    if (String(profile.email || '').trim().toLowerCase() === email)
      throw new Error('Email đã được dùng cho một tài khoản trong ứng dụng.');
  });

  const temporaryPassword = 'Pfd!' + Utilities.getUuid().replace(/-/g, '').slice(0, 22) + '9a';
  let uid = '';
  let newUserIdToken = '';
  let profileWritten = false;

  try {
    const signup = UrlFetchApp.fetch(
      'https://identitytoolkit.googleapis.com/v1/accounts:signUp?key=' + encodeURIComponent(CFG.FIREBASE_API_KEY),
      {
        method: 'post',
        contentType: 'application/json',
        payload: JSON.stringify({
          email: email,
          password: temporaryPassword,
          returnSecureToken: true
        }),
        muteHttpExceptions: true
      }
    );

    const signupBody = JSON.parse(signup.getContentText() || '{}');
    if (signup.getResponseCode() !== 200) {
      const code = signupBody && signupBody.error ? String(signupBody.error.message || '') : '';
      if (code === 'EMAIL_EXISTS') throw new Error('Email đã tồn tại trên Firebase Authentication.');
      throw new Error('Không tạo được tài khoản Firebase Authentication.');
    }

    uid = String(signupBody.localId || '');
    newUserIdToken = String(signupBody.idToken || '');
    if (!uid || !newUserIdToken)
      throw new Error('Firebase không trả đủ thông tin USER mới.');

    const permissions = {
      damage_entry: requestedPermissions.damage_entry === true,
      view_reports: requestedPermissions.view_reports === true,
      import_sku: requestedPermissions.import_sku === true,
      sync_google: requestedPermissions.sync_google === true
    };

    const profile = {
      uid: uid,
      username: username,
      display_name: displayName,
      email: email,
      role: 'user',
      active: true,
      permissions: permissions
    };

    const profileResponse = UrlFetchApp.fetch(
      CFG.FIREBASE_DB_URL + '/users/' + encodeURIComponent(uid) + '.json?auth=' + encodeURIComponent(adminIdToken),
      {
        method: 'put',
        contentType: 'application/json',
        payload: JSON.stringify(profile),
        muteHttpExceptions: true
      }
    );
    if (profileResponse.getResponseCode() !== 200)
      throw new Error('Không ghi được hồ sơ USER mới vào Firebase Database.');
    profileWritten = true;

    const reset = UrlFetchApp.fetch(
      'https://identitytoolkit.googleapis.com/v1/accounts:sendOobCode?key=' + encodeURIComponent(CFG.FIREBASE_API_KEY),
      {
        method: 'post',
        contentType: 'application/json',
        payload: JSON.stringify({ requestType: 'PASSWORD_RESET', email: email }),
        muteHttpExceptions: true
      }
    );
    if (reset.getResponseCode() !== 200)
      throw new Error('Không gửi được email đặt mật khẩu cho USER mới. Hệ thống sẽ rollback USER này.');

    PropertiesService.getScriptProperties().setProperty(aliasKey_(username), email);
    return { profile: profile, transport: 'google_gateway' };
  } catch (err) {
    if (profileWritten && uid) {
      try {
        UrlFetchApp.fetch(
          CFG.FIREBASE_DB_URL + '/users/' + encodeURIComponent(uid) + '.json?auth=' + encodeURIComponent(adminIdToken),
          { method: 'delete', muteHttpExceptions: true }
        );
      } catch (_) {}
    }

    if (newUserIdToken) {
      try {
        UrlFetchApp.fetch(
          'https://identitytoolkit.googleapis.com/v1/accounts:delete?key=' + encodeURIComponent(CFG.FIREBASE_API_KEY),
          {
            method: 'post',
            contentType: 'application/json',
            payload: JSON.stringify({ idToken: newUserIdToken }),
            muteHttpExceptions: true
          }
        );
      } catch (_) {}
    }
    throw err;
  }
}
