const RELEASE_MIRROR_V1411 = Object.freeze({
  REPOSITORY: 'tamnv2/pickfacedamage1291',
  ROOT_FOLDER_ID: '16jDCy5_Z1X5cKJyNPR1rn_ZqbExQSAeC',
  FOLDER_NAME: 'ReleaseMirror',
  CHUNK_SIZE: 4 * 1024 * 1024,
  MAX_RELEASE_BYTES: 300 * 1024 * 1024
});

function releaseMirrorManifestV1411_(payload) {
  const release = loadLatestReleaseV1411_();
  const mirror = findReleaseMirrorV1411_(release, false);
  return {
    tag: release.tag,
    html_url: release.html_url,
    notes: release.notes,
    asset_name: release.asset_name,
    asset_url: release.asset_url,
    digest: release.digest,
    size: release.size,
    mirror_ready: !!mirror,
    mirror_file_id: mirror ? mirror.file_id : ''
  };
}

function warmLatestReleaseMirrorV1411_() {
  const release = loadLatestReleaseV1411_();
  const existing = findReleaseMirrorV1411_(release, false);
  if (existing) {
    ensureMirrorManifestFileV1411_(release, existing);
    return {
      tag: release.tag,
      asset_name: release.asset_name,
      digest: release.digest,
      size: release.size,
      mirror_ready: true,
      mirror_file_id: existing.file_id,
      reused: true
    };
  }

  const folder = getReleaseMirrorFolderV1411_();
  const upload = uploadReleaseToDriveV1411_(release, folder.getId());
  ensureMirrorManifestFileV1411_(release, upload);
  return {
    tag: release.tag,
    asset_name: release.asset_name,
    digest: release.digest,
    size: release.size,
    mirror_ready: true,
    mirror_file_id: upload.file_id,
    reused: false
  };
}

function releaseMirrorChunkV1411_(payload) {
  const release = loadLatestReleaseV1411_();
  const requestedTag = String(payload.tag || '').trim();
  const requestedAsset = String(payload.asset_name || '').trim();
  if (requestedTag && requestedTag !== release.tag) throw new Error('Phiên bản yêu cầu không còn là release mới nhất. Hãy kiểm tra cập nhật lại.');
  if (requestedAsset && requestedAsset !== release.asset_name) throw new Error('Tên gói cập nhật không khớp release mới nhất.');

  let mirror = findReleaseMirrorV1411_(release, false);
  if (!mirror) mirror = warmLatestReleaseMirrorV1411_();
  if (!mirror || !mirror.mirror_file_id && !mirror.file_id) throw new Error('Chưa tạo được bản mirror trên Google Drive.');

  const fileId = String(mirror.file_id || mirror.mirror_file_id || '');
  const chunkIndex = Math.max(0, Math.floor(Number(payload.chunk_index || 0)));
  const chunkCount = Math.ceil(release.size / RELEASE_MIRROR_V1411.CHUNK_SIZE);
  if (chunkIndex >= chunkCount) throw new Error('Chỉ số chunk vượt phạm vi gói cập nhật.');

  const start = chunkIndex * RELEASE_MIRROR_V1411.CHUNK_SIZE;
  const end = Math.min(release.size - 1, start + RELEASE_MIRROR_V1411.CHUNK_SIZE - 1);
  const response = UrlFetchApp.fetch(
    'https://www.googleapis.com/drive/v3/files/' + encodeURIComponent(fileId) + '?alt=media&supportsAllDrives=true',
    {
      method: 'get',
      headers: {
        Authorization: 'Bearer ' + ScriptApp.getOAuthToken(),
        Range: 'bytes=' + start + '-' + end
      },
      muteHttpExceptions: true
    }
  );
  const code = response.getResponseCode();
  if (code !== 206 && code !== 200) throw new Error('Không đọc được bản mirror trên Drive (HTTP ' + code + ').');
  const bytes = response.getBlob().getBytes();
  const expected = end - start + 1;
  if (bytes.length !== expected) throw new Error('Kích thước chunk Drive không khớp (' + bytes.length + '/' + expected + ').');

  return {
    tag: release.tag,
    asset_name: release.asset_name,
    digest: release.digest,
    total_size: release.size,
    chunk_size: RELEASE_MIRROR_V1411.CHUNK_SIZE,
    chunk_index: chunkIndex,
    chunk_count: chunkCount,
    offset: start,
    data_base64: Utilities.base64Encode(bytes),
    chunk_sha256: sha256HexV1411_(bytes)
  };
}

