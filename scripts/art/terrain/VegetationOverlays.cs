using System;
using System.Collections.Generic;
using Godot;
using NWO.Art.Painterly;

namespace NWO.Art.Terrain;

// Vegetation feature overlays, painted over the finished base terrain so the
// ground shows between the trees (Phase 14 composites). Each takes the
// feature's colour from HexProjection.FeatureColor via the facade.
public static class VegetationOverlays
{
    public static void Forest(TerrainPaintContext ctx, Color leaf)
    {
        PaintCanopies(ctx, leaf,
            count: ctx.Rng.Range(8, 12), minR: 11f, maxR: 16f, hueJitter: 0.025f);
    }

    public static void Jungle(TerrainPaintContext ctx, Color leaf)
    {
        // Dark underbrush first so gaps between canopies read as thicket.
        var brushRamp = ColorRamp.Painterly(ColorRamp.ShiftHsv(leaf, 0f, +0.10f, -0.30f));
        int tufts = ctx.Rng.Range(6, 10);
        for (int i = 0; i < tufts; i++)
            if (ctx.TryPlace(12f, out var pos))
                TerrainProps.BladeTuft(ctx, pos, ctx.Rng.Range(6, 10), 6f, brushRamp);

        PaintCanopies(ctx, leaf,
            count: ctx.Rng.Range(12, 16), minR: 12f, maxR: 18f, hueJitter: 0.035f);
    }

