---
name: tune-map-generation
description: Tune NWO's procedural map generation — terrain shape, biome mix, mountains/hills, rivers, and resource scatter in MapGenerator.cs. Use when asked to make maps more varied/mountainous/wetter, change how biomes or rivers form, or rebalance resource placement. NOT for gameplay numbers (use tune-mechanics) or tile art (use generate-terrain-art).
allowed-tools: PowerShell, Read, Edit, Grep
---

# tune-map-generation

Changes how the world is *shaped* — the noise pipeline and the rules that turn it
into terrain, features, rivers, and resources. The pipeline spans
[scripts/map/MapGenerator.cs](../../../scripts/map/MapGenerator.cs) (noise +
classification + rivers + resources),
[scripts/map/MapPostProcess.cs](../../../scripts/map/MapPostProcess.cs) (lakes,
coastlines, outlier filter), and
[scripts/map/FeaturePlacer.cs](../../../scripts/map/FeaturePlacer.cs) (vegetation/
ice); per-script knobs in [scripts/map/MapScript.cs](../../../scripts/map/MapScript.cs).
Design rationale: [docs/MAP_GENERATION.md](../../../docs/MAP_GENERATION.md).

**Scope boundary** — this skill owns terrain *shape*. For gameplay *numbers*
(yields, movement cost, combat, economy) use `tune-mechanics`; for the *look* of a
terrain's tile use `generate-terrain-art`. Adding a brand-new `TerrainType` or
`Feature` touches all three: enum + colour/elevation (`HexProjection`), yields
(`TerrainYields`/`FeatureYields`) + legality (`FeatureRules`), art
(`TerrainArtGenerator`), and placement here.

## The pipeline (Phase 14, Civ5 model)

Per tile: continental shape (FBM + radial falloff) → mountain layer (domain-warped
ridged Simplex gated by an uplift mask; its `relief` also makes foothills) → climate
(independent **moisture** + **temperature** noise) → `Classify(...)` picks the BASE
terrain from **latitude bands**. Then `MapPostProcess` (lakes ≤9 tiles, adjacency
coastlines + shelf, single-tile outlier filter) → `TraceRivers` → `FeaturePlacer`
(Ice/Jungle/Forest/Marsh/Oasis as Features over the base) → `ScatterResources`.

Knobs: the `const float` blocks atop `MapGenerator` and `FeaturePlacer`, plus the
per-script `MapScriptParams`. Common requests:

| Want | Knob(s) |
| --- | --- |
| More/larger continents | `RadialFalloff` ↓, `BaseFrequency` ↓ |
| More mountains / longer chains | `MountainBoost` ↑, `MountainLevel` ↓, widen `UpliftLow..UpliftHigh` |
| More foothills around peaks | `HillRelief` ↓ |
| More **scattered** hills (off the mountains) | `HillThreshold` ↓ (more), `HillFrequency` ↑ (smaller patches) |
| Denser/sparser woods | `ForestThreshold` ↓/↑ (`MapScriptParams`); clump size via `ForestFrequency` |
| Bigger jungle belt | `JungleMaxLat` / `JungleMinMoisture` in `FeaturePlacer` |
| More/less polar ice | `PolarIceLat` / `IceRampLat` / `IceRampChance` |
| Wider/narrower desert belt | the `moisture < 0.34` / `temp > 0.50` cut in `Classify` |
| Shift snow/tundra caps | the `effLat` band cuts in `Classify` (0.86 / 0.72) |
| Broader shallows | `ShelfChance` (`MapScriptParams`) |
| Bigger lakes allowed | `MapPostProcess.LakeMaxArea` |
| Hotter/colder world | the `(1 - lat) * k + jitter` weights in the temperature line |
| More/denser resources | `CandidatesFor` chances; luxuries via `LuxuryPlacements` |
| More rivers | the `target` in `TraceRivers` |

Rivers always end in water: a river that bottoms out inland carves a **Lake** at
its terminus (`CarveLake`). Don't reintroduce rivers that stop mid-land — the
histogram's `rivers-not-reaching-water` must stay 0, as must
`legality-violations` (every feature placement obeys `FeatureRules`).

## Verify

`FastNoiseLite` is a Godot native type, so generation **cannot** run under xUnit —
distribution must be judged through headless Godot, not a unit test. Use the bundled
diagnostic, which prints a terrain histogram (% of land) + river/resource counts
across seeds:

```powershell
& .claude/skills/tune-map-generation/histogram.ps1            # 5 seeds, 60x40
& .claude/skills/tune-map-generation/histogram.ps1 -Seeds 8 -Size 80x52
```

Iterate: edit knobs → `histogram.ps1` → repeat until the spread looks right. A
healthy Continents map (60×40): Grassland/Plains-led land, Hills ~25-30%,
Forest+Jungle ~24-31% in coherent clumps, a 5-10% desert belt, polar ice
~15-18% of water, a few lakes/marshes/oases, `legality-violations=0`, and
`rivers-not-reaching-water=0`. Then run the **`run-checks`** skill (build +
tests + scene check) to confirm nothing broke. (`MapPostProcess` and
`FeaturePlacer` take plain data, so their logic — unlike the noise — also has
direct xUnit coverage in `MapPostProcessTests` / `FeaturePlacerTests`.)

**The look still needs a human F5 run** — headless reports counts, not whether the
mountain chains, hill scatter, and rivers actually *read* well at the oblique camera.
Flag that; don't claim the visuals are verified. The diagnostic tool
(`scripts/tools/MapHistogram.cs` + `scenes/tools/MapHistogram.tscn`) is dev-only and
not wired into the game, same as `BakeTerrainTiles`.

## Maintenance

If the `MapGenerator` knobs, the `Classify` signature, the river/lake logic, or the
Godot binary path change, update this file, `histogram.ps1`, and `MapHistogram.cs` to
match. Keep the scope boundary above aligned with `tune-mechanics` and
`generate-terrain-art`.
