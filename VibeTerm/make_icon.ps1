Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = "Stop"

function New-VibetermBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode        = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint    = [System.Drawing.Text.TextRenderingHint]::AntiAlias
    $g.InterpolationMode    = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)

    # --- rounded tile path ---
    $r    = [int]($size * 0.19)
    $d    = $r * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0,          0,          $d, $d, 180, 90)
    $path.AddArc($size - $d, 0,          $d, $d, 270, 90)
    $path.AddArc($size - $d, $size - $d, $d, $d,   0, 90)
    $path.AddArc(0,          $size - $d, $d, $d,  90, 90)
    $path.CloseAllFigures()

    # --- base: deep terminal glass, not pure black ---
    $bgRect = New-Object System.Drawing.RectangleF -ArgumentList @([float]0, [float]0, [float]$size, [float]$size)
    $bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush -ArgumentList @(
        $bgRect,
        [System.Drawing.Color]::FromArgb(255, 15, 18, 34),
        [System.Drawing.Color]::FromArgb(255, 4, 10, 20),
        [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal
    )
    $g.FillPath($bgBrush, $path)

    # --- saturated side lighting ---
    $cyan = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(0, 0)),
        (New-Object System.Drawing.PointF($size, $size)),
        [System.Drawing.Color]::FromArgb(120, 0, 225, 255),
        [System.Drawing.Color]::FromArgb(0, 0, 225, 255)
    )
    $g.FillPath($cyan, $path)

    $violet = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF($size, 0)),
        (New-Object System.Drawing.PointF(0, $size)),
        [System.Drawing.Color]::FromArgb(115, 168, 80, 255),
        [System.Drawing.Color]::FromArgb(0, 168, 80, 255)
    )
    $g.FillPath($violet, $path)

    # --- inner panel for terminal depth ---
    $pad = [float]($size * 0.145)
    $innerSize = [float]($size - (2 * $pad))
    $inner = New-Object System.Drawing.RectangleF -ArgumentList @($pad, $pad, $innerSize, $innerSize)
    $ir = [int]($size * 0.105)
    $id = $ir * 2
    $panel = New-Object System.Drawing.Drawing2D.GraphicsPath
    $panel.AddArc($inner.X, $inner.Y, $id, $id, 180, 90)
    $panel.AddArc($inner.Right - $id, $inner.Y, $id, $id, 270, 90)
    $panel.AddArc($inner.Right - $id, $inner.Bottom - $id, $id, $id, 0, 90)
    $panel.AddArc($inner.X, $inner.Bottom - $id, $id, $id, 90, 90)
    $panel.CloseAllFigures()
    $panelBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(155, 7, 13, 25))
    $g.FillPath($panelBrush, $panel)

    # --- glass highlight ---
    $highlightEnd = New-Object System.Drawing.PointF -ArgumentList @([float]0, [float]($size * 0.48))
    $highlight = New-Object System.Drawing.Drawing2D.LinearGradientBrush -ArgumentList @(
        (New-Object System.Drawing.PointF(0, 0)),
        $highlightEnd,
        [System.Drawing.Color]::FromArgb(70, 255, 255, 255),
        [System.Drawing.Color]::FromArgb(0, 255, 255, 255)
    )
    $g.FillPath($highlight, $path)

    # --- crisp outer and inner rims ---
    $bw  = [float]([math]::Max(1.35, $size * 0.027))
    $border = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $bgRect,
        [System.Drawing.Color]::FromArgb(245, 52, 238, 255),
        [System.Drawing.Color]::FromArgb(220, 178, 92, 255),
        [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal
    )
    $pen = New-Object System.Drawing.Pen -ArgumentList @($border, $bw)
    $g.DrawPath($pen, $path)

    $innerPen = New-Object System.Drawing.Pen -ArgumentList @([System.Drawing.Color]::FromArgb(80, 255, 255, 255), [float]([math]::Max(1, $size * 0.010)))
    $g.DrawPath($innerPen, $panel)

    # --- terminal mark ---
    $g.SetClip($path)
    $markPenWidth = [float]([math]::Max(2.2, $size * 0.082))
    if ($size -le 24) { $markPenWidth = [float]([math]::Max(1.8, $size * 0.105)) }

    $glowWidth = [float]($markPenWidth * 2.25)
    $glowPen = New-Object System.Drawing.Pen -ArgumentList @([System.Drawing.Color]::FromArgb(72, 31, 218, 255), $glowWidth)
    $glowPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $glowPen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    $glowPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    $markPen = New-Object System.Drawing.Pen -ArgumentList @([System.Drawing.Color]::FromArgb(255, 138, 246, 255), $markPenWidth)
    $markPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $markPen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    $markPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    $x1 = [float]($size * 0.325)
    $x2 = [float]($size * 0.505)
    $y1 = [float]($size * 0.365)
    $ym = [float]($size * 0.500)
    $y2 = [float]($size * 0.635)

    if ($size -ge 24) {
        $g.DrawLine($glowPen, $x1, $y1, $x2, $ym)
        $g.DrawLine($glowPen, $x2, $ym, $x1, $y2)
    }
    $g.DrawLine($markPen, $x1, $y1, $x2, $ym)
    $g.DrawLine($markPen, $x2, $ym, $x1, $y2)

    if ($size -ge 24) {
        $cursorX = [float]($size * 0.560)
        $cursorY = [float]($size * 0.625)
        $cursorW = [float]($size * 0.230)
        $cursorH = [float]([math]::Max(2.3, $size * 0.078))
        $cursor = New-Object System.Drawing.RectangleF -ArgumentList @($cursorX, $cursorY, $cursorW, $cursorH)
        $cursorGlow = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(95, 178, 102, 255))
        $cursorBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 216, 160, 255))
        $g.FillRectangle($cursorGlow, $cursorX - $size * 0.018, $cursorY - $size * 0.018, $cursorW + $size * 0.036, $cursorH + $size * 0.036)
        $g.FillRectangle($cursorBrush, $cursor)

        $sparkPen = New-Object System.Drawing.Pen -ArgumentList @([System.Drawing.Color]::FromArgb(170, 80, 255, 182), [float]([math]::Max(1, $size * 0.014)))
        $sparkPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $sparkPen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
        $g.DrawLine($sparkPen, [float]($size * 0.590), [float]($size * 0.335), [float]($size * 0.760), [float]($size * 0.335))
    }

    $g.ResetClip()
    $g.Dispose()
    return $bmp
}

