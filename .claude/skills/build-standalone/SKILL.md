# build-standalone skill

Export a self-contained Windows build of NWO that runs without Godot or .NET installed.

## Prerequisites (one-time, manual)

1. Open the Godot editor.
2. *Editor → Manage Export Templates* — download templates for Godot 4.6 stable (Mono).
3. *Project → Export → Add → Windows Desktop*:
   - Export path: `build/NWO.exe`
   - Enable **Embed PCK** (single file)
   - Under **.NET**: Export Mode = **Self-Contained**
4. Click **Close** — Godot writes `export_presets.cfg` automatically. Commit that file.

These steps are only needed once (or when upgrading the Godot version).

## Usage

```powershell
# Default output: build/NWO.exe
.\.claude\skills\build-standalone\build.ps1

# Custom output directory
.\.claude\skills\build-standalone\build.ps1 -OutDir dist
```

## Output

```
build/
  NWO.exe          ← native Windows launcher
  GodotSharp/      ← bundled .NET runtime (self-contained)
```

The `build/` folder is git-ignored. Share or zip the whole folder.

## Verification

1. Run `build/NWO.exe` on a machine **without** Godot installed.
2. Start a new game, play one turn, save, quit, reopen — confirm save/load works.
3. No "Cannot open assembly" or .NET runtime errors in the console.

## Maintenance

- If the Godot version changes, re-download export templates and update `export_presets.cfg`.
- If `--export-release` is renamed in a future Godot version, update the command in `build.ps1`.
