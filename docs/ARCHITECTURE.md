# Technical Architecture

> **Status:** Reflects the implementation through Phase 5 (tech tree & economy).
> Items still on the roadmap are flagged **[planned]**.

---

## Core Pattern: Headless model + scene coordinator

There is **no EventBus and there are no autoloaded singletons.** Instead the
codebase splits cleanly into pure-logic gameplay code (no Godot scene
dependencies, fully unit-testable) and a thin Godot presentation layer.

```
Pure logic (scripts/core, scripts/map, scripts/entities, scripts/ai)
├── GameState          — authoritative world model + pure operations on it
├── GameSession        — headless turn driver (player actions + end-turn loop)
├── TurnManager        — plain turn counter
├── AIController        — reactive AI, mutates GameState directly
├── CityWorkforceService — citizen auto-assignment + yield recompute (static)
├── CivEconomyService  — gold/science/research/disband + rush-buy per turn (static)
├── ResourceService    — strategic-resource reveal + access gating (static)
├── ImprovementService — tile-improvement rules: tech/terrain/yields (static)
├── CombatResolver     — pure combat formula (static)
├── DataCatalog        — id-indexed lookup of unit/building/tech data
├── DataLoader         — loads the JSON data files at startup
├── FogOfWar           — per-player visible/discovered sets
├── HexGrid            — static axial hex math + pathfinding
└── MapGenerator       — procedural map generation

Godot scene layer (scripts, scenes)
├── WorldMap (Node2D)  — scene coordinator: owns GameState, wires input,
│                         camera, animation, selection, queue, renderer, UI
├── WorldRenderer      — immediate-mode (_Draw) rendering of map/units/cities
├── UIController       — HUD widgets (top bar, city/unit panels, notifications)
├── TechTreePanelController — tech tree panel
├── CameraController   — pan/zoom/centering + post-anim delay
├── MovementAnimator   — tweens a unit along its path
├── SelectionState     — current selection + reachable/preview tiles
└── EndTurnQueue       — ordered "needs attention" items for end-turn flow
```

The live scene (`WorldMap`) and the headless tests (`NWO.Tests`) both drive the
same `GameSession`/`GameState`, so gameplay can be exercised without a running
scene. There is no `GameManager` state machine and no `MapManager` /
`CivilizationManager` — that responsibility lives on `GameState`.

---

## Design Decisions & Rationale

The original draft of this document (written before any code existed) proposed a
**Service Locator + EventBus + autoloaded-singletons** design — the pattern most
Godot tutorials reach for. Implementation deliberately went the other way. This
section records *why*, so the codebase isn't refactored back toward the tutorial
pattern by reflex.

### Rejected: EventBus + autoloaded singletons

1. **Testability.** The headless core (`GameState`, `GameSession`, the static
   services) runs under `NWO.Tests` with no SceneTree. Autoloaded singletons are
   bound to the Godot node lifecycle and are hard to instantiate or reset in
   isolation, which would have made that test suite far harder to write.
2. **Explicit state ownership.** One owned `GameState` object is straightforward
   to serialize (needed for Phase 6 save/load) and to instantiate more than once
   (AI lookahead, parallel test scenarios). Autoloads are implicit global mutable
   state with diffuse data flow.
3. **Sequential turn loop.** A turn-based game is inherently ordered. An explicit
   call chain (`GameSession.EndTurn()` → `GameState.EndPlayerTurn()` → services)
   is easier to trace and debug than signals fanning out across a global bus.
4. **Determinism.** Seeded combat RNG and deterministic tie-breaking give
   reproducible tests/replays; a global event bus works against that.

### Accepted: events at the scene boundary only

C# events *are* used — but only locally, to decouple the presentation layer from
the coordinator: `MovementAnimator.Completed`, `UIController.EndTurnPressed`,
`UIController.FoundCityPressed`. This is the disciplined use of events (UI/animation
→ `WorldMap`) without the downsides of a global, app-wide pub/sub hub.

### Deferred, not rejected

- **Scene-per-entity rendering.** The original plan had each Unit/City/Tile as its
  own scene; the implementation draws them in immediate mode
  (`WorldRenderer._Draw`). Revisit when art assets land (also see TECH_STACK.md).

### Capital tracking

