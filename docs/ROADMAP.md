# Development Roadmap

## MVP — ✅ COMPLETE (Phases 0–6)

The MVP is finished and the game is fully playable end to end. What shipped:

- **Foundation & map** — Godot 4 + C#/.NET 8 project; axial-hex `HexGrid`;
  fractal `MapGenerator` (terrain, strategic resources); immediate-mode
  `WorldRenderer` (flat coloured polygons); pan/zoom `Camera2D`.
- **Units & movement** — data-driven units (`data/units.json`); A* pathfinding;
  movement-range overlay; click-to-move with animation; fog of war.
- **Turns & cities** — `TurnManager` loop; found cities with Settlers; population,
  food basket, production queue; city panel; end-of-turn yields & notifications.
- **Combat & AI** — melee + ranged combat; unit death; city capture (bombard to 0
  HP then move in); reactive→competent `AIController` (research, production mix,
  expansion, military, worker development).
- **City management** — per-city `Workforce`; focus modes (Balanced/Food/Prod);
  tile control & locking; tile improvements (Farm/Mine/Pasture/Road); per-tile gold
  & rush-buy; strategic resources (Horses/Iron) gating units.
- **Tech & economy** — tech tree (`data/techs.json`); science per turn; unlocks;
  gold income, unit maintenance, auto-disband on bankruptcy.
- **Readable HUD** — minimap, scrolling event log, city-cycle hotkey, tile tooltip,
  combat-odds preview, on-map HP bars.
- **Win / menu / save / audio** — `VictoryService` (Domination + Score);
  result screen; main menu; named multi-slot save/load (System.Text.Json);
  `AudioManager` with placeholder synthesized SFX (drop-in real `.ogg`).

**Definition of Done (met):** launch from menu → new map → found city → build units
→ research techs → fight AI to a win/loss → save & reload losslessly.

> Implementation note: the world is drawn in **immediate mode** with flat coloured
> polygons and a straight-down `Camera2D`. There are no art assets yet — Phase 7
> addresses this.

---

## Phase 7 — Visual overhaul (true 3D tilted camera) 🔜 NEXT

Goal: make the game *look* like a game. Replace flat top-down polygons with a
**true 3D, fixed-tilt Civ5-style camera**: terrain is 3D hex prisms (top face +
cliff sides), units/cities are billboard sprites, all under a `Camera3D` (pan +
zoom, no rotation). The fiddly 2D bits (range/path overlays, selection rings, HP
bars, glyphs, fog dimming) are drawn in a screen-space overlay on top of the 3D
world via `Camera3D.UnprojectPosition`. Terrain art is **procedurally generated**
(`TerrainArtGenerator`, baked into `assets/art/`); a real hand-drawn/AI PNG still
overrides any tile with no code change. The pipeline is de-risked with runtime
placeholders first (mirrors the `AudioManager` pattern).

> Decision (was "baked 2.5D"): V7.1 originally shipped a foreshortened `Camera2D`
> with the tilt painted into the art. We switched to a real 3D camera so the tilt,
> elevation, and perspective are genuine geometry. Game logic was untouched (it
> works in axial `Vector2I`); only the view layer changed.
>
> Lens (V7.1 follow-up): the `Camera3D` uses a **Civ5-style telephoto lens** — a
> narrow (~30°) FOV with the camera dollied far back, at a ~45° oblique tilt. A wide
> FOV ballooned the near/edge hex tiles; the long lens keeps them near-uniform
> across the viewport while preserving a subtle depth cue (Civ5 is perspective, not
> orthographic). FOV / tilt / dolly distance are constants in `CameraController`.
> **Impact on the art still to come:** top-face terrain tiles (V7.2) and billboards
> (V7.3) are viewed at a fixed ~45° oblique angle, *not* top-down — author and judge
> that art at the oblique angle, not flat-on.

- [x] **V7.1 — 3D projection + asset pipeline (placeholders).** Flat-ground
      `HexProjection.AxialToWorld`→`Vector3` / `WorldToAxial` (ray-to-ground
      picking); real prism elevation via `TopHeight`; `TerrainMeshFactory`
      hex-prism meshes (vertex-coloured placeholder, real PNG tops drop in at
      V7.2); `WorldRenderer` (Node3D) + `WorldOverlay` (2D) split; fixed-tilt
      `CameraController` over a pivot+`Camera3D`; nearest-neighbour filtering.
      Projection round-trip + ground-pick regression tests. *Done when:* the map
      renders as a tilted 3D board on zero committed art, and picking/animation
      still land correctly.
