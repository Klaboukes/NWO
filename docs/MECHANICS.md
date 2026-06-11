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

Phase 14 adopts the **Civ5 terrain/feature split**: a tile is one BASE terrain
plus a mask of **Features** (vegetation, hills, ice — next section). The old
Forest/Jungle/Wetlands terrain types are gone; woods are now *Grassland + Forest*,
marsh is *Grassland + Marsh*, and so on. Yields per `TerrainYields`. Water carries
**+1 Gold** (trade); land tiles have none until improvements/buildings provide it.

| Terrain | Movement Cost | Food | Production | Gold |
| --- | --- | --- | --- | --- |
| Grassland | 1 | +2 | +0 | +0 |
| Plains | 1 | +1 | +1 | +0 |
| Desert | 1 | +0 | +1 | +0 |
| Tundra | 1 | +1 | +0 | +0 |
| Snow | 1 | +0 | +0 | +0 |
| Savanna | 1 | +1 | +0 | +0 |
| Mountain | impassable | — | — | — |
| Ocean | impassable | +1 | +0 | +1 |
| Coast | impassable | +2 | +0 | +1 |
| Lake | impassable | +2 | +0 | +1 |

Water passability splits in two (`TerrainYields`):

- **`IsWater`** (Ocean/Coast/Lake) — what land units can't enter, what cities
  can't be founded on, what counts for fertility/working rules.
- **`IsSeaWater`** (Ocean/Coast only) — what naval units (`UnitData.IsNaval`)
  sail on, paying movement cost **1**, and what "coastal" means to the AI's ship
  logic. A **Lake is water but not sea**: it's workable (2F/1G, fresh water) but
  no ship can enter it, and a lakeside city is *not* coastal (Civ5 rule). Pack
  **Ice** (feature) also blocks naval movement. Civilian cargo (land units stored
  in a `Transport` or `Galleon`) is off-map while at sea and moves with the
  transport.

### Features

Features overlay a base terrain as a **flag mask** (a tile is e.g. *Grassland +
Forest + Hills*) and stack with resources (*Grassland + Hills + Sheep*). Stored
sparsely in `MapData.Features`; yield deltas in `FeatureYields` (flag-additive),
legality in `FeatureRules`.

| Feature | Yields | Move | Legal on | Notes |
| --- | --- | --- | --- | --- |
| Hills | −1 Food / +1 Prod | +1 | any land except Mountain | raises the tile; the only feature that stacks (with Forest/Jungle). **Mine** site. |
| Forest | −1 Food / +2 Prod | +1 | Grassland, Plains, Tundra | *Grassland+Forest* = the classic 1F/2P woods; *Forest+Hills* = 0F/3P lumber-hill. Deer/Silk live here. |
| Jungle | −1 Food | +1 | Grassland, Plains | equatorial band; Banana/Spices live here. |
| Marsh | −1 Food | +1 | Grassland (flat) | poor, slow ground — deliberately worse than the old Wetlands terrain (Civ5 marsh). |
| Oasis | +3 Food / +1 Gold | — | Desert (flat) | rare desert springs, never beside water/rivers/another oasis. |
| Ice | — | blocks ships | Ocean, Coast | polar pack ice; never on a Lake. |

Worked-tile food is clamped at ≥0 after feature deltas. `MapGenerator` places
Hills both as a foothill skirt around mountain belts and via an independent
hilliness field; vegetation/ice come from the `FeaturePlacer` pass (see Map
Generation below).

### Improvements

Workers (`special: "build_improvement"`) build tile improvements over several
turns (`Unit.CurrentTask`, ticked in `GameState.EndPlayerTurn`; moving cancels
it). Rules live in `ImprovementService`; yields fold into `CityWorkforceService`.

| Improvement | Effect | Valid terrain | Tech | Turns |
| --- | --- | --- | --- | --- |
| Farm | +1 Food | Grassland/Plains, feature-free | — | 3 |
| Mine | +1 Prod | bare Hills feature (no vegetation) | Mining | 3 |
| Pasture | +1 Prod | Grassland/Plains, feature-free | Animal Husbandry | 3 |
| Road | halves entry move cost (min 1) | any passable land | — | 2 |

Farm/Pasture want clear flatland and Mine a bare hill — NWO has no tree-chopping,
so a Forest/Jungle/Marsh tile keeps its natural yields instead of taking an
improvement.

### Resources

`MapGenerator` scatters resources deterministically (seeded); per-resource tier and
yields live in `ResourceYields`, stored in `MapData.Resources`. Three Civ5-style
tiers (Phase 9):