`City.IsCapital` is tracked (set on the first city a player founds, in
`GameState.TryFoundCity`) specifically to enable the Phase 6 domination victory.
A captured capital **keeps** the flag — `CaptureCity` changes only `Owner` — so
"capture the opponent's capital" can be detected on the original capital city.

---

## Data vs. Instance

- **Data**: plain C# classes loaded from JSON, indexed by id in `DataCatalog`.
  Never modified at runtime.
  - `UnitData`, `BuildingData`, `TechData`
- **Model**: plain C# runtime state held in `GameState` (no Godot `Node`s).
  - `Unit`, `City`, `Civilization`, `Player`
- **Visuals**: the entities are **not** separate Godot scenes. `WorldRenderer`
  draws the map, units, and cities in immediate mode (`_Draw`). Only the HUD,
  tech tree, and world root are `.tscn` scenes.

A `Unit` holds a `UnitData` reference (its type) plus runtime state (HP,
position, owner, fortified).

---

## Key Classes

### `HexGrid` (static utility)
Flat-top axial coordinates. Cost-based pathfinding (not a boolean passability
predicate) — `int.MaxValue` means impassable.
```csharp
Vector2I[]     GetNeighbors(Vector2I axial)
int            Distance(Vector2I a, Vector2I b)
List<Vector2I> GetRing(Vector2I center, int radius)
List<Vector2I> GetRange(Vector2I center, int radius)               // filled disc
List<Vector2I> GetReachableTiles(Vector2I origin, int movementPoints,
                                 Func<Vector2I,int> movementCost)   // Dijkstra
List<Vector2I> FindPath(Vector2I from, Vector2I to,
                        Func<Vector2I,int> movementCost)            // A*
```

### `GameState`
Authoritative world model. Owns no Godot scene state.
```csharp
class GameState {
    MapData      Map;
    DataCatalog  Catalog;
    TurnManager  TurnManager;
    List<Player> Players;
    List<Unit>   Units;
    List<City>   Cities;
    int          CurrentPlayerIndex;
    Player       CurrentPlayer;            // => Players[CurrentPlayerIndex]
    // per-player fog + civ stored internally, reached via Fog(p) / Civ(p)
}
```
Exposes pure operations: `TryFoundCity`, `TryAttack`, `CaptureCity`,
`EndPlayerTurn`, `RecomputeFog`, `MovementCost`, `GetConnectedLandmass`.

### `GameSession`
Headless driver shared by the scene and tests. Player actions (`TryMove`,
`TryAttack`, `TryFoundCity`, `Fortify`) mutate state immediately; `EndTurn()`
ends the viewer's turn, runs each AI player synchronously, and returns the
notification stream.

### `Player` (identity) vs. `Civilization` (state)
Identity and civ-wide state are split into two types:
```csharp
record Player {                 // identity, set once
    int    Id;
    string Name;
    bool   IsHuman;
    Color  Color;
}
class Civilization {            // mutable civ-wide economy/research
    Player          Owner;
    int             Treasury;
    int             ScienceAccumulated;
    string?         CurrentResearch;       // tech id
    HashSet<string> ResearchedTechs;
}
```
Units and cities are **not** held on `Civilization` — they live in
`GameState.Units` / `GameState.Cities` and are filtered by `Owner`.

### `Unit`
```csharp
class Unit : IEndTurnItem {
    UnitData Data;             // type definition (stats, name)
    Player   Owner;
    Vector2I Position;         // axial coords
    int      HP;               // 0–100, dies at 0
    int      MovementRemaining;
    bool     Fortified;        // sleeps until woken by an order
}
```

### `City`
```csharp
class City : IEndTurnItem {
    string          Name;
    Player          Owner;
    Vector2I        Position;
    bool            IsCapital;             // first city the player founds; kept on capture
    int             HP;                    // 0..MaxHP; depleted by attacks, regen when calm
    bool            AttackedSinceTurn;     // gates HP regen
    int             Population;
    float           FoodAccumulated;
    int             ProductionProgress;
    string?         ProductionItem;        // "unit:warrior" / "building:granary"
    int             FoodYield;             // recomputed by CityWorkforceService
    int             ProductionYield;       //  "
    HashSet<string> Buildings;             // building ids
    CityWorkforce   Workforce;             // focus + assigned + locked tiles
    int             GrowthThreshold;       // => 15 + 6 * Population
}
```
There is no `Territory` or `WorkedTiles` field. Worked tiles live in
`Workforce.Assigned`; tile ownership is computed on demand by
`CityWorkforceService.ControllingCity` (nearest city center within work radius).
`IsCapital` is set in `GameState.TryFoundCity` (see Design Decisions above).

