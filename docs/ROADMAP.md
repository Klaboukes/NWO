# Development Roadmap

---

## Phase 0 — Foundation ✅ COMPLETE

Goal: A hex map renders on screen. Nothing is interactive yet.

- [x] Set up Godot 4 project with C# enabled
- [x] Implement `HexGrid` utility (axial coords, neighbors, distance, ring)
- [x] Implement `MapGenerator` (fractal heightmap → terrain assignment)
- [x] Render the generated map (Node2D + _Draw() with coloured polygons; TileMap deferred to Phase 5 when art assets exist)
- [x] Add basic camera (pan with WASD/arrows, zoom with scroll wheel)
- [x] Terrain data hardcoded in `MapGenerator.cs` — `terrain.json` deferred (no gameplay need until Phase 4)

**Done when:** You can see a procedurally generated hex map in the Godot editor running the scene.

---

## Phase 1 — Units & Movement ✅ COMPLETE

Goal: A unit exists on the map and can be moved by clicking.

- [x] Create `Unit` data class and `UnitNode` scene
- [x] Load unit definitions from `data/units.json`
- [x] Place a starting Warrior on the map
- [x] Implement A* pathfinding on the hex grid
- [x] Highlight reachable tiles on unit selection (movement range overlay)
- [x] Click-to-move: animate unit moving along path
- [x] Fog of war: reveal tiles around unit, grey out previously seen tiles

**Done when:** You can click a Warrior, see its movement range, click a destination, and watch it move there.

---

## Phase 2 — Turn System & Cities ✅ COMPLETE

Goal: A full turn loop runs. Cities grow. Production works.

- [x] Implement `TurnManager` (player turn → AI turn → advance turn number)
- [x] Implement `City` with population, food basket, production queue
- [x] Found a city with a Settler unit (select Settler → press F or click "Found City")
- [x] City UI panel: shows yields, production queue, build menu
- [x] Process yields at end of turn (food accumulation, growth, production progress)
- [x] "End Turn" button wires up to `TurnManager`
- [x] Notifications: city grew, production complete

**Done when:** You can found a city, queue a Warrior to build, end turns, and watch the city grow and produce the unit.

---

## Phase 3 — Combat & AI ✅ COMPLETE

Goal: Combat works. An AI opponent exists and fights back.

- [x] Implement combat resolution (attack/defense formula, HP damage)
- [x] Melee attack: click enemy unit to attack if in range
- [x] Ranged attack: Archer attacks adjacent-but-not-adjacent (range 2)
- [x] Unit death and removal from scene
- [x] City capture: military unit moves into city tile → city changes owner
- [x] Implement `AIController` (reactive: attack nearby, expand with Settler, queue Warrior)
- [x] AI takes its turn after player clicks "End Turn"

**Done when:** You can fight an AI unit, capture an AI city, and the AI can attack your units.

---

## Phase 4 — City Management ✅ COMPLETE

Goal: Cities feel alive. Yields are driven by which tiles their citizens work,
not a static number computed once at founding.

- [x] Per-city `Workforce` (focus + assigned tiles + locked tiles)
- [x] `CityWorkforceService.Recompute` — auto-assigns citizens by focus, layered on top of locked picks
- [x] Civ 5 city-center floor (2F / 1P minimum at the city tile)
- [x] Tile control: nearest city center within work radius wins; earlier-founded breaks ties
- [x] Enemy units blockade worked tiles (re-assigned on next turn)
- [x] Three focus modes in the city panel: Balanced / Food / Production
- [x] Right-click a workable tile on the map to lock/unlock a citizen
- [x] WorldRenderer tints workable / assigned / locked tiles when a friendly city is selected
- [x] Yields recompute on found, capture, building completion, growth, and end-of-turn

**Done when:** You can found a city, switch its focus, lock a Hills tile to bias toward production, and watch the food/production numbers change as citizens reassign.

---

## Phase 5 — Tech Tree & Economy ✅ COMPLETE

Goal: Research feels meaningful. Gold matters.

- [x] Load tech tree from `data/techs.json`
- [x] Tech tree UI: show available techs, prerequisites, costs
- [x] Science accumulates per turn; tech completes when threshold met
- [x] Unlocks wire up: researching a tech enables its building/unit in menus
- [x] Gold income and unit maintenance cost
- [x] Negative gold → disband cheapest unit

**Done when:** You can research Horseback Riding, then build Horsemen in your cities.

---

## Phase 5.5 — Pre-Phase-6 hardening (in progress)

Foundational gameplay/UX work so Phase 6 lands on solid systems. Sequenced:

