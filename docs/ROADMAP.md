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

## Phase 4 — Tech Tree & Economy (Week 11–12)

Goal: Research feels meaningful. Gold matters.

- [ ] Load tech tree from `data/techs.json`
- [ ] Tech tree UI: show available techs, prerequisites, costs
- [ ] Science accumulates per turn; tech completes when threshold met
- [ ] Unlocks wire up: researching a tech enables its building/unit in menus
- [ ] Gold income and unit maintenance cost
- [ ] Negative gold → disband cheapest unit

**Done when:** You can research Horseback Riding, then build Horsemen in your cities.

---

## Phase 5 — Win Conditions & Polish (Week 13–14)

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
