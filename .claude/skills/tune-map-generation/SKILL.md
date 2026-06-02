---
name: tune-map-generation
description: Tune NWO's procedural map generation — terrain shape, biome mix, mountains/hills, rivers, and resource scatter in MapGenerator.cs. Use when asked to make maps more varied/mountainous/wetter, change how biomes or rivers form, or rebalance resource placement. NOT for gameplay numbers (use tune-mechanics) or tile art (use generate-terrain-art).
allowed-tools: PowerShell, Read, Edit, Grep
---

# tune-map-generation

Changes how the world is *shaped* — the noise pipeline and the rules that turn it
into terrain, rivers, and resources. All of it lives in
[scripts/map/MapGenerator.cs](../../../scripts/map/MapGenerator.cs); the design
rationale is [docs/MAP_GENERATION.md](../../../docs/MAP_GENERATION.md).

**Scope boundary** — this skill owns terrain *shape*. For gameplay *numbers*
(yields, movement cost, combat, economy) use `tune-mechanics`; for the *look* of a
terrain's tile use `generate-terrain-art`. Adding a brand-new `TerrainType` touches
all three: enum + colour/elevation (`HexProjection`), yields (`TerrainYields`), art
(`TerrainArtGenerator`), and placement here.

## The pipeline (all in `MapGenerator.cs`)

Per tile: continental shape (FBM + radial falloff) → mountain layer (domain-warped
ridged Simplex gated by an uplift mask; its `relief` also makes foothills) → climate
(independent **moisture** + **temperature** noise) → `Classify(...)` picks the biome.
Then `TraceRivers` and `ScatterResources` run over the finished tiles.

The tuning knobs are the named `const float` block at the top. Common requests:

| Want | Knob(s) |
| --- | --- |
| More/larger continents | `RadialFalloff` ↓, `BaseFrequency` ↓ |
| More mountains / longer chains | `MountainBoost` ↑, `MountainLevel` ↓, widen `UpliftLow..UpliftHigh` |
| More foothills around peaks | `HillRelief` ↓ |
| More **scattered** hills (off the mountains) | `HillThreshold` ↓ (more), `HillFrequency` ↑ (smaller patches) |
| More biome variety / less monotone | `MoistureFrequency` ↑, `TemperatureFrequency` ↑ |
| Hotter/colder world | the `(1 - lat) * k + jitter` weights in the temperature line |
| Shift biome boundaries | the `tb`/`mb` band thresholds + the climate matrix in `Classify` |
| More/denser resources | `CandidatesFor` chances; luxuries via `LuxuryPlacements` |
| More rivers | the `target` in `TraceRivers` |

Rivers always end in water: a river that bottoms out inland carves a lake at its
terminus (`CarveLake`). Don't reintroduce rivers that stop mid-land.

## Verify

`FastNoiseLite` is a Godot native type, so generation **cannot** run under xUnit —
distribution must be judged through headless Godot, not a unit test. Use the bundled
diagnostic, which prints a terrain histogram (% of land) + river/resource counts
across seeds:

```powershell
& .claude/skills/tune-map-generation/histogram.ps1            # 5 seeds, 60x40
& .claude/skills/tune-map-generation/histogram.ps1 -Seeds 8 -Size 80x52
```

Iterate: edit knobs → `histogram.ps1` → repeat until the spread looks right (a
healthy map is roughly Grassland-led with Hills ~15-20% of land, Forest present,
Mountains in chains, and every other biome appearing). Then run the **`run-checks`**
skill (build + tests + scene check) to confirm nothing broke.

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
