#requires -Version 7
# Bakes every terrain tile from TerrainArtGenerator to assets/art/tiles/<terrain>.png
# by running the headless BakeTerrainTiles tool scene. Mirrors run-checks/scene-check.ps1
# for Godot-binary resolution. Deterministic: re-running with no code change rewrites
# byte-identical PNGs.
$ErrorActionPreference = 'Stop'
$repo  = Resolve-Path "$PSScriptRoot\..\..\.."
$godot = 'C:\source\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64_console.exe'
if (-not (Test-Path $godot)) { $godot = (Get-Command godot -ErrorAction SilentlyContinue)?.Source }
if (-not $godot) { throw "Godot .NET binary not found (see godot-binary-for-headless-checks memory)" }

Write-Host "==> dotnet build (the tool needs the current TerrainArtGenerator)" -ForegroundColor Cyan
& dotnet build "$repo\NWO.sln" -warnaserror | Select-Object -Last 3
if ($LASTEXITCODE -ne 0) { throw "build failed" }

Write-Host "==> godot --import" -ForegroundColor Cyan
& $godot --headless --path $repo --import 2>&1 | Select-Object -Last 2

Write-Host "==> baking terrain tiles" -ForegroundColor Cyan
$out = & $godot --headless --path $repo 'res://scenes/tools/BakeTerrainTiles.tscn' --quit-after 5 2>&1
$out | Select-String -Pattern 'baked|Bake|ERROR|SCRIPT ERROR|Exception'
if ($out | Select-String -Pattern 'ERROR|SCRIPT ERROR|Exception') { throw "bake reported errors" }

Write-Host "==> re-import baked PNGs" -ForegroundColor Cyan
& $godot --headless --path $repo --import 2>&1 | Select-Object -Last 2
Write-Host "tiles baked to assets/art/tiles/" -ForegroundColor Green
