---
name: run-checks
description: Build, test, and headless-verify the NWO game. Use after any code or data change, or when asked to build, run tests, verify, or "make sure it still works". Runs dotnet build (warnings = failures), dotnet test, and a Godot headless scene check.
allowed-tools: PowerShell, Read, Grep
---

# run-checks

The standard NWO verification loop. Prefer the bundled scripts over hand-typed
commands — they encode the correct paths and fail-fast behaviour.

## Procedure

Run the whole loop:

```powershell
& .claude/skills/run-checks/check.ps1
```

This runs, in order, stopping at the first failure:

1. **`build.ps1`** — `dotnet build NWO.sln -warnaserror`. Zero warnings is the bar.
2. **`test.ps1`** — `dotnet test NWO.Tests`. Pass `-Filter` to scope, e.g.
   `& .claude/skills/run-checks/test.ps1 -Filter FullyQualifiedName~Combat`.
3. **`scene-check.ps1`** — `godot --import` then instantiates a scene headless and
   greps the log for `ERROR|SCRIPT ERROR|Cannot|Exception|Node not found`. Override
   the scene with `-Scene 'res://scenes/ui/MainMenu.tscn'`.

To run just one step, invoke that script directly.

## Why the scene check matters

`dotnet build` and `dotnet test` cannot catch scene-load / `GetNode` path / node-wiring
errors — those only surface when Godot instantiates a `.tscn`. The scene check fills
that gap. A clean `WorldMap` run prints `Map seed: …`.

**Limitation:** headless has no input/display, so button-driven flows (menus,
save/load click-through) still need an interactive editor run (open `project.godot`,
press **F5**). Flag those for the user rather than claiming they're verified.

Reference: [docs/TECH_STACK.md](../../../docs/TECH_STACK.md) "Build, Test & Verify".
The Godot .NET binary location is recorded in the `godot-binary-for-headless-checks`
memory; `scene-check.ps1` hard-codes it with a PATH fallback.

## Maintenance

If the build/test commands, the Godot path, or the error-grep patterns change, update
the scripts here so this stays the single source of truth for "is it green?".
