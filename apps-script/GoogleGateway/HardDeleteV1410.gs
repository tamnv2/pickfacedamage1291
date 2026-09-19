const GATEWAY_VERSION_V1410 = '1.4.12';
const GATEWAY_CAPABILITIES_V1410 = Object.freeze([
  'pull_changes','push_products','pull_products','append_audit','list_audit','delete_audit_range',
  'upload_log','sync_report','hard_delete_reports','get_image','rtdb_proxy',
  'firebase_refresh_session','firebase_admin_create_user','adaptive_office_route_v1412','release_metadata'
]);

// v1.4.10 wrapper. Legacy doPost in Code.gs is renamed to doPostLegacy_ during verified promotion.
function doPost(e) {
  try {
    if (!e || !e.postData || !e.postData.contents) return doPostLegacy_(e);
    const request = JSON.parse(e.postData.contents);
    const action = String(request.action || '');
    const payload = request.payload || {};

    if (action === 'gateway_info') {
      return json_({ ok:true, version:GATEWAY_VERSION_V1410, capabilities:GATEWAY_CAPABILITIES_V1410 });
    }

    if (action === 'firebase_refresh_session') {
      return json_({ ok:true, ...firebaseRefreshSessionV1412_(String(request.session_refresh || '')) });
    }

    if (action === 'firebase_admin_create_user') {
      const idToken = String(request.id_token || '');
      const auth = authenticateFirebase_(idToken);
      requireAdmin_(auth.profile);
      return json_({ ok:true, ...firebaseAdminCreateUserV1412_(auth, idToken, payload) });
    }

    if (action === 'hard_delete_reports') {
      const idToken = String(request.id_token || '');
      const auth = authenticateFirebase_(idToken);
      requireAdmin_(auth.profile);
      const lock = LockService.getScriptLock();
      if (!lock.tryLock(10000)) throw new Error('Hệ thống đang xử lý dữ liệu khác. Hãy thử lại sau vài giây.');
      try {
        return json_({ ok:true, ...hardDeleteReportsV1410_(auth, payload) });
      } finally {
        lock.releaseLock();
      }
    }

    if (action === 'pull_changes') {
      const idToken = String(request.id_token || '');
      const auth = authenticateFirebase_(idToken);
      requireReportReadPermissionV140_(auth.profile);
      return json_({ ok:true, ...pullReportChangesV1410_(payload) });
    }

    if (action === 'delete_audit_range') {
      const idToken = String(request.id_token || '');
      const auth = authenticateFirebase_(idToken);
      requireAdmin_(auth.profile);
      return json_({ ok:true, ...deleteAuditRangeV1410_(auth, payload) });
    }

    if (action === 'release_metadata') {
      return json_({ ok:true, ...releaseMetadataV1432_() });
    }

    return doPostLegacy_(e);
  } catch (err) {
    return json_({ ok:false, error:cleanError_(err) });
  }
}

