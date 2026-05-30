#requires -Version 7
param([string]$Filter)               # optional: e.g. FullyQualifiedName~Combat
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path "$PSScriptRoot\..\..\.."
$testArgs = @("$repo\NWO.Tests\NWO.Tests.csproj")
if ($Filter) { $testArgs += @('--filter', $Filter) }
Write-Host "==> dotnet test" -ForegroundColor Cyan
dotnet test @testArgs
if ($LASTEXITCODE -ne 0) { throw "tests failed ($LASTEXITCODE)" }