    public static void Marsh(TerrainPaintContext ctx, Color marsh)
    {
        // Murky standing pools: explicit blue-grey water (not ramp-derived) so
        // they read as water, with darker depth toward each pool's middle.
        var waterRamp = ColorRamp.Painterly(new Color(0.24f, 0.38f, 0.44f));
        var bank      = ColorRamp.ShiftHsv(marsh, 0f, +0.08f, -0.35f);

        int pools = ctx.Rng.Range(4, 7);
        for (int i = 0; i < pools; i++)
        {
            if (!ctx.TryPlace(26f, out var c)) continue;
            float r = ctx.Rng.Range(11f, 20f);
            var c2 = c + ctx.Rng.InUnitDisc() * (r * 0.7f);
            float r2 = r * ctx.Rng.Range(0.5f, 0.8f);
            Func<Vector2, float> pool = p => Sdf.SmoothUnion(
                Sdf.Ellipse(p, c, new Vector2(r, r * 0.7f)),
                Sdf.Ellipse(p, c2, new Vector2(r2, r2 * 0.7f)), r * 0.5f);

            var bounds = TerrainProps.CentredBounds(c, r * 2.2f);
            ctx.Painter.StrokeSdf(pool, bounds, bank, 3f);
            ctx.Painter.FillSdf(pool, bounds,
                (p, d) => waterRamp.Sample(0.62f + d / r * 0.5f)); // darker mid-pool
            // Sun glint near the upper-left of the pool.
            var g = c + new Vector2(-r * 0.3f, -r * 0.25f);
            ctx.Painter.FillSdf(p => Sdf.Capsule(p, g, g + new Vector2(4f, 0f), 0.9f),
                                TerrainProps.CentredBounds(g, 7f),
                                new Color(0.95f, 0.97f, 0.97f), 0.8f);

            // Reeds along the bank.
            var reedRamp = ColorRamp.Painterly(marsh);
            int reeds = ctx.Rng.Range(2, 4);
            for (int t = 0; t < reeds; t++)
            {
                float a = ctx.Rng.Float() * Mathf.Tau;
                var rp = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.7f) * (r + 4f);
                if (HexTile.InFootprint(rp.X, rp.Y, ctx.Size, 8f))
                    TerrainProps.BladeTuft(ctx, rp, ctx.Rng.Range(4, 7), 8f, reedRamp, spread: 5f);
            }
        }
    }

    public static void Oasis(TerrainPaintContext ctx, Color lush)
    {
        int S = ctx.Size;
        var centre = new Vector2(S / 2f + ctx.Rng.Range(-16f, 16f),
                                 S / 2f + ctx.Rng.Range(-16f, 16f));
        float r = ctx.Rng.Range(26f, 34f);
        var waterRamp = ColorRamp.Painterly(new Color(0.22f, 0.46f, 0.62f));
        var bounds = TerrainProps.CentredBounds(centre, r * 2f);

        // Lush bank ring, then the spring-fed pond deepening toward its middle.
        Func<Vector2, float> pond = p => Sdf.Ellipse(p, centre, new Vector2(r, r * 0.8f));
        ctx.Painter.FillShaded(p => Sdf.Ellipse(p, centre, new Vector2(r + 7f, (r + 7f) * 0.8f)),
                               bounds, ColorRamp.Painterly(lush), 6f);
        ctx.Painter.FillSdf(pond, bounds, (p, d) => waterRamp.Sample(0.68f + d / r * 0.55f));

        // Sun glint.
        var g = centre + new Vector2(-r * 0.3f, -r * 0.25f);
        ctx.Painter.FillSdf(p => Sdf.Capsule(p, g, g + new Vector2(6f, -1f), 1.1f),
                            TerrainProps.CentredBounds(g, 9f), Colors.White, 0.8f);

        // Palms round the waterline.
        int palms = ctx.Rng.Range(3, 5);
        for (int i = 0; i < palms; i++)
        {
            float a  = ctx.Rng.Float() * Mathf.Tau;
            var baseP = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.8f) * (r + 10f);
            if (HexTile.InFootprint(baseP.X, baseP.Y, ctx.Size, 22f))
                TerrainProps.Palm(ctx, baseP, lush);
        }

        var reedRamp = ColorRamp.Painterly(lush);
        int tufts = ctx.Rng.Range(3, 6);
        for (int i = 0; i < tufts; i++)
        {
            float a  = ctx.Rng.Float() * Mathf.Tau;
            var rp = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.8f) * (r + 5f);
            if (HexTile.InFootprint(rp.X, rp.Y, ctx.Size, 8f))
                TerrainProps.BladeTuft(ctx, rp, ctx.Rng.Range(3, 6), 6f, reedRamp, spread: 5f);
        }
    }

    public static void Ice(TerrainPaintContext ctx, Color ice)
    {
        // Drifting pack-ice floes: flat shaded slabs with rounded edges; the
        // open water shows in the leads between them.
        var floeRamp = ColorRamp.Painterly(ice, contrast: 0.8f);
        var seam     = new Color(0.45f, 0.62f, 0.78f);

        int floes = ctx.Rng.Range(8, 13);
        for (int i = 0; i < floes; i++)
        {
            if (!ctx.TryPlace(18f, out var c)) continue;
            float r = ctx.Rng.Range(12f, 26f);
            var pts = new Vector2[7];
            for (int v = 0; v < 7; v++)
            {
                float a   = v / 7f * Mathf.Tau + ctx.Rng.Range(-0.2f, 0.2f);
                float rad = r * ctx.Rng.Range(0.7f, 1.1f);
                pts[v] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.85f) * rad;
            }
            Func<Vector2, float> floe = p => Sdf.Polygon(p, pts);
            var bounds = TerrainProps.CentredBounds(c, r * 1.4f);
            ctx.Painter.StrokeSdf(floe, bounds, seam, 2f);
            // Low inflate vs radius = flat slab with rounded shoulders.
            ctx.Painter.FillShaded(floe, bounds, floeRamp, Mathf.Min(4.5f, r * 0.3f),
                                   specular: 0.30f);
        }

        int sparkles = ctx.Rng.Range(18, 30);
        for (int i = 0; i < sparkles; i++)
            if (ctx.TryPlace(8f, out var pos))
                ctx.Painter.FillSdf(p => Sdf.Circle(p, pos, ctx.Rng.Range(0.6f, 1.1f)),
                                    TerrainProps.CentredBounds(pos, 3f), Colors.White, 0.7f);
    }

    // Shared canopy painter for Forest/Jungle: place, sort back-to-front by Y so
    // nearer crowns overlap farther ones, then draw shaded trees with slight
    // per-tree hue variation so the wood reads as many individuals.
    private static void PaintCanopies(TerrainPaintContext ctx, Color leaf,
                                      int count, float minR, float maxR, float hueJitter)
    {
        var trees = new List<(Vector2 pos, float r, Color col)>();
        for (int i = 0; i < count; i++)
        {
            if (!ctx.TryPlace(20f, out var pos)) continue;
            float r = ctx.Rng.Range(minR, maxR);
            var col = ColorRamp.ShiftHsv(leaf, ctx.Rng.Range(-hueJitter, hueJitter),
                                         ctx.Rng.Range(-0.05f, 0.08f),
                                         ctx.Rng.Range(-0.10f, 0.10f));
            trees.Add((pos, r, col));
        }
        trees.Sort((a, b) => a.pos.Y.CompareTo(b.pos.Y));
        foreach (var (pos, r, col) in trees)
            TerrainProps.Tree(ctx, pos, r, col);
    }
}