- **Strategic** (Horses, Iron) — tech-revealed, sparse, **+1 Prod** when worked, and
  gate unit production via `requiredResource` (Horseman → Horses, Swordsman → Iron).
- **Bonus** (Wheat, Fish, Cattle, Sheep, Deer, Stone, Banana) — **always visible**
  (no reveal tech), denser, **+1 Food** (Wheat/Fish/Cattle/Deer/Banana) or **+1 Prod**
  (Sheep/Stone) when worked. Placed by terrain *and feature* affinity in
  `ScatterResources` (Phase 14): Deer in the Forest feature, Banana in Jungle,
  Iron/Sheep/Stone on Hills; Marsh/Oasis/Ice/Lake tiles carry nothing.
- **Luxury** (Gems, GoldOre, Silver, Silk, Spices, Dyes, Cotton, Incense, Ivory) —
  tech-revealed (Mining → Gems/GoldOre/Silver; Calendar → Silk/Spices/Dyes/Cotton/
  Incense; Animal Husbandry → Ivory), **very sparse** (1–3 of each per map).
  Gems/GoldOre/Silver sit on Hills, Silk in Forest, Spices/Dyes in Forest/Jungle
  canopy, the rest on open affinity terrain. **+1 Gold** when worked.
  `ResourceService.ControlledUniqueLuxuries`
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

Phase 14 pipeline (full detail in
[MAP_GENERATION.md](MAP_GENERATION.md)), in order:

1. **Height layers** (Phase 9.1, unchanged): a low-freq continental shape
   (simplex base+detail blended 70/30) with a radial falloff that pushes map
   edges to ocean, plus a **domain-warped ridged Simplex** mountain layer gated
   by a low-freq uplift mask — coherent mountain chains whose **relief** rings
   the crests with a foothill skirt of Hills; a separate mid-freq hilliness
   field scatters Hills across open lowland. The land/water split is calibrated
   to each map script's `TargetLandPercent` (percentile trick, Phase 11).
2. **Base terrain — latitude bands** (`Classify`): snow/tundra polar caps, a
   dryness-gated **desert belt** (cold-dry becomes steppe Plains), a hot
   semi-arid **savanna band**, and a moisture-split grass/plains heartland. The
   temperature jitter doubles as band-edge raggedness, so borders wobble
   instead of running in straight rows.
3. **Lakes & coastlines** (`MapPostProcess`): water regions are flood-filled —
   an enclosed basin of ≤9 tiles becomes **Lake** (bigger enclosed seas stay
   navigable); then every Ocean tile beside land becomes **Coast** and a
   probabilistic `ShelfChance` second ring extends the shallows (Archipelago
   gets broader shelves). A **majority filter** then absorbs single-tile land
   outliers (a lone Snow tile in grassland) without touching water, mountains,
   or features.
4. **Rivers** (Phase 9.4): traced downhill from Mountain/Hills sources along
   tile edges (count scales with highland area), stored in `MapData.Rivers`.
   Every river ends in water — one that bottoms out inland carves a **Lake** at
   its terminus. Rendered as blue channels in `WorldOverlay`; adjacent worked
   tiles gain the floodplain **+1 Food**.
