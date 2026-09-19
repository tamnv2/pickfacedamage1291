const RELEASE_METADATA_V1432 = Object.freeze({
  REPOSITORY: 'tamnv2/pickfacedamage1291',
  MAX_RELEASE_BYTES: 300 * 1024 * 1024
});

function releaseMetadataV1432_() {
  const url = 'https://api.github.com/repos/' + RELEASE_METADATA_V1432.REPOSITORY + '/releases/latest';
  const response = UrlFetchApp.fetch(url, {
    method: 'get',
    headers: {
      Accept: 'application/vnd.github+json',
      'User-Agent': 'PickfaceDamage1291-GoogleGateway-Metadata'
    },
    muteHttpExceptions: true
  });

  const code = response.getResponseCode();
  if (code !== 200)
    throw new Error('Không đọc được GitHub Release từ Google Gateway (HTTP ' + code + ').');

  const body = JSON.parse(response.getContentText() || '{}');
  if (body.draft === true)
    throw new Error('Release mới nhất đang ở trạng thái draft.');

  const assets = Array.isArray(body.assets) ? body.assets : [];
  const asset = assets.find(item => {
    const name = String(item && item.name || '');
    return /win-x64/i.test(name) && /\.zip$/i.test(name);
  });
  if (!asset)
    throw new Error('Release mới nhất chưa có gói Windows x64 ZIP.');

  const size = Math.max(0, Number(asset.size || 0));
  if (!size || size > RELEASE_METADATA_V1432.MAX_RELEASE_BYTES)
    throw new Error('Kích thước release không hợp lệ.');

  const digest = String(asset.digest || '').trim();
  return {
    tag: String(body.tag_name || '').trim(),
    html_url: String(body.html_url || '').trim(),
    notes: String(body.body || ''),
    asset_name: String(asset.name || '').trim(),
    asset_url: String(asset.browser_download_url || '').trim(),
    digest: /^sha256:[0-9a-f]{64}$/i.test(digest) ? digest : '',
    size: size,
    download_transport: 'github_only',
    metadata_transport: 'google_gateway',
    drive_mirror: false
  };
}
