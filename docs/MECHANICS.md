# Core Game Mechanics (MVP)

> **Status:** Reflects the implementation through Phase 5 (tech tree & economy).
> Numbers below match the code (`TerrainYields`, `CityWorkforceService`,
> `CivEconomyService`, `CombatResolver`, the JSON in `data/`). Features not yet
> built are flagged **[planned]**.

---

## 1. Map

### Hex Grid
- Map size: 60×40 tiles (MVP default, configurable)
- Coordinate system: axial (q, r) — see TECH_STACK.md
- Each tile currently has exactly one **terrain type**. Tile **features** (e.g.
  oases, floodplains) are **[planned]** — `MapData` stores only terrain today.

### Terrain Types

Yields per `TerrainYields`. There is **no per-tile Gold** in the code yet — gold
comes only from buildings (see §9), so the Gold column is **[planned]**.

| Terrain | Movement Cost | Food | Production |
|---------|--------------|------|------------|
| Grassland | 1 | +2 | +0 |
| Plains | 1 | +1 | +1 |
| Desert | 1 | +0 | +1 |
| Tundra | 1 | +1 | +0 |
| Snow | 1 | +0 | +0 |
| Hills | 2 | +1 | +2 |
| Forest | 2 | +1 | +2 |
| Mountain | impassable | — | — |
| Ocean | impassable | +1 | +0 |
| Coast | impassable | +2 | +0 |

Ocean and Coast are currently **impassable** (no naval units yet); their food
values only matter to coastal cities working those tiles. Naval movement costs
are **[planned]**.

### Resources **[planned]**
No resource system is implemented yet — `MapGenerator` places terrain only, and
`MapData` stores nothing but terrain. The data files already reference resources
(`horses` for Horseman, `iron` revealed by Bronze Working), and techs list
`revealedResources`, but nothing scatters or grants them. Planned types:
- **Luxury**: gold + happiness when connected to a city
- **Strategic**: required to build certain units (e.g., Horses → Horseman)
- **Bonus**: +yield to the tile (e.g., Wheat +1 Food)

### Map Generation (Procedural)
- Algorithm: two layers of simplex noise (`FastNoiseLite`) blended 70/30, with a
  radial falloff that pushes map edges to ocean → island-like continents.
- Rivers: not in MVP
- Continents: the falloff tends to produce multiple landmasses, but a minimum
  count is **not** enforced.
- Resource scatter: **[planned]** (see above).

---

## 2. Turn Structure

```
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
- **[planned]** Turn limit (500) and score-based winner are not implemented yet.
- The AI runs a single reactive pass (attack / settle / advance) and auto-queues
  Warriors; it does not have a real city or research phase (see ARCHITECTURE.md).

---

## 3. Units

### Unit Properties

| Property | Description |
|----------|-------------|
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
  is the natural way to heal.

### MVP Unit Roster

| Unit | Cost (Production) | Atk | Def | Move | Range |
|------|------------------|-----|-----|------|-------|
| Warrior | 40 | 8 | 8 | 2 | 1 |
| Archer | 70 | 7 | 4 | 2 | 2 |
| Spearman | 60 (needs Bronze Working) | 6 | 12 | 2 | 1 |
| Horseman | 80 (needs Horseback Riding) | 12 | 7 | 4 | 1 |
| Swordsman | 90 (needs Iron Working) | 14 | 14 | 2 | 1 |
| Settler | 100 | 0 | 0 | 2 | — |
| Worker | 70 | 0 | 0 | 2 | — |

All units have sight radius 2 and 1 gold maintenance (Settler/Worker cost 0).
The Horseman/Swordsman list a `requiredResource` (`horses`/`iron`), but since
resources aren't implemented yet those requirements aren't enforced.

### Special Units
- **Settler**: Can found a city (`special: "found_city"`). Consumed on use.
- **Worker**: Carries `special: "build_improvement"`, but **tile improvements
  (Road, Farm, Mine) are not implemented yet [planned]** — a Worker currently
  has no action it can perform.

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
|----------|------|-----------|--------|--------------|
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

```
Pottery → Writing → Philosophy
    └→ Animal Husbandry → Horseback Riding
Mining → Bronze Working → Iron Working
```

| Tech | Cost (Science) | Unlocks |
|------|---------------|---------|
| Pottery | 35 | Granary |
| Writing | 55 | Library |
| Philosophy | 80 | Monument |
| Animal Husbandry | 35 | Pasture improvement, reveals Horse resource |
| Horseback Riding | 100 | Horseman unit |
| Mining | 35 | Mine improvement |
| Bronze Working | 55 | Spearman unit, reveals Iron resource |
| Iron Working | 100 | Swordsman unit |

- Only one tech can be researched at a time
- Prerequisites must be completed before a tech is available
- **Building/unit unlocks work** (a researched tech enables its building/unit in
  the city build menu). The **improvement** unlocks (Pasture, Mine) and
  **revealed resources** (Horse, Iron) are data only — those systems aren't built
  yet **[planned]**.

---

## 7. Win Conditions (MVP) **[planned]**

Not implemented yet (Phase 6). City capture works mechanically, but no
victory/defeat is detected or surfaced, and there is no turn limit or score
computation in code. The intended conditions:

### Domination Victory
- Capture the **capital city** of the opponent
- The capital is the first city founded by a civilization (note: there is no
  `IsCapital` flag in the model yet — this needs adding)

### Score Victory (fallback)
- After 500 turns, the civilization with the highest score wins
- Score = (population × 4) + (number of cities × 10) + (techs researched × 5)

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
- **Income**: sum of building Gold yields (Market +2). There is no per-tile or
  trade gold yet **[planned]**.
- **Maintenance**: each unit's `maintenanceGold` (1 for military, 0 for
  Settler/Worker), minus a **2-gold free grace** (`FreeUnitMaintenance`) — so a
  Warrior + Settler opening costs 0/turn.
- **Negative treasury**: units are disbanded until the treasury is non-negative,
  cheapest by **production cost** first, then lowest HP. Disbanding refunds the
  unit's maintenance for the turn but no production.
- **[planned]** Buying buildings/units instantly with gold is not implemented.

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

**[planned]:** "unit under attack" alerts and the score-victory warning (last 50
turns) aren't implemented. The notification surface is a single transient/
persistent label, not yet a scrolling event log (Phase 6).
