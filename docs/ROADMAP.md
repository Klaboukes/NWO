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

## Phase 5.5 — Pre-Phase-6 hardening ✅ COMPLETE

Foundational gameplay/UX work so Phase 6 lands on solid systems. Sequenced:

- **M1 — Combat & survivability ✅ COMPLETE**
  - [x] Cities have HP + defense strength (base + population + Walls + garrison)
  - [x] Capture is earned: bombard a city to 0 HP, then move a melee unit in
        (ranged/civilians can't capture; captured cities start at half HP)
  - [x] `Walls` finally grants its +5 city defense
  - [x] Units heal when idle (+10, +15 near a friendly city)
  - [x] Combat-odds preview on hover + on-map HP bars for units & cities
- **M2 — Economy & expansion ✅ COMPLETE**
  - [x] Tile improvements (`ImprovementType` Farm/Mine/Pasture/Road in `MapData`):
        Worker multi-turn build task (`Unit.CurrentTask`) ticked in `EndPlayerTurn`,
        cancelled on move; busy workers don't block End Turn. Yields via
        `ImprovementService` + `CityWorkforceService`: Farm +1 food, Mine/Pasture
        +1 prod, Road halves move cost (`GameState.MovementCost`). Tech-gated
        (Mining→Mine, Animal Husbandry→Pasture). Build menu on the unit panel.
  - [x] Per-tile gold: `TerrainYields.Gold` (Coast/Ocean trade), summed over worked
        tiles in `CivEconomyService.GoldPerTurn`. Gold rush-buy ("Buy now" button)
        at 4 gold per remaining hammer (`BuyCost` / `TryBuyProduction`).
  - [x] Strategic resources: `MapGenerator` scatters Horses (Plains/Grassland) and
        Iron (Hills) into `MapData.Resources`; `ResourceService` handles reveal
        (tech-gated) + access (controlling a resource tile); gates Horseman/Swordsman
        in the build list; +1 prod for a revealed worked resource tile.
- **M3 — Readable HUD ✅ COMPLETE**
  - [x] Minimap: new `MinimapController` (+ `UI.tscn` node) drawing scaled map with
        fog, unit/city dots, camera viewport rect; click to recenter.
  - [x] Event log: replaced `List<string>` notifications with
        `GameEvent { string Text; Vector2I? Focus }` out of `EndPlayerTurn`/session;
        `EventLogController` shows last 6, click focuses the tile.
  - [x] City list/cycle hotkey (`C`) to jump between own cities.
  - [x] Tile hover tooltip: terrain + yields + revealed resource + improvement.
- **M4 — Competent AI ✅ COMPLETE** (reworked `AIController.TakeTurn`)
  - [x] Research an available tech via `CivEconomyService.SetResearch` when idle
        (curated economy-first preference order).
  - [x] Production mix: defenders for undefended cities first; Walls when threatened;
        Settlers while below target and safe; ~1 Worker per city; attackers otherwise.
        All gated by tech/resource availability (mirrors the build-list filter).
  - [x] Expansion: Settlers found on the spot when legal, else march to the
        best-scored nearby site (≥ MinCityDistance, surrounding yields).
  - [x] Military: `CombatResolver.Expected` gates attacks (no suicidal melee, ranged
        always fires); wounded units retreat to the nearest city; lone garrisons hold;
        spare units reinforce threatened cities or advance on the enemy.
  - [x] City focus set by need (grow small cities, else pump current production).
  - [x] Workers develop controlled tiles (best non-Road improvement) and walk to the
        nearest improvable tile when idle.

> Sequencing M2 → M3 → M4. Each milestone: implement → `dotnet build` (0 warnings)
> → `dotnet test` (green, add tests) → sync `docs/` (drop matching **[planned]**
> flags) → commit. M3 also needs a manual Godot run (new draw code).

---

## Phase 6 — Win Conditions, Main Menu, Save/Load, Audio

Goal: The game has a beginning, middle, and end — start from a menu, play to a
win/loss, see a result screen, and save/reload without data loss.

> Mini-map and the scrolling event log (notification bar) already shipped in
> Phase 5.5 (M3) and are dropped from this phase. Rolled out one milestone at a
> time, each independently shippable: build (0 warnings) → tests green → docs
> synced → commit. Save/load uses **named multi-slot** files; victory uses a
> **dedicated result scene**; audio ships now with **placeholder sounds**.

### P6.1 — Win conditions + result scene ✅ COMPLETE
- [x] `VictoryService` (headless): `VictoryType { Domination, Score }`,
      `GameResult(Winner, Type, Score)`, `Evaluate(GameState) → GameResult?`.
- [x] Elimination = owns zero cities **and** holds no settler-capable unit
      (guards the opening turns before anyone founds).
- [x] Domination: exactly one non-eliminated player remains (leans on the
      existing `City.IsCapital`, preserved through `GameState.CaptureCity`).
- [x] Score victory at turn 500 via `ScoreService.Score` (cities/population/
      techs/gold, tunable named constants); highest score wins.
- [x] `GameSession.EndTurn` returns the `GameResult?` on `EndTurnSummary`.
- [x] `VictoryScreen.tscn` + controller (winner, type, score; Play Again / Quit —
      P6.2 swaps Play Again for a route to the main menu); `WorldMap` routes to it
      via `ChangeSceneToFile` on game over. Result crosses the scene boundary via
      the `GameLaunch` static handoff.
- [x] Tests: elimination rule, domination fires, opening turns yield nothing,
      score victory at turn 500, score ranking.

### P6.2 — Main menu + save/load (named slots)
- [ ] Bootstrap refactor: static `GameLaunch` handoff (NewGame seed / LoadGame
      state + last `GameResult`) and `GameFactory.NewGame(seed)` extracted from
      `WorldMap._Ready()` so new-game and load share one path.
- [ ] `MainMenu.tscn` (New Game / Load Game / Quit), set as `run/main_scene`.
- [ ] `SaveService` (System.Text.Json): DTO layer with a `Vector2I` converter and
      ownership stored by `Player.Id` (not object ref); `DataCatalog` re-attached
      from `res://data` on load, not serialized. Captures workforce, worker tasks,
      civ economy, turn number, current player, combat seed.
- [ ] Named slots under `user://saves/*.json` (header: name, timestamp, turn);
      save dialog + load/delete list UI.
- [ ] Tests: full-state round-trip, `Vector2I` converter, ownership rebinding,
      loaded state keeps playing.

### P6.3 — Audio (placeholder sounds)
- [ ] `AudioManager` autoload (pool of `AudioStreamPlayer`) with
      `Play(Sfx { Click, Move, Attack, CityFound, Win, Lose })`.
- [ ] Placeholder `.ogg` clips in `assets/audio/` (stand-ins, swap later).
- [ ] Trigger points: UI clicks, move animation, attack resolution, city founded,
      win/lose on the result screen.

**Done when:** You can start a new game from the menu, found a city, build units,
research techs, fight the AI to a capture-win or loss, and save/reload losslessly.

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