function loadLatestReleaseV1411_() {
  const url = 'https://api.github.com/repos/' + RELEASE_MIRROR_V1411.REPOSITORY + '/releases/latest';
  const response = UrlFetchApp.fetch(url, {
    method: 'get',
    headers: {
      Accept: 'application/vnd.github+json',
      'User-Agent': 'PickfaceDamage1291-GoogleGateway'
    },
    muteHttpExceptions: true
  });
  if (response.getResponseCode() !== 200) throw new Error('Không đọc được GitHub Release từ Google gateway (HTTP ' + response.getResponseCode() + ').');
  const body = JSON.parse(response.getContentText() || '{}');
  if (body.draft === true) throw new Error('Release mới nhất đang ở trạng thái draft.');

  const assets = Array.isArray(body.assets) ? body.assets : [];
  const asset = assets.find(item => {
    const name = String(item && item.name || '');
    return /win-x64/i.test(name) && /\.zip$/i.test(name);
  });
  if (!asset) throw new Error('Release mới nhất chưa có gói Windows x64 ZIP.');

  const size = Math.max(0, Number(asset.size || 0));
  const digest = String(asset.digest || '').trim();
  if (!size || size > RELEASE_MIRROR_V1411.MAX_RELEASE_BYTES) throw new Error('Kích thước release không hợp lệ hoặc vượt giới hạn mirror.');
  if (!/^sha256:[0-9a-f]{64}$/i.test(digest)) throw new Error('GitHub Release chưa có SHA-256 hợp lệ; không tạo mirror để tránh cập nhật không kiểm chứng.');

  return {
    tag: String(body.tag_name || '').trim(),
    html_url: String(body.html_url || '').trim(),
    notes: String(body.body || ''),
    asset_name: String(asset.name || '').trim(),
    asset_url: String(asset.browser_download_url || '').trim(),
    digest: digest,
    size: size
  };
}

function getReleaseMirrorFolderV1411_() {
  const root = DriveApp.getFolderById(RELEASE_MIRROR_V1411.ROOT_FOLDER_ID);
  if (root.getId() !== CFG.ROOT_FOLDER_ID) throw new Error('Drive root không khớp phạm vi PICKFACE DAMAGE 1291.');
  const folders = root.getFoldersByName(RELEASE_MIRROR_V1411.FOLDER_NAME);
  if (folders.hasNext()) return folders.next();
  return root.createFolder(RELEASE_MIRROR_V1411.FOLDER_NAME);
}

function findReleaseMirrorV1411_(release, createFolder) {
  const root = DriveApp.getFolderById(RELEASE_MIRROR_V1411.ROOT_FOLDER_ID);
  let folder = null;
  const folders = root.getFoldersByName(RELEASE_MIRROR_V1411.FOLDER_NAME);
  if (folders.hasNext()) folder = folders.next();
  else if (createFolder) folder = root.createFolder(RELEASE_MIRROR_V1411.FOLDER_NAME);
  if (!folder) return null;

  const files = folder.getFilesByName(release.asset_name);
  while (files.hasNext()) {
    const file = files.next();
    if (Number(file.getSize()) !== Number(release.size)) continue;
    return { file_id: file.getId(), name: file.getName(), size: file.getSize() };
  }
  return null;
}

