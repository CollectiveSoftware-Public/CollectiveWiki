# SPDX-License-Identifier: GPL-3.0-or-later
<#
.SYNOPSIS
  Generates a multi-resolution Windows .ico (and a master PNG) for a Collective<Name> app.
.DESCRIPTION
  The shared, dependency-free way to produce desktop app icons across the Collective
  Software repos. Draws the product mark — a rounded square in the product accent colour
  plus an emblem — at every standard size with System.Drawing (present on the Windows dev
  host; no ImageMagick/Inkscape/NuGet needed) and packs the sizes into one .ico whose
  entries are PNG-encoded (Windows Vista+ and Avalonia both read PNG-in-ICO).

  Mobile heads do NOT use this: MAUI rasterises the brand SVG itself at build time
  (MauiIcon + resizetizer). Keep the SVG visually in sync with the emblem drawn here.
.PARAMETER OutPath
  Destination .ico path (e.g. src/Vault.Desktop/Assets/collectivevault.ico).
.PARAMETER Color
  Accent colour as #RRGGBB (the rounded-square background).
.PARAMETER Emblem
  Which mark to draw: Keyhole (vault/secrets), Branch (git/VCS), or Letter (generic fallback).
.PARAMETER Letter
  The glyph drawn when -Emblem Letter (single character).
.EXAMPLE
  ./build/generate-app-icon.ps1 -OutPath src/Vault.Desktop/Assets/collectivevault.ico -Color '#2B6BD4' -Emblem Keyhole
.EXAMPLE
  ./build/generate-app-icon.ps1 -OutPath src/Foo.Desktop/Assets/collectivefoo.ico -Color '#512BD4' -Emblem Letter -Letter F
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$OutPath,
    [string]$Color = '#2B6BD4',
    [ValidateSet('Keyhole', 'Branch', 'Prompt', 'Letter')] [string]$Emblem = 'Keyhole',
    [string]$Letter = 'C',
    [int[]]$Sizes = @(16, 24, 32, 48, 64, 128, 256),
    # Also drop a 512px master PNG next to the .ico (handy for stores/READMEs).
    [switch]$NoMasterPng
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function ConvertFrom-Hex([string]$hex) {
    $hex = $hex.TrimStart('#')
    [System.Drawing.Color]::FromArgb(
        255,
        [Convert]::ToInt32($hex.Substring(0, 2), 16),
        [Convert]::ToInt32($hex.Substring(2, 2), 16),
        [Convert]::ToInt32($hex.Substring(4, 2), 16))
}

# Rounded-rectangle path used for the icon background.
function New-RoundedRectPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    , $p
}

# A classic keyhole (circle + tapering stem) as a single fillable path, centred in [0,s].
function New-KeyholePath([int]$s) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $cx = $s * 0.5
    $headR = $s * 0.155
    $headCy = $s * 0.37
    $p.AddEllipse($cx - $headR, $headCy - $headR, $headR * 2, $headR * 2)
    $topW = $s * 0.065
    $botW = $s * 0.155
    $stemTop = $s * 0.45
    $stemBot = $s * 0.72
    $pts = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new([float]($cx - $topW), [float]$stemTop),
        [System.Drawing.PointF]::new([float]($cx + $topW), [float]$stemTop),
        [System.Drawing.PointF]::new([float]($cx + $botW), [float]$stemBot),
        [System.Drawing.PointF]::new([float]($cx - $botW), [float]$stemBot)
    )
    $p.AddPolygon($pts)
    , $p
}

