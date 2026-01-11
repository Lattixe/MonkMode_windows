# Create a minimalist Monk Mode icon using proper ICO format
Add-Type -AssemblyName System.Drawing

$iconPath = Join-Path $PSScriptRoot "Resources\app.ico"
$resourceDir = Join-Path $PSScriptRoot "Resources"

# Create Resources directory
if (-not (Test-Path $resourceDir)) {
    New-Item -ItemType Directory -Path $resourceDir -Force | Out-Null
}

# Create a 256x256 bitmap
$size = 256
$bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)

# High quality rendering
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

# Background - dark rounded square
$bgColor = [System.Drawing.Color]::FromArgb(255, 12, 12, 13)
$bgBrush = New-Object System.Drawing.SolidBrush($bgColor)
$g.FillRectangle($bgBrush, 0, 0, $size, $size)

# Draw zen circle (ensō) - incomplete circle
$penWidth = 18
$pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 250, 250, 250), $penWidth)
$pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

$margin = 45
$circleSize = $size - ($margin * 2)

# Draw arc (incomplete circle - zen style)
$g.DrawArc($pen, $margin, $margin, $circleSize, $circleSize, -70, 310)

# Add a small dot in center (focus point) - blue accent
$dotSize = 24
$dotBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 59, 130, 246))
$dotX = ($size - $dotSize) / 2
$g.FillEllipse($dotBrush, $dotX, $dotX, $dotSize, $dotSize)

$g.Dispose()

# Convert to icon using System.Drawing.Icon
$iconHandle = $bmp.GetHicon()
$icon = [System.Drawing.Icon]::FromHandle($iconHandle)

# Save as ICO
$fs = [System.IO.File]::Create($iconPath)
$icon.Save($fs)
$fs.Close()
$fs.Dispose()

# Cleanup
$icon.Dispose()
$bmp.Dispose()

Write-Host "Icon created: $iconPath" -ForegroundColor Green
