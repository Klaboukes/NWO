---
name: add-art-asset
description: Add or wire visual assets for NWO's Phase 7 visual overhaul — terrain hex tiles, unit/city sprites, or other art under assets/art/. Use when dropping in PNG art or extending the texture registry. The pipeline is placeholder-first: real art overrides synthesized placeholders with no code change.
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
- Geometry + placeholder material live in `scripts/map/TerrainMeshFactory.cs`
  (vertex-coloured prism). To use a real top-face texture, give the top surface UVs
  and an `AlbedoTexture` from `res://assets/art/tiles/<terrain>.png` (lowercase
  `TerrainType` name, e.g. `grassland.png`). Keep the prism geometry contract (top
  hex at `TopHeight`, cliffs to `Y = 0`) so picking and anchoring stay correct.
- Add a new `TerrainType` enum value only if introducing genuinely new terrain.

## Unit & city sprites (V7.3)

- Units/cities are `Sprite3D` billboards anchored on tile tops, owner-tinted via
  `Sprite3D.Modulate` (see `WorldRenderer.Ensure`). Placeholder tokens are
  synthesized (`MakeDiscToken` / `MakeSquareToken`).
- To use real art, resolve a per-type PNG (e.g. `assets/art/units/<id>.png`) in the
  same "real PNG else placeholder" pattern and assign it to the sprite's `Texture`.
  Keep the selection / fortify / HP / letter overlays (drawn in `WorldOverlay`) intact.

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
