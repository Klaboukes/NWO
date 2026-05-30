#requires -Version 7
param([string]$Scene = 'res://scenes/world/WorldMap.tscn', [int]$Frames = 30)
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path "$PSScriptRoot\..\..\.."
$godot = 'C:\source\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64_console.exe'
if (-not (Test-Path $godot)) { $godot = (Get-Command godot -ErrorAction SilentlyContinue)?.Source }
if (-not $godot) { throw "Godot .NET binary not found (see godot-binary-for-headless-checks memory)" }
Write-Host "==> godot --import" -ForegroundColor Cyan
& $godot --headless --path $repo --import 2>&1 | Tee-Object -Variable out1
Write-Host "==> godot scene check: $Scene" -ForegroundColor Cyan
& $godot --headless --path $repo $Scene --quit-after $Frames 2>&1 | Tee-Object -Variable out2
$bad = ($out1 + $out2) | Select-String -Pattern 'ERROR|SCRIPT ERROR|Cannot|Exception|Node not found'
if ($bad) { $bad; throw "scene check reported errors" }
Write-Host "scene check clean" -ForegroundColor Green