function hardDeleteReportsV1410_(auth, payload) {
  verifyFixedResources_();
  ensureReportHeadersV140_();
  const requested = Array.isArray(payload.reports) ? payload.reports.slice(0, 100) : [];
  if (!requested.length) throw new Error('Chưa có phiếu cần xoá.');

  const sheet = getDataSheet_();
  const username = String(auth.profile.username || payload.deleted_by || 'ADMIN');
  const deviceId = safeTextV140_(payload.device_id, 200);
  const sessionId = safeTextV140_(payload.session_id, 200);
  const clientTime = safeTextV140_(payload.client_time, 200);
  const results = [];
  let deletedReports = 0;
  let deletedImages = 0;

  requested.forEach(item => {
    const reportId = String(item.report_id || '').trim();
    const expectedVersion = Math.max(1, Number(item.version || 1));
    if (!reportId) {
      results.push({ report_id:'', deleted:false, already_deleted:false, deleted_images:0, error:'Thiếu report_id.' });
      return;
    }

    try {
      let row = findReportRow_(sheet, reportId);
      if (!row) {
        if (findHardDeleteAuditV1410_(reportId)) {
          results.push({ report_id:reportId, deleted:false, already_deleted:true, deleted_images:0, error:'' });
        } else {
          results.push({ report_id:reportId, deleted:false, already_deleted:false, deleted_images:0, error:'Không tìm thấy dòng Google Sheet và chưa có lịch sử xoá hoàn tất.' });
        }
        return;
      }

      const values = sheet.getRange(row, 1, 1, REPORT_HEADERS_V140.length).getValues()[0];
      const currentVersion = Math.max(1, Number(values[17] || 1));
      if (currentVersion > expectedVersion) {
        results.push({
          report_id:reportId,
          deleted:false,
          already_deleted:false,
          deleted_images:0,
          error:'Phiếu trên Google đã lên phiên bản ' + currentVersion + '. Hãy đồng bộ lại trước khi xoá.'
        });
        return;
      }

      const imageIds = [];
      values.slice(9, 14).forEach(link => {
        const id = extractDriveFileId_(String(link || ''));
        if (id && imageIds.indexOf(id) < 0) imageIds.push(id);
      });

      // Fail closed: each existing image must belong to the fixed project image folder.
      const states = imageIds.map(id => inspectImageFileV1410_(id));
      states.forEach(state => {
        if (state.exists && !state.in_scope)
          throw new Error('Phát hiện ảnh không thuộc thư mục ảnh cố định của PICKFACE DAMAGE 1291. Đã dừng xoá.');
      });

      let imageDeletedForReport = 0;
      const imageFailures = [];
      states.forEach(state => {
        if (!state.exists) return;
        try {
          permanentlyDeleteImageV1410_(state.file_id);
          imageDeletedForReport++;
        } catch (err) {
          imageFailures.push(state.file_id + ': ' + cleanError_(err));
        }
      });

      if (imageFailures.length) {
        try {
          appendAuditV140_(auth, {
            event_id:Utilities.getUuid().replace(/-/g,''),
            device_id:deviceId,
            session_id:sessionId,
            action:'DAMAGE_REPORT_HARD_DELETE_PARTIAL',
            client_time:clientTime || new Date().toISOString(),
            details:{
              report_id:reportId,
              sku:String(values[4] || item.sku || ''),
              deleted_images:imageDeletedForReport,
              image_failures:imageFailures
            }
          });
        } catch (_) {}
        deletedImages += imageDeletedForReport;
        results.push({
          report_id:reportId,
          deleted:false,
          already_deleted:false,
          deleted_images:imageDeletedForReport,
          error:'Chưa xoá được toàn bộ ảnh Drive. Dòng Google Sheet được giữ lại để có thể thử lại.'
        });
        return;
      }

      row = findReportRow_(sheet, reportId);
      if (!row) throw new Error('Dòng Google Sheet thay đổi trong lúc xoá. Hãy đồng bộ và thử lại.');

      const changeSeq = nextCounterV140_('REPORT_CHANGE_SEQ_V140', sheet, 21);
      const eventId = Utilities.getUuid().replace(/-/g,'');
      appendAuditV140_(auth, {
        event_id:eventId,
        device_id:deviceId,
        session_id:sessionId,
        action:'DAMAGE_REPORT_HARD_DELETED',
        client_time:clientTime || new Date().toISOString(),
        details:{
          report_id:reportId,
          sku:String(values[4] || item.sku || ''),
          change_seq:changeSeq,
          version:currentVersion + 1,
          created_at:asIsoV140_(values[14]),
          created_by:String(values[16] || item.created_by || ''),
          deleted_at:new Date().toISOString(),
          deleted_by:username,
          deleted_images:imageDeletedForReport
        }
      });

      try {
        sheet.deleteRow(row);
        SpreadsheetApp.flush();
      } catch (deleteErr) {
        removeAuditEventV1410_(eventId);
        throw deleteErr;
      }

      deletedReports++;
      deletedImages += imageDeletedForReport;
      results.push({ report_id:reportId, deleted:true, already_deleted:false, deleted_images:imageDeletedForReport, error:'' });
    } catch (err) {
      results.push({ report_id:reportId, deleted:false, already_deleted:false, deleted_images:0, error:cleanError_(err) });
    }
  });

  return { deleted_reports:deletedReports, deleted_images:deletedImages, results:results };
}

function inspectImageFileV1410_(fileId) {
  const url = 'https://www.googleapis.com/drive/v3/files/' + encodeURIComponent(fileId) + '?fields=id,parents&supportsAllDrives=true';
  const response = UrlFetchApp.fetch(url, {
    method:'get',
    headers:{ Authorization:'Bearer ' + ScriptApp.getOAuthToken() },
    muteHttpExceptions:true
  });
  const code = response.getResponseCode();
  if (code === 404) return { file_id:fileId, exists:false, in_scope:true };
  if (code !== 200) throw new Error('Không xác minh được ảnh Drive ' + fileId + ' (HTTP ' + code + ').');
  const meta = JSON.parse(response.getContentText() || '{}');
  const parents = Array.isArray(meta.parents) ? meta.parents.map(String) : [];
  return { file_id:fileId, exists:true, in_scope:parents.indexOf(CFG.IMAGE_FOLDER_ID) >= 0 };
}

function permanentlyDeleteImageV1410_(fileId) {
  const url = 'https://www.googleapis.com/drive/v3/files/' + encodeURIComponent(fileId) + '?supportsAllDrives=true';
  let lastCode = 0;
  let lastBody = '';
  for (let attempt = 0; attempt < 3; attempt++) {
    const response = UrlFetchApp.fetch(url, {
      method:'delete',
      headers:{ Authorization:'Bearer ' + ScriptApp.getOAuthToken() },
      muteHttpExceptions:true
    });
    lastCode = response.getResponseCode();
    lastBody = response.getContentText() || '';
    if (lastCode === 200 || lastCode === 204 || lastCode === 404) return;
    Utilities.sleep(250 * (attempt + 1));
  }
  throw new Error('Không xoá vĩnh viễn được ảnh Drive (HTTP ' + lastCode + '). ' + safeTextV140_(lastBody, 250));
}

