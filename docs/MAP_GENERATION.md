# Procedural Terrain Generation Patterns for a Civ-Style World Map

## Overview

For a Civilization-style hex world, the biggest terrain quality improvements usually come from separating:

1. Continental shape
2. Regional uplift
3. Local roughness
4. Erosion and river carving
5. Terrain classification

A strong terrain pipeline often looks like:

```text
Macro landmass generation
→ tectonic/uplift simulation
→ erosion shaping
→ local detail noise
→ terrain classification
```

The key idea is:

> Avoid generating the entire world from a single noise map.

Instead, combine multiple geological layers with different scales and purposes.

---

## 1. Tectonic Plates

### Why this matters

This is the single most effective pattern for believable mountain ranges.

Real mountain systems form from tectonic interactions:

* collision → mountains
* sliding → ridges/hills
* separation → oceans/rifts

This creates:

* long coherent mountain chains
* realistic continental structure
* directional terrain
* strategic chokepoints

---

### Basic Implementation

#### Step A — Generate Plates

Use Voronoi regions.

Each tile belongs to:

* a plate ID
* a movement vector

Example:

* 12–30 plates on a huge map

```text
plate[x,y] = nearest_voronoi_seed
velocity[plate] = random_direction
```

---

#### Step B — Detect Plate Borders

Compare neighboring plate IDs.

At boundaries:

```text
relative_velocity = velocityA - velocityB
compression = dot(relative_velocity, border_normal)
```

Interpretation:

| Compression | Result |
| --- | --- |
| High positive | Mountains |
| Mild positive | Hills/ridges |
| Near zero | Stable terrain |
| Negative | Rift/ocean trench |

---

### Gameplay Benefits

This naturally creates:

* continent-dividing mountain chains
* defendable borders
* strategic passes
* inland basins
* isolated regions

It produces much more readable strategy maps than pure noise.

---

## 2. Ridge Noise and Domain Warping

### Problem with Basic Noise

Standard Perlin/simplex noise tends to create:

* blobs
* isolated bumps
* unrealistic terrain

Real mountains are directional.

---

### Domain Warping

Instead of:

```text
height = noise(x, y)
```

Use:

```text
x2 = x + noise2(x,y) * warp_strength
y2 = y + noise3(x,y) * warp_strength

height = noise(x2, y2)
```

This bends and stretches terrain into:

* ridges
* folds
* curved mountain systems

---

### Ridged Multifractal Noise

Use:

* ridged simplex
* ridged Perlin
* multifractal FBM

These emphasize peaks naturally.

Excellent for:

* alpine terrain
* jagged mountain regions
* rough hill systems

---

## 3. Explicit Mountain Spine Graphs

### Why this works well in strategy games

Instead of relying entirely on simulation, generate intentional mountain chains.

This gives better control over:

* regional separation
* chokepoints
* pass locations
* civilization borders

---

### Technique

#### Step 1 — Select uplift nodes

Choose several major mountain anchors.

#### Step 2 — Connect with splines

Generate curved spline paths between nodes.

#### Step 3 — Stamp elevation

```text
height += max(0, 1 - distance/radius)^2
```

Then apply local noise afterward.

---

### Result

You get:

* Himalaya-style chains
* continent-spanning barriers
* mountain arcs
* strategic valleys

This often produces better gameplay than fully realistic geology.

---

## 4. Regional Height Masks

### Purpose

Avoid “mountains everywhere.”

Create large-scale regions with different terrain character.

---

### Example

Generate low-frequency uplift:

```text
uplift = lowfreq_noise(x,y)
```

Then:

```text
mountain_strength = clamp(uplift)
```

Only apply strong mountain generation where uplift exists.

---

### Result

This creates:

* flat continental interiors
* distinct mountain belts
* rolling hill countries
* regional identity

---

## 5. Foothill Systems

### Important Observation

Real mountains rarely transition directly into flat plains.

Foothills improve:

* realism
* traversal
* visual readability
* gameplay pacing

---

### Technique

After mountains are generated:

```text
hilliness = exp(-distance_to_mountains * k)
```

or:

```text
hilliness = blur(mountain_mask)
```

Use hilliness to bias terrain toward rolling hills.

---

### Result

Creates:

* foothill belts
* gradual transitions
* natural expansion difficulty

---

## 6. Erosion Simulation

### Why erosion matters

Mountains without erosion look artificial.

Even lightweight erosion dramatically improves terrain.

---

### Hydraulic Erosion

Simulate:

