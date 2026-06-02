# Core Game Mechanics (MVP)

> **Status:** Reflects the implementation through Phase 5 (tech tree & economy).
> Numbers below match the code (`TerrainYields`, `CityWorkforceService`,
> `CivEconomyService`, `CombatResolver`, the JSON in `data/`). Features not yet
> built are flagged **[planned]**.

---

## 0. Direction (where these mechanics are heading) [planned]

The sections below describe the **Civ5-style MVP as built**. The spin-off reframe
(see [OVERVIEW.md](OVERVIEW.md), [LORE.md](LORE.md), [FACTIONS.md](FACTIONS.md), and
ROADMAP Phase 10) layers on top of them — it does not throw them away:

- **Capped / contested settlement.** Infinite settler-spam (§1, §4) gives way to a small
  number of cities and **pre-placed sites factions fight over**, cutting expansion micro.
- **Tech-regression arc.** The antiquity roster (§3) and tech tree (§6) are reframed as the
  *first tier* of a re-climb — **salvaged-primitive → recovered colony tech → new planetary
  tech** — so existing units are reskins and later tiers go sci-fi.
- **Faction asymmetry.** A fixed cast of ideological factions (player-chosen 2–8 per match)
  adds signature passives + unique-unit variants, mostly via `data/*.json` and small hooks
  in the existing static services. The current single "Barbarians" AI becomes **the Reavers**.
- **Objective victory + shorter clock.** Alongside Domination/Score (§7), an *Establish the
  New World Order* key-site-control win, with a turn cap well below 500 and more lethal
  combat (§3) so matches resolve faster.

Everything in §0 is `[planned]`; the rest of this document is the current implementation.

---

## 1. Map

### Hex Grid

- Map size: 60×40 tiles (MVP default, configurable)
- Coordinate system: axial (q, r) — see TECH_STACK.md
- Each tile has one **terrain type**. **Rivers** (Phase 9.4) are stored separately
  as an edge-set on `MapData` (`HashSet<(tile, dir)>`); a tile bordering a river edge
  is a floodplain and gains **+1 Food** when worked (`MapData.IsRiverAdjacent`).

### Terrain Types

Yields per `TerrainYields`. Coast/Ocean now carry **+1 Gold** (trade); land tiles
have none until improvements/buildings provide it (see §9).

| Terrain | Movement Cost | Food | Production | Gold |
| --- | --- | --- | --- | --- |
| Grassland | 1 | +2 | +0 | +0 |
| Plains | 1 | +1 | +1 | +0 |
| Desert | 1 | +0 | +1 | +0 |
| Tundra | 1 | +1 | +0 | +0 |
| Snow | 1 | +0 | +0 | +0 |
| Hills | 2 | +1 | +2 | +0 |
| Forest | 2 | +1 | +2 | +0 |
| Savanna | 1 | +1 | +0 | +0 |
| Jungle | 2 | +1 | +0 | +0 |
| Wetlands | 2 | +2 | +0 | +0 |
| Mountain | impassable | — | — | — |
| Ocean | impassable | +1 | +0 | +1 |
| Coast | impassable | +2 | +0 | +1 |

Savanna, Jungle, and Wetlands (Phase 9.1) are climate-driven biomes placed by the
moisture axis rather than by height alone.

Ocean and Coast are currently **impassable** (no naval units yet); their food
values only matter to coastal cities working those tiles. Naval movement costs
are **[planned]**.

### Improvements

Workers (`special: "build_improvement"`) build tile improvements over several
turns (`Unit.CurrentTask`, ticked in `GameState.EndPlayerTurn`; moving cancels
it). Rules live in `ImprovementService`; yields fold into `CityWorkforceService`.

| Improvement | Effect | Valid terrain | Tech | Turns |
| --- | --- | --- | --- | --- |
| Farm | +1 Food | Grassland/Plains | — | 3 |
| Mine | +1 Prod | Hills | Mining | 3 |
| Pasture | +1 Prod | Grassland/Plains | Animal Husbandry | 3 |
| Road | halves entry move cost (min 1) | any passable land | — | 2 |

