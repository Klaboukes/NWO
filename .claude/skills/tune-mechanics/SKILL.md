---
name: tune-mechanics
description: Change NWO gameplay numbers and balance — terrain yields, combat formula, city growth/workforce, gold/science economy, victory scoring. Use when asked to rebalance, retune, or adjust how a rule behaves (not when adding new content — use add-content for that).
allowed-tools: Read, Edit, PowerShell, Grep
---

# tune-mechanics

Gameplay rules and constants live in the **headless core** so they're unit-testable.
Tuning means: change the authoritative constant, keep the docs row in lockstep, and
re-run the tests that pin the behaviour.

## Architecture boundary

All of these live in pure C# (`scripts/core`, `scripts/map`) with no Godot
dependencies, exercised by `NWO.Tests` without a scene. Keep them that way: no RNG
that isn't seeded, no new global mutable state, no Godot nodes. Determinism (seeded
combat RNG, deterministic tie-breaks) is a deliberate design property — see
[docs/ARCHITECTURE.md](../../../docs/ARCHITECTURE.md).

## Where each number lives

| Tuning area | Authoritative source | Pinning tests |
|---|---|---|
| Terrain yields & movement cost | `scripts/map/TerrainYields.cs` | `CityWorkforceServiceTests` |
| Combat formula & expected damage | `scripts/core/CombatResolver.cs` | `CombatResolverTests`, `GameStateCombatTests` |
| City growth, food, workforce, work radius | `scripts/core/CityWorkforceService.cs`, `scripts/entities/City.cs` | `CityWorkforceServiceTests`, `CityTests` |
| Gold income, maintenance, rush-buy, research | `scripts/core/CivEconomyService.cs` | `EconomyTests`, `CivEconomyServiceTests` |
| Improvements (yields/terrain/tech/turns) | `scripts/core/ImprovementService.cs` | `ImprovementTests` |
| Strategic-resource reveal/access | `scripts/core/ResourceService.cs` | `ResourceTests` |
| Victory scoring & turn limit | `scripts/core/ScoreService.cs`, `scripts/core/VictoryService.cs` | `VictoryServiceTests` |
| Per-entity stats (costs/atk/def) | `data/*.json` (use the **add-content** skill) | — |

Most balance knobs are **named constants** at the top of these files (e.g.
`WorkRadius`, `StartingTreasury`, `FreeUnitMaintenance`, `ScoreVictoryTurn`,
`MinCityDistance`). Grep for the constant name before editing.

## Procedure

1. Locate the constant/formula in the table above (grep the named constant).
2. Make the change in the headless core; preserve determinism.
3. Update the matching row/number in [docs/MECHANICS.md](../../../docs/MECHANICS.md) —
   the docs are the source of truth and must not drift.
4. Update or add the pinning test so the new value is asserted (a balance change that
   breaks a test means the test encoded the old number — update it deliberately).
5. Run the **`run-checks`** skill.

## Maintenance

If a constant moves to a new file or the test mapping changes, update the table above.
