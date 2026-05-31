# Tech Stack

---

## Engine: Godot 4.6 (.NET / C#)

**Why Godot 4:**
- Free and open-source, exports natively to Windows and macOS
- C# support via .NET — full language features, good IDE integration
- Lightweight — no runtime royalties, no launcher, no account required

**Rendering note:** the MVP rendered the map in immediate mode (flat polygons under
a `Camera2D`). As of Phase 7 the world is **true 3D, fixed-tilt**: terrain is
hex-prism `MeshInstance3D`s and units/cities are `Sprite3D` billboards
(`WorldRenderer`, a `Node3D`) under a `Camera3D` (a Civ5-style telephoto lens —
narrow FOV, ~45° oblique tilt — so hex tiles read near-uniformly across the
viewport); the range/path/HP/selection/fog
overlays are drawn in a screen-space `WorldOverlay` (Node2D, `_Draw`) projected via
`Camera3D.UnprojectPosition`. Only the HUD (`UI.tscn`), tech tree
(`TechTreePanel.tscn`), and world root (`WorldMap.tscn`) are scenes.

**Why not Unity:** recent licensing changes make it risky for indie projects; heavier setup overhead.  
**Why not Unreal:** C++ complexity and 3D focus are overkill for a 2D hex strategy MVP.

---

## Language: C# (.NET 8)

- Strong typing catches bugs at compile time
- LINQ is useful for querying game state (finding valid moves, filtering units)
- Familiar to experienced devs from web/backend backgrounds
- Godot's C# API is first-class and well-documented

---

## Development Tools

| Tool | Purpose |
|------|---------|
| Godot 4.x | Game engine + editor |
| Visual Studio 2022 or Rider | C# IDE with Godot plugin |
| Git + GitHub | Version control |
| Tiled (optional) | External map editor if needed |

---

## Build, Test & Verify

The standard loop for any change: `dotnet build` (expect **0 warnings**) →
`dotnet test` (green) → **headless scene check** → commit.

```powershell
dotnet build NWO.sln                         # compile (treat warnings as failures)
dotnet test  NWO.Tests/NWO.Tests.csproj      # headless xUnit gameplay tests
```

**Headless scene checks** catch scene-load / `GetNode` path / node-wiring errors
that the C# compiler and xUnit can't (those only surface when Godot instantiates a
`.tscn`). Requires the **C#/.NET ("mono") build** of Godot — the plain build can't
load this project. The `_console.exe` variant prints to stdout, so prefer it for
capturing logs:

```powershell
# 1. Import + register classes; reports asset/script import errors
godot --headless --path . --import

# 2. Instantiate a scene for a few frames and watch for errors.
#    A clean WorldMap run prints "Map seed: …".
godot --headless --path . "res://scenes/world/WorldMap.tscn" --quit-after 30
godot --headless --path . --quit-after 20      # boot scene = MainMenu.tscn
```

Filter the output for `ERROR`, `SCRIPT ERROR`, `Cannot`, `Exception`, `Node not found`.

**Limitation:** headless has no input/display, so button-driven flows (menus,
save/load click-through) still need an interactive editor run (open `project.godot`,
press **F5**). Save files are written to `user://saves/*.json`
(`%APPDATA%\Godot\app_userdata\NWO\saves\` on Windows).

> If `godot` isn't found, the .NET build's folder must be on PATH (a `godot.cmd`
> shim alongside the executable works well). See `docs/ROADMAP.md` for what each
> phase additionally requires (e.g. milestones touching draw code need a manual run).

---

## Project Structure (actual)

```
NWO/
├── project.godot
├── NWO.sln  NWO.csproj          # game project
├── NWO.Tests/                   # xUnit test project (headless gameplay tests)
├── addons/                      # (empty placeholder)
├── scenes/
│   ├── world/   WorldMap.tscn   # main scene / coordinator
│   ├── ui/      UI.tscn, TechTreePanel.tscn
│   ├── entities/ game/          # (empty — entities are drawn, not scened)
├── scripts/
│   ├── core/    # GameState, GameSession, TurnManager, DataCatalog, DataLoader,
│   │            # CombatResolver, CivEconomyService, CityWorkforceService,
│   │            # FogOfWar, EndTurnQueue, SelectionState, MovementAnimator,
│   │            # CameraController, IEndTurnItem
│   ├── map/     # HexGrid, MapData, MapGenerator, TerrainType, TerrainYields,
│   │            # WorldMap, WorldRenderer
│   ├── entities/# Unit, UnitData, City, CityWorkforce, Civilization, Player,
│   │            # BuildingData, TechData
│   ├── ai/      # AIController
│   └── ui/      # UIController, TechTreePanelController
├── data/
│   ├── units.json      # Unit definitions (stats, cost, tech/resource reqs)
│   ├── buildings.json  # Building definitions (cost, yields, effects)
│   └── techs.json      # Tech tree nodes (cost, prereqs, unlocks)
└── docs/               # This folder
```

Terrain numbers live in `TerrainYields.cs`. Committed art is optional: the 3D
terrain prisms and unit/city billboards render from synthesized placeholders until
real PNGs are dropped into `assets/art/` (see the `add-art-asset` skill). Gameplay
logic in `scripts/` is engine-light and exercised by `NWO.Tests` without a running
scene.

---

## Data Format: JSON

Unit types, building types, tech nodes, and terrain definitions are stored as JSON in `data/`. This keeps game-designers (or your future self) from needing to touch C# to tweak numbers.

---

## Coordinate System: Axial (Hex)

Use **axial coordinates** (q, r) for the hex grid — not offset coordinates. Axial math is simpler for pathfinding, distance, and neighbor lookup. Godot's `TileMap` in hex mode works natively with this system.

Reference: https://www.redblobgames.com/grids/hexagons/ (the canonical hex grid guide)
