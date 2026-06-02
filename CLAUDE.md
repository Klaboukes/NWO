# Claude Code Instructions

## Project Docs

This is **NWO**, a Civ 5-inspired turn-based 4X strategy game built in
**Godot 4 + C# (.NET 8)**. Before working on a feature, consult the relevant
design doc in `docs/`:

- [docs/OVERVIEW.md](docs/OVERVIEW.md) — vision, MVP scope, what's out of scope
- [docs/TECH_STACK.md](docs/TECH_STACK.md) — engine, language, folder layout, axial hex coords
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — singletons + EventBus, key classes, turn flow, AI
- [docs/MECHANICS.md](docs/MECHANICS.md) — terrain/unit/city/tech/combat rules and numbers
- [docs/LORE.md](docs/LORE.md) — setting, tone, and world canon (planet Cradle, ark Exodus, the Sundering)
- [docs/FACTIONS.md](docs/FACTIONS.md) — faction identities, signature passives, and unique-unit hooks (Phase 10 implementation index)
- [docs/ROADMAP.md](docs/ROADMAP.md) — phased plan; current progress tracked via checkboxes
- [docs/MAP_GENERATION.md](docs/MAP_GENERATION.md) — procedural terrain generation patterns; NWO-specific notes on the layered noise pipeline, moisture biome axis, and resource placement tiers
- [docs/CONTROLS.md](docs/CONTROLS.md) — full mouse/keyboard spec, camera centering rules, end-turn queue flow

These docs are the source of truth for design intent. Keep `docs/ROADMAP.md`
checkboxes in sync as phases complete (ties into the auto-commit rule below).

## Skills

Repo-specific workflows are encoded as skills in `.claude/skills/` — invoke the one
matching the task instead of re-deriving the procedure:

- `run-checks` — build + test + headless scene check (the verify loop; bundled `.ps1`).
- `add-content` — add a unit/building/tech in `data/*.json`.
- `tune-mechanics` — change gameplay numbers/balance in the headless core.
- `tune-map-generation` — tune terrain shape / biomes / mountains / rivers / resource scatter in `MapGenerator.cs` (bundled headless histogram diagnostic).
- `add-art-asset` — drop in Phase 7 terrain tiles / sprites.
- `generate-terrain-art` — re-bake procedural pixel-art terrain tiles (`assets/art/tiles/`); use after adding a terrain type or tweaking palette/motif.
- `hud-ui` — HUD / UI / controls work (Civ 5 conventions).
- `finish-phase` — tick the roadmap, sync docs, verify, commit+push.
- `build-standalone` — export a Windows standalone build to `build/NWO.exe` (requires export templates + `export_presets.cfg`; see Phase 8).

Skills run commands in **PowerShell**. They keep improving: when a procedure drifts,
update the relevant `SKILL.md` (each has a `## Maintenance` note).

## Git & GitHub

- Auto-commit and push to `origin/main` when a roadmap phase is marked complete.
- Also commit and push on explicit user request ("commit", "push", etc.).
- Use the git identity already configured in this repo (`Klaboukes` / `barthoukes@gmail.com`).
- Follow the standard commit format: concise subject line, blank line, short body if needed, trailing `Co-Authored-By` line.

## Markdown Style

All `.md` files must pass **markdownlint** (VS Code extension; config in
`.markdownlint.json` at the repo root). Keep new and edited files clean.

## UI/UX

Match Civ 5 conventions for all controls and camera behaviour. Full spec — mouse,
keyboard, camera centering, end-turn queue flow — is in
[docs/CONTROLS.md](docs/CONTROLS.md).