### Resources

`MapGenerator` scatters resources deterministically (seeded); per-resource tier and
yields live in `ResourceYields`, stored in `MapData.Resources`. Three Civ5-style
tiers (Phase 9):

- **Strategic** (Horses, Iron) — tech-revealed, sparse, **+1 Prod** when worked, and
  gate unit production via `requiredResource` (Horseman → Horses, Swordsman → Iron).
- **Bonus** (Wheat, Fish, Cattle, Sheep, Deer, Stone, Banana) — **always visible**
  (no reveal tech), denser, **+1 Food** (Wheat/Fish/Cattle/Deer/Banana) or **+1 Prod**
  (Sheep/Stone) when worked. Placed by per-terrain affinity in `ScatterResources`.
- **Luxury** (Gems, GoldOre, Silver, Silk, Spices, Dyes, Cotton, Incense, Ivory) —
  tech-revealed (Mining → Gems/GoldOre/Silver; Calendar → Silk/Spices/Dyes/Cotton/
  Incense; Animal Husbandry → Ivory), **very sparse** (1–3 of each per map on
  affinity terrain), **+1 Gold** when worked. `ResourceService.ControlledUniqueLuxuries`
  reports the distinct luxuries a civ controls — a scaffold for a future
  amenity/happiness system (Phase 10); no happiness effect yet.

`ResourceService` governs two gates:

- **Reveal** — a tech-gated resource is visible/usable only once the civ researches
  its revealing tech (Animal Husbandry → Horses, Bronze Working → Iron). Bonus
  resources are gated by no tech and are always revealed.
- **Access** — the civ must control a tile bearing the resource (within a city's
  work radius). This gates units with a matching `requiredResource` in the build list.

Resource trading remains **[planned]**.

### Map Generation (Procedural)

- Algorithm (Phase 9.1): three independent layers — (1) a low-freq continental
  shape (simplex base+detail blended 70/30) with a radial falloff that pushes map
  edges to ocean → island-like continents; (2) a **domain-warped ridged Simplex**
  mountain layer gated by a low-freq uplift mask, forming coherent mountain chains;
  (3) an independent low-freq **moisture** pass. Height + moisture map to a biome
  via `HeightMoistureToBiome` (height-banded × moisture-banded lookup, with a polar
  Snow/Tundra override near the top/bottom map edges).
- Rivers (Phase 9.4): 3–5 rivers traced downhill from Mountain/Hills sources along
  tile edges, stored in `MapData.Rivers`; rendered as thin blue lines in
  `WorldOverlay` and granting a floodplain **+1 Food** to adjacent worked tiles.
- Continents: the falloff tends to produce multiple landmasses, but a minimum
  count is **not** enforced.
- Resource scatter: strategic + bonus resources by per-terrain affinity (one per
  tile), seeded so a given map seed always produces the same layout (see Resources
  above).

---

## 2. Turn Structure

```text
Start of Turn
│
├── Each Civilization (in order):
│   ├── Unit phase: move/attack/skip each unit
│   ├── City phase: set production queue for each city
│   └── Research phase: choose tech (if not already chosen)
│
└── End of Turn
    ├── Cities produce yields → Food/Production/Gold/Science accumulated
    ├── Growth check: city grows if Food surplus hits threshold
    ├── Production complete: unit or building added if queue finished
    ├── Science: advance tech if accumulated science ≥ tech cost
    └── AI takes its turn
```

- Simultaneous movement is **not** in MVP — player goes first, then the AI runs
  synchronously inside `GameSession.EndTurn`.
- The turn-500 score limit and domination check run at the end of each
  `GameSession.EndTurn` (see §7 Win Conditions).
