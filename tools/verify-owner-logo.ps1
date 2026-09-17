$ErrorActionPreference = 'Stop'

$sourcePath = 'src/PickfaceDamage1291/BbbgOfficialLogoV1425.cs'
$expectedSha256 = 'e7db98ba6e61285462d09f99ba8c44d437b7ad633f9c949cd17e9a3a2dc3e8e2'
$expectedBytes = 12651

if (!(Test-Path $sourcePath)) {
    throw "Không tìm thấy source logo OWNER: $sourcePath"
}

$source = Get-Content $sourcePath -Raw
$match = [regex]::Match($source, '(?s)private const string Base64 = """\s*(.*?)\s*""";')
if (!$match.Success) {
    throw 'Không đọc được Base64 logo OWNER từ source.'
}

$base64 = [regex]::Replace($match.Groups[1].Value, '\s', '')
try {
    $bytes = [Convert]::FromBase64String($base64)
} catch {
    throw "Base64 logo OWNER không hợp lệ: $($_.Exception.Message)"
}

if ($bytes.Length -ne $expectedBytes) {
    throw "Logo OWNER sai kích thước byte. Expected=$expectedBytes Actual=$($bytes.Length)"
}

$sha = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
if ($sha -ne $expectedSha256) {
    throw "Logo OWNER sai SHA256. Expected=$expectedSha256 Actual=$sha"
}

$pngSignature = '89504e470d0a1a0a'
$actualSignature = [Convert]::ToHexString($bytes[0..7]).ToLowerInvariant()
if ($actualSignature -ne $pngSignature) {
    throw "Logo OWNER không phải PNG hợp lệ. Signature=$actualSignature"
}

Write-Host "OWNER logo probe PASS"
Write-Host "Bytes: $($bytes.Length)"
Write-Host "SHA256: $sha"
