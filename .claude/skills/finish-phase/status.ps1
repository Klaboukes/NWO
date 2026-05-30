#requires -Version 7
$repo = Resolve-Path "$PSScriptRoot\..\..\.."
git -C $repo status --short --branch
Write-Host "--- recent ---"
git -C $repo log --oneline -8