function New-IconBitmap([int]$s, [System.Drawing.Color]$accent) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.Clear([System.Drawing.Color]::Transparent)

        $pad = [float]($s * 0.045)
        $radius = [float]($s * 0.225)
        $bg = New-RoundedRectPath $pad $pad ([float]($s - 2 * $pad)) ([float]($s - 2 * $pad)) $radius
        try {
            # Subtle top-to-bottom shade so the mark has a little depth.
            $lighter = [System.Drawing.Color]::FromArgb(255,
                [Math]::Min(255, [int]($accent.R + 22)),
                [Math]::Min(255, [int]($accent.G + 22)),
                [Math]::Min(255, [int]($accent.B + 22)))
            $rect = New-Object System.Drawing.RectangleF(0, 0, $s, $s)
            $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $lighter, $accent, 90.0)
            try { $g.FillPath($brush, $bg) } finally { $brush.Dispose() }
        }
        finally { $bg.Dispose() }

        $white = [System.Drawing.Color]::FromArgb(245, 255, 255, 255)
        if ($Emblem -eq 'Keyhole') {
            $kh = New-KeyholePath $s
            $brush = New-Object System.Drawing.SolidBrush($white)
            try { $g.FillPath($brush, $kh) } finally { $brush.Dispose(); $kh.Dispose() }
        }
        elseif ($Emblem -eq 'Branch') {
            # A git branch graph: a trunk (top + bottom nodes) with a branch diverging to a
            # third node on the right.
            $brush = New-Object System.Drawing.SolidBrush($white)
            $pen = New-Object System.Drawing.Pen($white, [float]($s * 0.072))
            $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
            $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
            try {
                $g.DrawLine($pen, [float]($s * 0.37), [float]($s * 0.31), [float]($s * 0.37), [float]($s * 0.69))
                $g.DrawLine($pen, [float]($s * 0.37), [float]($s * 0.43), [float]($s * 0.62), [float]($s * 0.43))
                $r = [float]($s * 0.082)
                foreach ($n in @(@(0.37, 0.31), @(0.37, 0.69), @(0.62, 0.43))) {
                    $g.FillEllipse($brush, [float]($s * $n[0] - $r), [float]($s * $n[1] - $r), [float]($r * 2), [float]($r * 2))
                }
            }
            finally { $pen.Dispose(); $brush.Dispose() }
        }
        elseif ($Emblem -eq 'Prompt') {
            # A shell prompt: a chevron ">" and an underscore cursor "_".
            $pen = New-Object System.Drawing.Pen($white, [float]($s * 0.075))
            $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
            $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
            $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
            try {
                $chevron = [System.Drawing.PointF[]]@(
                    [System.Drawing.PointF]::new([float]($s * 0.30), [float]($s * 0.34)),
                    [System.Drawing.PointF]::new([float]($s * 0.47), [float]($s * 0.50)),
                    [System.Drawing.PointF]::new([float]($s * 0.30), [float]($s * 0.66))
                )
                $g.DrawLines($pen, $chevron)
                $g.DrawLine($pen, [float]($s * 0.54), [float]($s * 0.66), [float]($s * 0.72), [float]($s * 0.66))
            }
            finally { $pen.Dispose() }
        }
        else {
            $fontSize = [float]($s * 0.52)
            $font = New-Object System.Drawing.Font('Segoe UI', $fontSize, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
            $fmt = New-Object System.Drawing.StringFormat
            $fmt.Alignment = [System.Drawing.StringAlignment]::Center
            $fmt.LineAlignment = [System.Drawing.StringAlignment]::Center
            $brush = New-Object System.Drawing.SolidBrush($white)
            $rect = New-Object System.Drawing.RectangleF(0, 0, $s, $s)
            try { $g.DrawString($Letter, $font, $brush, $rect, $fmt) }
            finally { $brush.Dispose(); $font.Dispose(); $fmt.Dispose() }
        }
    }
    finally { $g.Dispose() }
    , $bmp
}

function Get-PngBytes([System.Drawing.Bitmap]$bmp) {
    $ms = New-Object System.IO.MemoryStream
    try {
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        # Wrap in a one-element array (comma operator) so the pipeline does not unroll the
        # byte[] into an Object[] — callers must get a real byte[] for BinaryWriter.Write.
        return , ([byte[]]$ms.ToArray())
    }
    finally { $ms.Dispose() }
}

$accent = ConvertFrom-Hex $Color
$outDir = Split-Path -Parent $OutPath
if ($outDir -and -not (Test-Path $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }

# Render each size to PNG bytes.
$entries = foreach ($s in ($Sizes | Sort-Object -Unique)) {
    $bmp = New-IconBitmap $s $accent
    try { [pscustomobject]@{ Size = $s; Png = (Get-PngBytes $bmp) } }
    finally { $bmp.Dispose() }
}

# Assemble the ICO container: ICONDIR (6) + N*ICONDIRENTRY (16) + concatenated PNG data.
$fs = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($fs)
try {
    $bw.Write([uint16]0)              # reserved
    $bw.Write([uint16]1)              # type = icon
    $bw.Write([uint16]$entries.Count) # image count

    $offset = 6 + (16 * $entries.Count)
    foreach ($e in $entries) {
        $dim = if ($e.Size -ge 256) { 0 } else { $e.Size }  # 0 means 256
        $bw.Write([byte]$dim)         # width
        $bw.Write([byte]$dim)         # height
        $bw.Write([byte]0)            # colours in palette
        $bw.Write([byte]0)            # reserved
        $bw.Write([uint16]1)          # colour planes
        $bw.Write([uint16]32)         # bits per pixel
        $bw.Write([uint32]$e.Png.Length)
        $bw.Write([uint32]$offset)
        $offset += $e.Png.Length
    }
    foreach ($e in $entries) { $bw.Write([byte[]]$e.Png) }
    $bw.Flush()
    [System.IO.File]::WriteAllBytes($OutPath, $fs.ToArray())
}
finally { $bw.Dispose(); $fs.Dispose() }

Write-Host "Wrote $OutPath ($((Get-Item $OutPath).Length) bytes, sizes: $($entries.Size -join ', '))"

if (-not $NoMasterPng) {
    $masterPath = [System.IO.Path]::ChangeExtension($OutPath, $null).TrimEnd('.') + '-512.png'
    $bmp = New-IconBitmap 512 $accent
    try { [System.IO.File]::WriteAllBytes($masterPath, (Get-PngBytes $bmp)) } finally { $bmp.Dispose() }
    Write-Host "Wrote $masterPath (512x512 master)"
}
