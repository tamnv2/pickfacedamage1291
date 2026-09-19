$ErrorActionPreference = 'Stop'

function Require-Text([string]$Path, [string]$Needle, [string]$Message) {
    if (!(Test-Path $Path)) { throw "Missing file: $Path" }
    $text = Get-Content $Path -Raw
    if (!$text.Contains($Needle)) { throw $Message }
}

function Forbid-Text([string]$Path, [string]$Needle, [string]$Message) {
    if (!(Test-Path $Path)) { throw "Missing file: $Path" }
    $text = Get-Content $Path -Raw
    if ($text.Contains($Needle)) { throw $Message }
}

function Require-MissingPath([string]$Path, [string]$Message) {
    if (Test-Path $Path) { throw $Message }
}

$src = 'src/PickfaceDamage1291'

Require-Text "$src/GoogleGatewayV140.cs" 'GATEWAY_NON_JSON_RESPONSE' 'Missing HTML/non-JSON gateway guard.'
Require-Text "$src/GoogleGatewayV140.cs" 'GATEWAY_TRANSIENT_RETRY' 'Missing transient gateway retry.'
Require-Text "$src/GoogleGatewayV140.cs" 'GoogleGatewayResilienceV1428.TransportGate' 'Missing bounded gateway concurrency.'
Require-Text "$src/GoogleGatewayResilienceV1428.cs" 'code is 404 or 408 or 425 or 429 || code >= 500' 'Transient HTTP policy no longer covers intermittent 404/429/5xx.'
Require-Text "$src/UsernameAuthService.cs" 'LooksLikeHtmlOrInvalidEnvelope' 'Username login no longer handles HTML gateway responses.'

Require-Text "$src/V1428Runtime.cs" 'WmSetRedraw' 'Missing tab redraw freeze used to stop top-down repaint.'
Require-Text "$src/V1428Runtime.cs" 'DoubleBufferedProperty' 'Missing UI double buffering.'
Require-Text "$src/Program.cs" 'V1428Runtime.Apply(main);' 'Smooth-tab runtime is not applied.'

Require-Text "$src/RuntimeConfigService.cs" 'StartBackgroundRefresh' 'Startup network metadata/probe is no longer backgrounded.'
Require-Text "$src/SessionBootstrap.cs" 'profile_reused' 'Login bootstrap no longer avoids the duplicate profile read.'
Require-Text "$src/Program.cs" 'DurableAuditQueue.Enqueue(session, "LOGOUT"' 'Logout audit is no longer queued locally.'
Forbid-Text "$src/Program.cs" 'FirebaseClient.AppendAuditAsync(session, "LOGOUT"' 'Regression: logout blocks on remote audit again.'

Require-Text "$src/VersionUpdateServiceV2.cs" 'DirectGitHubAvailable' 'Updater no longer distinguishes GitHub-download availability from metadata-only fallback.'
Require-Text "$src/VersionUpdateServiceV2.cs" 'release_metadata' 'Updater no longer uses the lightweight metadata-only Google Gateway fallback.'
Require-Text "$src/VersionUpdateServiceV2.cs" 'Ứng dụng không tải bản cập nhật qua Google Drive' 'Updater warning no longer states that Google Drive download fallback is disabled.'
Forbid-Text "$src/VersionUpdateServiceV2.cs" 'release_mirror_chunk' 'Regression: updater can download release chunks through Google Drive again.'
Forbid-Text "$src/VersionUpdateServiceV2.cs" 'DownloadFromGoogleMirrorAsync' 'Regression: Google Drive release download fallback returned.'
Require-Text "$src/MainFormV3.cs" 'Mạng hiện tại vẫn kiểm tra được phiên bản qua Google Gateway' 'Office-network update notice is missing.'
Require-Text "apps-script/GoogleGateway/HardDeleteV1410.gs" "release_metadata" 'Gateway no longer exposes metadata-only update detection.'
Require-Text "apps-script/GoogleGateway/ReleaseMetadataV1432.gs" "drive_mirror: false" 'Metadata proxy no longer explicitly disables Drive mirroring.'
Require-MissingPath "apps-script/GoogleGateway/ReleaseMirrorV1411.gs" 'Regression: obsolete Google Drive release mirror implementation exists again.'
Require-MissingPath ".github/workflows/warm-release-mirror.yml" 'Regression: automatic Google Drive release mirror workflow exists again.'

