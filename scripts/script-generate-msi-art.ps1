#Requires -Version 7.0

[CmdletBinding()]
param(
    [string]$Source = '',
    [string]$OutDir = '',

    [ValidateRange(1, 6)]
    [int]$Scale = 3
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RepoRoot = Split-Path $PSScriptRoot -Parent
if (-not $Source) { $Source = Join-Path $RepoRoot 'source\assets\photo-viewer-logo.png' }
if (-not $OutDir) { $OutDir = Join-Path $RepoRoot 'source\assets\win\msi\assets' }

if (-not (Test-Path $Source)) { throw "Source image not found: $Source" }

Add-Type -AssemblyName System.Drawing

$BannerW = 493
$BannerH = 58
$DialogW = 493
$DialogH = 312

$PanelW = 175

$DialogFace = [System.Drawing.Color]::FromArgb(240, 240, 240)
$White      = [System.Drawing.Color]::FromArgb(255, 255, 255)
$PanelLight = [System.Drawing.Color]::FromArgb(242, 247, 251)
$PanelDeep  = [System.Drawing.Color]::FromArgb(219, 233, 245)
$Divider    = [System.Drawing.Color]::FromArgb(205, 218, 229)

function New-Canvas([int]$Width, [int]$Height) {
    $bmp = [System.Drawing.Bitmap]::new($Width, $Height,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode    = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    return @($bmp, $g)
}

function Save-Png([System.Drawing.Bitmap]$Bitmap, [string]$Path) {
    $Bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $kb = [math]::Round((Get-Item $Path).Length / 1KB)
    Write-Host ("    {0,-12}: {1} x {2}  ({3} KB)" -f
        [System.IO.Path]::GetFileName($Path), $Bitmap.Width, $Bitmap.Height, $kb)
}

New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

Write-Host "==> Generating MSI installer artwork"
Write-Host "    Source      : $Source"
Write-Host "    Scale       : ${Scale}x (logical 493x58 and 493x312)"
Write-Host "    Output      : $OutDir"

$src = [System.Drawing.Image]::FromFile((Resolve-Path $Source).Path)
try {
    $canvas = New-Canvas ($BannerW * $Scale) ($BannerH * $Scale)
    $bmp = $canvas[0]
    $g   = $canvas[1]
    try {
        $g.Clear($White)

        $logo = 44 * $Scale
        $x    = ($BannerW * $Scale) - $logo - (10 * $Scale)
        $y    = [int](((($BannerH * $Scale)) - $logo) / 2)
        $g.DrawImage($src, $x, $y, $logo, $logo)

        Save-Png $bmp (Join-Path $OutDir 'banner.png')
    }
    finally {
        $g.Dispose()
        $bmp.Dispose()
    }

    $canvas = New-Canvas ($DialogW * $Scale) ($DialogH * $Scale)
    $bmp = $canvas[0]
    $g   = $canvas[1]
    try {
        $g.Clear($DialogFace)

        $panel = [System.Drawing.Rectangle]::new(0, 0, $PanelW * $Scale, $DialogH * $Scale)
        $brush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
            $panel, $PanelLight, $PanelDeep,
            [System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
        try { $g.FillRectangle($brush, $panel) } finally { $brush.Dispose() }

        $pen = [System.Drawing.Pen]::new($Divider, [float]$Scale)
        try { $g.DrawLine($pen, ($PanelW * $Scale), 0, ($PanelW * $Scale), ($DialogH * $Scale)) }
        finally { $pen.Dispose() }

        $logo = 108 * $Scale
        $x    = [int]((($PanelW * $Scale) - $logo) / 2)
        $y    = [int]((($DialogH * $Scale) - $logo) / 2) - (20 * $Scale)
        $g.DrawImage($src, $x, $y, $logo, $logo)

        Save-Png $bmp (Join-Path $OutDir 'dialog.png')
    }
    finally {
        $g.Dispose()
        $bmp.Dispose()
    }
}
finally {
    $src.Dispose()
}

Write-Host ''
Write-Host 'Done.'
Write-Host "  Artwork : $OutDir"
Write-Host '  Next    : rebuild the installer (pack-win-x64-msi) to pick it up.'