- The AI runs a single reactive pass (attack / settle / advance) and auto-queues
  Warriors; it does not have a real city or research phase (see ARCHITECTURE.md).

---

## 3. Units

### Unit Properties

| Property | Description |
| --- | --- |
| `Type` | Warrior, Archer, Spearman, etc. |
| `HP` | 0–100. Unit dies at 0. |
| `Attack` | Combat strength for melee |
| `Defense` | Combat strength when defending |
| `Movement` | Movement points per turn |
| `Range` | 1 = melee, 2 = ranged |
| `Owner` | Civilization that controls the unit |

### Movement

- Each unit has `MovementRemaining`, reset to its `Movement` at the start of its
  civ's turn (fortified units stay at 0 until woken by an order)
- Moving onto a tile costs the tile's movement cost
- Enemy units block pathfinding (treated as impassable)
- **[planned]** Stacking rules: there is currently no enforced 1-military +
  1-civilian-per-tile limit. The AI avoids stepping onto any occupied tile.

### Combat (Simplified)

- Attacker and defender each roll: `strength × (HP/100) × random(0.85, 1.15)`
- Higher roll deals damage equal to `(attacker_roll / defender_roll) × 30` to the loser, proportionally less to the winner
- Attacking costs all remaining movement points
- Ranged units attack without retaliation (range 2)
- **Combat-odds preview:** hovering an in-range enemy with a unit selected shows
  the *expected* damage to each side (`CombatResolver.Expected`, jitter at its
  1.0 mean — deterministic).

### Healing

- A unit that did **not** move or attack this turn recovers **+10 HP** at end of
  turn (**+15** if on or adjacent to a friendly city), capped at 100. Fortifying
  (`F`) or skipping (`Space`) a unit both leave it idle, so both heal.
- **`H` — fortify until healed:** the unit fortifies and keeps sleeping until it
  reaches full HP, then automatically wakes. Any manual order (move/attack/found)
  cancels the standing order early.

### MVP Unit Roster

| Unit | Cost (Production) | Atk | Def | Move | Range |
| --- | --- | --- | --- | --- | --- |
| Warrior | 40 | 8 | 8 | 2 | 1 |
| Archer | 70 | 7 | 4 | 2 | 2 |
| Spearman | 60 (needs Bronze Working) | 6 | 12 | 2 | 1 |
| Horseman | 80 (needs Horseback Riding) | 12 | 7 | 4 | 1 |
| Swordsman | 90 (needs Iron Working) | 14 | 14 | 2 | 1 |
| Catapult | 100 (needs Iron Working) | 14 | 4 | 1 | 2 |
| Settler | 100 | 0 | 0 | 2 | — |
| Worker | 70 | 0 | 0 | 2 | — |

All units have sight radius 2 and 1 gold maintenance (Settler/Worker cost 0).
The Horseman/Swordsman `requiredResource` (`horses`/`iron`) is enforced in the
build list — you must have access to the resource (see Resources, §1).

### Special Units

- **Settler**: Can found a city (`special: "found_city"`). Consumed on use.
- **Worker**: Carries `special: "build_improvement"`. When selected, the unit
  panel offers the improvements buildable on its tile (terrain/tech permitting);
  the build runs over several turns and the worker is parked until it finishes
  (see Improvements, §1). Moving the worker cancels the build.

---

## 4. Cities

### City Defense & Capture

- Each city has an **HP** pool (max 100) and a **defense strength** =
  `6 + Population + Walls(+5) + best garrisoned unit's Defense`.
- Attacking a city (a unit in range bombards/assaults it) reduces its HP via the
  normal combat formula; a melee attacker takes retaliation scaled by the city's
  defense, ranged takes none.
- A city tile **blocks movement** until its HP reaches 0. Once depleted, a
  **melee** unit moving onto the tile **captures** it (ranged/civilians cannot);
  the captured city starts at half HP under its new owner.
- A garrison unit on the city tile both defends the tile (attack it separately)
  and raises the city's defense strength.
