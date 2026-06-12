using Godot;
using NWO.Art.Painterly;
using NWO.Art.Terrain;
using NWO.Map;

namespace NWO.Art;

// Procedural painterly generator for NWO terrain top-face tiles (Phase 7 V7.5).
//
// STYLE  "painterly v2" — 256px full-colour tiles with a 3D-shaded read: every
// terrain builds a height field (dunes, wave swell, a ridged massif, drifts),
// lights it with the shared upper-left sun (Painterly.Lighting), and lays
// anti-aliased SDF props (shaded trees, boulders, palms, blade tufts) on top.
// No dithering, no hard pixel grid: tiles render with Linear filtering.
//
// WHY PROCEDURAL  One shared recipe keeps all ten terrains a cohesive family and
// re-bakes in seconds. A real PNG dropped into assets/art/tiles/ still overrides
// any tile with no code change (TerrainTextureRegistry / add-art-asset skill).
//
// PIPELINE  (per terrain + optional vegetation Feature, Phase 14 composites)
//   1. TerrainPaintContext   — canvas + RNG/seed + the terrain's painterly ramp
//                              (from HexProjection.TerrainColor, so art tracks
//                              gameplay tinting).
//   2. A terrain painter     — Water/Dunes/Grass/Rock painter builds the height
//                              field, relights it, and places props.
//   3. VegetationOverlays    — the feature's overlay (canopies/pools/floes)
//                              painted over the base, from FeatureColor.
//   4. HexTile.EdgeAo        — ambient-occlusion rim inside the hex footprint.
//
// DETERMINISM  Everything is seeded from (TerrainType, Feature) only — a combo
// always bakes byte-identical art, so committed PNGs are stable in git and the
// runtime placeholder matches the baked file.
//
// TWEAKING  (see the generate-terrain-art skill for the full guide)
//   • A terrain's colour → HexProjection.TerrainColor (drives the ramp).
//   • Ground character   → the GroundStyle/GrassStyle params in its painter.
//   • A terrain's look   → its painter in scripts/art/terrain/.
//   • Props' look        → TerrainProps (shared across all terrains).
//   • Tile size / rim    → TileSize here / EdgeAo band below.
//   After any change, re-bake (generate-terrain-art skill) and run run-checks.
public static class TerrainArtGenerator
{
    public const int TileSize = 256; // px — painterly v2 (2x the v1 pixel-art scale)

    private const float EdgeBand   = 40f;   // px-wide ambient-occlusion rim at the hex edge
    private const float EdgeDarken = 0.30f; // max brightness drop at the very edge

    // Build the finished tile for a (terrain, vegetation-feature) combination —
    // the base terrain's lit ground + props, then the feature's overlay painted on
    // top (Grassland + Forest = trees over the meadow). Pure: same input → same Image.
    public static Image Generate(TerrainType terrain, Feature veg = Feature.None)
    {
        veg &= FeatureRules.VegMask; // Hills is geometry (a taller prism), not art

        var ctx = new TerrainPaintContext(
            TileSize,
            rngSeed: 0x9E3779B97F4A7C15UL * (ulong)((int)terrain + 1)
                   + 0xBF58476D1CE4E5B9UL * (ulong)(int)veg,
            noiseSeed: ((int)terrain * 31 + (int)veg) * 911,
            baseColor: HexProjection.TerrainColor(terrain));

        switch (terrain)
        {
            case TerrainType.Ocean:     WaterPainter.Paint(ctx, WaterPainter.Kind.Ocean); break;
            case TerrainType.Coast:     WaterPainter.Paint(ctx, WaterPainter.Kind.Coast); break;
            case TerrainType.Lake:      WaterPainter.Paint(ctx, WaterPainter.Kind.Lake);  break;
            case TerrainType.Desert:    DunesPainter.Paint(ctx);                          break;
            case TerrainType.Plains:    GrassPainter.Paint(ctx, GrassPainter.Plains);    break;
            case TerrainType.Grassland: GrassPainter.Paint(ctx, GrassPainter.Grassland); break;
            case TerrainType.Savanna:   GrassPainter.Paint(ctx, GrassPainter.Savanna);   break;
            case TerrainType.Tundra:    RockPainter.PaintTundra(ctx);                    break;
            case TerrainType.Snow:      RockPainter.PaintSnow(ctx);                      break;
            case TerrainType.Mountain:  RockPainter.PaintMountain(ctx);                  break;
        }

        if (veg != Feature.None)
        {
            var fc = HexProjection.FeatureColor(veg);
            switch (veg)
            {
                case Feature.Forest: VegetationOverlays.Forest(ctx, fc); break;
                case Feature.Jungle: VegetationOverlays.Jungle(ctx, fc); break;
                case Feature.Marsh:  VegetationOverlays.Marsh(ctx, fc);  break;
                case Feature.Oasis:  VegetationOverlays.Oasis(ctx, fc);  break;
                case Feature.Ice:    VegetationOverlays.Ice(ctx, fc);    break;
            }
        }

        HexTile.EdgeAo(ctx.Canvas, EdgeBand, EdgeDarken);
        return ctx.Canvas.ToImage(opaque: true);
    }
}
