# Technical Architecture

> **Status:** Reflects the implementation through Phase 5 (tech tree & economy).
> Items still on the roadmap are flagged **[planned]**.

---

## Core Pattern: Headless model + scene coordinator

There is **no EventBus, and the only autoloaded singleton is `AudioManager`**
(presentation-only sound; see *Audio* below). Gameplay state and logic stay out of
autoloads. Instead the codebase splits cleanly into pure-logic gameplay code (no
Godot scene dependencies, fully unit-testable) and a thin Godot presentation layer.

```text
Pure logic (scripts/core, scripts/map, scripts/entities, scripts/ai)
├── GameState          — authoritative world model + pure operations on it
├── GameSession        — headless turn driver (player actions + end-turn loop)
├── TurnManager        — plain turn counter
├── AIController        — strategic AI, mutates GameState directly
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
├── WorldMap (Node3D)  — scene coordinator: owns GameState, wires input,
│                         camera, animation, selection, queue, renderer, UI
├── WorldInputRouter   — decodes raw input into semantic intents for WorldMap
├── TileTooltipController — tile-tooltip dwell timer (driven from WorldMap._Process)
├── HexProjection      — axial ↔ 3D-world math (AxialToWorld/WorldToAxial/TopHeight)
├── WorldRenderer (Node3D) — 3D world: hex-prism terrain + unit/city billboards
├── WorldOverlay (Node2D)  — screen-space overlay (range/path/HP/selection/glyphs/fog)
│                            drawn over the 3D view via Camera3D.UnprojectPosition
├── TerrainMeshFactory — builds the per-terrain hex-prism meshes
├── UIController       — HUD widgets (top bar, city/unit panels, notifications)
├── TechTreePanelController — tech tree panel
├── CameraController   — Civ5-style telephoto Camera3D (narrow FOV, ~45° oblique tilt) pan/zoom/centering + post-anim delay
├── MovementAnimator   — tweens a unit along its path
├── SelectionState     — current selection + reachable/preview tiles
└── EndTurnQueue       — ordered "needs attention" items for end-turn flow

Pure helpers (no scene state): CombatMessages (combat result text),
EventVisibilityFilter (per-viewer event-log rules), Scenes (scene-path constants).
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
  own scene. As of Phase 7 the terrain is 3D hex-prism `MeshInstance3D`s and
  units/cities are `Sprite3D` billboards (`WorldRenderer`, a `Node3D`), while
  selection/HP/range overlays are drawn in screen space by `WorldOverlay._Draw`.
  Real per-type art replaces the placeholder meshes/tokens with no structural change.

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
  (Node3D) builds the map as hex-prism meshes and units/cities as `Sprite3D`
  billboards; `WorldOverlay` (Node2D) paints range/path/HP/selection/fog on top.
  Only the HUD, tech tree, and world root are `.tscn` scenes.

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

```text
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
  → RecomputeFog(viewer)
  → VictoryService.Evaluate(state)   (win/loss check)
  → return notifications + production completions + GameResult?
```

`EndTurnSummary.Result` is non-null when the game is over. `VictoryService`
(headless, in `scripts/core/`) reports a **domination** win (one non-eliminated
player left — a player is eliminated only once they hold no city *and* no
settler-capable unit, so the opening turns don't trigger it) or, at
`ScoreVictoryTurn` (500), a **score** win ranked by `ScoreService.Score`
(cities/population/techs/gold). `WorldMap.ProcessTurn` stashes the result in the
`GameLaunch` static handoff and changes to `scenes/ui/VictoryScreen.tscn`.

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

## AI (Strategic)

`AIController` runs one pass per turn: **research → unit actions → production**.
It is deterministic (no RNG) — combat decisions use `CombatResolver.Expected`.

**Research.** When `CurrentResearch` is idle it picks the first tech in a curated,
economy-first preference list whose prerequisites are met (`SetResearch`).

**Units**, dispatched by role:

- *Settlers* found on the current tile when legal, else march to the best-scored
  nearby site (≥ `MinCityDistance`, ranked by surrounding yields).
- *Workers* build the best non-Road improvement on a controlled tile, or walk to
  the nearest improvable tile.
- *Military*, in order: attack an in-range enemy unit when the forecast favours
  it (never suicidal melee; ranged always fires) → bombard/assault an in-range city
  it can survive → retreat to the nearest city when wounded (HP < 35) → hold if
  it's a lone garrison → reinforce a threatened undefended city in range → else
  advance on the nearest enemy. Captures still require depleting a city's HP then
  moving a melee unit in (the same rule the player follows).

**Production** for each idle city: a defender if undefended → Walls if threatened
→ a Settler while below the expansion target and safe → a Worker (≈ one per city)
→ otherwise an attacker. All choices are gated by tech/resource availability
(mirrors the build-list filter). City focus is set by need (grow small cities,
else pump current production).

The AI sees through fog (iterates `GameState.Units`/`Cities` directly).

In `WorldMap` the AI player is seeded as a single opponent named "Barbarians",
confined to the player's landmass (`GetConnectedLandmass` + `PickAISpawn`).

---

## UI Layout

The HUD (`UIController`, `scenes/ui/UI.tscn`) currently provides:

``` text
┌─────────────────────────────────────────────────────────┐
│  Top bar: Turn # | Treasury (+/turn) | Science (+/turn)  │
├─────────────────────────────────────────────────────────┤
│                                                           │
│      HEX MAP (3D prisms + billboards; 2D overlay)         │
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

