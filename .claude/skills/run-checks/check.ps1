#requires -Version 7
$ErrorActionPreference = 'Stop'
& "$PSScriptRoot\build.ps1"
& "$PSScriptRoot\test.ps1"
& "$PSScriptRoot\scene-check.ps1"
Write-Host "ALL CHECKS GREEN" -ForegroundColor Green
