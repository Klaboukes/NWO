#requires -Version 7
# Runs the headless MapHistogram tool: prints a terrain histogram + river/resource
# counts across a span of seeds, so map-generation tuning can be judged without a
# display. Mirrors run-checks/scene-check.ps1 for Godot-binary resolution.
#
#   ./histogram.ps1                 # 5 seeds, 60x40 (defaults)
#   ./histogram.ps1 -Seeds 8        # sample more seeds
#   ./histogram.ps1 -Size 80x52     # different map size
param([int]$Seeds = 5, [string]$Size = '60x40')
$ErrorActionPreference = 'Stop'
$repo  = Resolve-Path "$PSScriptRoot\..\..\.."
$godot = 'C:\source\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64_console.exe'
if (-not (Test-Path $godot)) { $godot = (Get-Command godot -ErrorAction SilentlyContinue)?.Source }
if (-not $godot) { throw "Godot .NET binary not found (see godot-binary-for-headless-checks memory)" }

Write-Host "==> dotnet build (the tool needs the current MapGenerator)" -ForegroundColor Cyan
& dotnet build "$repo\NWO.sln" -warnaserror | Select-Object -Last 3
if ($LASTEXITCODE -ne 0) { throw "build failed" }

Write-Host "==> godot --import" -ForegroundColor Cyan
& $godot --headless --path $repo --import 2>&1 | Select-Object -Last 2

Write-Host "==> map histogram ($Seeds seeds, $Size)" -ForegroundColor Cyan
$out = & $godot --headless --path $repo 'res://scenes/tools/MapHistogram.tscn' `
    --quit-after 5 -- --seeds $Seeds --size $Size 2>&1
$out | Select-String -Pattern 'seed|%|rivers|ERROR|SCRIPT ERROR|Exception'
if ($out | Select-String -Pattern 'ERROR|SCRIPT ERROR|Exception') { throw "histogram reported errors" }
