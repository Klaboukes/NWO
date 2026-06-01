param([string]$OutDir = "build")
#requires -Version 7
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path "$PSScriptRoot\..\..\.."

$godot = (Get-Command godot -ErrorAction SilentlyContinue)?.Source
if (-not $godot) { throw "Godot .NET binary not found — add the Godot_*_console.exe directory to your PATH" }

$exe = Join-Path $repo $OutDir "NWO.exe"
New-Item -ItemType Directory -Force (Join-Path $repo $OutDir) | Out-Null

Write-Host "Exporting Windows standalone to $exe ..." -ForegroundColor Cyan
& $godot --headless --path $repo --export-release "Windows Desktop" $exe
if ($LASTEXITCODE -ne 0) { throw "Godot export failed (exit $LASTEXITCODE)" }

Write-Host "Build complete:" -ForegroundColor Green
Get-ChildItem (Join-Path $repo $OutDir) | Format-Table Name, Length
