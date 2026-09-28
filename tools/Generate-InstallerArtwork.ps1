[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$assetRoot = Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..')).Path 'installer\NetOpsTerminal.Installer\Assets'
$iconPath = Join-Path $assetRoot 'NetOpsTerminal-256.png'
$icon = [System.Drawing.Image]::FromFile($iconPath)
$navy = [System.Drawing.Color]::FromArgb(11, 23, 40)
$blue = [System.Drawing.Color]::FromArgb(36, 160, 231)
$white = [System.Drawing.Color]::White
try {
    $welcome = [System.Drawing.Bitmap]::new(493, 312)
    $g = [System.Drawing.Graphics]::FromImage($welcome)
    $navyBrush = [System.Drawing.SolidBrush]::new($navy)
    $blueBrush = [System.Drawing.SolidBrush]::new($blue)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.Clear($white)
        # Keep the artwork clear of the stock wizard text area at every DPI scale.
        $g.FillRectangle($navyBrush, 0, 0, 140, 312)
        $g.FillRectangle($blueBrush, 139, 0, 1, 312)
        $g.DrawImage($icon, 24, 101, 92, 92)
        $welcome.Save((Join-Path $assetRoot 'Welcome.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $g.Dispose()
        $navyBrush.Dispose(); $blueBrush.Dispose()
        $welcome.Dispose()
    }

    $banner = [System.Drawing.Bitmap]::new(493, 58)
    $g = [System.Drawing.Graphics]::FromImage($banner)
    $lineBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(196, 217, 232))
    try {
        $g.Clear($white)
        $g.FillRectangle($lineBrush, 0, 57, 493, 1)
        $g.DrawImage($icon, 445, 8, 40, 40)
        $banner.Save((Join-Path $assetRoot 'Banner.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $g.Dispose(); $lineBrush.Dispose(); $banner.Dispose()
    }
}
finally {
    $icon.Dispose()
}


