$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assets = Join-Path (Split-Path $PSScriptRoot -Parent) 'src\ConversorAulas.App\Assets'
New-Item -ItemType Directory -Path $assets -Force | Out-Null
$fontName = 'Segoe Fluent Icons'
$availableFonts = [System.Drawing.Text.InstalledFontCollection]::new()
if ($fontName -notin $availableFonts.Families.Name) { $fontName = 'Segoe MDL2 Assets' }
$font = [System.Drawing.FontFamily]::new($fontName)
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = [System.Collections.Generic.List[byte[]]]::new()
foreach ($size in $sizes) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $background = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $padding = [single]($size * .025)
    $diameter = [single]($size * .38)
    $edge = [single]($size - $padding - $diameter)
    $background.AddArc($padding, $padding, $diameter, $diameter, 180, 90)
    $background.AddArc($edge, $padding, $diameter, $diameter, 270, 90)
    $background.AddArc($edge, $edge, $diameter, $diameter, 0, 90)
    $background.AddArc($padding, $edge, $diameter, $diameter, 90, 90)
    $background.CloseFigure()
    $brush = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#5B5BD6'))
    $graphics.FillPath($brush, $background)
    # Windows Audio glyph U+E8D6. Only its rendered outline is embedded, not the font.
    $glyph = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $glyph.AddString([string][char]0xE8D6, $font, 0, 128, [System.Drawing.PointF]::new(0, 0), [System.Drawing.StringFormat]::GenericTypographic)
    $bounds = $glyph.GetBounds()
    $scale = [single]($size * .59 / [Math]::Max($bounds.Width, $bounds.Height))
    $matrix = [System.Drawing.Drawing2D.Matrix]::new($scale, 0, 0, $scale,
        [single](($size - $bounds.Width * $scale) / 2 - $bounds.X * $scale),
        [single](($size - $bounds.Height * $scale) / 2 - $bounds.Y * $scale))
    $glyph.Transform($matrix)
    $graphics.FillPath([System.Drawing.Brushes]::White, $glyph)
    $stream = [System.IO.MemoryStream]::new()
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames.Add($stream.ToArray())
    if ($size -eq 256) { $bitmap.Save((Join-Path $assets 'AppIcon.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $stream.Dispose(); $matrix.Dispose(); $glyph.Dispose(); $brush.Dispose()
    $background.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$iconStream = [System.IO.File]::Create((Join-Path $assets 'AppIcon.ico'))
$writer = [System.IO.BinaryWriter]::new($iconStream)
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$sizes.Count)
$offset = [uint32](6 + 16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dimension = if ($sizes[$i] -eq 256) { [byte]0 } else { [byte]$sizes[$i] }
    $writer.Write($dimension); $writer.Write($dimension)
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$frames[$i].Length); $writer.Write($offset)
    $offset += [uint32]$frames[$i].Length
}
foreach ($frame in $frames) { $writer.Write($frame) }
$writer.Dispose()
$font.Dispose(); $availableFonts.Dispose()
Write-Host "Ícone Audio de $fontName gerado em nove resoluções."
