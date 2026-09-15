// Office-network fallback transport for the minimum Firebase RTDB data required by the desktop app.
// The desktop app uses this endpoint only after direct Firebase RTDB access fails.
// Scope is deliberately fixed to active_operator and one authenticated user profile; arbitrary
// Firebase paths are not accepted. Firebase Security Rules remain the authorization authority.

function doGet(e) {
  try {
    const p = (e && e.parameter) || {};
    if (String(p.action || '') !== 'operator_proxy') {
      return json_({ ok: true, service: 'PickfaceDamage1291 Google Gateway', version: GATEWAY_VERSION });
    }

    const idToken = String(p.id_token || '');
    const op = String(p.op || '').toLowerCase();
    if (!idToken) throw new Error('Thiếu phiên Firebase.');

    if (op === 'now') {
      return json_({ ok: true, server_now: Date.now() });
    }

    if (op === 'profile') {
      const uid = String(p.uid || '').trim();
      if (!uid || /[.#$\[\]\/]/.test(uid)) throw new Error('UID Firebase không hợp lệ.');
      const response = UrlFetchApp.fetch(profileUrl_(uid, idToken), {
        method: 'get',
        muteHttpExceptions: true
      });
      assertFirebaseProxyResponse_(response, 'đọc hồ sơ');
      const text = response.getContentText() || 'null';
      return json_({ ok: true, value: JSON.parse(text) });
    }

    if (op === 'get') {
      const response = UrlFetchApp.fetch(operatorUrl_(idToken), {
        method: 'get',
        headers: { 'X-Firebase-ETag': 'true' },
        muteHttpExceptions: true
      });
      assertFirebaseProxyResponse_(response, 'đọc active_operator');
      const headers = response.getAllHeaders ? response.getAllHeaders() : {};
      const etag = String(headers.ETag || headers.Etag || headers.etag || '*');
      const text = response.getContentText() || 'null';
      return json_({ ok: true, etag: etag, value: JSON.parse(text) });
    }

    if (op === 'put') {
      // Do not trust client-side role/UID assertions. Firebase Security Rules verify auth.uid,
      // role, active state and the complete lease shape before accepting this write.
      const value = decodeOperatorPayload_(String(p.payload_b64 || ''));
      if (!value || !String(value.uid || '')) throw new Error('Dữ liệu active_operator không hợp lệ.');
      const response = UrlFetchApp.fetch(operatorUrl_(idToken), {
        method: 'put',
        contentType: 'application/json',
        headers: { 'if-match': String(p.etag || '*') },
        payload: JSON.stringify(value),
        muteHttpExceptions: true
      });
      if (response.getResponseCode() === 412) return json_({ ok: true, applied: false });
      assertFirebaseProxyResponse_(response, 'ghi active_operator');
      return json_({ ok: true, applied: true });
    }

    if (op === 'delete') {
      const response = UrlFetchApp.fetch(operatorUrl_(idToken), {
        method: 'delete',
        headers: { 'if-match': String(p.etag || '*') },
        muteHttpExceptions: true
      });
      if (response.getResponseCode() === 412) return json_({ ok: true, applied: false });
      assertFirebaseProxyResponse_(response, 'xoá active_operator');
      return json_({ ok: true, applied: true });
    }

    throw new Error('Firebase fallback operation không được hỗ trợ.');
  } catch (err) {
    return json_({ ok: false, error: cleanError_(err) });
  }
}

function operatorUrl_(idToken) {
  return CFG.FIREBASE_DB_URL + '/active_operator.json?auth=' + encodeURIComponent(idToken);
}

function profileUrl_(uid, idToken) {
  return CFG.FIREBASE_DB_URL + '/users/' + encodeURIComponent(uid) + '.json?auth=' + encodeURIComponent(idToken);
}

function decodeOperatorPayload_(payloadB64) {
  if (!payloadB64) throw new Error('Thiếu dữ liệu active_operator.');
  const bytes = Utilities.base64Decode(payloadB64);
  const json = Utilities.newBlob(bytes).getDataAsString('UTF-8');
  return JSON.parse(json || 'null');
}

function assertFirebaseProxyResponse_(response, actionLabel) {
  const code = response.getResponseCode();
  if (code >= 200 && code < 300) return;
  const body = String(response.getContentText() || '');
  throw new Error('Firebase RTDB không cho phép ' + actionLabel + ' qua gateway (HTTP ' + code + '). ' + body.slice(0, 200));
}
