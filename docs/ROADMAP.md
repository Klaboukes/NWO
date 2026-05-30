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

## Phase 8 — "The Sundering" (factions & fast-warfare reframe) 🔭 PLANNED

Goal: turn the Civ5-style MVP into the **NWO spin-off** — ideological factions, decisive
fast warfare, and an objective victory tied to the lore. Setting & design intent:
[LORE.md](LORE.md), [OVERVIEW.md](OVERVIEW.md), [FACTIONS.md](FACTIONS.md). Most of this is
**data + small hooks** on the existing headless core, not new subsystems. Can run alongside
or after Phase 7 (visuals) — they're independent.

- [ ] **8.1 — Faction data model.** Add `FactionData` (loaded via `DataLoader`/`DataCatalog`)
      + a faction id and signature-passive hooks on `Player`/`Civilization`. Replace the
      hard-coded "Barbarians" AI with **The Reavers**. *Done when:* a match can be seeded
      with a named faction whose passive is read by the core.
- [ ] **8.2 — Match setup (player-chosen factions).** Setup screen to choose faction
      **count (2–8)** and which factions; wire through `GameFactory.NewGame` and
      `WorldMap.ResolveLaunch` / `PickAISpawn`. *Done when:* the player picks the roster and
      it spawns correctly.
- [ ] **8.3 — Faction asymmetry (v1: 6 + Reavers).** Implement signature passives +
      unique-unit variants per [FACTIONS.md](FACTIONS.md) in `data/*.json` and the static
      services they touch (`CombatResolver`, `CivEconomyService`, movement/sight). Piety &
      Aesthetics stay shelved. *Done when:* each faction plays to its one-sentence identity.
- [ ] **8.4 — Anti-micro settlement model.** Capped cities / pre-placed contested sites
      (`MinCityDistance`, `MapGenerator`, settle rules). *Done when:* a match is decided by
      fighting over sites, not by settler-carpeting.
- [ ] **8.5 — Objective victory + shorter clock.** *Establish the New World Order*
      key-site-control win in `VictoryService`/`ScoreService`; lower the turn cap; tune
      combat lethality via the `tune-mechanics` skill. *Done when:* a typical match resolves
      well under the old 500-turn limit.
- [ ] **8.6 — Light diplomacy/economy.** Alliances / non-aggression; **hire-Reavers** via
      gold. *Done when:* gold and stance choices meaningfully affect a war without becoming
      its focus.

> Tech-regression art/flavour (salvaged → colony → planetary tiers) rides on Phase 7's
> sprite pipeline as factions and tiers land — see [LORE.md](LORE.md).

---

## Post-MVP backlog (unscheduled)

Beyond Phase 7 (visuals) and Phase 8 (factions & fast-warfare reframe), candidate
directions. Factions, light diplomacy, and the objective victory now live in **Phase 8**,
not here.

- **Piety & Aesthetics factions** — once light morale / influence layers exist, un-shelve
  the two trees held back from the Phase 8 roster (see [FACTIONS.md](FACTIONS.md)).
- Full tech tree (50+ techs), extending the salvaged → colony → planetary regression arc
- Deeper diplomacy (trade deals beyond the Phase 8 alliance/non-aggression basics)
- Culture and borders; Religion (currently out of scope)
- More unit types (naval, siege; plus the sci-fi late-tier units from the regression arc)
- **Cross-continent / multi-island AI spawning** — once naval units exist, drop the
  `WorldMap.PickAISpawn` same-landmass constraint and spawn players on separate
  continents.
- Map editor; Multiplayer (hot-seat → network); Mod support (data-driven JSON helps)
- Possible later: true 3D / free camera rotation if 2.5D proves limiting.