* rainfall
* downhill water flow
* sediment transport

Effects:

* valleys
* river basins
* smoother terrain
* realistic drainage

---

### Thermal Erosion

Move material from steep slopes to lower neighbors.

Effects:

* softened slopes
* talus formation
* realistic hill transitions

---

### Gameplay Benefits

Erosion creates:

* traversable valleys
* river corridors
* settlement zones
* natural movement paths

---

## 7. Rivers as Terrain Features

### Rivers should shape terrain

Do not treat rivers as visual decals.

Generate rivers after uplift but before biome assignment.

---

### Typical River Pipeline

1. Calculate flow direction
2. Accumulate water flow
3. Identify river thresholds
4. Carve shallow channels
5. Add floodplains nearby

---

### Benefits

Rivers naturally create:

* fertile land
* civilization corridors
* valleys
* strategic geography

Mountain ranges without rivers often feel fake.

---

## 8. Geological Layering

### Recommended Model

Instead of:

```text
final_height = one_big_noise_map
```

Use:

```text
final_height =
    continent_shape +
    tectonic_uplift +
    erosion +
    local_noise
```

Each layer serves a different purpose.

---

### Suggested Responsibilities

| Layer | Purpose |
| --- | --- |
| Continents | Large-scale land/ocean layout |
| Tectonics | Major mountain systems |
| Regional uplift | Terrain identity |
| Erosion | Realistic shaping |
| Local noise | Detail variation |

---

## 9. Terrain Age System

### Realistic Variation

Real worlds contain:

* ancient eroded cratons
* young sharp mountains
* stable plateaus
* recently uplifted ranges

---

### Implementation

Assign regions:

* old
* medium
* young

Apply:

* stronger erosion to older terrain
* sharper ridges to younger terrain

---

### Result

Adds huge visual variety.

Example:

| Terrain Age | Characteristics |
| --- | --- |
| Old | Flat, smooth, fertile |
| Medium | Rolling hills |
| Young | Jagged mountains |

---

## 10. Directionality

### Important Principle

Noise is isotropic.

Real geology is directional.

Mountain systems usually align along stress directions.

---

### Recommendation

Assign preferred orientations per continent.

Examples:

* east-west fold mountains
* north-south ridges
* curved island arcs

---

### Result

This makes worlds feel:

* coherent
* authored
* geologically believable

---

## Recommended Hybrid Pipeline

This setup works extremely well for Civ-style strategy games.

---

### Step 1 — Continents

Generate low-frequency continental shapes.

Recommended:

* FBM simplex noise
* continent masks
* tectonic ocean separation

---

### Step 2 — Tectonic Plates

Generate Voronoi plates and movement vectors.

Use plate interactions to determine:

* mountains
* hills
* rifts
* ocean trenches

---

### Step 3 — Mountain Chains

Generate explicit spline-based mountain systems along compressive borders.

Add:

* ridge masks
* warped ridged noise
* elevation falloff

---

### Step 4 — Regional Uplift

Apply low-frequency uplift masks.

These define:

* flatlands
* plateaus
* rough regions
* mountain provinces

---

### Step 5 — Local Detail

Add:

* ridged multifractal noise
* warped simplex noise
* micro variation

---

### Step 6 — Erosion

Run:

* hydraulic erosion
* thermal erosion

Even a few iterations help significantly.

---

### Step 7 — Rivers

Generate flow accumulation and river carving.

Use rivers to shape valleys and fertile land.

---

### Step 8 — Terrain Classification

Example:

```text
height < sea_level      → ocean
height < plains_level   → plains
slope < hill_threshold  → hills
otherwise               → mountains
```

You can additionally use:

* moisture
* temperature
* latitude
* rainfall

for biome assignment.

---

## Gameplay-Oriented Advice

Pure realism is often bad for strategy gameplay.

Good Civ-style worlds need:

* navigable interiors
* readable geography
* defendable borders
* expansion zones
* chokepoints
* settlement corridors
* varied terrain quality

---

### Terrain Patterns That Work Well

| Pattern | Gameplay Effect |
| --- | --- |
| Long mountain arcs | Natural empire borders |
| Broken passes | Strategic warfare |
| Interior basins | Valuable contested land |
| Foothill belts | Gradual expansion difficulty |
| River valleys | Settlement corridors |
| Coastal ranges | Naval chokepoints |
| Plateaus | Regional civilization identity |
| Ancient flatlands | Productive heartlands |

---

## One Extremely Effective Combination

A particularly strong setup:

* tectonic uplift + erosion for mountains
* warped low-frequency noise for hills

Why:

* mountains should feel structured
* hills should feel organic and rolling

This combination tends to produce terrain that feels both believable and enjoyable to play.

---

## Final Recommendation

If development time is limited, prioritize these systems in order:

1. Plate-based uplift
2. Explicit mountain spines
3. Domain-warped ridged noise
4. Foothill generation
5. Rivers
6. Lightweight erosion

Even implementing only the first three can dramatically improve world quality over traditional noise-only terrain generation.

---

## NWO-Specific Notes

## Current Baseline (as of Phase 7.2)

`MapGenerator.cs` already implements:

* Low-frequency `FastNoiseLite` continental shape + radial edge falloff → land/ocean mask
* Higher-frequency detail noise summed in
* Single 1D height threshold table → `TerrainType` (10 types, height only)
* Deterministic scatter of 2 strategic resources (Horses on Plains/Grassland, Iron on Hills)

This is a solid starting point. The improvements below layer on top without discarding it.

---

## Scale Considerations for a 60×40 Map

Full tectonic-plate simulation and multi-pass hydraulic erosion are designed for large
maps (>200×200). On a 60×40 map:

* Tectonic plate boundaries land every ~6–10 tiles — too dense to read as geography.
* Hydraulic erosion needs many iterations to be visible at this scale.

**Recommended substitute:**

| "Full" technique | NWO substitute | Why |
| --- | --- | --- |
| Voronoi tectonic plates | Domain-warped ridged Simplex | Same coherent chains, O(1) per tile |
| Hydraulic erosion | Blurred mountain falloff for foothills | Visual effect without the simulation |
| Thermal erosion | Skip | Mountains are strategic, not scenic |

The domain-warp approach from section 2 + ridged multifractal from section 2 is the
single highest-value improvement for this map size.

---

## Adding a Moisture Axis

The rest of this document describes terrain purely as a function of *height*.
That produces Tundra and Desert at similar elevations — geographically confusing.

Add a second independent low-frequency noise pass for **moisture** (scale ~0.03):

```text
moisture = moistureNoise(x, y)   // 0..1, independent of height
biome    = HeightMoistureToBiome(height, moisture)
```

Biome lookup (simplified):

| Height band | Low moisture | Mid moisture | High moisture |
| --- | --- | --- | --- |
| Low (plains) | Desert | Plains | Grassland |
| Mid (lowlands) | Savanna | Grassland | Jungle / Wetlands |
| High (uplands) | Hills | Forest | Forest |
| Very high | Mountains | Mountains | Mountains |
| Arctic (polar Y) | Snow | Tundra | Tundra |

This single change gives 3–5 distinct biome regions per map with minimal code.

---

## Resource Placement Patterns

NWO uses three resource tiers, matching Civ5 conventions:

### Bonus resources

Always visible (no tech reveal). Dense scatter. +1 Food or +1 Prod on the worked tile.

| Resource | Terrain affinity | Target density |
| --- | --- | --- |
| Wheat | Plains, Grassland | 6% of valid tiles |
| Cattle | Grassland | 5% |
| Sheep | Hills, Grassland | 5% |
| Fish | Coast, Ocean | 7% |
| Deer | Forest, Tundra | 6% |
| Stone | Hills, Plains | 4% |
| Banana | Jungle | 8% |

### Strategic resources

Tech-revealed. Sparse. Gate unit production.

| Resource | Terrain affinity | Reveal tech |
| --- | --- | --- |
| Horses | Plains, Grassland | Animal Husbandry |
| Iron | Hills | Bronze Working |
| Coal | Hills, Mountains | (future industrial tech) |
| Oil | Desert, Coast | (future tech) |

### Luxury resources

Tech-revealed. Very sparse (1–3 per map). +1 Gold on the worked tile. Each unique type
controlled contributes to a future amenity/happiness system.

| Resource | Terrain affinity | Reveal tech |
| --- | --- | --- |
| Gems | Hills, Mountains | Mining |
| Gold (mineral) | Hills, Mountains | Mining |
| Silver | Hills | Mining |
| Silk | Forest | Calendar |
| Spices | Jungle, Forest | Calendar |
| Dyes | Forest, Jungle | Calendar |
| Cotton | Plains, Grassland | Calendar |
| Incense | Desert, Plains | Calendar |
| Ivory | Plains, Grassland | Animal Husbandry |

---

## NWO Priority Order

For Phase 7.5, implement in this order (higher payoff first):