## Win Conditions

`VictoryService.Evaluate(GameState)` (headless) runs at the end of each
`GameSession.EndTurn` and returns a `GameResult?` (winner, `VictoryType`
Domination/Score, score). **Domination** fires when only one non-eliminated player
remains; a player is eliminated only once they own no city *and* hold no
settler-capable unit. **Score** fires at `ScoreVictoryTurn` (500), ranking players
by `ScoreService.Score` (cities×10 + population×3 + techs×5 + gold/10, tunable
constants). `WorldMap.ProcessTurn` routes a non-null result through the
`GameLaunch` static handoff to `scenes/ui/VictoryScreen.tscn`.

## Bootstrap & Scene Flow

`MainMenu.tscn` is the boot scene (`run/main_scene`). All paths into a match
converge in `WorldMap.ResolveLaunch`, which reads the `GameLaunch` static handoff:
`LoadedGame` (a deserialized `GameState`) resumes that save, otherwise
`GameFactory.NewGame(seed)` builds a fresh world (map generation, players, starting
units, AI spawn — all extracted from the old `_Ready`). `GameLaunch` also carries
`LastResult` to the victory screen. Scene changes use `GetTree().ChangeSceneToFile`;
a plain C# static survives the transition, so `GameLaunch` (gameplay handoff) stays
a static rather than an autoload — only audio, which needs a live persistent `Node`,
is.

## Audio

`AudioManager` is the project's single **autoload** (`/root/AudioManager`, registered
in `project.godot`). It's a `Node` so its small `AudioStreamPlayer` pool persists
across scene changes, giving MainMenu, WorldMap, and VictoryScreen one shared channel.
Callers use `AudioManager.Instance?.Play(Sfx.…)`; `Instance` is null under `NWO.Tests`
(no autoload runs there), so every trigger is a safe no-op headless. Each `Sfx`
(`Click, Move, Attack, CityFound, Win, Lose`) resolves once at startup to a real
`res://assets/audio/<name>.ogg` if present, otherwise to a tone synthesized in code
(`AudioStreamWav`) — so audio ships with no committed binaries and real clips can
be dropped in later with no code change.

## Saving / Loading

Split in two so the logic stays headless-testable:

- **`SaveSerializer`** (no Godot file IO) maps `GameState` ↔ a flat DTO graph and
  JSON via System.Text.Json. A `Vector2IJsonConverter` handles both values and
  dictionary keys (`MapData`'s dicts are keyed by `Vector2I`); enums serialize as
  strings. Ownership is stored by `Player.Id` and **rebound to the loaded `Player`
  instances** on `Deserialize` so `Civ()`/`Fog()` lookups resolve. The full
  `MapData` (tiles + resources + improvements) is serialized; `DataCatalog` is
  re-attached from `res://data`, not stored; fog `Visible` and city yields are
  recomputed rather than saved (only `Discovered` is persisted). Combat is
  reproduced from the stored seed (deterministic from the seed, not a byte-exact
  continuation of the pre-save RNG stream).
- **`SaveService`** wraps the serializer with `user://saves/*.json` file IO
  (`FileAccess`/`DirAccess`): `Save`, `Load`, `ListSaves`, `Delete`. Each file's
  header (name, timestamp, turn) drives the slot list.

UI: the reusable `SaveBrowser` modal lists slots (load/delete) and, in save mode,
takes a name; it's opened from `MainMenu` (load) and from the in-game **HUD
"Menu" → pause overlay** (save/load/main-menu/quit). Saving/loading needs the
livestate, so `UIController` raises `SaveRequested`/`LoadRequested`/`MainMenuRequested`
and `WorldMap` performs the `SaveService` call (loading re-enters via `GameLaunch`).

---

## Performance Targets

| Metric | Target |
| --- | --- |
| Map generation (60×40) | < 1 second |
| Turn processing | < 200 ms |
| Frame rate during gameplay | 60 fps |
| Memory (all game data loaded) | < 256 MB |