Require-Text "$src/CloudSyncService.cs" 'PullReportsOnlyAsync' 'Missing report-only smart sync path.'
Require-Text "$src/V142Runtime.cs" 'PullReportsOnlyAsync(progress, TimeSpan.FromSeconds(30), ct)' 'Network recovery regressed to a heavier shared-data pull.'
Require-Text "$src/V1416ExcelExport.cs" 'PullReportsOnlyAsync' 'Excel export pre-sync regressed to pulling the SKU catalog.'
Forbid-Text "$src/V1416ExcelExport.cs" 'new XLWorkbook(path)' 'Regression: Excel export reopens the XLSX package after the first save.'
Forbid-Text "$src/V1416ExcelExport.cs" 'wb.Save();' 'Regression: Excel export performs a second workbook save.'
Require-Text "$src/DamageReportExportV148.cs" 'beforeSave?.Invoke(wb, ws, reports, ct);' 'Missing in-memory Excel formatting hook before the single save.'
Require-Text "$src/DamageReportExportV148.cs" 'EXPORT_EXCEL_PERF' 'Missing Excel stage performance telemetry.'

Require-Text "$src/BbbgInventoryWordExporterV1419.cs" 'Hôm nay, ngày {entryDates[0]:dd/MM/yyyy}, vào lúc' 'BBBG no longer fills the actual entry date sentence.'
Require-Text "$src/V1416ExcelExport.cs" 'shift_includes_entry_date' 'Multi-day information export no longer marks entry dates in the shift column.'
Require-Text "$src/MainFormV3.cs" 'Thiết kế và phát triển bởi: tamnv2 - Chuyên viên Pick Pack 1291' 'Developer credit is missing from the main header.'
Require-Text "$src/MainFormV3.cs" 'WindowState == FormWindowState.Maximized' 'Developer credit no longer hides outside full-screen mode.'

Require-Text "$src/AppLog.cs" 'LocalRetention = TimeSpan.FromDays(7)' 'Local log retention is no longer seven days.'
Require-Text "$src/AppLog.cs" 'MaxFileBytes = 2L * 1024 * 1024' 'Log rotation size changed from the approved 2 MB threshold.'
Require-Text "$src/AppLog.cs" 'crash_' 'Crash logs no longer receive the crash_ prefix.'
Require-Text "$src/AppLog.cs" 'machine_name' 'Log records no longer include the machine name.'
Require-Text "$src/AppLog.cs" 'log_upload_state.json' 'Automatic log upload deduplication state is missing.'
Forbid-Text "$src/AppLog.cs" 'LOG_UPLOAD_LOCAL_RESET' 'Regression: successful log upload deletes/resets local history.'
Forbid-Text "$src/AppLog.cs" 'ResetForVersionIfNeededLocked' 'Regression: version update deletes local logs again.'
Require-Text "$src/Program.cs" 'AppLog.BindSession(active.Profile.Username);' 'Log filenames are no longer bound to the signed-in username.'
Require-Text "$src/Program.cs" 'AppLog.CaptureCrash' 'Unhandled exceptions no longer create crash logs.'
Require-Text "$src/V142Runtime.cs" 'AppLog.TriggerAutoUpload();' 'Network recovery no longer retries retained log uploads.'
Require-Text "$src/UiRuntimeFixes.cs" 'AppLog.TriggerAutoUpload();' 'Google reconnect no longer retries retained log uploads.'

Require-Text "apps-script/GoogleGateway/Code.gs" "folder.getFilesByName(name).hasNext()" 'Drive log upload no longer resolves duplicate names safely.'
Require-Text "apps-script/GoogleGateway/Code.gs" "original=sanitizeFilePart_" 'Drive log upload no longer preserves the client diagnostic filename.'

Require-Text "$src/V1419Runtime.cs" 'PullReportsOnlyAsync' 'Information export regressed to a full SKU/report pull.'
Require-Text "$src/V1420Runtime.cs" 'PullReportsOnlyAsync' 'BBBG v1420 regressed to a full SKU/report pull.'
Require-Text "$src/V1427Runtime.cs" 'PullReportsOnlyAsync' 'BBBG v1427 regressed to a full SKU/report pull.'

Write-Host 'cumulative regression probe PASS'
