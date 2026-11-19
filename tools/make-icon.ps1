# Generates assets\neverforget.ico (16, 24, 32, 48, 64, 256 px) by drawing a simple sticky note.
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot '..\assets\neverforget.ico'
$sizes = 16, 24, 32, 48, 64, 256

function Draw-Note([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $pad = [Math]::Max(1, [int]($s * 0.08))
    $w = $s - 2 * $pad
    $fold = [int]($w * 0.30)
    $r = [Math]::Max(2, [int]($w * 0.14))
    $x = $pad; $y = $pad

    # Note body with a folded bottom-right corner
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc($x, $y, 2 * $r, 2 * $r, 180, 90)
    $p.AddArc($x + $w - 2 * $r, $y, 2 * $r, 2 * $r, 270, 90)
    $p.AddLine($x + $w, $y + $r, $x + $w, $y + $w - $fold)
    $p.AddLine($x + $w, $y + $w - $fold, $x + $w - $fold, $y + $w)
    $p.AddArc($x, $y + $w - 2 * $r, 2 * $r, 2 * $r, 90, 90)
    $p.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 242, 196, 73))), $p)

    # Fold
    $f = New-Object System.Drawing.Drawing2D.GraphicsPath
    $f.AddPolygon(@(
        (New-Object System.Drawing.PointF ($x + $w), ($y + $w - $fold)),
        (New-Object System.Drawing.PointF ($x + $w - $fold), ($y + $w - $fold)),
        (New-Object System.Drawing.PointF ($x + $w - $fold), ($y + $w))))
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 196, 146, 38))), $f)

    # Text lines
    if ($s -ge 24) {
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(200, 92, 64, 14)), ([Math]::Max(1.5, $s * 0.06))
        $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
        $lx = $x + $w * 0.2
        $g.DrawLine($pen, $lx, $y + $w * 0.32, $x + $w * 0.78, $y + $w * 0.32)
        $g.DrawLine($pen, $lx, $y + $w * 0.52, $x + $w * 0.62, $y + $w * 0.52)
    }
    $g.Dispose()
    return $bmp
}

$frames = foreach ($s in $sizes) {
    $bmp = Draw-Note $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}

$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $frames[$i].Length
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$len); $bw.Write([uint32]$offset)
    $offset += $len
}
foreach ($f in $frames) { $bw.Write($f) }
$bw.Close()
Write-Output "Icon written: $out"