---

## Turn Flow (code sequence)

There is no `StartPlayerTurn` / `StartAITurn` ceremony and no turn-start event.
The whole loop lives in `GameSession.EndTurn()` → `GameState.EndPlayerTurn()`:

```
GameSession.EndTurn()
  → GameState.EndPlayerTurn(viewer)
      → for each of the player's cities:
          recompute workforce/yields → ProcessFood (growth)
          → AdvanceProduction (completion) → RegenIfUnharassed (city HP)
      → heal idle units, then reset the player's units' MovementRemaining
      → CivEconomyService.ProcessEndOfTurn  (gold, science, research, disband)
      → advance CurrentPlayerIndex; wrap → TurnManager.AdvanceTurn()
  → while CurrentPlayer is not human:
      AIController.TakeTurn(aiPlayer)
      GameState.EndPlayerTurn(aiPlayer)
  → RecomputeFog(viewer); return notifications + production completions
```

Win-condition checking is **not** wired into this loop yet — see **[planned]**
under Saving/Win below.

On the scene side, `WorldMap` adds a Civ-5-style "end-turn queue": before the
turn actually ends it walks the player's idle units and idle cities
(`EndTurnQueue`), centering on each so nothing is skipped accidentally.

---

## Pathfinding

**A\*** on the axial hex grid (`HexGrid.FindPath`), cost-based:

- Graph nodes: `Vector2I` axial coordinates
- Edge cost: movement cost to enter the destination tile (`TerrainYields`)
- Impassable (`int.MaxValue`): Mountain, Ocean, Coast (no naval units yet),
  and — at the caller's discretion — enemy-occupied tiles
- Heuristic: axial hex distance
- A `MaxExpansions` ceiling (50k) guards against runaway searches when no path
  exists.

`GetReachableTiles` uses Dijkstra over the same cost function for the movement
overlay.

---

## AI (Reactive)

`AIController` runs one pass per turn. Per unit, in order:

1. **Attack**: if an enemy unit is within attack range, attack it.
2. **Settle**: if the unit is a Settler, try to found a city on its *current*
   tile (it does not seek out a site ≥3 tiles away — it just walks toward the
   enemy and settles when founding succeeds).
3. **Advance**: otherwise step toward the nearest enemy unit or city, stopping
   short of occupied tiles. It bombards an enemy city in range and only captures
   one once its HP is depleted (the same rule the player follows).

Idle AI cities always queue a **Warrior**. The current AI does **not** build
other units/buildings, does **not** manage city focus, and does **not** research
techs. It also sees through fog (iterates `GameState.Units` directly).

In `WorldMap` the AI player is seeded as a single opponent named "Barbarians",
confined to the player's landmass (`GetConnectedLandmass` + `PickAISpawn`).

---

## UI Layout

The HUD (`UIController`, `scenes/ui/UI.tscn`) currently provides:

```
┌─────────────────────────────────────────────────────────┐
│  Top bar: Turn # | Treasury (+/turn) | Science (+/turn)  │
├─────────────────────────────────────────────────────────┤
│                                                           │
│           HEX MAP (WorldRenderer, immediate mode)         │
│   [selection highlight] [movement overlay] [path preview] │
│   [worked/locked tile tints when a city is selected]      │
│                                                           │
│  Notification label (transient or persistent)             │
│                                                           │
│  Side panels: Unit panel / City panel (build + focus)     │
│  Buttons: [End Turn]  [Found City]  •  Tech tree on [T]   │
└─────────────────────────────────────────────────────────┘
```

**[planned]** Mini-map, scrolling event log, score readout, and a city-list
button are Phase 6 and not implemented yet.

---

## Saving / Loading & Win Conditions **[planned]**

Not implemented yet (Phase 6). There is currently no serialization of
`GameState`, no auto-save, and no victory/defeat detection. When added, the plan
is to serialize `GameState` to JSON via Godot's `FileAccess` with one save slot
and an auto-save at the start of each player turn.

---

## Performance Targets

| Metric | Target |
|--------|--------|
| Map generation (60×40) | < 1 second |
| Turn processing | < 200 ms |
| Frame rate during gameplay | 60 fps |
| Memory (all game data loaded) | < 256 MB |
