param(
  [string]$OutFile = "src/PickfaceDamage1291/Assets/PickfaceDamage1291.ico"
)

Add-Type -AssemblyName System.Drawing

$dir = Split-Path -Parent $OutFile
if ($dir) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }

$base = New-Object System.Drawing.Bitmap(256, 256, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($base)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)

# Blue field + white pickface rack + amber damage bolt. No text so it stays clear at 16/32 px.
$blue = [System.Drawing.Color]::FromArgb(255, 24, 84, 155)
$white = [System.Drawing.Color]::White
$amber = [System.Drawing.Color]::FromArgb(255, 246, 166, 35)
$brush = New-Object System.Drawing.SolidBrush($blue)
$g.FillEllipse($brush, 10, 10, 236, 236)
$brush.Dispose()

$rack = New-Object System.Drawing.Pen($white, 16)
$rack.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$rack.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$g.DrawLine($rack, 62, 60, 62, 194)
$g.DrawLine($rack, 176, 60, 176, 194)
$g.DrawLine($rack, 62, 72, 176, 72)
$g.DrawLine($rack, 62, 126, 176, 126)
$g.DrawLine($rack, 62, 180, 176, 180)

$bolt = New-Object System.Drawing.Pen($amber, 18)
$bolt.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$bolt.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$points = @(
  (New-Object System.Drawing.Point(198, 76)),
  (New-Object System.Drawing.Point(160, 116)),
  (New-Object System.Drawing.Point(194, 116)),
  (New-Object System.Drawing.Point(151, 177))
)
$g.DrawLines($bolt, $points)
$g.Dispose()
$rack.Dispose()
$bolt.Dispose()

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = @()
foreach ($size in $sizes) {
  $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $gr = [System.Drawing.Graphics]::FromImage($bmp)
  $gr.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $gr.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
  $gr.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $gr.DrawImage($base, 0, 0, $size, $size)
  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  $frames += ,@($size, $ms.ToArray())
  $ms.Dispose()
  $gr.Dispose()
  $bmp.Dispose()
}
$base.Dispose()

$fs = [System.IO.File]::Open($OutFile, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]$frames.Count)
$offset = 6 + (16 * $frames.Count)
foreach ($frame in $frames) {
  $size = [int]$frame[0]
  $bytes = [byte[]]$frame[1]
  $bw.Write([byte]($(if ($size -ge 256) { 0 } else { $size })))
  $bw.Write([byte]($(if ($size -ge 256) { 0 } else { $size })))
  $bw.Write([byte]0)
  $bw.Write([byte]0)
  $bw.Write([UInt16]1)
  $bw.Write([UInt16]32)
  $bw.Write([UInt32]$bytes.Length)
  $bw.Write([UInt32]$offset)
  $offset += $bytes.Length
}
foreach ($frame in $frames) { $bw.Write([byte[]]$frame[1]) }
$bw.Flush()
$bw.Dispose()
$fs.Dispose()

Write-Host "Generated $OutFile"