- **M1 — Combat & survivability ✅ COMPLETE**
  - [x] Cities have HP + defense strength (base + population + Walls + garrison)
  - [x] Capture is earned: bombard a city to 0 HP, then move a melee unit in
        (ranged/civilians can't capture; captured cities start at half HP)
  - [x] `Walls` finally grants its +5 city defense
  - [x] Units heal when idle (+10, +15 near a friendly city)
  - [x] Combat-odds preview on hover + on-map HP bars for units & cities
- **M2 — Economy & expansion** (next up)
  - [ ] Tile improvements: `enum ImprovementType { None, Farm, Mine, Road, Pasture }`
        in `MapData`; worker gets a multi-turn build task (`Unit.CurrentTask`)
        processed in `EndPlayerTurn`. Yields via `TerrainYields`/`CityWorkforceService`:
        Farm +1 food, Mine +1 prod, Pasture +1 prod (Animal Husbandry), Road halves
        move cost. Tech-gated (Mining→Mine, etc.).
  - [ ] Per-tile gold: add Gold to `TerrainYields`, sum worked-tile gold in
        `CivEconomyService.GoldPerTurn` (currently buildings-only). "Buy" button in
        city panel spends treasury to finish production (cost ∝ remaining).
  - [ ] Strategic resources: `MapGenerator` scatters horses (Plains/Grassland) and
        iron (Hills) into `MapData`; `ResourceService` answers "does this civ work
        resource X"; enforce `UnitData.RequiredResource` in the build-list filter
        (`UIController.ShowCityPanel`); reveal only after the revealing tech.
- **M3 — Readable HUD**
  - [ ] Minimap: new `MinimapController` (+ `UI.tscn` node) drawing scaled map with
        fog, unit/city dots, camera viewport rect; click to recenter.
  - [ ] Event log: replace `List<string>` notifications with
        `GameEvent { string Text; Vector2I? Focus }` out of `EndPlayerTurn`/session;
        `EventLogController` shows last N, click focuses the tile.
  - [ ] City list/cycle hotkey (`C`) to jump between own cities.
  - [ ] Tile hover tooltip: terrain + yields + improvement/resource.
- **M4 — Competent AI** (rework `AIController.TakeTurn`)
  - [ ] Research an available tech via `CivEconomyService.SetResearch` when idle.
  - [ ] Production mix: early Settler/Worker when safe; defenders for undefended
        cities; attackers gated by tech/resources; Walls when threatened.
  - [ ] Expansion: move Settlers to scored valid sites (≥ MinCityDistance), found there.
  - [ ] Military: use `CombatResolver.Expected` to avoid suicidal attacks; garrison
        cities; retreat damaged units toward a friendly city to heal.
  - [ ] City focus set by need (growth vs. production).

> Sequencing M2 → M3 → M4. Each milestone: implement → `dotnet build` (0 warnings)
> → `dotnet test` (green, add tests) → sync `docs/` (drop matching **[planned]**
> flags) → commit. M3 also needs a manual Godot run (new draw code).

---

## Phase 6 — Win Conditions & Polish (Week 13–14)

Goal: The game has a beginning, middle, and end.

- [ ] Domination victory: detect when player captures AI capital → show victory screen
- [ ] Score victory: at turn 500, compare scores → show result screen
- [ ] Main menu scene (New Game, Load Game, Quit)
- [ ] Save/load game state to JSON file
- [ ] Mini-map
- [ ] Notification bar (scrolling event log)
- [ ] Basic sound effects (unit move, attack, city founded)

**Done when:** You can start a new game, play to a win or loss, and return to the main menu.

---

## Post-MVP (Future)

After shipping the MVP, the natural next steps are:

- Additional civilizations with unique abilities
- Diplomacy system (peace, war declarations, trade deals)
- Full tech tree (50+ techs)
- Culture and borders
- Religion
- More unit types (naval, siege)
- **Cross-continent / multi-island AI spawning.** For MVP the AI is confined to
  the player's landmass (see `WorldMap.PickAISpawn`); once naval units exist,
  drop that constraint and let each player spawn on a separate continent so
  expansion and exploration have meaning.
- Map editor
- Multiplayer (hot-seat, then network)
- Mod support (data-driven via JSON already helps here)

---

## Definition of Done (MVP)

The MVP is complete when a developer can:

1. Launch the game from the Godot editor
2. Generate a new map, found a city, build units, and research techs
3. Fight the AI and either capture its capital (win) or lose their own
4. Save and reload the game without data loss