- Cities **regenerate +10 HP/turn** when not attacked since the owner's last turn.

### Founding

- A Settler unit can found a city on any non-Ocean, non-Mountain tile not within
  3 tiles of another city (`MinCityDistance = 3`)
- Cities work tiles within a radius of **2** (`CityWorkforceService.WorkRadius`),
  excluding the city center. There is no separate stored "territory" — a tile
  belongs to the nearest city center within work radius (earlier-founded city
  wins ties).
- Citizens are auto-assigned to the best workable tiles by the city's focus at
  founding; the player can lock/unlock specific tiles (see Phase 4 / §4 below).

### City Yields (Food & Production)

- Each turn a city's worked tiles drive its **Food** and **Production** yields,
  recomputed by `CityWorkforceService` (on found, capture, building completion,
  growth, and end of turn).
- Yields = city-center floor + worked tiles + building bonuses.
- **City-center floor:** at least **2 Food / 1 Production** at the city tile
  (Civ 5 rule), regardless of the center terrain.
- **Science** is +1 per city plus building science (summed civ-wide each turn by
  `CivEconomyService`, not stored per city). **Gold** comes only from buildings
  (see §9). Neither is computed from worked tiles yet.

### Growth

- Each turn: `FoodAccumulated += FoodYield − Population` (each citizen eats 1
  food). `FoodAccumulated` is clamped at ≥ 0.
- Growth threshold: `15 + (6 × population)`
- When threshold reached: population +1, surplus carries over (threshold
  subtracted, not reset to 0)
- **[planned]** Starvation: there is no pop loss when food is negative yet —
  `FoodAccumulated` simply can't drop below 0.

### Production Queue

- One item produced at a time
- Player sets queue at start of their turn for each city
- Overflow production carries over to next item

### Buildings (MVP set)

| Building | Cost | Req. Tech | Effect | Implemented? |
| --- | --- | --- | --- | --- |
| Monument | 60 | — | +2 Culture | data only — no culture system |
| Granary | 80 | Pottery | +2 Food | ✅ |
| Barracks | 100 | — | New units +15 XP | data only — no XP system |
| Library | 90 | Writing | +2 Science | ✅ |
| Market | 120 | — | +2 Gold | ✅ |
| Walls | 130 | — | +5 City Defense | ✅ |

Food / Science / Gold yields and the Walls city-defense bonus feed the
simulation. Culture (Monument) and unit XP (Barracks) are declared in
`data/buildings.json` but have **no gameplay effect yet [planned]**. The Monument
requires **Philosophy** (its +2 Culture has no effect until a culture system
exists).

---

## 5. Civilizations

### MVP: 2 Players

- **Player** (`IsHuman = true`): human-controlled, color blue
- **Barbarians** (`IsHuman = false`): one AI opponent, color red

Identity (`Player`: id/name/human/color) is separate from civ-wide state
(`Civilization`: treasury, science, research) — see ARCHITECTURE.md.

### Starting Setup

- Each player begins with 1× Warrior + 1× Settler.
- The AI spawns on the **same landmass** as the player, at least 10 tiles away
  (`MinAISpawnDistance`) — not on the opposite side of the map. Cross-continent
  spawning waits on naval units (see ROADMAP Post-MVP).

### Unique Abilities (MVP — placeholder, implement in post-MVP)

- Both players use identical unit/building stats in MVP for simplicity

---

## 6. Technology Tree (MVP — 8 Techs)

```text
Pottery → Writing → Philosophy
    └→ Animal Husbandry → Horseback Riding
Mining → Bronze Working → Iron Working
```

| Tech | Cost (Science) | Unlocks |
| --- | --- | --- |
| Pottery | 35 | Granary |
| Writing | 55 | Library |
| Philosophy | 80 | Monument |
| Animal Husbandry | 35 | Pasture improvement, reveals Horse resource |
| Horseback Riding | 100 | Horseman unit |
| Mining | 35 | Mine improvement |
| Bronze Working | 55 | Spearman unit, reveals Iron resource |
| Iron Working | 100 | Swordsman unit, Catapult unit |

