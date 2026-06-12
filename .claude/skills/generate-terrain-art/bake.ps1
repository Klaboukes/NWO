#requires -Version 7
# Bakes EVERY procedural art asset (terrain tiles, unit/city sprites, resource +
# HUD icons, banner) to its assets/art/ path by running the headless BakeAllArt
# tool scene. Mirrors run-checks/scene-check.ps1 for Godot-binary resolution.
# Deterministic: re-running with no code change rewrites byte-identical PNGs.
$ErrorActionPreference = 'Stop'
$repo  = Resolve-Path "$PSScriptRoot\..\..\.."
$godot = 'C:\source\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64_console.exe'
if (-not (Test-Path $godot)) { $godot = (Get-Command godot -ErrorAction SilentlyContinue)?.Source }
if (-not $godot) { throw "Godot .NET binary not found (see godot-binary-for-headless-checks memory)" }

Write-Host "==> dotnet build (the tool needs the current generators)" -ForegroundColor Cyan
& dotnet build "$repo\NWO.sln" -warnaserror | Select-Object -Last 3
if ($LASTEXITCODE -ne 0) { throw "build failed" }

Write-Host "==> godot --import" -ForegroundColor Cyan
& $godot --headless --path $repo --import 2>&1 | Select-Object -Last 2

Write-Host "==> baking all art" -ForegroundColor Cyan
$out = & $godot --headless --path $repo 'res://scenes/tools/BakeAllArt.tscn' --quit-after 5 2>&1
$out | Select-String -Pattern 'BakeAllArt:|ERROR|SCRIPT ERROR|Exception'
if ($out | Select-String -Pattern 'ERROR|SCRIPT ERROR|Exception') { throw "bake reported errors" }

Write-Host "==> re-import baked PNGs" -ForegroundColor Cyan
& $godot --headless --path $repo --import 2>&1 | Select-Object -Last 2
Write-Host "all art baked under assets/art/" -ForegroundColor Green
