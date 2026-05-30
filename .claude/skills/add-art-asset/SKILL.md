---
name: add-art-asset
description: Add or wire visual assets for NWO's Phase 7 visual overhaul — terrain hex tiles, unit/city sprites, or other art under assets/art/. Use when dropping in PNG art or extending the texture registry. The pipeline is placeholder-first: real art overrides synthesized placeholders with no code change.
allowed-tools: Read, Edit, PowerShell, Glob
---

# add-art-asset

NWO Phase 7 replaces flat polygons with a baked **2.5D pixel-art** look. The pipeline
(mirroring the `AudioManager` placeholder policy) is: code synthesizes a placeholder
so the game always renders, and a real PNG dropped into `assets/art/` overrides it
with **no code change**. See Phase 7 in [docs/ROADMAP.md](../../../docs/ROADMAP.md).

## Terrain tiles (V7.2)

- Drop `res://assets/art/tiles/<terrain>.png` (lowercase `TerrainType` name, e.g.
  `grassland.png`, `hills.png`). `TileTextureSet.Resolve` picks it up automatically;
  no registry edit needed for an existing terrain.
- **Anchoring contract** (`scripts/map/TileTextureSet.cs`): each texture is `TileW`
  wide and `TopFaceH + SkirtH` tall; the hex top-face **center** sits at
  `(TileW/2, TopFaceH/2)`. The top face is a foreshortened flat-top hex; a darker
  cliff skirt hangs below. Match these dimensions or the tile won't register on the
  grid. Add a new `TerrainType` enum value only if introducing genuinely new terrain.

## Unit & city sprites (V7.3)

- Registry-driven billboard sprites anchored on tile tops, owner-tinted. Keep the
  selection / fortify / HP overlays intact. Add the sprite registry alongside
  `TileTextureSet` following the same "real PNG else placeholder" resolve pattern.

## Rendering invariants (don't break these)

- **Nearest-neighbour** filtering (pixel art must stay crisp — no bilinear blur).
- **Painter's order** draw (back-to-front) so elevation/overlap reads correctly.
- The projection must stay **invertible**: picking and movement animation rely on
  `AxialToWorld`/`WorldToAxial` round-tripping. `WorldRenderer` holds the projection;
  `ProjectionTests` pins the round-trip. Run them after any projection/anchor change.

## Procedure

1. Place the PNG under `assets/art/...` with the exact name the resolver expects, or
   extend the registry following the existing resolve pattern.
2. Respect the anchoring contract and rendering invariants above.
3. Run the **`run-checks`** skill (the scene check + `ProjectionTests` catch
   mis-anchored or non-round-tripping art). Note: the *look* still needs a human F5
   run — flag that, don't claim the visuals are verified headless.
4. When a sub-phase's art is complete, tick it via the **finish-phase** skill.

## Maintenance

If the asset path convention, anchoring contract, or registry changes, update this
file and keep it aligned with `TileTextureSet` and `WorldRenderer`.
