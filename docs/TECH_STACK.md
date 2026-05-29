# Tech Stack

---

## Engine: Godot 4.6 (.NET / C#)

**Why Godot 4:**
- Free and open-source, exports natively to Windows and macOS
- C# support via .NET — full language features, good IDE integration
- Lightweight — no runtime royalties, no launcher, no account required

**Rendering note:** the original plan was a `TileMap` with each Unit/City/Tile as
its own scene. The implementation instead renders the map, units, and cities in
**immediate mode** (`WorldRenderer._Draw` with coloured polygons). Only the HUD
(`UI.tscn`), tech tree (`TechTreePanel.tscn`), and world root (`WorldMap.tscn`)
are scenes. A `TileMap` migration is deferred until art assets exist.

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

There is no `assets/` directory or `terrain.json` yet — terrain numbers live in
`TerrainYields.cs`, and there are no art/audio assets (immediate-mode rendering).
Gameplay logic in `scripts/` is engine-light and exercised by `NWO.Tests`
without a running scene.

---

## Data Format: JSON

Unit types, building types, tech nodes, and terrain definitions are stored as JSON in `data/`. This keeps game-designers (or your future self) from needing to touch C# to tweak numbers.

---

## Coordinate System: Axial (Hex)

Use **axial coordinates** (q, r) for the hex grid — not offset coordinates. Axial math is simpler for pathfinding, distance, and neighbor lookup. Godot's `TileMap` in hex mode works natively with this system.

Reference: https://www.redblobgames.com/grids/hexagons/ (the canonical hex grid guide)
