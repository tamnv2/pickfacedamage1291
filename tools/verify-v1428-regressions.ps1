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

Require-Text "$src/CloudSyncService.cs" 'PullReportsOnlyAsync' 'Missing report-only smart sync path.'
Require-Text "$src/V142Runtime.cs" 'PullReportsOnlyAsync(progress, TimeSpan.FromSeconds(30), ct)' 'Network recovery regressed to a heavier shared-data pull.'
Require-Text "$src/V1416ExcelExport.cs" 'PullReportsOnlyAsync' 'Excel export pre-sync regressed to pulling the SKU catalog.'
Forbid-Text "$src/V1416ExcelExport.cs" 'new XLWorkbook(path)' 'Regression: Excel export reopens the XLSX package after the first save.'
Forbid-Text "$src/V1416ExcelExport.cs" 'wb.Save();' 'Regression: Excel export performs a second workbook save.'
Require-Text "$src/DamageReportExportV148.cs" 'beforeSave?.Invoke(wb, ws, reports, ct);' 'Missing in-memory Excel formatting hook before the single save.'
Require-Text "$src/DamageReportExportV148.cs" 'EXPORT_EXCEL_PERF' 'Missing Excel stage performance telemetry.'

Write-Host 'cumulative regression probe PASS'
