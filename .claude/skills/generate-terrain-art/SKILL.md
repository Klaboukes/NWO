---
name: generate-terrain-art
description: Generate or re-bake NWO's procedural pixel-art terrain tiles (the 10 top-face hex textures in assets/art/tiles/). Use when tweaking how terrain looks — palette/contrast, surface texture, or a terrain's motif — or after changing a terrain's base colour. Bakes deterministic PNGs from TerrainArtGenerator via a headless Godot tool.
allowed-tools: Read, Edit, PowerShell, Glob
---

# generate-terrain-art

NWO's terrain tiles are **procedurally generated pixel art**, not hand-drawn or
AI-image files. One generator builds all ten terrains from a shared palette/dither
recipe, so they read as a cohesive family, stay crisp on an exact pixel grid, and
re-bake in seconds. See Phase 7 V7.2 in [docs/ROADMAP.md](../../../docs/ROADMAP.md).

## Where the pieces live

- **`scripts/art/TerrainArtGenerator.cs`** — the engine. `Generate(TerrainType)` →
  a 128×128 detailed-pixel-art `Image`: a Bayer-dithered value-noise ground from a
  6-tone naturalistic ramp, outlined hand-placed features (trees, rocks, cacti,
  faceted snow-capped peaks), scattered decorative props (flowers, foam, drifts), and
  a hex-edge ambient-occlusion rim. Deterministic — same terrain always bakes
  byte-identical art.
- **`scripts/map/TerrainTextureRegistry.cs`** — at runtime, loads
  `assets/art/tiles/<terrain>.png` when present, else calls the generator. So the
  baked PNGs and the live fallback are identical.
- **`scenes/tools/BakeTerrainTiles.tscn` + `scripts/tools/BakeTerrainTiles.cs`** —
  headless one-shot that writes all ten PNGs and quits.
- **`assets/art/tiles/<terrain>.png`** — the committed tiles (lowercase
  `TerrainType` name, e.g. `grassland.png`).

## Bake / re-bake the tiles

```powershell
& .claude/skills/generate-terrain-art/bake.ps1
```

Builds (the tool needs the current generator), imports, runs the bake scene, and
re-imports the PNGs. Then **inspect** the result — open the PNGs, or F5 the game to
judge them at the real ~45° oblique camera angle (headless can't verify the look).

## Tweaking the look

Edit `TerrainArtGenerator.cs`, then re-bake:

- **A terrain's overall colour** → `HexProjection.TerrainColor` (the ramp is derived
  from it; art and gameplay tinting stay in sync). Not in the generator itself.
- **Contrast / tone count** → `Ramp(...)` (the shadow→highlight HSV deltas).
- **Surface blotchiness / grain** → `NoiseCellLarge` / `NoiseCellSmall` in
  `PaintGround` (bigger cell = broader blotches).
- **A terrain's character** → its `Paint<Terrain>` method (e.g. tree count/size in
  `PaintForest`, dune amplitude in `PaintDesert`, peak shape in `PaintMountain`).
- **Tile resolution** → `TileSize` (keep it small — this is pixel art).

To override one terrain with hand-drawn or AI-generated art instead, just drop a real
`assets/art/tiles/<terrain>.png` over the baked one — the registry prefers it with no
code change (that's the `add-art-asset` drop-in path).

## Invariants (don't break these)

- Output stays **deterministic** (seed only from `TerrainType`) so committed PNGs are
  stable in git.
- Tiles are authored/judged at the **oblique telephoto angle**, not top-down (see
  `add-art-asset`).
- Nearest-neighbour filtering downstream keeps pixels crisp — don't add blur here.

## Verify

Run **`run-checks`** after baking (build + tests + headless scene check confirm the
PNGs import and load). The *look* still needs a human F5 run — flag it, don't claim
it's verified headless. When the art is approved, tick the sub-phase via
**`finish-phase`**.

## Maintenance

If the generator's API, the bake scene path, the tile folder/naming, or the Godot
binary location changes, update this file and `bake.ps1` to match. Keep this skill and
`add-art-asset` cross-consistent (that one covers the drop-in PNG override path).
