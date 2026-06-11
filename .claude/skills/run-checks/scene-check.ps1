#requires -Version 7
param([string]$Scene = 'res://scenes/world/WorldMap.tscn', [int]$Frames = 30)
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path "$PSScriptRoot\..\..\.."
# No hardcoded install paths: this repo is worked on from several machines with
# Godot in different folders, so PATH is the single source of truth. Accept either
# a plain `godot` command or the stock versioned exe name (Godot_*console*.exe).
$godot = (Get-Command godot -ErrorAction SilentlyContinue)?.Source ??
         (Get-Command 'Godot*console*' -ErrorAction SilentlyContinue | Select-Object -First 1).Source
if (-not $godot) { throw "Godot .NET binary not found on PATH. Add the directory containing Godot_*_console.exe to your PATH environment variable (system or user), then restart the shell." }
Write-Host "==> godot --import" -ForegroundColor Cyan
& $godot --headless --path $repo --import 2>&1 | Tee-Object -Variable out1
Write-Host "==> godot scene check: $Scene" -ForegroundColor Cyan
& $godot --headless --path $repo $Scene --quit-after $Frames 2>&1 | Tee-Object -Variable out2
$bad = ($out1 + $out2) | Select-String -Pattern 'ERROR|SCRIPT ERROR|Cannot|Exception|Node not found'
if ($bad) { $bad; throw "scene check reported errors" }
Write-Host "scene check clean" -ForegroundColor Green