- Only one tech can be researched at a time
- Prerequisites must be completed before a tech is available
- **Building/unit, improvement, and resource-reveal unlocks all work**: a
  researched tech enables its building/unit in the build menu, allows its
  improvement (Pasture, Mine) to be built by Workers, and reveals its strategic
  resource (Horses, Iron) on the map and for unit gating.

---

## 7. Win Conditions

Evaluated by `VictoryService.Evaluate(GameState)` at the end of every
`GameSession.EndTurn`; a result routes to the victory screen.

### Elimination

A player is out of the game once they own **no city** *and* hold **no
settler-capable unit** (`Special == "found_city"`). The settler clause keeps the
opening turns — before anyone founds — from ending the game.

### Domination Victory

- Triggered when exactly **one** non-eliminated player remains.
- With two players this is the classic "wipe out the opponent": capture their
  cities and destroy/deny any settler. `City.IsCapital` (the first city a civ
  founds) is preserved through capture for display/flavour.

### Score Victory (fallback)

- At **turn 500** (`VictoryService.ScoreVictoryTurn`), if no one has won by
  domination, the highest-scoring civilization wins.
- Score = (cities × 10) + (population × 3) + (techs researched × 5) + (gold ÷ 10).
  Weights are named constants in `ScoreService`, easy to retune.

---

## 8. Fog of War

- Tiles start **undiscovered** (black)
- A unit or city with sight radius reveals tiles within range
- Unit sight radius: 2 tiles by default
- Tiles previously seen but no longer in sight range become **greyed out** (visible terrain, but no unit/city info updated)

---

## 9. Gold & Economy

Handled civ-wide by `CivEconomyService` each turn:

- Each civ starts with a treasury of **50** gold (`StartingTreasury`).
- **Income**: sum of building Gold yields (Market +2) **plus worked-tile trade
  gold** (Coast/Ocean +1 each).
- **Maintenance**: each unit's `maintenanceGold` (1 for military, 0 for
  Settler/Worker), minus a **2-gold free grace** (`FreeUnitMaintenance`) — so a
  Warrior + Settler opening costs 0/turn.
- **Negative treasury**: units are disbanded until the treasury is non-negative,
  cheapest by **production cost** first, then lowest HP. Disbanding refunds the
  unit's maintenance for the turn but no production.
- **Rush-buy**: the city panel's "Buy now" button completes the current
  production instantly for **4 gold per remaining hammer** (`BuyCost` /
  `GameSession.TryBuyProduction`), disabled when idle or unaffordable.

### Research

- Science accumulates civ-wide each turn (1 per city + building science).
- When `ScienceAccumulated ≥ tech.scienceCost`, the current research completes,
  the cost is subtracted (overflow carries over), and the tech is added to
  `ResearchedTechs`. Switching research keeps banked science.
- Prerequisites must be researched first. The AI does not research.

---

## 10. Notifications & Events

Notifications currently emitted:

- City grew (population +1)
- Production complete
- Tech research complete
- Treasury depleted → unit disbanded
- City founded / city captured
- Combat result (hit / kill / both killed)

The top-center banner is the **combat/event feed** — it shows combat results and
one-shot events (founded/captured/grew/built/researched) and action errors. It no
longer shows turn-blocking prompts: what's blocking End Turn is on the **End Turn
button** ("Needs Orders ▶" / "Choose Production ▶" / "Choose Research ▶"), and the
per-unit control hints (`[Space] Skip / [F] Fortify / [H] Heal`) live on the
selected-unit panel.

**[planned]:** "unit under attack" alerts and the score-victory warning (last 50
turns) aren't implemented. The banner is still a single label, not yet a
scrolling event log (Phase 6 / M3).