function Get-PngBytes([System.Drawing.Bitmap]$bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return ,$ms.ToArray()
}

$sizes  = @(16, 32, 48, 64, 128, 256)
$images = @{}
foreach ($s in $sizes) {
    $b = New-VibetermBitmap $s
    $images[$s] = Get-PngBytes $b
    $b.Dispose()
    Write-Host "  rendered ${s}x${s}"
}

# --- write ICO (PNG-in-ICO format) ---
$outPath = Join-Path $PSScriptRoot "app.ico"
$stream  = [System.IO.File]::Create($outPath)
$w       = New-Object System.IO.BinaryWriter($stream)

$w.Write([uint16]0)                   # reserved
$w.Write([uint16]1)                   # type = ICO
$w.Write([uint16]$sizes.Count)        # image count

$offset = 6 + 16 * $sizes.Count
foreach ($s in $sizes) {
    $dim = if ($s -eq 256) { 0 } else { $s }
    $w.Write([byte]$dim)
    $w.Write([byte]$dim)
    $w.Write([byte]0)
    $w.Write([byte]0)
    $w.Write([uint16]1)
    $w.Write([uint16]32)
    $w.Write([uint32]$images[$s].Length)
    $w.Write([uint32]$offset)
    $offset += $images[$s].Length
}
foreach ($s in $sizes) { $w.Write($images[$s]) }
$w.Flush(); $w.Dispose()

Write-Host ""
Write-Host "Icon saved: $outPath  ($([math]::Round((Get-Item $outPath).Length/1KB,1)) KB)"
