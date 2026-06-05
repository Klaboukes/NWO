#requires -Version 7
<#
.SYNOPSIS
    Turn raw AI-generated / hand-drawn art into engine-ready NWO PNGs.

.DESCRIPTION
    Reads raw images from tools/art/raw/<category>/ (named by their final target
    name, e.g. grassland.png, warrior.png, goldore.png) and produces engine-ready
    PNGs under assets/art/. For each file it:
      - (sprites/icons) removes the background via rembg, if installed;
      - square-pads the canvas (transparent for sprites/icons, edge-extended for
        opaque terrain) and resizes to the category target with point/nearest scaling;
      - writes to the correct assets/art/ subfolder.

    Drop-in contract: the texture registries pick up a present PNG with no code
    change. After running, regenerate Godot .import siblings (open the editor once
    or run a headless import) and run the run-checks skill.

.PARAMETER Category
    tiles | units | cities | resources | all

.PARAMETER Force
    Overwrite existing engine PNGs without prompting.

.EXAMPLE
    tools/art/Process-Art.ps1 -Category tiles
.EXAMPLE
    tools/art/Process-Art.ps1 -Category all -Force

.NOTES
    Requires ImageMagick (magick) on PATH. Background removal additionally uses
    rembg (pip install rembg) when present. See docs/ART_ASSETS.md.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('tiles', 'units', 'cities', 'resources', 'all')]
    [string]$Category,

    [switch]$Force
)

$ErrorActionPreference = 'Stop'

# Repo root = two levels up from tools/art/.
$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$RawRoot = Join-Path $PSScriptRoot 'raw'
$ArtRoot = Join-Path $RepoRoot 'assets/art'

# Per-category config: source raw dir, destination art dir, edge size, and whether
# the result keeps a transparent background (sprites/icons) or stays opaque (tiles).
$Categories = @{
    tiles     = @{ Dest = 'tiles';     Size = 128; Transparent = $false }
    units     = @{ Dest = 'units';     Size = 128; Transparent = $true  }
    cities    = @{ Dest = 'cities';    Size = 128; Transparent = $true  }
    resources = @{ Dest = 'resources'; Size = 32;  Transparent = $true  }
}

function Test-Tool($name) {
    return [bool](Get-Command $name -ErrorAction SilentlyContinue)
}

if (-not (Test-Tool 'magick')) {
    throw "ImageMagick ('magick') not found on PATH. Install from https://imagemagick.org and re-run."
}
$HasRembg = Test-Tool 'rembg'
if (-not $HasRembg) {
    Write-Warning "rembg not found — background removal is skipped. Install with 'pip install rembg' for sprite/icon cutouts."
}

function Invoke-OneCategory($catName) {
    $cfg = $Categories[$catName]
    $srcDir = Join-Path $RawRoot $catName
    $dstDir = Join-Path $ArtRoot $cfg.Dest

    if (-not (Test-Path $srcDir)) {
        Write-Warning "No raw dir for '$catName' (expected $srcDir) — skipping."
        return
    }
    if (-not (Test-Path $dstDir)) {
        New-Item -ItemType Directory -Path $dstDir -Force | Out-Null
    }

    $files = Get-ChildItem -Path $srcDir -Filter *.png -File -ErrorAction SilentlyContinue
    if (-not $files) {
        Write-Warning "No .png files in $srcDir — skipping '$catName'."
        return
    }

    $size = $cfg.Size
    foreach ($f in $files) {
        $dst = Join-Path $dstDir $f.Name
        if ((Test-Path $dst) -and -not $Force) {
            Write-Host "  skip (exists): $($cfg.Dest)/$($f.Name)  [-Force to overwrite]" -ForegroundColor DarkYellow
            continue
        }

        # Work on temp copies so reruns are idempotent. rembg needs DISTINCT input
        # and output paths — it truncates the output before reading the input, so
        # reusing one path yields an empty (unreadable) image.
        $stem    = [System.IO.Path]::GetTempFileName()
        $srcWork = "$stem.src.png"
        $cutWork = "$stem.cut.png"
        Copy-Item $f.FullName $srcWork -Force
        $magickInput = $srcWork

        if ($cfg.Transparent -and $HasRembg) {
            & rembg i $srcWork $cutWork
            if ($LASTEXITCODE -eq 0 -and (Test-Path $cutWork)) {
                $magickInput = $cutWork
            }
            else {
                Write-Warning "  rembg failed for $($f.Name) — background NOT removed."
            }
        }

        if ($cfg.Transparent) {
            # Trim to content, square-pad with transparency, resize with point filter.
            & magick $magickInput -trim +repage `
                -background none -gravity center -resize "$($size)x$($size)" `
                -extent "$($size)x$($size)" -filter point `
                PNG32:$dst
        }
        else {
            # Opaque terrain: just square-resize to target (keep tileability — no trim/pad).
            & magick $magickInput -filter point -resize "$($size)x$($size)!" `
                -alpha off PNG24:$dst
        }
        $magickExit = $LASTEXITCODE

        Remove-Item $stem, $srcWork, $cutWork -ErrorAction SilentlyContinue

        if ($magickExit -eq 0) {
            Write-Host "  wrote: $($cfg.Dest)/$($f.Name)" -ForegroundColor Green
        }
        else {
            Write-Warning "  magick failed for $($f.Name) (exit $magickExit) — not written."
        }
    }
}

$targets = if ($Category -eq 'all') { $Categories.Keys } else { @($Category) }
foreach ($t in $targets) {
    Write-Host "Processing '$t'..." -ForegroundColor Cyan
    Invoke-OneCategory $t
}

Write-Host ""
Write-Host "Done. Next: regenerate Godot .import files (open the editor once or run a" -ForegroundColor Cyan
Write-Host "headless import), then run the run-checks skill. The look still needs a human F5 pass." -ForegroundColor Cyan