function pullReportChangesV1410_(payload) {
  ensureReportHeadersV140_();
  const sheet = getDataSheet_();
  const after = Math.max(0, Number(payload.after_seq || 0));
  const limit = Math.max(1, Math.min(1000, Number(payload.limit || 500)));
  const matches = [];

  const last = sheet.getLastRow();
  if (last >= 2) {
    const rows = sheet.getRange(2, 1, last - 1, REPORT_HEADERS_V140.length).getValues();
    rows.forEach(values => {
      const seq = Number(values[20] || 0);
      if (!String(values[0] || '').trim() || seq <= after) return;
      matches.push({ seq:seq, change:rowToChangeV140_(values) });
    });
  }

  hardDeleteAuditChangesV1410_(after).forEach(change => {
    const seq = Number(change.change_seq || 0);
    if (seq > after) matches.push({ seq:seq, change:change });
  });

  matches.sort((a,b) => a.seq - b.seq);
  return {
    changes:matches.slice(0, limit).map(x => x.change),
    latest_seq:getCounterV140_('REPORT_CHANGE_SEQ_V140', sheet, 21),
    has_more:matches.length > limit
  };
}

function hardDeleteAuditChangesV1410_(afterSeq) {
  const sheet = getOrCreateSheetV140_('AuditHistory', AUDIT_HEADERS_V140);
  const last = sheet.getLastRow();
  if (last < 2) return [];
  const rows = sheet.getRange(2, 1, last - 1, AUDIT_HEADERS_V140.length).getValues();
  const out = [];
  rows.forEach(row => {
    if (String(row[7] || '') !== 'DAMAGE_REPORT_HARD_DELETED') return;
    let details = null;
    try { details = JSON.parse(String(row[8] || '{}')); } catch (_) { details = null; }
    if (!details) return;
    const seq = Number(details.change_seq || 0);
    const reportId = String(details.report_id || '');
    if (!reportId || seq <= afterSeq) return;
    out.push({
      change_seq:seq,
      deleted:true,
      fingerprint:'',
      deleted_at:String(details.deleted_at || ''),
      deleted_by:String(details.deleted_by || row[4] || ''),
      report:{
        report_id:reportId,
        occurred_date:'', hour:0, minute:0, shift:'', sku:'__DELETED__', product_name:'ĐÃ XÓA',
        location:'', quantity:0, base_unit:'', created_at:String(details.created_at || ''),
        created_by:String(details.created_by || ''), version:Math.max(1, Number(details.version || 1)),
        updated_at:String(details.deleted_at || ''), updated_by:String(details.deleted_by || row[4] || '')
      },
      images:[]
    });
  });
  return out;
}

function findHardDeleteAuditV1410_(reportId) {
  const changes = hardDeleteAuditChangesV1410_(0);
  for (let i = changes.length - 1; i >= 0; i--)
    if (String(changes[i].report.report_id || '') === String(reportId || '')) return changes[i];
  return null;
}

function removeAuditEventV1410_(eventId) {
  const sheet = getOrCreateSheetV140_('AuditHistory', AUDIT_HEADERS_V140);
  const last = sheet.getLastRow();
  if (last < 2) return;
  const found = sheet.getRange(2, 1, last - 1, 1).createTextFinder(String(eventId || '')).matchEntireCell(true).findNext();
  if (found) sheet.deleteRow(found.getRow());
}

function deleteAuditRangeV1410_(auth, payload) {
  const from = Number(payload.from_server_time || 0), to = Number(payload.to_server_time || 0);
  if (!from || !to || from > to) throw new Error('Khoảng ngày xóa không hợp lệ.');
  const sheet = getOrCreateSheetV140_('AuditHistory', AUDIT_HEADERS_V140);
  const last = sheet.getLastRow();
  if (last < 2) return { deleted_count:0 };
  const rows = sheet.getRange(2, 1, last - 1, AUDIT_HEADERS_V140.length).getValues();
  const kept = [];
  let deleted = 0;
  rows.forEach(r => {
    const t = Number(r[1] || 0), action = String(r[7] || '');
    if (t >= from && t <= to && action !== 'AUDIT_LOGS_DELETED' && action !== 'DAMAGE_REPORT_HARD_DELETED') deleted++;
    else kept.push(r);
  });
  sheet.getRange(2, 1, last - 1, AUDIT_HEADERS_V140.length).clearContent();
  if (kept.length) sheet.getRange(2, 1, kept.length, AUDIT_HEADERS_V140.length).setValues(kept);
  appendAuditV140_(auth, {
    event_id:Utilities.getUuid().replace(/-/g,''), device_id:'server', session_id:'', action:'AUDIT_LOGS_DELETED',
    client_time:new Date().toISOString(), details:{ from_server_time:from, to_server_time:to, deleted_count:deleted }
  });
  SpreadsheetApp.flush();
  return { deleted_count:deleted };
}