- [x] **V7.2 — Terrain art.** UV-mapped, textured **top-face** tiles wired in
      `TerrainMeshFactory` (two-surface prism: textured top + vertex-coloured cliffs)
      via `TerrainTextureRegistry` (real `assets/art/tiles/<terrain>.png` if present,
      else a synthesized fallback). Art is **procedurally generated** —
      `TerrainArtGenerator` bakes all ten 128px detailed-pixel-art tiles (6-tone
      naturalistic ramp, ordered dither, outlined trees/rocks/cacti/peaks, decorative
      props, hex-edge ambient-occlusion rim) via the headless `BakeTerrainTiles` tool;
      see the `generate-terrain-art` skill. A real hand-drawn/AI PNG still overrides
      any tile with no code change. *Done:* all terrain uses real baked tiles (art may
      be refined further later).
- [x] **V7.3 — Unit & city sprites.** Replace the placeholder billboard tokens with
      real per-type `Sprite3D` textures anchored on tile tops, owner-tinted; keep
      the selection/fortify/HP overlays. *Done when:* units and cities are real
      sprites, not placeholder tokens.
- [x] **V7.4 — HUD / UI polish.** A Godot `Theme` (pixel font, restyled
      panels/buttons/bars), framed minimap. Independent of the renderer. *Done
      when:* the HUD reads as a cohesive styled UI.

---

## Phase 8 — Standalone Distribution ✅ COMPLETE

Goal: ship a playable Windows build that requires no Godot installation or developer
tooling. Players can double-click `NWO.exe` on a bare Windows machine.

- [x] **8.1 — Export configuration.** Download the Godot 4.6 Mono export templates
      (*Editor → Manage Export Templates* in the Godot editor). Create
      `export_presets.cfg` via *Project → Export → Windows Desktop* (x86\_64,
      embed PCK, self-contained .NET). Commit the preset file.
- [x] **8.2 — Build skill & artifact.** Wire the `build-standalone` skill
      (`godot --headless --export-release "Windows Desktop" build/NWO.exe`).
      `build/` excluded from git. Verify: launch `build/NWO.exe` on a machine without
      Godot; start a game, play a turn, save, reload. Optional: GitHub Actions job that
      exports on version tags and attaches the zip to a GitHub Release.

---

## Phase 9 — Map Generation Overhaul & Terrain Features ✅ COMPLETE

Goal: replace the single-noise `MapGenerator` with a geologically-layered pipeline and
expand the resource system into three Civ5-style tiers (bonus / strategic / luxury).
See [MAP_GENERATION.md](MAP_GENERATION.md) for the techniques and NWO-specific notes.

- [x] **9.1 — Layered terrain generation.** Replace the two-noise flat pass in
      `MapGenerator.cs` with three independent layers: (1) continental shape mask
      (existing low-freq FBM + radial falloff — keep as-is); (2) domain-warped ridged
      Simplex for coherent mountain chains; (3) moisture noise axis (separate low-freq
      pass, independent of height). Update `HeightToTerrain()` to a `(height, moisture)`
      → biome lookup, enabling Savanna, Jungle, and Wetlands terrain types. Add
      elevation, colour, and procedural art (`TerrainArtGenerator`) for the new types.
      *Done when:* maps show coherent mountain arcs and 2–3 distinct biome transitions
      per seed.
- [x] **9.2 — Bonus resources.** Add 7 always-visible bonus resources to `ResourceType`
      (Wheat, Fish, Cattle, Sheep, Deer, Stone, Banana). Scatter with per-type terrain
      affinity and density targets in `ScatterResources()`. Update `TerrainYields` with
      resource yield bonuses (+1 Food or +1 Prod). No tech reveal required. *Done when:*
      maps populate with bonus resources and city yields reflect them when tiles are worked.
