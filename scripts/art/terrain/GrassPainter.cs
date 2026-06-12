using Godot;
using NWO.Art.Painterly;

namespace NWO.Art.Terrain;

// The grass family — Grassland, Plains, Savanna — share one recipe parametrized
// by density, prop set, and ground character. Blades come in clustered tufts
// (organic patches, not uniform speckle), bushes are shaded blobs, and
// grassland gets wildflowers.
public static class GrassPainter
{
    public readonly record struct GrassStyle(
        int Tufts, int BladesPerTuft, float BladeLen,
        int Bushes, float BushR, bool Acacia, int Flowers, float GroundVar);

    public static GrassStyle Grassland => new(14, 12, 7f, 3, 11f, false, 8, 0.50f);
    public static GrassStyle Plains    => new(8,  9,  6f, 2, 9f,  false, 4, 0.62f);
    public static GrassStyle Savanna   => new(10, 10, 8f, 2, 13f, true,  0, 0.58f);

    private static readonly Color[] Petals =
    {
        Colors.White,
        new(1f, 0.9f, 0.3f),
        new(0.95f, 0.4f, 0.5f),
        new(0.7f, 0.5f, 0.95f),
    };

    public static void Paint(TerrainPaintContext ctx, GrassStyle style)
    {
        GroundPainter.Paint(ctx, new GroundPainter.GroundStyle(
            Scale: 46f, Warp: 0.55f, Relief: 11f, Octaves: 4,
            AoStrength: 14f, AlbedoVar: style.GroundVar));

        for (int t = 0; t < style.Tufts; t++)
            if (ctx.TryPlace(12f, out var centre))
                TerrainProps.BladeTuft(ctx, centre,
                    style.BladesPerTuft + ctx.Rng.Range(-2, 3), style.BladeLen, ctx.Ramp);

        // Bush/acacia crowns lean toward true leaf-green rather than inheriting a
        // dry-yellow ground colour wholesale (savanna acacias are olive, not gold).
        var leaf = ColorRamp.ShiftHsv(ctx.BaseColor, 0f, +0.12f, style.Acacia ? +0.04f : -0.08f)
                            .Lerp(new Color(0.30f, 0.48f, 0.22f), style.Acacia ? 0.55f : 0.35f);
        for (int b = 0; b < style.Bushes; b++)
            if (ctx.TryPlace(24f, out var pos))
                TerrainProps.Bush(ctx, pos, style.BushR * ctx.Rng.Range(0.8f, 1.2f),
                                  leaf, style.Acacia);

        int rocks = ctx.Rng.Range(1, 4);
        for (int r = 0; r < rocks; r++)
            if (ctx.TryPlace(16f, out var pos))
                TerrainProps.Rock(ctx, pos, ctx.Rng.Range(3f, 6f));

        for (int f = 0; f < style.Flowers; f++)
            if (ctx.TryPlace(10f, out var pos))
                TerrainProps.Flower(ctx, pos, Petals[ctx.Rng.Range(0, Petals.Length)]);
    }
}
