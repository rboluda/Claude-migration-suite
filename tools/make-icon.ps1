# Genera el icono de Migrador (src/Migrador.App/Assets/Migrador.ico y docs/img/icono.png).
# Uso: pwsh tools/make-icon.ps1
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent

function Draw([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    $s = $size / 256.0
    function RoundRect($x, $y, $w, $h, $r) {
        $p = New-Object System.Drawing.Drawing2D.GraphicsPath
        $p.AddArc($x, $y, 2 * $r, 2 * $r, 180, 90); $p.AddArc($x + $w - 2 * $r, $y, 2 * $r, 2 * $r, 270, 90)
        $p.AddArc($x + $w - 2 * $r, $y + $h - 2 * $r, 2 * $r, 2 * $r, 0, 90); $p.AddArc($x, $y + $h - 2 * $r, 2 * $r, 2 * $r, 90, 90)
        $p.CloseFigure(); $p
    }
    # Fondo: cuadrado redondeado azul con degradado.
    $bg = RoundRect (8 * $s) (8 * $s) (240 * $s) (240 * $s) (52 * $s)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new(0, 0)), ([System.Drawing.PointF]::new($size, $size)), ([System.Drawing.Color]::FromArgb(37, 99, 235)), ([System.Drawing.Color]::FromArgb(14, 165, 233))
    $g.FillPath($brush, $bg)
    $white = [System.Drawing.Brushes]::White
    # Caja de archivo: tapa y cuerpo.
    $g.FillPath($white, (RoundRect (52 * $s) (112 * $s) (152 * $s) (30 * $s) (8 * $s)))
    $g.FillPath($white, (RoundRect (64 * $s) (148 * $s) (128 * $s) (66 * $s) (10 * $s)))
    $blue = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(37, 99, 235))
    $g.FillPath($blue, (RoundRect (104 * $s) (164 * $s) (48 * $s) (12 * $s) (6 * $s)))
    # Flecha hacia la derecha, encima de la caja.
    $pen = New-Object System.Drawing.Pen $white, ([Math]::Max(1.5, 18 * $s))
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
    $g.DrawLine($pen, 76 * $s, 74 * $s, 172 * $s, 74 * $s)
    $g.DrawLines($pen, [System.Drawing.PointF[]]@([System.Drawing.PointF]::new(146 * $s, 48 * $s), [System.Drawing.PointF]::new(176 * $s, 74 * $s), [System.Drawing.PointF]::new(146 * $s, 100 * $s)))
    $g.Dispose()
    $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($n in $sizes) { $b = Draw $n; $ms = New-Object System.IO.MemoryStream; $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose(); , $ms.ToArray() }

# ICO con imágenes PNG (admitido desde Windows Vista).
$ico = Join-Path $root 'src/Migrador.App/Assets/Migrador.ico'
New-Item -ItemType Directory -Force (Split-Path $ico) | Out-Null
$fs = [System.IO.File]::Create($ico); $w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $n = $sizes[$i]; $len = $pngs[$i].Length
    $w.Write([byte]($n % 256)); $w.Write([byte]($n % 256)); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$len); $w.Write([uint32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Dispose()

$png = Join-Path $root 'docs/img/icono.png'
New-Item -ItemType Directory -Force (Split-Path $png) | Out-Null
[System.IO.File]::WriteAllBytes($png, $pngs[-1])
"Icono: $ico`nPNG: $png"
