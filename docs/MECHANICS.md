# Core Game Mechanics (MVP)

---

## 1. Map

### Hex Grid
- Map size: 60×40 tiles (MVP default, configurable)
- Coordinate system: axial (q, r) — see TECH_STACK.md
- Each tile has exactly one **terrain type** and zero or one **feature**

### Terrain Types

| Terrain | Movement Cost | Food | Production | Gold |
|---------|--------------|------|------------|------|
| Grassland | 1 | +2 | +0 | +0 |
| Plains | 1 | +1 | +1 | +0 |
| Desert | 1 | +0 | +0 | +0 |
| Tundra | 1 | +1 | +0 | +0 |
| Snow | 1 | +0 | +0 | +0 |
| Hills | 2 | +0 | +2 | +0 |
| Forest | 2 | +1 | +1 | +0 |
| Mountain | impassable | — | — | — |
| Ocean | 3 (naval only) | +1 | +0 | +1 |
| Coast | 2 (naval only) | +1 | +0 | +1 |

### Resources (MVP — 3 types)
- **Luxury**: +4 gold when connected to a city; happiness bonus (not implemented in MVP)
- **Strategic**: required to build certain units (e.g., Horses → Horseman)
- **Bonus**: +yield to the tile (e.g., Wheat +1 Food)

### Map Generation (Procedural)
- Algorithm: fractal heightmap → terrain assignment → resource scatter
- Rivers: not in MVP
- Continents: at least 2 per map

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

- Turn limit: 500 turns (configurable). Score-based winner if limit reached.
- Simultaneous movement is **not** in MVP — player goes first, then AI.

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
- Each unit has `MovementPoints` reset at start of its civ's turn
- Moving onto a tile costs the tile's movement cost
- Cannot move through enemy units
- Can move through friendly units (stacking: 1 military + 1 civilian max per tile in MVP)

### Combat (Simplified)
- Attacker and defender each roll: `strength × (HP/100) × random(0.85, 1.15)`
- Higher roll deals damage equal to `(attacker_roll / defender_roll) × 30` to the loser, proportionally less to the winner
- Attacking costs all remaining movement points
- Ranged units attack without retaliation (range 2)

### MVP Unit Roster

| Unit | Cost (Production) | Atk | Def | Move | Range |
|------|------------------|-----|-----|------|-------|
| Warrior | 40 | 8 | 8 | 2 | 1 |
| Archer | 70 | 7 | 4 | 2 | 2 |
| Spearman | 60 | 6 | 12 | 2 | 1 |
| Horseman | 80 (needs Horses) | 12 | 7 | 4 | 1 |
| Settler | 100 | 0 | 0 | 2 | — |
| Worker | 70 | 0 | 0 | 2 | — |

### Special Units
- **Settler**: Can found a city. Consumed on use.
- **Worker**: Can build tile improvements (Road, Farm, Mine). Takes 3 turns per improvement.

---

## 4. Cities

### Founding
- A Settler unit can found a city on any non-water, non-mountain tile not within 3 tiles of another city
- The city claims the surrounding 3-tile radius as its **territory**
- The 6 tiles immediately adjacent are worked automatically at founding

### City Yields
- Each turn a city produces: **Food**, **Production**, **Gold**, **Science**
- Yields come from worked tiles + buildings + base city values
- Base city values: +1 Food, +1 Production, +1 Gold, +1 Science

### Growth
- Food surplus accumulates in a **Food Basket**
- Basket threshold: `15 + (6 × population)`
- When threshold reached: population +1, basket resets to 0
- Population starves (−1 pop) if Food yield < 0 for 3 consecutive turns

### Production Queue
- One item produced at a time
- Player sets queue at start of their turn for each city
- Overflow production carries over to next item

### Buildings (MVP set)

| Building | Cost | Effect |
|----------|------|--------|
| Monument | 60 | +2 Culture |
| Granary | 80 | +2 Food |
| Barracks | 100 | New units start with +15 XP |
| Library | 90 | +2 Science |
| Market | 120 | +2 Gold |
| Walls | 130 | +5 City Defense |

---

## 5. Civilizations

### MVP: 2 Civilizations
- **Player Civilization**: human-controlled
- **AI Civilization**: one opponent

### Civilization Properties
- Name, color, starting unit set (1× Warrior, 1× Settler)
- Starting position: placed on opposite sides of the map

### Unique Abilities (MVP — placeholder, implement in post-MVP)
- Both civs use identical stats in MVP for simplicity

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

---

## 7. Win Conditions (MVP)

### Domination Victory
- Capture the **capital city** of the opponent
- The capital is the first city founded by a civilization

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

- Gold accumulates each turn from city yields and trade
- Gold is spent on: unit maintenance, buying buildings/units instantly
- Unit maintenance: each military unit costs 1 gold/turn
- If gold goes negative: units are disbanded (cheapest first) until balanced

---

## 10. Notifications & Events

Events the player must be informed of each turn:
- City grew (population +1)
- Production complete
- Tech research complete
- Unit under attack
- City captured
- Score victory warning (last 50 turns)
