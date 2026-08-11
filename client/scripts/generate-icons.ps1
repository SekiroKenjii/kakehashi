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

  The artwork is the brand mark, docs/brand/kakehashi-mark.svg: a bridge in three strokes - the
  span, the arch beneath it, the water it crosses - on a dark rounded plate. The drawing routine
  below mirrors that SVG's 256-based geometry stroke for stroke, because GDI+ cannot read an SVG
  and this script deliberately has no external tooling. That makes the geometry live in two
  places; if the mark ever changes, change both.

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

# --- Brand palette (mirrors docs/brand/kakehashi-mark.svg - keep the two in sync) ------------
$PlateTop = [System.Drawing.Color]::FromArgb(0xFF, 0x22, 0x24, 0x2A)  # plate gradient, top
$PlateBottom = [System.Drawing.Color]::FromArgb(0xFF, 0x13, 0x15, 0x19)  # plate gradient, bottom
$PlateStroke = [System.Drawing.Color]::FromArgb(0xFF, 0x33, 0x36, 0x3D)  # plate border
$SpanLeft = [System.Drawing.Color]::FromArgb(0xFF, 0xFF, 0xFF, 0xFF)  # span gradient, left
$SpanRight = [System.Drawing.Color]::FromArgb(0xFF, 0xE9, 0xE7, 0xE3)  # span gradient, right
$ArchColor = [System.Drawing.Color]::FromArgb(0xFF, 0xE0, 0x50, 0x3A)  # shu vermilion
$WaterColor = [System.Drawing.Color]::FromArgb(0xFF, 0x8A, 0x8D, 0x95)  # stone grey

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

# Appends an SVG quadratic curve (P0, control Q, P2) to a path. GDI+ only has cubic Beziers;
# the lift is exact: C1 = P0 + 2/3 (Q - P0), C2 = P2 + 2/3 (Q - P2).
function Add-QuadCurve {
  param(
    [System.Drawing.Drawing2D.GraphicsPath]$Path,
    [float]$X0, [float]$Y0, [float]$Qx, [float]$Qy, [float]$X2, [float]$Y2
  )
  $twoThirds = 2.0 / 3.0
  $Path.AddBezier(
    $X0, $Y0,
    ($X0 + $twoThirds * ($Qx - $X0)), ($Y0 + $twoThirds * ($Qy - $Y0)),
    ($X2 + $twoThirds * ($Qx - $X2)), ($Y2 + $twoThirds * ($Qy - $Y2)),
    $X2, $Y2)
}

# Draws the square app logo, edge-to-edge, into a transparent bitmap of the given pixel size.
# Coordinates are the SVG mark's, on its 256 viewBox, scaled by $u.
function New-LogoBitmap {
  param([int]$Size)

  $bmp = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  try {
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $u = [float]$Size / 256.0

    # The plate: rounded square with a vertical gradient.
    $plateRect = New-Object System.Drawing.RectangleF 0, 0, ($u * 256), ($u * 256)
    $platePath = New-RoundedRectPath 0 0 ($u * 256) ($u * 256) ($u * 58)
    $plateBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush `
      $plateRect, $PlateTop, $PlateBottom, ([System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
    $g.FillPath($plateBrush, $platePath)
    $plateBrush.Dispose()
    $platePath.Dispose()

    # The plate border - only where it survives as at least a pixel; below that it is edge noise.
    if (($u * 3) -ge 1.0) {
      $borderPath = New-RoundedRectPath ($u * 1.5) ($u * 1.5) ($u * 253) ($u * 253) ($u * 56.5)
      $borderPen = New-Object System.Drawing.Pen $PlateStroke, ($u * 3)
      $g.DrawPath($borderPen, $borderPath)
      $borderPen.Dispose()
      $borderPath.Dispose()
    }

    # Below 32px a faithfully scaled stroke is under a pixel of ink and the mark turns to smear,
    # so each stroke gets a floor in pixels. The floors are chosen so every max() degrades to the
    # SVG's exact geometry at 32px and above - no special-cased small-size branch to drift.
    $spanH = [Math]::Max($u * 16, 2.0)
    $archT = [Math]::Max($u * 16, 2.0)   # crescent thickness at the apex
    $waterH = [Math]::Max($u * 14, 1.5)

    # The span: rect x=40 y=78 w=176 h=16 rx=8, horizontal white gradient. Midline y=86.
    $spanRect = New-Object System.Drawing.RectangleF ($u * 40), (($u * 86) - ($spanH / 2)), ($u * 176), $spanH
    $spanPath = New-RoundedRectPath $spanRect.X $spanRect.Y $spanRect.Width $spanRect.Height ($spanH / 2)
    $spanBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush `
      $spanRect, $SpanLeft, $SpanRight, ([System.Drawing.Drawing2D.LinearGradientMode]::Horizontal)
    $g.FillPath($spanBrush, $spanPath)
    $spanBrush.Dispose()
    $spanPath.Dispose()

    # The arch: M48 148 Q128 58 208 148 Q128 90 48 148 Z - out along the top curve, back along
    # the underside, closing a crescent that tapers to nothing at both ends. The outer curve's
    # apex sits at y=103; the inner control point is derived from the apex thickness so that
    # $archT = 16u reproduces the SVG's control point (y=90) exactly.
    $innerQy = ($u * 58) + (2 * $archT)
    $archPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    Add-QuadCurve $archPath ($u * 48) ($u * 148) ($u * 128) ($u * 58) ($u * 208) ($u * 148)
    Add-QuadCurve $archPath ($u * 208) ($u * 148) ($u * 128) $innerQy ($u * 48) ($u * 148)
    $archPath.CloseFigure()
    $archBrush = New-Object System.Drawing.SolidBrush $ArchColor
    $g.FillPath($archBrush, $archPath)
    $archBrush.Dispose()
    $archPath.Dispose()

    # The water: rect x=88 y=164 w=80 h=14 rx=7. Midline y=171.
    $waterPath = New-RoundedRectPath ($u * 88) (($u * 171) - ($waterH / 2)) ($u * 80) $waterH ($waterH / 2)
    $waterBrush = New-Object System.Drawing.SolidBrush $WaterColor
    $g.FillPath($waterBrush, $waterPath)
    $waterBrush.Dispose()
    $waterPath.Dispose()
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
