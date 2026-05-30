#requires -Version 7
param([Parameter(Mandatory)][string]$Message)   # full message incl. Co-Authored-By line
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path "$PSScriptRoot\..\..\.."
git -C $repo add -A
git -C $repo commit -m $Message
if ($LASTEXITCODE -ne 0) { throw "commit failed (nothing staged? hook failed?)" }
git -C $repo push origin main
