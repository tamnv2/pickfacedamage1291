// Office-network fallback transport for active_operator only.
// The desktop app uses this endpoint only after direct Firebase RTDB access fails.
// Scope is deliberately fixed to /active_operator; arbitrary Firebase paths are not accepted.
// Firebase Security Rules validate the ID token and remain the final authorization authority.

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

    if (op === 'get') {
      const response = UrlFetchApp.fetch(operatorUrl_(idToken), {
        method: 'get',
        headers: { 'X-Firebase-ETag': 'true' },
        muteHttpExceptions: true
      });
      assertOperatorProxyResponse_(response, 'đọc');
      const headers = response.getAllHeaders ? response.getAllHeaders() : {};
      const etag = String(headers.ETag || headers.Etag || headers.etag || '*');
      const text = response.getContentText() || 'null';
      return json_({ ok: true, etag: etag, value: JSON.parse(text) });
    }

    if (op === 'put') {
      // Do not trust the client-side UID. Firebase Security Rules verify auth.uid, role, active
      // and the complete lease shape before accepting this write.
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
      assertOperatorProxyResponse_(response, 'ghi');
      return json_({ ok: true, applied: true });
    }

    if (op === 'delete') {
      const response = UrlFetchApp.fetch(operatorUrl_(idToken), {
        method: 'delete',
        headers: { 'if-match': String(p.etag || '*') },
        muteHttpExceptions: true
      });
      if (response.getResponseCode() === 412) return json_({ ok: true, applied: false });
      assertOperatorProxyResponse_(response, 'xoá');
      return json_({ ok: true, applied: true });
    }

    throw new Error('Operator proxy operation không được hỗ trợ.');
  } catch (err) {
    return json_({ ok: false, error: cleanError_(err) });
  }
}

function operatorUrl_(idToken) {
  return CFG.FIREBASE_DB_URL + '/active_operator.json?auth=' + encodeURIComponent(idToken);
}

function decodeOperatorPayload_(payloadB64) {
  if (!payloadB64) throw new Error('Thiếu dữ liệu active_operator.');
  const bytes = Utilities.base64Decode(payloadB64);
  const json = Utilities.newBlob(bytes).getDataAsString('UTF-8');
  return JSON.parse(json || 'null');
}

function assertOperatorProxyResponse_(response, actionLabel) {
  const code = response.getResponseCode();
  if (code >= 200 && code < 300) return;
  const body = String(response.getContentText() || '');
  throw new Error('Firebase RTDB không cho phép ' + actionLabel + ' active_operator qua gateway (HTTP ' + code + '). ' + body.slice(0, 200));
}