5. **Features** (`FeaturePlacer`, Civ5's AddFeatures): polar **Ice** on the
   coldest sea, an equatorial **Jungle** band, **Forest** grown from a
   dedicated clump-noise field (per-script `ForestThreshold`), rare **Marsh**
   on the wettest flat grassland, isolated **Oasis** springs in open desert.
   Every placement is validated against `FeatureRules`.
6. **Resources**: strategic + bonus by terrain/feature affinity (one per tile),
   then sparse luxuries — all seeded so a given map seed always produces the
   same layout (see Resources above).

Continents: the falloff tends to produce multiple landmasses, but a minimum
count is **not** enforced. Verify changes with the `tune-map-generation`
histogram (terrain/feature percentages, legality violations, dry-river count).

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
- Higher roll deals damage equal to `(attacker_roll / defender_roll) × 40` to the loser, proportionally less to the winner (the `DamageScale` was raised 30 → 40 in Phase 10.5 for more decisive, faster wars)
- A unit's effective `strength` is its base value × its faction's combat multiplier × its veterancy multiplier (see §11)
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
| Scout | 25 | 6 | 6 | 3 | 1 |
| Warrior | 40 | 8 | 8 | 2 | 1 |
| Archer | 70 | 7 | 4 | 2 | 2 |
| Spearman | 60 (needs Bronze Working) | 6 | 12 | 2 | 1 |
| Horseman | 80 (needs Horseback Riding) | 12 | 7 | 4 | 1 |
| Swordsman | 90 (needs Iron Working) | 14 | 14 | 2 | 1 |
| Catapult | 100 (needs Iron Working) | 14 | 4 | 1 | 2 |
| Settler | 100 | 0 | 0 | 2 | — |
| Worker | 70 | 0 | 0 | 2 | — |

All units have sight radius 2 (the Scout sees 3) and 1 gold maintenance
(Settler/Worker cost 0).
The Horseman/Swordsman `requiredResource` (`horses`/`iron`) is enforced in the
build list — you must have access to the resource (see Resources, §1).

The **Scout** is a cheap exploration unit (`ignoresTerrainCost: true`): every
passable tile costs a flat 1 movement, so its 3 moves carry it across forest,
hills, and jungle as fast as open ground. It also sees one tile farther than
other units (sight 3). It still can't enter impassable tiles (Mountain/Ocean),
and its combat strength is ~75% of a Warrior's. As a recon
unit it **cannot capture cities** (`canCaptureCities: false`) — an enemy city
blocks its path just like an impassable tile.

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
- Cities start working tiles within a radius of **2** (`City.InitialBorderRadius`)
  excluding the city center, and **culture expands** that ring to a maximum of
  **3** (`City.MaxBorderRadius`) — see "Culture & Borders" below. There is no
  separate stored "territory" — a tile belongs to the nearest city center whose
  border radius reaches it (earlier-founded city wins ties).
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
- **Starvation:** a food deficit first drains the basket; once the basket is
  empty, each further deficit turn costs **−1 population** (Civ 5 style). A city
  never starves below 1 citizen. (`City.ProcessFood` → `CityFoodResult.Starved`)

### Culture & Borders

- Every city radiates **1 base culture/turn** (`CivEconomyService.CityBaseCulture`)
  plus its buildings' culture yields (Monument +2).
- Culture banks **per city** toward a border expansion: at
  `City.NextBorderCost` (30 × ring) the border radius grows by one ring, up to
  `City.MaxBorderRadius` (3). Expansion re-runs tile-control for neighbours.
- The same culture also accumulates **civ-wide** as a lifetime total
  (`Civilization.CultureAccumulated`) worth **1 score per 5 culture**
  (`ScoreService.CultureDivisor`). Social policies remain post-MVP backlog.

### Production Queue

- One item produced at a time
- Player sets queue at start of their turn for each city
- Overflow production carries over to next item

### Buildings (MVP set)

| Building | Cost | Req. Tech | Effect | Implemented? |
| --- | --- | --- | --- | --- |
| Monument | 60 | Philosophy | +2 Culture | ✅ border growth + score |
| Granary | 80 | Pottery | +2 Food | ✅ |
| Barracks | 100 | — | New units +15 XP | ✅ |
| Library | 90 | Writing | +2 Science | ✅ |
| Market | 120 | — | +2 Gold | ✅ |
| Walls | 130 | — | +5 City Defense | ✅ |

All building yields and effects feed the simulation. Numeric effects are
**data-driven tags** parsed by `GameState.BuildingEffectSum` —
`new_units_bonus_xp_<n>` (Barracks) and `city_defense_plus_<n>` (Walls) — so new
buildings carrying these tags work without code changes.

---

## 5. Civilizations

### MVP: 2 Players

- **Player** (`IsHuman = true`): human-controlled, color blue
- **Barbarians** (`IsHuman = false`): one AI opponent, color red

Identity (`Player`: id/name/human/color) is separate from civ-wide state
(`Civilization`: treasury, science, research) — see ARCHITECTURE.md.

### Starting Setup

- Each player begins with 1× Scout + 1× Settler.
- On **Continents** and **Archipelago** maps (Phase 13.3) each player is assigned their
  own landmass (human gets the largest; AI players take smaller ones, cycling if needed).
  On **Pangaea** and **Highlands** all players share the single largest landmass.
  Farthest-point sampling and the fertility floor apply within each player's assigned landmass.

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

### Objective Victory ("Establish the New World Order", Phase 10.5)

- The map carries a few contested **key sites** (`MapData.KeySites`, placed by
  `GameFactory` away from spawns). A site is **controlled** by whoever owns the
  nearest city within `KeySiteService.ControlRadius` (3 tiles).
- A player who controls **every** key site wins immediately — checked before
  domination, so it can fire on any turn while rivals still live.

### Score Victory (fallback)

- At **turn 250** (`VictoryService.ScoreVictoryTurn`, lowered from 500 in Phase
  10.5), if no one has won by objective or domination, the highest-scoring
  civilization wins.
- Score = (cities × 10) + (population × 3) + (techs researched × 5) +
  (key sites controlled × 25) + (gold ÷ 10). Weights are named constants in
  `ScoreService`, easy to retune.

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
  Scout + Settler opening costs 0/turn.
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

---

## 11. Factions & Fast Warfare (Phase 10)

A match is seeded with a **roster** of 2–8 players, each tied to a faction from
`data/factions.json` (chosen on the `FactionSetup` screen). Faction identity lives
on `Player.FactionId`; the modifier bag is resolved by `DataCatalog.FactionOf`,
which returns a neutral all-identity faction for slots with no faction (so every
hook is an unconditional multiply/add). See [FACTIONS.md](FACTIONS.md) for the roster.

### Signature passives (the v1 hooks)

- **Combat strength** — `FactionData.CombatStrengthMult` scales a unit's effective
  attack/defense in `GameState.TryAttack`/`TryAttackCity` (Iron Pact > 1).
- **Gold / Science / Rush-buy** — `GoldMult`, `ScienceMult`, `RushBuyDiscount` in
  `CivEconomyService` (Syndicate gold, Cognate science).
- **Sight & movement** — `SightBonus` (`FogOfWar`) and `TerrainCostMult`
  (`GameState.MovementCost`) for the Voyagers.
- **Fortress capital** — `CityDefenseBonus`, `CityRegenMult`,
  `CapitalProductionBonus` apply to a faction's **capital** only (Dominion).
- **Settlement** — `MinCityDistanceDelta` (effective settle spacing) and
  `SettleCostMult` (cheaper settlers) for the Free Settlements, which also get a
  free defender in every new city (`new_city_defender` trait).
- **Unique units** — `UnitVariants` maps a base unit id to a faction's variant;
  the swap happens once, at `GameState.CompleteProduction`, so UI/AI keep queuing
  base ids.

### Veterancy (unit XP)

- Units accrue `Experience` by surviving combat (`CombatXpPerFight`, scaled by the
  faction's `XpGainMult` — Iron Pact levels twice as fast).
- XP thresholds **15 / 45 / 90** grant levels 1–3; each level adds **+10%** combat
  strength (`Unit.VeterancyMult`). XP persists through save/load.

### Settlement normalization (Phase 10.4)

- `GameFactory` places the human at the map centre (nudged to the most fertile tile
  within a short radius), then spreads the other players by **farthest-point
  sampling** restricted to tiles clearing a **fertility floor** (work-radius yield
  sum) — Civ-5 start-normalization without the full region machinery.

### Diplomacy & hire-Reavers (Phase 10.6)

- `Diplomacy` holds symmetric pairwise stances (**War / Peace / NonAggression /
  Alliance**); the default is War (all-vs-all). Only players at War may attack each
  other — enforced in `GameState` combat and respected by **all** AI targeting:
  peaceful players are never attacked, marched on, counted as threats, or invaded
  amphibiously (`AIController.IsHostile`).
- **Minimal AI stance changes** (`AIController.ReviewDiplomacy`): from Peace the AI
  declares war when its HP-weighted military strength is ≥ **2×** the target's;
  an AI-vs-AI war whose forces are roughly even (within **1.33×** both ways)
  settles into Peace. The AI never breaks a NonAggression/Alliance pact and never
  ends a war with the human unilaterally — peace with the player is the player's
  call once the diplomacy UI lands.
- The **Syndicate** (the `can_hire_reavers` trait) may spend **60 gold**
  (`CivEconomyService.HireReaver`) to spawn a hired Mercenary — its gold-for-force
  signature play.

---

## 12. Civilopedia (in-game reference, Phase 12)

A Civ-5-style **Civilopedia** documents the game for the player: factions, units,
buildings, technologies, terrain, resources, core mechanics, and world lore. Reachable
from the **main menu**, the in-game **pause menu**, and the **F1** hotkey (see
[CONTROLS.md](CONTROLS.md)); in-game it is an overlay that leaves the match untouched.

- **Content** is authored in `data/civilopedia.json`, kept separate from the mechanical
  data files: a `prose` map (key → flavor text, e.g. `"faction:dominion"`, `"unit:warrior"`,
  `"terrain:jungle"`) plus standalone `articles` (lore / rules) grouped by category.
- **`CivilopediaService`** (headless, unit-tested) assembles the categories. Catalog-backed
  entries (factions/units/buildings/techs/terrain/resources) render a **live stats block**
  from `DataCatalog` / `TerrainYields` plus the authored prose, so numbers never go stale
  and a missing prose key degrades to stats-only. `world` / `mechanics` entries come
  straight from the articles.
- The UI (`Civilopedia.tscn` + `CivilopediaController`) is a content-only browser that
  raises `CloseRequested`; the in-game overlay hides on close, the standalone menu scene
  returns to the main menu.
