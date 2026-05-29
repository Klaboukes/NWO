# Claude Code Instructions

## Project Docs

This is **NWO**, a Civ 5-inspired turn-based 4X strategy game built in
**Godot 4 + C# (.NET 8)**. Before working on a feature, consult the relevant
design doc in `docs/`:

- [docs/OVERVIEW.md](docs/OVERVIEW.md) — vision, MVP scope, what's out of scope
- [docs/TECH_STACK.md](docs/TECH_STACK.md) — engine, language, folder layout, axial hex coords
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — singletons + EventBus, key classes, turn flow, AI
- [docs/MECHANICS.md](docs/MECHANICS.md) — terrain/unit/city/tech/combat rules and numbers
- [docs/ROADMAP.md](docs/ROADMAP.md) — phased plan; current progress tracked via checkboxes

These docs are the source of truth for design intent. Keep `docs/ROADMAP.md`
checkboxes in sync as phases complete (ties into the auto-commit rule below).

## Git & GitHub

- Auto-commit and push to `origin/main` when a roadmap phase is marked complete.
- Also commit and push on explicit user request ("commit", "push", etc.).
- Use the git identity already configured in this repo (`Klaboukes` / `barthoukes@gmail.com`).
- Follow the standard commit format: concise subject line, blank line, short body if needed, trailing `Co-Authored-By` line.

## UI/UX — Civ 5 inspired controls

The game is loosely modeled on Civilization V. Players coming from Civ 5 must find the controls (mouse, keyboard, unit actions) intuitively familiar. When adding or changing UI/UX, match Civ 5 conventions before inventing new ones.

### Mouse
- **Left-click** on own unit or city: select it (centers camera smoothly).
- **Left-click** on an empty/unselectable tile: deselect.
- **Right-click** on a reachable tile while a unit is selected: execute the move.
- **Middle-mouse drag**: pan the camera.
- **Mouse hover** while a unit is selected: shows the path preview to the hovered tile — no button held.
- **Scroll wheel**: zoom.

### Keyboard
- **WASD / arrow keys**: pan camera.
- **Enter**: end turn (or advance the end-turn queue if a prompt is active).
- **Tab**: cycle to next unit needing attention.
- **Space**: skip the current end-turn-queue item.
- **B**: Build city (when a settler is selected).
- **F**: Fortify selected unit (Found city if the unit is a settler — `B` is preferred).
- **Esc**: cancel / deselect / clear notification.

## UI/UX — Camera & Game Progression Flow

These rules govern how the camera behaves and how the end-turn queue advances. All future edits must respect this flow.

### Camera centering
- Selecting a unit or city (player click) centers the camera on it immediately and smoothly (exponential lerp).
- Manual keyboard/mouse-drag panning cancels any in-progress camera tween.

### Unit movement
- While a unit animates, the camera follows it each frame (`_cameraTarget = _animPos`).
- When animation ends, the camera rests exactly on the destination tile.

### End-turn queue progression
- When animation ends, **game state advances immediately**: `PruneAndShowEndTurnQueue()` is called right away, selecting the next unit/city in the queue (or triggering `ProcessTurn` if the queue is empty).
- **Camera centering to the next item is deferred** by `PostAnimCenterDelay` (0.5 s), so the player can see the destination before the camera jumps away. This is implemented via `_postAnimCenterDelay` + `_deferredCenterPos` — the position is stored in `DeferOrCenter()` and applied once the timer expires in `_Process`.
- If the player presses Space or F during the delay, `AdvanceEndTurnQueue` clears the timer and deferred position so the next item centers immediately.
- Pressing End Turn for the first time (no animation pending) shows and centers on the first queue item immediately — no delay.
