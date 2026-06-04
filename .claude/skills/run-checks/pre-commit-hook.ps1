#requires -Version 7
# PreToolUse hook — gate git commit/push on the NWO verification loop.
#
# Reads the hook payload on stdin. Fires only for `git commit` / `git push`
# commands, and only when the change actually touches code the checks can catch
# (C#, scenes, data JSON, project/solution files). Doc-only or skill/markdown
# changes skip straight through. On a failing check it exits 2, which blocks the
# tool call and shows stderr back to Claude.
#
# A short-lived green marker lets a `commit` immediately followed by a `push`
# reuse one passing run instead of building + testing twice in a row.

$ErrorActionPreference = 'Stop'

# --- parse the hook payload ------------------------------------------------
$raw = [Console]::In.ReadToEnd()
try { $payload = $raw | ConvertFrom-Json } catch { exit 0 }  # not JSON -> don't block

$cmd = $payload.tool_input.command
if (-not $cmd) { exit 0 }

# Only gate genuine commit/push invocations.
if ($cmd -notmatch '\bgit\b[\s\S]*\b(commit|push)\b') { exit 0 }

# --- skip when nothing code-relevant is changing ---------------------------
# Files the checks can actually validate: C#, scenes/resources, content JSON,
# and the project/solution wiring. Everything else (docs, *.md, skill scripts,
# *.uid, markdownlint config) is invisible to build/test/scene-load.
$codePattern = '\.(cs|csproj|sln|tscn|tres)$|^data/.*\.json$|(^|/)project\.godot$'

$files = $null
try {
    $set = @()
    $set += git diff --cached --name-only 2>$null            # staged for commit
    $set += git diff --name-only 2>$null                     # modified tracked (git commit -a)
    $upstream = git rev-parse --abbrev-ref --symbolic-full-name '@{u}' 2>$null
    if ($LASTEXITCODE -eq 0 -and $upstream) {
        $set += git diff --name-only "$upstream..HEAD" 2>$null # unpushed commits (git push)
    }
    $files = $set | Where-Object { $_ } | Sort-Object -Unique
} catch {
    $files = $null  # detection failed -> fall through and run checks (fail-safe)
}

if ($null -ne $files) {
    if ($files.Count -eq 0) { exit 0 }                       # nothing to record; let git handle it
    if (-not ($files | Where-Object { $_ -match $codePattern })) {
        exit 0                                               # docs/skills/markdown only -> skip
    }
}

# --- reuse a recent green run ----------------------------------------------
$marker = Join-Path ([System.IO.Path]::GetTempPath()) 'nwo-checks-green.marker'
if (Test-Path $marker) {
    $age = (Get-Date) - (Get-Item $marker).LastWriteTime
    if ($age.TotalSeconds -lt 120) { exit 0 }
}

# --- run the verification loop ---------------------------------------------
try {
    & "$PSScriptRoot\check.ps1"
    if ($LASTEXITCODE -ne 0) { throw "checks exited $LASTEXITCODE" }
} catch {
    [Console]::Error.WriteLine("run-checks failed — commit/push blocked. Fix the failure above and retry.")
    exit 2
}

New-Item -ItemType File -Path $marker -Force | Out-Null
exit 0