function uploadReleaseToDriveV1411_(release, folderId) {
  const token = ScriptApp.getOAuthToken();
  const init = UrlFetchApp.fetch('https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable&supportsAllDrives=true', {
    method: 'post',
    contentType: 'application/json; charset=UTF-8',
    headers: {
      Authorization: 'Bearer ' + token,
      'X-Upload-Content-Type': 'application/zip',
      'X-Upload-Content-Length': String(release.size)
    },
    payload: JSON.stringify({
      name: release.asset_name,
      parents: [folderId],
      description: 'PICKFACE DAMAGE 1291 release mirror ' + release.tag + ' | ' + release.digest
    }),
    muteHttpExceptions: true
  });
  if (init.getResponseCode() !== 200) throw new Error('Không khởi tạo được Drive resumable upload (HTTP ' + init.getResponseCode() + ').');
  const headers = init.getAllHeaders();
  const sessionUrl = String(headers.Location || headers.location || '');
  if (!sessionUrl) throw new Error('Drive không trả resumable upload URL.');

  let finalMeta = null;
  for (let start = 0; start < release.size; start += RELEASE_MIRROR_V1411.CHUNK_SIZE) {
    const end = Math.min(release.size - 1, start + RELEASE_MIRROR_V1411.CHUNK_SIZE - 1);
    const source = UrlFetchApp.fetch(release.asset_url, {
      method: 'get',
      headers: {
        Range: 'bytes=' + start + '-' + end,
        'User-Agent': 'PickfaceDamage1291-GoogleGateway'
      },
      followRedirects: true,
      muteHttpExceptions: true
    });
    const sourceCode = source.getResponseCode();
    if (sourceCode !== 206 && !(sourceCode === 200 && start === 0 && release.size <= RELEASE_MIRROR_V1411.CHUNK_SIZE))
      throw new Error('Không tải được release chunk từ GitHub (HTTP ' + sourceCode + ', bytes ' + start + '-' + end + ').');
    const bytes = source.getBlob().getBytes();
    const expected = end - start + 1;
    if (bytes.length !== expected) throw new Error('GitHub trả chunk sai kích thước (' + bytes.length + '/' + expected + ').');

    const upload = UrlFetchApp.fetch(sessionUrl, {
      method: 'put',
      contentType: 'application/zip',
      headers: { 'Content-Range': 'bytes ' + start + '-' + end + '/' + release.size },
      payload: bytes,
      muteHttpExceptions: true
    });
    const uploadCode = upload.getResponseCode();
    const last = end + 1 >= release.size;
    if (!last && uploadCode !== 308) throw new Error('Drive từ chối upload chunk (HTTP ' + uploadCode + ').');
    if (last) {
      if (uploadCode !== 200 && uploadCode !== 201) throw new Error('Drive không hoàn tất release mirror (HTTP ' + uploadCode + ').');
      finalMeta = JSON.parse(upload.getContentText() || '{}');
    }
  }

  const fileId = String(finalMeta && finalMeta.id || '');
  if (!fileId) throw new Error('Drive không trả file ID sau khi mirror release.');
  const metaResponse = UrlFetchApp.fetch(
    'https://www.googleapis.com/drive/v3/files/' + encodeURIComponent(fileId) + '?fields=id,name,size,parents&supportsAllDrives=true',
    { method: 'get', headers: { Authorization: 'Bearer ' + token }, muteHttpExceptions: true }
  );
  if (metaResponse.getResponseCode() !== 200) throw new Error('Không xác minh được file mirror vừa tạo.');
  const meta = JSON.parse(metaResponse.getContentText() || '{}');
  const parents = Array.isArray(meta.parents) ? meta.parents.map(String) : [];
  if (parents.indexOf(folderId) < 0 || Number(meta.size || 0) !== Number(release.size))
    throw new Error('File mirror không nằm đúng thư mục hoặc sai kích thước.');
  return { file_id: fileId, name: String(meta.name || release.asset_name), size: Number(meta.size || release.size) };
}

function ensureMirrorManifestFileV1411_(release, mirror) {
  const folder = getReleaseMirrorFolderV1411_();
  const name = 'manifest-' + release.tag.replace(/[^A-Za-z0-9._-]/g, '_') + '.json';
  const body = JSON.stringify({
    repository: RELEASE_MIRROR_V1411.REPOSITORY,
    tag: release.tag,
    asset_name: release.asset_name,
    digest: release.digest,
    size: release.size,
    drive_file_id: String(mirror.file_id || mirror.mirror_file_id || ''),
    mirrored_at: new Date().toISOString()
  }, null, 2);
  const files = folder.getFilesByName(name);
  if (files.hasNext()) files.next().setContent(body);
  else folder.createFile(name, body, MimeType.PLAIN_TEXT);
}

function sha256HexV1411_(bytes) {
  const digest = Utilities.computeDigest(Utilities.DigestAlgorithm.SHA_256, bytes);
  return digest.map(b => ('0' + ((b + 256) % 256).toString(16)).slice(-2)).join('');
}
