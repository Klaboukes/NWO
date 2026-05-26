# Technical Architecture

---

## Core Pattern: Service Locator + Event Bus

Godot's node tree handles scene composition. Game logic systems are **autoloaded singletons** (Godot's equivalent of services) that any scene can access. Systems communicate via a central **EventBus** (signal-based pub/sub) to avoid tight coupling.

```
Autoloaded Singletons (always in scene tree):
├── GameManager       — owns game state machine (MainMenu, InGame, Paused, GameOver)
├── TurnManager       — tracks whose turn it is, fires turn start/end events
├── MapManager        — owns hex grid data, tile queries, pathfinding
├── CivilizationManager — owns all Civ objects, their units and cities
├── EventBus          — global signal hub (no direct node references needed)
└── DataLoader        — loads JSON data files at startup (units, buildings, techs)
```

---

## Data vs. Instance

- **Data**: plain C# records loaded from JSON. Never modified at runtime.
  - `UnitData`, `BuildingData`, `TechData`, `TerrainData`
- **Instance**: Godot nodes that hold runtime state and visuals.
  - `UnitNode`, `CityNode`, `TileNode`

Keep these separate. A `UnitNode` holds a `UnitData` reference (its type) plus runtime state (HP, position, owner).

---

## Key Classes

### `HexGrid` (static utility)
```csharp
// Axial coordinate helpers
Vector2I[] GetNeighbors(Vector2I axial)
int Distance(Vector2I a, Vector2I b)
List<Vector2I> GetReachableTiles(Vector2I origin, int movement)
List<Vector2I> FindPath(Vector2I from, Vector2I to, Func<Vector2I, bool> isPassable)
Vector2I[] GetRing(Vector2I center, int radius)
```

### `GameState`
```csharp
class GameState {
    int CurrentTurn;
    int CurrentCivIndex;          // whose turn it is
    List<Civilization> Civs;
    HexMapData Map;
    GamePhase Phase;              // UnitPhase, CityPhase, ResearchPhase, AIPhase
}
```

### `Civilization`
```csharp
class Civilization {
    string Name;
    Color Color;
    List<Unit> Units;
    List<City> Cities;
    int Gold;
    int ScienceAccumulated;
    TechNode CurrentResearch;
    List<TechNode> ResearchedTechs;
}
```

### `Unit`
```csharp
class Unit {
    UnitData Data;         // type definition (stats, name)
    Vector2I Position;     // axial coords
    int HP;
    int MovementRemaining;
    Civilization Owner;
    bool HasActed;
}
```

### `City`
```csharp
class City {
    string Name;
    Vector2I Position;
    Civilization Owner;
    int Population;
    int FoodBasket;
    int ProductionAccumulated;
    ProductionItem CurrentProduction;  // unit or building being built
    List<BuildingData> Buildings;
    List<Vector2I> Territory;          // tiles this city owns
    List<Vector2I> WorkedTiles;        // tiles currently being worked (≤ population)
    bool IsCapital;
}
```

---

## Turn Flow (code sequence)

```
TurnManager.StartPlayerTurn()
  → EventBus.Emit(TurnStarted, playerCiv)
  → Reset all player unit MovementRemaining
  → Phase = UnitPhase (player selects/moves units via UI)

Player clicks "End Turn"
  → TurnManager.EndPlayerTurn()
  → Process city yields for player
  → TurnManager.StartAITurn()
  → AIController.ExecuteTurn()        // moves AI units, queues AI production
  → Process city yields for AI
  → TurnManager.AdvanceTurn()
  → Check win conditions
  → TurnManager.StartPlayerTurn()     // loop
```

---

## Pathfinding

Use **A\*** on the axial hex grid.

- Graph nodes: `Vector2I` hex coordinates
- Edge cost: destination tile movement cost (from `TerrainData`)
- Impassable: Mountain, enemy-occupied tiles, water (for land units)
- Heuristic: axial hex distance

Godot 4's `AStarGrid2D` can be configured for hex grids, or implement a custom A* over the axial grid for full control.

---

## AI (MVP — Reactive)

The MVP AI is a simple **reactive agent**, not a strategic planner. It runs one pass per turn:

1. **Expand**: If AI has a Settler and a valid founding site >3 tiles from existing cities, move Settler toward it and found.
2. **Attack**: For each military unit, if an enemy unit or city is within movement range, attack. Otherwise move toward nearest enemy.
3. **Produce**: If a city has no production queued, build a Warrior. If population > 3, build a Granary first.
4. **Research**: Pick the first available unresearched tech.

The AI does not trade, negotiate, or plan more than 1 turn ahead. That's intentional for MVP.

---

## UI Layout

```
┌─────────────────────────────────────────────────────────┐
│  Top Bar: Turn # | Civilization | Gold | Science | Score │
├─────────────────────────────────────────┬───────────────┤
│                                         │               │
│           HEX MAP (main view)           │  Mini-map     │
│                                         │               │
│   [selected unit highlight]             ├───────────────┤
│   [movement range overlay]              │  Selection    │
│                                         │  Panel        │
├─────────────────────────────────────────┤  (unit/city   │
│  Notification bar (scrolling events)    │   details)    │
├─────────────────────────────────────────┴───────────────┤
│  [End Turn]  [Tech Tree]  [City List]                    │
└─────────────────────────────────────────────────────────┘
```

---

## Saving / Loading (MVP)

- Use Godot's built-in `FileAccess` to serialize `GameState` to JSON
- One save slot in MVP (no save management UI needed)
- Auto-save at start of each player turn

---

## Performance Targets

| Metric | Target |
|--------|--------|
| Map generation (60×40) | < 1 second |
| Turn processing | < 200 ms |
| Frame rate during gameplay | 60 fps |
| Memory (all game data loaded) | < 256 MB |
