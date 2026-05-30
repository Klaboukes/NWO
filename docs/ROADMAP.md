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

## Phase 7 — Visual overhaul (baked 2.5D pixel art) 🔜 NEXT

Goal: make the game *look* like a game. Replace flat top-down polygons with a
tilted, Civ5-style **baked 2.5D pixel-art** look — the tilt is painted into the
art + a foreshortened projection, keeping the `Camera2D` (no true 3D). Art is
AI-generated and dropped into `assets/art/` with no code change. Pipeline is
de-risked with runtime placeholders first (mirrors the `AudioManager` pattern).

- [x] **V7.1 — Projection + asset pipeline (placeholders).** Foreshorten the
      (still-invertible) `AxialToWorld`/`WorldToAxial` projection; draw-only
      elevation lift for hills/mountains; `TileTextureSet` registry (real PNG or
      synthesized placeholder); nearest-neighbour filtering; painter's-order draw.
      Projection round-trip regression test. *Done when:* the map renders tilted
      with textured tiles on zero committed art, and picking/animation still land
      correctly.
- [ ] **V7.2 — Terrain art.** Drop in 10 AI-generated pixel hex tiles (top face +
      cliff skirt); no code change. *Done when:* all terrain uses real tiles.
- [ ] **V7.3 — Unit & city sprites.** Registry-driven billboard sprites anchored on
      tile tops, owner-tinted; keep selection/fortify/HP overlays. *Done when:*
      units and cities are sprites, not circles/rects.
- [ ] **V7.4 — HUD / UI polish.** A Godot `Theme` (pixel font, restyled
      panels/buttons/bars), framed minimap. Independent of the renderer. *Done
      when:* the HUD reads as a cohesive styled UI.

---

## Post-MVP backlog (unscheduled)

After the visual overhaul, candidate directions:

- Additional civilizations with unique abilities
- Diplomacy (peace, war, trade deals)
- Full tech tree (50+ techs)
- Culture and borders; Religion
- More unit types (naval, siege)
- **Cross-continent / multi-island AI spawning** — once naval units exist, drop the
  `WorldMap.PickAISpawn` same-landmass constraint and spawn players on separate
  continents.
- Map editor; Multiplayer (hot-seat → network); Mod support (data-driven JSON helps)
- Possible later: true 3D / free camera rotation if 2.5D proves limiting.
