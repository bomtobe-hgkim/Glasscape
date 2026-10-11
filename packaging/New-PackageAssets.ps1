#Requires -Version 5.1
[CmdletBinding()]
param([string]$Destination = (Join-Path $PSScriptRoot 'Assets'))
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$source = [Drawing.Image]::FromFile((Join-Path $PSScriptRoot '..\assets\app-icon.png'))
$assets = [ordered]@{ 'StoreLogo.png' = 50; 'Square150x150Logo.png' = 150; 'Square44x44Logo.png' = 44 }
foreach ($size in @(16, 24, 32, 44, 48, 256)) { $assets["Square44x44Logo.targetsize-${size}_altform-unplated.png"] = $size }
try {
    foreach ($name in $assets.Keys) {
        $size = $assets[$name]
        $bitmap = New-Object Drawing.Bitmap($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.DrawImage($source, (New-Object Drawing.Rectangle(0, 0, $size, $size)))
            $bitmap.Save((Join-Path $Destination $name), [Drawing.Imaging.ImageFormat]::Png)
        }
        finally { $graphics.Dispose(); $bitmap.Dispose() }
    }
}
finally { $source.Dispose() }
