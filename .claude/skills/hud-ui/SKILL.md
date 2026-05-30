---
name: hud-ui
description: Build or change NWO's HUD, UI panels, theme, and player controls (mouse/keyboard/camera). Use when editing the top bar, unit/city panels, notifications, minimap, tech tree panel, Godot Theme, or input handling. Follows Civ 5 conventions.
allowed-tools: Read, Edit, PowerShell, Grep
---

# hud-ui

NWO's UX is loosely modeled on **Civilization V** — players from Civ 5 must find the
controls intuitively familiar. Match Civ 5 conventions before inventing new ones.

## Read these first (they are the spec)

The full control + camera/flow contract lives in
[CLAUDE.md](../../../CLAUDE.md): the **UI/UX — Civ 5 inspired controls** section
(mouse, keyboard hotkeys) and the **Camera & Game Progression Flow** section
(centering, unit-movement follow, end-turn-queue progression with `PostAnimCenterDelay`).
Honor these exactly — they were tuned deliberately.

## Architecture boundary

UI is **presentation only** and lives in the Godot scene layer. Never put gameplay
state or rules into UI code — read from `GameState`/`GameSession` and raise events back
to the coordinator. Events stay local to the scene boundary (e.g. `EndTurnPressed`,
`FoundCityPressed`, `SaveRequested`); there is no global EventBus and no autoload
besides `AudioManager`. Rationale: [docs/ARCHITECTURE.md](../../../docs/ARCHITECTURE.md).

## Where things live

| Area | File / scene |
|---|---|
| HUD widgets (top bar, unit/city panels, notifications, buttons) | `scripts/ui/UIController.cs`, `scenes/ui/UI.tscn` |
| Event log feed | `scripts/ui/EventLogController.cs` |
| Minimap | `scripts/ui/MinimapController.cs` |
| Tech tree panel | `scripts/ui/TechTreePanelController.cs`, `scenes/ui/TechTreePanel.tscn` |
| Main menu / victory / save browser | `scripts/ui/MainMenuController.cs`, `VictoryScreenController.cs`, `SaveBrowserController.cs` |
| Raw input → semantic intents | `scripts/map/WorldInputRouter.cs` |
| Camera pan/zoom/centering + post-anim delay | `scripts/core/CameraController.cs` |
| Tile tooltip dwell | `scripts/map/TileTooltipController.cs` |
| Scene coordination (owns state, wires UI/camera/selection) | `scripts/map/WorldMap.cs` |

Input flows raw events → `WorldInputRouter` (intents) → `WorldMap`; UI widgets raise
C# events that `WorldMap` handles. Phase 7.4 Theme work (pixel font, restyled
panels/buttons/bars, framed minimap) is a Godot `Theme` and is independent of the
renderer.

## Procedure

1. Confirm the desired behaviour against the CLAUDE.md control/flow spec; if it
   conflicts, prefer the spec (or flag the conflict to the user).
2. Edit the relevant controller/scene; keep state and rules out of the UI layer.
3. Route new input through `WorldInputRouter`; route new actions back via events.
4. Run the **`run-checks`** skill — the headless scene check catches `GetNode`/wiring
   breakage. **But** button-driven and visual flows need a human **F5** run; say so
   rather than claiming UI is verified headless.

## Maintenance

If a control binding, camera rule, or end-turn-queue behaviour changes, update the
relevant section of CLAUDE.md (the spec) in the same change — not just the code.