1. **Domain-warped ridged noise for mountains** — biggest visual improvement, minimal code
2. **Moisture axis → biome table** — replaces height-only thresholds; adds Savanna/Jungle
3. **Bonus resources** — always-visible, simple scatter, immediate yield payoff
4. **Luxury resources** — tech-revealed, sparse, happiness scaffold
5. **Rivers** — edge data structure + downhill tracing + floodplain yield + rendering
6. **Foothills (blur-of-mountain mask)** — polish pass, low priority

Skip for NWO at this scale:

* Full Voronoi tectonic simulation
* Multi-pass hydraulic / thermal erosion
* Terrain age system (nice idea, not needed at 60×40)

---

## Civ 5 Patterns — Adopted / Candidate / Skipped

Civ 5's generator is Lua: a `Fractal` (midpoint-displacement) height field with
**percentile thresholds**, latitude + rainfall terrain, edge rivers, and the large
`AssignStartingPlots.lua` that places starts and the three resource tiers with
"impact-and-ripple" spacing and start normalization. NWO borrows the *patterns*, not
the implementation — Civ's heaviest machinery serves 8-player huge-map multiplayer
fairness, whereas NWO is 60×40 and (per Phase 10) is deliberately decided by fighting
over **pre-placed contested sites**, not settler-carpeting. This matrix records what
we took, what's worth taking, and what we won't.

### Adopted (already in `MapGenerator.cs` / `WorldOverlay`)

| Civ 5 pattern | NWO form |
| --- | --- |
| Latitude + rainfall → terrain | temperature × moisture climate matrix in `Classify` |
| Three resource tiers (bonus / strategic / luxury) | `ResourceType` + `ResourceYields` + tech-reveal |
| Edge-based rivers from highlands to water | `MapData.Rivers` edge-set + downhill trace (+ lake carving) |
| Foothills around uplift | mountain `relief` skirt + scattered hilliness field |
| (improved on Civ) directional mountain chains | domain-warped **ridged** Simplex (Civ's plain fractal is blobbier) |
| Start normalization (fair, viable spawns) | `GameFactory` fertility floor (work-radius yield sum) + farthest-point/impact-ripple spawn spread; human nudged to the most fertile tile near map centre (Phase 10.4) |
| Contested objective sites | `MapData.KeySites` placed away from spawns by farthest-point sampling; control = nearest city within radius (`KeySiteService`, Phase 10.5) |

### Candidate — cheap wins (do under the `tune-map-generation` skill, no phase)

| Civ 5 pattern | Why | Effort |
| --- | --- | --- |
| **Percentile height thresholds** (sort land heights; sea/hills/mountains at fixed percentiles) | stable land/hill/mountain ratio every seed — fixes our seed-to-seed wobble from fixed cutoffs | ~½ day, isolated to `MapGenerator` |
| **Impact-and-ripple resource placement** (stamp a suppression falloff around each placed resource) | resources spaced out instead of randomly clumping | ~½–1 day, `ScatterResources` only |

### Candidate — scheduled features (own phase / existing phase)

| Civ 5 pattern | Where it lands | Effort / ripple |
| --- | --- | --- |
| **Map scripts** (Continents / Pangaea / Archipelago / Highlands) selectable at setup | ✅ **Phase 11 complete** — `MapScript` enum + `MapScriptParams` record; percentile OceanLevel trick; World + Size dropdowns on FactionSetup | shipped |
| ~~Start normalization + region balancing~~ — **shipped in Phase 10.4** (see Adopted table): fertility-floor + farthest-point spawn spread in `GameFactory`, without Civ's region machinery | ✅ done | — |
| **Terrain vs. features split** (forest/jungle/marsh/oasis/floodplain as *features* on a base terrain, like Civ) | post-MVP phase only if we want Civ-depth terrain — **partly started**: Hills already shipped as a `Feature` (`MapData.Features`), so the scaffolding exists | ~1 week+ for the rest; architectural — ripples through `MapData`, save format, yields, workforce, AI, art, tooltips |

### Deliberately skipped

* The `Fractal` / midpoint-displacement noise class — Simplex + ridged warp is equal-or-better here; only the **percentile-threshold trick** is worth lifting, not the noise function.
* `AssignStartingPlots.lua` wholesale — its region division and 8-player luxury distribution target Civ's scale/multiplayer; NWO takes only the normalization *pattern*.
* Everything in the "Skip for NWO at this scale" list above (tectonics, multi-pass erosion, terrain age).