- [x] **9.3 — Luxury resources.** Add 9 luxury resources to `ResourceType` (Gems,
      GoldOre, Silver, Silk, Spices, Dyes, Cotton, Incense, Ivory). Each scatters very
      sparsely (1–3 per map) on appropriate terrain, is tech-revealed, and yields +1 Gold
      on the worked tile. Wire reveal techs in `data/techs.json`. Scaffold a
      "unique luxury controlled" count on `Player` for future happiness consumption (Phase
      10 / post-MVP). *Done when:* luxury resources are hidden until the reveal tech and
      yield correctly when worked.
- [x] **9.4 — Rivers (basic).** Store rivers as a `HashSet<(Vector2I tile, int dir)>`
      edge-set on `MapData`. Trace 3–5 rivers per map downhill from mountain/hill tiles
      to coast in `MapGenerator`. Grant +1 Food to all tiles adjacent to a river edge
      (floodplain modifier in `TerrainYields`). Render river edges as thin coloured lines
      in `WorldOverlay`. *Done when:* rivers trace visibly from highlands to coast and the
      tile tooltip shows the +1 Food bonus on adjacent tiles.

---

## Phase 10 — "The Sundering" (factions & fast-warfare reframe) ✅ COMPLETE

Goal: turn the Civ5-style MVP into the **NWO spin-off** — ideological factions, decisive
fast warfare, and an objective victory tied to the lore. Setting & design intent:
[LORE.md](LORE.md), [OVERVIEW.md](OVERVIEW.md), [FACTIONS.md](FACTIONS.md). Most of this is
**data + small hooks** on the existing headless core, not new subsystems.

- [x] **10.1 — Faction data model.** `FactionData` (flat modifier bag, loaded via
      `DataLoader`/`DataCatalog` from `data/factions.json`) + `Player.FactionId` resolved by
      `DataCatalog.FactionOf` (neutral all-identity default). Hard-coded "Barbarians" AI is now
      **The Reavers**. Faction id round-trips through save/load.
- [x] **10.2 — Match setup (player-chosen factions).** `FactionSetup` screen chooses player
      **count (2–8)** and each slot's faction; `GameFactory.NewGame(seed, roster)` spawns one
      player per `FactionChoice` (spawn logic in the headless-testable `GameFactory.Populate`).
      Wired through `GameLaunch.NewGameRoster` / `WorldMap.ResolveLaunch`.
- [x] **10.3 — Faction asymmetry (v1: 6 + Reavers).** Signature passives + unique-unit variants
      per [FACTIONS.md](FACTIONS.md), each a single inline hook in `CombatResolver` (via
      `GameState`), `CivEconomyService`, `FogOfWar`, `GameState.MovementCost`, `City` /
      `CityWorkforceService`. Includes a minimal unit-**XP/veterancy** subsystem
      (`Unit.Experience` → level → strength bonus; Iron Pact levels faster). Piety & Aesthetics
      stay shelved.
- [x] **10.4 — Anti-micro settlement model.** Per-faction effective city spacing
      (`MinCityDistanceDelta`) + settle-cost discount (`SettleCostMult`), and Civ-5
      **start-normalization** (fertility floor + farthest-point/impact-and-ripple spawn spread)
      in `GameFactory`.
- [x] **10.5 — Objective victory + shorter clock.** *Establish the New World Order*
      key-site-control win (`MapData.KeySites` + `KeySiteService`) in `VictoryService`/`ScoreService`;
      turn cap lowered 500 → 250; combat lethality raised (`DamageScale` 30 → 40).
- [x] **10.6 — Light diplomacy/economy.** Pairwise `Diplomacy` stances (War/Peace/NonAggression/
      Alliance) gate combat in `GameState` and the AI; **hire-Reavers** via gold for the Syndicate
      (`CivEconomyService.HireReaver`). Both persist through save/load.

> Follow-ups (not blocking): a diplomacy UI panel to set stances in-game, and a HUD readout of
> key-site control, both ride on the existing headless model. Tech-regression art/flavour rides
> on Phase 7's sprite pipeline — see [LORE.md](LORE.md).

---

## Phase 11 — Map scripts (Civ5-style world types) 🔭 PLANNED

Goal: add selectable **map scripts** so a match can be Continents, Pangaea,
Archipelago, Highlands, etc., instead of one fixed continental layout — borrowing the
Civ 5 pattern (each script parameterizes the same generation core differently). See
the "Civ 5 Patterns" matrix in [MAP_GENERATION.md](MAP_GENERATION.md).

