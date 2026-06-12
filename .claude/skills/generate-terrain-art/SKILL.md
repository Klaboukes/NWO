---
name: generate-terrain-art
description: Generate or re-bake NWO's procedural painterly art — terrain tiles, unit/city sprites, resource + HUD icons, and the owner banner (everything under assets/art/). Use when tweaking how any terrain, feature, unit, city, or icon looks, or after changing a base colour. Bakes deterministic PNGs from the v2 generators via a headless Godot tool.
allowed-tools: Read, Edit, PowerShell, Glob
---

# generate-terrain-art

NWO's art is **procedurally generated painterly art** (v2, Phase 7 V7.5), not
hand-drawn or AI-image files. A shared painting library (`scripts/art/painterly/`)
gives every generator the same vocabulary — anti-aliased SDF shapes, height-field
relief lighting from one upper-left sun, hue-shifted colour ramps, soft shadows —
so all ~61 assets read as one family and re-bake in under a minute. No dithering,
no pixel grid: textures render with **Linear** filtering.

## Where the pieces live

- **`scripts/art/painterly/`** — the shared library: `Sdf` (shapes), `Painter`
  (AA fills; `FillShaded` = the volumetric pillow-shading workhorse),
  `HeightField` + `Lighting` (relief + the one sun), `ColorRamp`/`MaterialRamps`
  (hue-shifted ramps, shared materials), `SpriteFx` (rim light, dark rim, contact
  shadow, AlphaBleed), `HexTile`, `NoiseField`, `Rng`. Managed-types-only except
  `Canvas.ToImage` — unit-tested headless in `NWO.Tests/PainterlyTests.cs`.
- **`scripts/art/TerrainArtGenerator.cs`** — facade. `Generate(TerrainType, veg)`
  → 256×256 opaque tile. Internals in `scripts/art/terrain/`: `GroundPainter`,
  `WaterPainter`, `DunesPainter`, `GrassPainter`, `RockPainter`,
  `VegetationOverlays`, `TerrainProps` (shared rocks/trees/palms/tufts).
- **`scripts/art/UnitArtGenerator.cs`** — facade. `Generate(unitId)` → 256×256
  transparent sprite. Internals in `scripts/art/units/`: `HumanoidPainter` (the
  parametric figure), `WeaponPainter`, `ShipPainter`, `VehiclePainter`,
  `AnimalPainter`, `UnitCatalog` (the 19 per-id recipes + disc fallback).
- **`scripts/art/CityArtGenerator.cs`** + `scripts/art/cities/CityPainter.cs` —
  city/capital settlement sprites (256px).
- **`scripts/art/ResourceIconGenerator.cs` / `HudIconGenerator.cs`** — 64px glossy
  icons, shared finish in `scripts/art/icons/IconFx.cs`.
- **`scripts/art/BannerArtGenerator.cs`** — the owner-colour pennant (cloth stays
  white/grey; `Sprite3D.Modulate` dyes it).
- **Registries** (`scripts/map/*TextureRegistry.cs`, `scripts/ui/HudIconRegistry.cs`)
  — load the committed PNG when present, else call the generator. Baked files and
  live fallback are identical.
- **`scenes/tools/BakeAllArt.tscn` + `scripts/tools/BakeAllArt.cs`** — headless
  one-shot that writes every asset (tiles, units, cities, resources, ui) and quits.

## Bake / re-bake

```powershell
& .claude/skills/generate-terrain-art/bake.ps1
```

Builds, imports, runs the bake scene, re-imports. Then **inspect** the result —
Read the PNGs, or F5 the game to judge tiles at the real ~45° oblique camera angle
(headless can't verify the look).

## Tweaking the look

Edit the relevant painter, then re-bake:

- **A terrain's overall colour** → `HexProjection.TerrainColor`; **a feature
  overlay's colour** → `HexProjection.FeatureColor` (ramps derive from these; art
  and minimap tinting stay in sync).
- **Ground character** → the `GroundStyle`/`GrassStyle` params in that terrain's
  painter (noise scale, relief strength, albedo variation).
- **A terrain's character** → its painter in `scripts/art/terrain/` (dune height
  in `DunesPainter`, peak shape in `RockPainter.Peak`, wave amp in `WaterPainter`).
- **Props (rocks/trees/palms)** → `TerrainProps` (shared by all terrains).
- **A unit's kit/pose** → its recipe in `UnitCatalog`; **figure anatomy** →
  `HumanoidPainter`; **materials** → `MaterialRamps`.
- **Global light direction / shading** → `Lighting.SunDir` / `Lighting.Shade`
  (changes EVERYTHING — re-bake all and re-judge).
- **Sizes** → `TerrainArtGenerator.TileSize` (256), `UnitPaintContext.Size` (256),
  `*IconGenerator.IconSize` (64). `WorldRenderer` derives `PixelSize` from the
  texture, so sizes can change freely.

To override any asset with hand-made art, drop a real PNG at its registry path —
the registry prefers it with no code change (the `add-art-asset` drop-in path).

## Invariants (don't break these)

- Output stays **deterministic** (seeds only from the asset identity: terrain enum,
  unit id string, …) so committed PNGs are stable in git. Bake twice + `git status`
  to prove it.
- One **sun from the upper-left** (`Lighting.SunDir`) across all assets.
- Tiles are authored/judged at the **oblique telephoto angle**, not top-down.
- **Linear** filtering downstream; transparent sprites must go through
  `Canvas.ToImage()` (it runs `AlphaBleed` — the no-dark-halo guarantee).
- The project-wide canvas filter in `project.godot` stays **Nearest** (pixel font);
  icon nodes set Linear per-node.

## Verify

Run **`run-checks`** after baking (build + tests + headless scene check confirm the
PNGs import and load). The *look* still needs a human F5 run — flag it, don't claim
it's verified headless. When the art is approved, tick the sub-phase via
**`finish-phase`**.

## Maintenance

If a generator's API, the bake scene path, an asset folder/naming, or the Godot
binary location changes, update this file and `bake.ps1` to match. Keep this skill
and `add-art-asset` cross-consistent (that one covers the drop-in PNG override path).
