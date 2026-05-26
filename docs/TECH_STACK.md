# Tech Stack

---

## Engine: Godot 4.x

**Why Godot 4:**
- Built-in `TileMap` node with hex grid support (offset and axial coordinates)
- Scene/Node system maps cleanly to game entities (each Unit, City, Tile is a scene)
- Free and open-source, exports natively to Windows and macOS
- C# support via .NET 8 — full language features, good IDE integration
- Lightweight — no runtime royalties, no launcher, no account required

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

## Project Structure (Godot)

```
NWO/
├── project.godot
├── addons/            # Third-party Godot plugins (if any)
├── assets/
│   ├── art/           # Sprites, tilesets, UI textures
│   ├── audio/         # SFX and music
│   └── fonts/
├── scenes/
│   ├── world/         # HexMap, WorldGenerator
│   ├── entities/      # Unit.tscn, City.tscn, Tile.tscn
│   ├── ui/            # HUD, menus, panels
│   └── game/          # GameManager, TurnManager
├── scripts/
│   ├── core/          # GameState, TurnSystem, EventBus
│   ├── map/           # HexGrid, MapGenerator, FogOfWar
│   ├── entities/      # Unit.cs, City.cs, Civilization.cs
│   ├── ai/            # AIController, AIStrategy
│   └── ui/            # HUDController, SelectionPanel
├── data/
│   ├── units.json     # Unit definitions (stats, cost, movement)
│   ├── buildings.json # Building definitions
│   └── techs.json     # Tech tree nodes
└── docs/              # This folder
```

---

## Data Format: JSON

Unit types, building types, tech nodes, and terrain definitions are stored as JSON in `data/`. This keeps game-designers (or your future self) from needing to touch C# to tweak numbers.

---

## Coordinate System: Axial (Hex)

Use **axial coordinates** (q, r) for the hex grid — not offset coordinates. Axial math is simpler for pathfinding, distance, and neighbor lookup. Godot's `TileMap` in hex mode works natively with this system.

Reference: https://www.redblobgames.com/grids/hexagons/ (the canonical hex grid guide)
