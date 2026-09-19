<#
.SYNOPSIS
  Generates src\AxClaude\AxClaude.ico: a rounded dark-blue square with the letters Ax, at the usual Windows sizes.

.DESCRIPTION
  Uses System.Drawing only, so it runs on any Windows machine. The 256 pixel image is stored PNG-compressed, the
  smaller ones as 32-bit bitmaps, which is what Windows expects in an icon file. Re-run after changing the design;
  the result is committed.
#>
[CmdletBinding()]
param(
    [string]$Out
)

$ErrorActionPreference = 'Stop'
if (-not $Out) {
    # $PSScriptRoot is not set while parameter defaults are evaluated in Windows PowerShell 5.1.
    $Out = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\src\AxClaude\AxClaude.ico'
}
Add-Type -AssemblyName System.Drawing

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)

    $radius = [Math]::Max(2, [int]($size * 0.22))
    $rect = New-Object System.Drawing.Rectangle 0, 0, ($size - 1), ($size - 1)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $fill = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 31, 78, 121))
    $g.FillPath($fill, $path)

    $fontSize = [Math]::Max(5, $size * 0.5)
    $font = New-Object System.Drawing.Font 'Segoe UI', $fontSize, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    $box = New-Object System.Drawing.RectangleF 0, ($size * 0.02), $size, $size
    $g.DrawString('Ax', $font, [System.Drawing.Brushes]::White, $box, $format)

    $g.Dispose()
    return $bmp
}

function Get-PngBytes([System.Drawing.Bitmap]$bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return , $ms.ToArray()   # the comma keeps the byte array in one piece on the pipeline
}

function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $w = $bmp.Width
    $h = $bmp.Height
    $maskStride = [int]((($w + 31) -band (-bnot 31)) / 8)
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter $ms
    $bw.Write([int32]40)            # biSize
    $bw.Write([int32]$w)            # biWidth
    $bw.Write([int32]($h * 2))      # biHeight: colour bitmap plus AND mask
    $bw.Write([int16]1)             # biPlanes
    $bw.Write([int16]32)            # biBitCount
    $bw.Write([int32]0)             # biCompression
    $bw.Write([int32]($w * $h * 4 + $maskStride * $h))
    $bw.Write([int32]0); $bw.Write([int32]0); $bw.Write([int32]0); $bw.Write([int32]0)
    for ($y = $h - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $w; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
        }
    }
    $bw.Write((New-Object byte[] ($maskStride * $h)))   # AND mask: all visible, alpha decides
    $bw.Flush()
    return , $ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 256
$images = @()
foreach ($size in $sizes) {
    $bmp = New-IconBitmap $size
    if ($size -ge 256) { [byte[]]$bytes = Get-PngBytes $bmp } else { [byte[]]$bytes = Get-DibBytes $bmp }
    $images += [pscustomobject]@{ Size = $size; Bytes = $bytes }
    $bmp.Dispose()
}

$stream = [System.IO.File]::Create((Join-Path (Split-Path -Parent $Out) (Split-Path -Leaf $Out)))
$writer = New-Object System.IO.BinaryWriter $stream
$writer.Write([int16]0)                 # reserved
$writer.Write([int16]1)                 # type: icon
$writer.Write([int16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($image in $images) {
    $dim = if ($image.Size -ge 256) { 0 } else { $image.Size }
    $writer.Write([byte]$dim)           # width
    $writer.Write([byte]$dim)           # height
    $writer.Write([byte]0)              # colours in palette
    $writer.Write([byte]0)              # reserved
    $writer.Write([int16]1)             # planes
    $writer.Write([int16]32)            # bits per pixel
    $writer.Write([int32]$image.Bytes.Length)
    $writer.Write([int32]$offset)
    $offset += $image.Bytes.Length
}
foreach ($image in $images) {
    $writer.Write([byte[]]$image.Bytes, 0, $image.Bytes.Length)
}
$writer.Flush()
$stream.Close()
Write-Host ("Wrote {0} ({1:N0} bytes, sizes {2})" -f (Resolve-Path $Out), (Get-Item $Out).Length, ($sizes -join ', '))