- [ ] **11.1 — Map-script abstraction.** Factor `MapGenerator.Generate` so the
      continental-shape / sea-level / landmass-splitting step is pluggable (a `MapScript`
      strategy), leaving climate, mountains, rivers, and resources shared. *Done when:*
      the current map is one script among an interface, with no behaviour change by default.
- [ ] **11.2 — Script library.** Implement Continents, Pangaea, Archipelago, and
      Highlands by varying the shape/falloff/uplift parameters (+ ideally the Civ 5
      **percentile-threshold** trick for a stable land ratio per script). *Done when:*
      each script produces a recognisably different, playable world.
- [ ] **11.3 — Setup-screen selection.** Expose the map-script (and size) choice in the
      new-game setup UI, wired through `GameFactory.NewGame`. *Done when:* the player picks
      a world type and it generates.

> The two cheap Civ 5 wins — **percentile thresholds** and **impact-and-ripple resource
> spacing** — are small, isolated tuning jobs; do them anytime under the
> `tune-map-generation` skill rather than blocking on this phase.

---

## Phase 12 — In-game Civilopedia (player documentation) ✅ COMPLETE

Goal: a Civ5-style **Civilopedia** — a browsable in-game reference documenting factions,
units, buildings, technologies, terrain, resources, core mechanics, and world lore.
Reachable from the **main menu** and **in-game** (pause menu + F1). Flavor text lives in a
new `data/civilopedia.json`, decoupled from the mechanical data files; live stats are
pulled from `DataCatalog` / `TerrainYields` so entries never go stale. Copy is adapted from
[FACTIONS.md](FACTIONS.md), [LORE.md](LORE.md), and [MECHANICS.md](MECHANICS.md).

- [x] **12.1 — Content model + service.** `data/civilopedia.json` (per-entry `prose` map +
      standalone `articles`); `DataLoader.LoadCivilopedia()`; headless `CivilopediaService`
      that assembles categories/entries and renders each detail view by combining live stats
      (units/buildings/techs/terrain/resources/factions) with authored prose. Faction
      modifier-bag → readable-text formatter. *Done when:* the service lists every catalog
      entry and renders stats+prose, with graceful stats-only fallback; unit tests green.
- [x] **12.2 — Civilopedia browser UI.** Reusable content-only browser
      (`scenes/ui/Civilopedia.tscn` + `CivilopediaController`): category/entry list, search
      filter, scrollable detail pane, `CloseRequested` event. Reachable from the main menu via
      a new button + `Scenes.Civilopedia`. *Done when:* you can open it from the menu, browse
      every category, search, and read entries.
- [x] **12.3 — In-game access + docs sync.** Same browser instanced as a toggled overlay in
      `UIController` (pause-menu entry + `F1`), closing without unloading the match. Update
      [CONTROLS.md](CONTROLS.md) (F1) and note the feature in [MECHANICS.md](MECHANICS.md).
      *Done when:* F1 / pause-menu opens and closes the Civilopedia mid-match with no state loss.

---

## Post-MVP backlog (unscheduled)

Beyond Phase 7 (visuals), Phase 9 (map generation & terrain features), Phase 10
(factions & fast-warfare reframe), Phase 11 (map scripts), and Phase 12 (in-game
Civilopedia), candidate directions.
Factions, light diplomacy, and the objective victory now live in **Phase 10**, not here.

- **Piety & Aesthetics factions** — once light morale / influence layers exist, un-shelve
  the two trees held back from the Phase 10 roster (see [FACTIONS.md](FACTIONS.md)).
- Full tech tree (50+ techs), extending the salvaged → colony → planetary regression arc
- Deeper diplomacy (trade deals beyond the Phase 10 alliance/non-aggression basics)
- Culture and borders; Religion (currently out of scope)
- More unit types (naval, siege; plus the sci-fi late-tier units from the regression arc)
- **Cross-continent / multi-island AI spawning** — once naval units exist, drop the
  `WorldMap.PickAISpawn` same-landmass constraint and spawn players on separate
  continents.
- Map editor; Multiplayer (hot-seat → network); Mod support (data-driven JSON helps)
- Possible later: **free camera rotation** (the world is already true 3D as of
  Phase 7, but the tilt/heading is fixed Civ5-style — orbiting would add picking,
  overlay, and billboard-facing work).
