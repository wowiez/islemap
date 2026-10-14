param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\src\TheIsleOverlay.App\Assets\IsleLiveMap.ico'),
    [string]$SourcePath = (Join-Path $PSScriptRoot '..\src\TheIsleOverlay.App\Assets\IsleOfWieIcon.png')
)

# Writes a multi-size ICO whose frames are 32-bit PNGs. Icon.Save() from a
# GetHicon() handle drops to a low colour depth, which posterised the logo.
Add-Type -AssemblyName System.Drawing

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
$resolvedSource = [System.IO.Path]::GetFullPath($SourcePath)
[System.IO.Directory]::CreateDirectory((Split-Path -Parent $resolvedOutput)) | Out-Null

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$source = [System.Drawing.Bitmap]::FromFile($resolvedSource)
$frames = foreach ($size in $sizes) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $graphics.DrawImage($source, 0, 0, $size, $size)
    $graphics.Dispose()
    $png = New-Object System.IO.MemoryStream
    $bitmap.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    [pscustomobject]@{ Size = $size; Bytes = $png.ToArray() }
}
$source.Dispose()

$stream = [System.IO.File]::Create($resolvedOutput)
$writer = New-Object System.IO.BinaryWriter $stream
$writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($frame in $frames) {
    $edge = if ($frame.Size -ge 256) { 0 } else { $frame.Size }
    $writer.Write([byte]$edge); $writer.Write([byte]$edge); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([UInt16]1); $writer.Write([UInt16]32)
    $writer.Write([UInt32]$frame.Bytes.Length); $writer.Write([UInt32]$offset)
    $offset += $frame.Bytes.Length
}
foreach ($frame in $frames) { $writer.Write($frame.Bytes) }
$writer.Dispose()

Write-Output $resolvedOutput
