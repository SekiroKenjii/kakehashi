#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Generates the application's icon set for both the unpackaged (.exe) and packaged (MSIX) apps.

.DESCRIPTION
  Produces, from a single vector-style drawing routine (GDI+), every raster asset the project
  needs, with no external tooling (no ImageMagick / Inkscape):

    * Assets\app.ico                        - multi-resolution icon for the unpackaged .exe and
                                              the app windows (16,24,32,48,64,128,256 px frames).
    * Assets\Square44x44Logo*.png           - taskbar / Start tile (+ scale-200 and unplated
                                              target sizes used by the shell).
    * Assets\Square150x150Logo*.png         - medium Start tile (+ scale-200).
    * Assets\Wide310x150Logo*.png           - wide Start tile (+ scale-200).
    * Assets\StoreLogo.png                  - Store / installer logo.
    * Assets\SplashScreen*.png              - MSIX splash screen (+ scale-200).

  The artwork is the brand mark from docs/mockups: three left-aligned rounded bars (long, short,
  medium) on a dark rounded square - a nod to the modular-monolith layering. Rebrand by editing
  the $PlateColor / $GlyphColor colors below; everything else is derived.

.EXAMPLE
  pwsh scripts/generate-icons.ps1
#>
[CmdletBinding()]
param(
  # Output folder for the generated assets. Defaults to the app's Assets folder.
  [string]$AssetsDir = (Join-Path $PSScriptRoot '..\src\App\Kakehashi.App\Assets')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# --- Brand palette (edit to rebrand) ---------------------------------------------------------
$PlateColor = [System.Drawing.Color]::FromArgb(0xFF, 0x34, 0x32, 0x31)  # warm graphite (mockup accent)
$GlyphColor = [System.Drawing.Color]::FromArgb(0xFF, 0xFF, 0xFF, 0xFF)  # white

# --- Drawing primitives ----------------------------------------------------------------------

function New-RoundedRectPath {
  param([float]$X, [float]$Y, [float]$W, [float]$H, [float]$Radius)
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $d = $Radius * 2.0
  if ($d -gt 0) {
    $path.AddArc($X, $Y, $d, $d, 180, 90)
    $path.AddArc(($X + $W - $d), $Y, $d, $d, 270, 90)
    $path.AddArc(($X + $W - $d), ($Y + $H - $d), $d, $d, 0, 90)
    $path.AddArc($X, ($Y + $H - $d), $d, $d, 90, 90)
    $path.CloseFigure()
  } else {
    $path.AddRectangle((New-Object System.Drawing.RectangleF $X, $Y, $W, $H))
  }
  return $path
}

# Draws the square app logo, edge-to-edge, into a transparent bitmap of the given pixel size.
function New-LogoBitmap {
  param([int]$Size)

  $bmp = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  try {
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = [float]$Size
    # Slight inset so the rounded plate is not clipped by the bitmap edge.
    $inset = [Math]::Max(1.0, $s * 0.04)
    $plate = $s - ($inset * 2.0)
    $radius = $plate * 0.25

    $rect = New-Object System.Drawing.RectangleF $inset, $inset, $plate, $plate
    $platePath = New-RoundedRectPath $rect.X $rect.Y $rect.Width $rect.Height $radius

    # Flat dark plate.
    $brush = New-Object System.Drawing.SolidBrush $PlateColor
    $g.FillPath($brush, $platePath)
    $brush.Dispose()

    # Layered-tiles mark: three left-aligned rounded bars (long, short, medium), centered as a
    # block. Geometry mirrors the mockup's 88px icon: 40/28/34 wide, 9 high, 8 apart.
    $glyphBrush = New-Object System.Drawing.SolidBrush $GlyphColor
    $barH = $plate * 0.102
    $gap = $plate * 0.091
    $barRadius = $barH * 0.5
    $widths = @(0.455, 0.318, 0.386)
    $left = $inset + (($plate - ($plate * $widths[0])) * 0.5)
    $totalH = ($barH * 3) + ($gap * 2)
    $top = $inset + (($plate - $totalH) * 0.5)
    for ($i = 0; $i -lt 3; $i++) {
      $bw = $plate * $widths[$i]
      $by = $top + ($i * ($barH + $gap))
      $barPath = New-RoundedRectPath $left $by $bw $barH $barRadius
      $g.FillPath($glyphBrush, $barPath)
      $barPath.Dispose()
    }
    $glyphBrush.Dispose()
    $platePath.Dispose()
  } finally {
    $g.Dispose()
  }
  return $bmp
}

# Centers a scaled copy of the square logo on a transparent canvas of arbitrary dimensions.
function Save-CanvasPng {
  param([int]$Width, [int]$Height, [double]$LogoFraction, [string]$Path)

  $canvas = New-Object System.Drawing.Bitmap $Width, $Height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($canvas)
  try {
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $logoSize = [int]([Math]::Round([Math]::Min($Width, $Height) * $LogoFraction))
    $logo = New-LogoBitmap -Size $logoSize
    try {
      $x = [int](($Width - $logoSize) / 2)
      $y = [int](($Height - $logoSize) / 2)
      $g.DrawImage($logo, $x, $y, $logoSize, $logoSize)
    } finally {
      $logo.Dispose()
    }
  } finally {
    $g.Dispose()
  }
  $canvas.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
  $canvas.Dispose()
  Write-Host ("  {0}  ({1}x{2})" -f (Split-Path $Path -Leaf), $Width, $Height)
}

function Save-SquarePng {
  param([int]$Size, [string]$Path)
  $bmp = New-LogoBitmap -Size $Size
  $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  Write-Host ("  {0}  ({1}x{1})" -f (Split-Path $Path -Leaf), $Size)
}

# Assembles a multi-frame .ico from PNG-compressed frames (Vista+ ICO format).
function Save-Ico {
  param([int[]]$Sizes, [string]$Path)

  $frames = foreach ($size in $Sizes) {
    $bmp = New-LogoBitmap -Size $size
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    [pscustomobject]@{ Size = $size; Bytes = $ms.ToArray() }
    $ms.Dispose()
  }

  $fs = [System.IO.File]::Create($Path)
  $bw = New-Object System.IO.BinaryWriter $fs
  try {
    # ICONDIR
    $bw.Write([uint16]0)              # reserved
    $bw.Write([uint16]1)              # type = icon
    $bw.Write([uint16]$frames.Count)  # image count

    $offset = 6 + (16 * $frames.Count)
    foreach ($frame in $frames) {
      $dim = if ($frame.Size -ge 256) { 0 } else { $frame.Size }  # 0 means 256
      $bw.Write([byte]$dim)           # width
      $bw.Write([byte]$dim)           # height
      $bw.Write([byte]0)              # palette count
      $bw.Write([byte]0)              # reserved
      $bw.Write([uint16]1)            # color planes
      $bw.Write([uint16]32)           # bits per pixel
      $bw.Write([uint32]$frame.Bytes.Length)
      $bw.Write([uint32]$offset)
      $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) {
      $bw.Write($frame.Bytes)
    }
  } finally {
    $bw.Dispose()
    $fs.Dispose()
  }
  Write-Host ("  {0}  (frames: {1})" -f (Split-Path $Path -Leaf), ($Sizes -join ', '))
}

# --- Generate ---------------------------------------------------------------------------------

$AssetsDir = [System.IO.Path]::GetFullPath($AssetsDir)
New-Item -ItemType Directory -Force -Path $AssetsDir | Out-Null
Write-Host "Generating icon set into: $AssetsDir"

# Unpackaged .exe + window icon.
Save-Ico -Sizes @(16, 24, 32, 48, 64, 128, 256) -Path (Join-Path $AssetsDir 'app.ico')

# MSIX: Square 44x44 (taskbar / app list), plated base + scale-200, plus unplated target sizes.
Save-SquarePng -Size 44  -Path (Join-Path $AssetsDir 'Square44x44Logo.png')
Save-SquarePng -Size 88  -Path (Join-Path $AssetsDir 'Square44x44Logo.scale-200.png')
foreach ($t in 16, 24, 32, 48, 256) {
  Save-SquarePng -Size $t -Path (Join-Path $AssetsDir ("Square44x44Logo.targetsize-{0}_altform-unplated.png" -f $t))
}

# MSIX: Square 150x150 (medium tile).
Save-SquarePng -Size 150 -Path (Join-Path $AssetsDir 'Square150x150Logo.png')
Save-SquarePng -Size 300 -Path (Join-Path $AssetsDir 'Square150x150Logo.scale-200.png')

# MSIX: Wide 310x150 tile (logo centered on transparent canvas).
Save-CanvasPng -Width 310 -Height 150 -LogoFraction 0.78 -Path (Join-Path $AssetsDir 'Wide310x150Logo.png')
Save-CanvasPng -Width 620 -Height 300 -LogoFraction 0.78 -Path (Join-Path $AssetsDir 'Wide310x150Logo.scale-200.png')

# MSIX: Store logo.
Save-SquarePng -Size 50  -Path (Join-Path $AssetsDir 'StoreLogo.png')

# MSIX: splash screen.
Save-CanvasPng -Width 620  -Height 300 -LogoFraction 0.42 -Path (Join-Path $AssetsDir 'SplashScreen.png')
Save-CanvasPng -Width 1240 -Height 600 -LogoFraction 0.42 -Path (Join-Path $AssetsDir 'SplashScreen.scale-200.png')

Write-Host "Done."
