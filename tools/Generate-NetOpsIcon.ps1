# Rebuild the NetOps Terminal icon from the approved reference-inspired vector artwork.
Add-Type -AssemblyName System.Drawing
$repoRoot = Split-Path -Parent $PSScriptRoot
$assetDirectory = Join-Path $repoRoot 'src\NetOpsTerminal.App\Assets'
$frameDirectory = Join-Path $repoRoot 'artifacts\reference-icon-frames'
New-Item -ItemType Directory -Force -Path $frameDirectory | Out-Null

function IconColor([string] $hex) {
    [System.Drawing.ColorTranslator]::FromHtml($hex)
}

function DrawCircle([System.Drawing.Graphics] $surface, [float] $cx, [float] $cy,
                    [float] $radius, [float] $strokeWidth,
                    [string] $fillColor, [string] $strokeColor) {
    $fill = [System.Drawing.SolidBrush]::new((IconColor $fillColor))
    $outline = [System.Drawing.Pen]::new((IconColor $strokeColor), $strokeWidth)
    $surface.FillEllipse($fill, $cx - $radius, $cy - $radius, 2 * $radius, 2 * $radius)
    $surface.DrawEllipse($outline, $cx - $radius, $cy - $radius, 2 * $radius, 2 * $radius)
    $outline.Dispose()
    $fill.Dispose()
}

function DrawArtwork([System.Drawing.Graphics] $surface) {
    $tilePath = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $tilePath.AddArc(8, 8, 54, 54, 180, 90)
    $tilePath.AddArc(66, 8, 54, 54, 270, 90)
    $tilePath.AddArc(66, 66, 54, 54, 0, 90)
    $tilePath.AddArc(8, 66, 54, 54, 90, 90)
    $tilePath.CloseFigure()
    $tileBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.Rectangle]::new(8, 8, 112, 112),
        (IconColor '#223247'), (IconColor '#0B1523'),
        [System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
    $surface.FillPath($tileBrush, $tilePath)

    $topPen = [System.Drawing.Pen]::new((IconColor '#218FDB'), 4.0)
    $topPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $topPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $surface.DrawLine($topPen, 43, 43, 88, 43)

    $stemBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.Rectangle]::new(25, 38, 8, 43),
        (IconColor '#30C8F1'), (IconColor '#1879DC'),
        [System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
    $stemPen = [System.Drawing.Pen]::new($stemBrush, 8.0)
    $stemPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $stemPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $surface.DrawLine($stemPen, 29, 38, 29, 80)

    $ribbonBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.Rectangle]::new(26, 28, 74, 65),
        (IconColor '#38D6F1'), (IconColor '#1769CE'),
        [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
    $ribbonPen = [System.Drawing.Pen]::new($ribbonBrush, 19.0)
    $ribbonPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $ribbonPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $surface.DrawLine($ribbonPen, 36, 38, 89, 82)

    $rightPen = [System.Drawing.Pen]::new((IconColor '#26B8EF'), 4.0)
    $rightPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $rightPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $surface.DrawLine($rightPen, 89, 49, 89, 80)

    DrawCircle $surface 89 43 8.5 5.5 '#15283C' '#2BC8F1'
    DrawCircle $surface 29 80 7.5 5.5 '#102238' '#33CFF2'
    DrawCircle $surface 89 80 9.5 6.5 '#102238' '#218DE7'

    $promptPen = [System.Drawing.Pen]::new((IconColor '#F6FAFF'), 5.5)
    $promptPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Square
    $promptPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Square
    $surface.DrawLines($promptPen, [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new(44, 80),
        [System.Drawing.PointF]::new(55, 87),
        [System.Drawing.PointF]::new(44, 94)))
    $surface.DrawLine($promptPen, 60, 95, 71, 95)

    $promptPen.Dispose()
    $rightPen.Dispose()
    $ribbonPen.Dispose()
    $ribbonBrush.Dispose()
    $stemPen.Dispose()
    $stemBrush.Dispose()
    $topPen.Dispose()
    $tileBrush.Dispose()
    $tilePath.Dispose()
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$payloads = @()
foreach ($size in $sizes) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $surface = [System.Drawing.Graphics]::FromImage($bitmap)
    $surface.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $surface.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $surface.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $surface.Clear([System.Drawing.Color]::Transparent)
    $surface.ScaleTransform($size / 128.0, $size / 128.0)
    DrawArtwork $surface
    $framePath = Join-Path $frameDirectory "$size.png"
    $bitmap.Save($framePath, [System.Drawing.Imaging.ImageFormat]::Png)
    $surface.Dispose()
    $bitmap.Dispose()
    $payloads += , [System.IO.File]::ReadAllBytes($framePath)
}

$stream = [System.IO.MemoryStream]::new()
$writer = [System.IO.BinaryWriter]::new($stream)
$writer.Write([uint16] 0)
$writer.Write([uint16] 1)
$writer.Write([uint16] $sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte] $dimension)
    $writer.Write([byte] $dimension)
    $writer.Write([byte] 0)
    $writer.Write([byte] 0)
    $writer.Write([uint16] 1)
    $writer.Write([uint16] 32)
    $writer.Write([uint32] $payloads[$i].Length)
    $writer.Write([uint32] $offset)
    $offset += $payloads[$i].Length
}
foreach ($payload in $payloads) {
    $writer.Write([byte[]] $payload)
}
[System.IO.File]::WriteAllBytes((Join-Path $assetDirectory 'NetOpsTerminal.ico'),
    $stream.ToArray())
$writer.Dispose()
$stream.Dispose()
