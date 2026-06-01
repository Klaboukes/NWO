param([string]$OutDir = "build")

$exe = Join-Path $OutDir "NWO.exe"
New-Item -ItemType Directory -Force $OutDir | Out-Null

Write-Host "Exporting Windows standalone to $exe ..."
godot --headless --path . --export-release "Windows Desktop" $exe
if ($LASTEXITCODE -ne 0) { throw "Godot export failed (exit $LASTEXITCODE)" }

Write-Host "Build complete:"
Get-ChildItem $OutDir | Format-Table Name, Length
