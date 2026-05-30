#requires -Version 7
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path "$PSScriptRoot\..\..\.."
Write-Host "==> dotnet build (warnings = failures)" -ForegroundColor Cyan
dotnet build "$repo\NWO.sln" -warnaserror
if ($LASTEXITCODE -ne 0) { throw "build failed ($LASTEXITCODE)" }
