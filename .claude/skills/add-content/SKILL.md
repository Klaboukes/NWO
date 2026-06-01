---
name: add-content
description: Add or edit a data-driven unit, building, or tech in NWO. Use when adding/changing entries in data/units.json, data/buildings.json, or data/techs.json — e.g. "add a Catapult unit", "add an Aqueduct building", "add the Masonry tech". Content is JSON; no C# change is normally needed.
allowed-tools: Read, Edit, PowerShell, Grep, Glob
---

# add-content

NWO content is **data-driven**: units, buildings, and techs live in `data/*.json`,
loaded by `DataLoader` and indexed by id in `DataCatalog`. Adding content is normally
a JSON edit plus a doc/table update plus a test — **no C# change**.

## Architecture boundary

Data classes (`UnitData`, `BuildingData`, `TechData`) are immutable definitions loaded
from JSON, never mutated at runtime. Don't add gameplay logic to them. Keep new rules
in the headless core (the static services), not in Godot nodes. Rationale:
[docs/ARCHITECTURE.md](../../../docs/ARCHITECTURE.md) "Data vs. Instance".

## Schemas (copy an existing sibling entry, then change ids/numbers)

**`data/units.json`** — `id`, `name`, `productionCost`, `attack`, `defense`,
`movement`, `range` (1 melee / 2 ranged), `sight`, `maintenanceGold`, `requiredTech`
(tech id or `null`), `requiredResource` (`"horses"`/`"iron"`/`null`). Optional
`special`: `"found_city"` or `"build_improvement"`.

**`data/buildings.json`** — `id`, `name`, `productionCost`, `requiredTech`,
`yields` (any of `food`/`production`/`gold`/`science`/`culture`), optional `effect`
string. Note: only `food`/`science`/`gold` yields and the Walls/`city_defense_plus_5`
effect feed the simulation today; `culture` and the Barracks XP effect are data-only.

**`data/techs.json`** — `id`, `name`, `scienceCost`, `prerequisites` (array of tech
ids), `unlocks` object with any of `units`, `buildings`, `improvements`,
`revealedResources` (arrays of ids).

## Gating (how content becomes reachable)

- `requiredTech` hides a unit/building in the build list until that tech is researched.
- A tech's `unlocks` is what enables its items — a unit with `requiredTech: "x"` should
  appear in tech `x`'s `unlocks.units`. Keep both sides consistent.
- `requiredResource` additionally gates a unit on the civ controlling that resource
  (see `ResourceService`); `revealedResources` on the unlocking tech reveals it.

## Procedure

1. Add the entry to the right `data/*.json`, mirroring an existing sibling's shape.
2. If gated by a new/existing tech, wire **both** `requiredTech` and the tech's
   `unlocks` list.
3. Add or extend a test in `NWO.Tests` (e.g. `DataCatalogTests`, `TechCatalogTests`)
   asserting the new id loads and the unlock/gating resolves.
4. Update the matching roster/table in [docs/MECHANICS.md](../../../docs/MECHANICS.md)
   (§3 units, §4 buildings, or §6 techs) so docs stay the source of truth.
5. **If adding a unit:** add a `case` for the new `unitId` in
   `scripts/art/UnitArtGenerator.cs` → `Generate()` (draw a distinctive silhouette),
   then invoke the **`add-art-asset`** skill to bake and commit
   `assets/art/units/<id>.png`. If you skip this, the unit falls back to the generic
   disc — acceptable temporarily, but every shipped unit should have its own shape.
6. Run the **`run-checks`** skill.

## Maintenance

If a new field is added to any data schema, or the catalog/loader changes, update the
schema notes above. When new unit IDs are added, keep `UnitArtGenerator.Generate()`
in sync so every ID has a dedicated silhouette (not the generic disc fallback).
