---
name: add-art-asset
description: Add or wire visual assets under assets/art/ — terrain hex tiles, unit/city sprites (Phase 7), and resource icons (Phase 9). Use when dropping in PNG art or extending a texture/icon registry. The pipeline is placeholder-first: real art overrides synthesized placeholders with no code change.
allowed-tools: Read, Edit, PowerShell, Glob
---

# add-art-asset

NWO Phase 7 renders a **true 3D, fixed-tilt** world (hex prisms + billboard sprites
under a `Camera3D`). The pipeline (mirroring the `AudioManager` placeholder policy)
is: code synthesizes a placeholder so the game always renders, and real art drops
into `assets/art/` to override it with **minimal/no code change**. See Phase 7 in
[docs/ROADMAP.md](../../../docs/ROADMAP.md).

## Terrain tiles (V7.2)

- Each terrain is a hex **prism**: a top-face hexagon raised to
  `HexProjection.TopHeight(terrain)` with six cliff side walls dropping to the
  ground plane. Cliffs are **real geometry now** — art only needs the **top face**
  (no baked skirt).
- The camera is a Civ5-style telephoto lens at a fixed **~45° oblique tilt** (narrow
  FOV, dollied far back — constants in `CameraController`). Top faces are therefore
  always seen at that angle, foreshortened along the view axis, *never* straight
  down. Author and judge tile art at the oblique angle; designs that only read
  top-down will look wrong in-game.
- Top faces are UV-mapped and textured per `(terrain, vegetation-feature)` combo in
  `scripts/map/TerrainMeshFactory.cs` (two-surface prism: textured top + vertex-
  coloured cliffs), resolved through `TerrainTextureRegistry`. The committed tiles
  live at `res://assets/art/tiles/<stem>.png` — lowercase `TerrainType` name, plus
  `_<feature>` for a Phase 14 composite (`grassland.png`, `grassland_forest.png`,
  `desert_oasis.png`; Hills is geometry, never a texture) — and are **procedurally
  generated**: to change how terrain or a feature looks, use the
  **`generate-terrain-art`** skill (edits `TerrainArtGenerator` + re-bakes).
- To override one combo with hand-drawn or AI-generated art, just drop a real
  `assets/art/tiles/<stem>.png` over the baked one — the registry prefers a present
  PNG with no code change. Keep the prism geometry contract (top hex at `TopHeight`,
  cliffs to `Y = 0`) so picking and anchoring stay correct.
- Add a new `TerrainType` or `Feature` enum value only if introducing genuinely new
  terrain (legality lives in `FeatureRules`; see `tune-map-generation`).

## Unit & city sprites (V7.3)

Units and cities are `Sprite3D` billboards (128 px, RGBA8, white fill + dark outline
so `Sprite3D.Modulate = owner.Color` tints the whole sprite). Textures are resolved
by `UnitTextureRegistry` / `CityTextureRegistry` in the same placeholder-first pattern:

- **Unit sprites**: `res://assets/art/units/<unitId>.png` (e.g. `warrior.png`).
  Resolved by `UnitTextureRegistry.For(unitId)`. Falls back to `UnitArtGenerator.Generate(unitId)`
  which synthesises a distinctive 128 px silhouette per unit type.
- **City sprites**: `res://assets/art/cities/city.png` and `assets/art/cities/capital.png`.
  Resolved by `CityTextureRegistry.For(isCapital)`. Falls back to `CityArtGenerator.Generate(isCapital)`.

Adding new hand-drawn unit art:

1. Drop `assets/art/units/<unitId>.png` (128 px, RGBA8, white/light fill + dark outline).
2. The registry picks it up automatically — no code change.
3. Run **`run-checks`**.

Adding a new unit type that needs its own synthesised silhouette:

1. Add a `case "<newId>":` branch in `UnitArtGenerator.Generate()` that draws the shape.
2. Optionally bake it to a PNG and commit under `assets/art/units/<newId>.png`.
3. Run **`run-checks`**. The `add-content` skill reminds you of this step.

Keep the selection / fortify / HP overlays (in `WorldOverlay`) intact — they draw over
the sprite in screen space and are unaffected by texture changes.

## Resource icons (Phase 9)

Map resources (bonus / strategic / luxury — see the `add-content` and resource docs)
draw as small 2D icons in `WorldOverlay.DrawResources`, resolved by
`ResourceIconRegistry` in the same placeholder-first pattern as units/cities (no
baking — synthesized at runtime, real PNG overrides):

- **Resource icons**: `res://assets/art/resources/<resource>.png` (lowercase
  `ResourceType` name, e.g. `wheat.png`, `goldore.png`, `horses.png`).
  Resolved by `ResourceIconRegistry.For(resourceType)`. Falls back to
  `ResourceIconGenerator.Generate(resourceType)`, a 32 px RGBA icon whose **shape
  encodes the tier** (bonus = round token, strategic = ingot bar, luxury = faceted
  gem) and **colour is per-resource**.
- These icons are drawn in 2D screen space (not billboards) at the tile's resource
  anchor, scaled with zoom; `WorldOverlay` is set to **Nearest** texture filtering so
  they stay crisp. They are sized for legibility (~half a hex) and unaffected by
  owner tint.

Adding new hand-drawn resource art:

1. Drop `assets/art/resources/<resource>.png` (32 px or a clean multiple, RGBA8,
   transparent background, dark outline so it reads on any terrain).
2. The registry picks it up automatically — no code change.
3. Run **`run-checks`**.

Adding a new resource that needs its own synthesised icon:

1. Add the enum value (see `add-content` / the resource pipeline) and give it a colour
   in `ResourceIconGenerator.BaseColor()`; the tier shape is automatic via
   `ResourceYields.Tier`.
2. Optionally drop a real PNG to override the placeholder.
3. Run **`run-checks`**.

## Rendering invariants (don't break these)

- **Nearest-neighbour** filtering everywhere (pixel art must stay crisp — set it on
  materials/sprites/overlays, no bilinear blur).
- The projection must stay **invertible**: picking (ray → ground plane) and movement
  animation rely on `HexProjection.AxialToWorld` / `WorldToAxial` round-tripping.
  `HexProjection` holds the math; `ProjectionTests` pins the round-trip and the
  ground-pick contract (picking ignores prism height). Run them after any
  projection/anchor change.
- The 2D overlay (`WorldOverlay`) positions everything via `Camera3D.UnprojectPosition`
  — keep tile/sprite anchors (`TileTop`, `TopHeight`) in sync with the 3D geometry.

## Procedure

1. Place the PNG under `assets/art/...` with the exact name the resolver expects, or
   extend the resolve pattern in `TerrainMeshFactory` / `WorldRenderer`.
2. Respect the geometry/anchoring contract and rendering invariants above.
3. Run the **`run-checks`** skill (the scene check + `ProjectionTests` catch
   mis-anchored or non-round-tripping art). Note: the *look* still needs a human F5
   run — flag that, don't claim the visuals are verified headless.
4. When a sub-phase's art is complete, tick it via the **finish-phase** skill.

## Maintenance

If the asset path convention, prism/anchoring contract, or resolve pattern changes,
update this file and keep it aligned with `TerrainMeshFactory`, `WorldRenderer`,
`WorldOverlay`, and `HexProjection`.
