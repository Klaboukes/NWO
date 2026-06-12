using Godot;
using NWO.Art.Painterly;

namespace NWO.Art.Terrain;

// The cold/rocky terrains. Mountain raises a ridged-fBm massif whose snow line
// falls out of height + slope; Tundra is mossy frost-bitten ground with scrub;
// Snow is drifted white with blue hollow shadows and frost sparkle.
public static class RockPainter
{
    private const float MassifHeight = 26f;

    public static void PaintMountain(TerrainPaintContext ctx)
    {
        // Rocky ground, then painted faceted peaks (the v1 read, gone painterly):
        // a shoulder peak behind, the main massif in front. A smooth lit cone just
        // reads as a bump from above — discrete peaks with a hard lit-west /
        // shadowed-east ridge split is what reads "mountain" through the camera.
        GroundPainter.Paint(ctx, new GroundPainter.GroundStyle(
            Scale: 50f, Warp: 0.5f, Relief: 9f, Octaves: 4,
            AoStrength: 10f, AlbedoVar: 0.45f));

        int S = ctx.Size;

        // Drop the ground into half-light so the sunlit peak faces pop off it.
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
            ctx.Canvas.ScaleRgb(x, y, 0.78f);
        Peak(ctx, apex: new Vector2(S * 0.66f, S * 0.30f), baseY: S * 0.78f,
             halfWidth: S * 0.30f, capDepth: 0.22f, dim: 0.85f);
        Peak(ctx, apex: new Vector2(S * 0.42f, S * 0.16f), baseY: S * 0.82f,
             halfWidth: S * 0.40f, capDepth: 0.30f, dim: 1f);

        int scree = ctx.Rng.Range(3, 6);
        for (int i = 0; i < scree; i++)
            if (ctx.TryPlace(14f, out var pos))
                TerrainProps.Rock(ctx, pos, ctx.Rng.Range(3f, 7f));
    }

    // One painted peak: a slightly skewed triangle, west face lit / east face in
    // shadow with crag noise breaking both, snow cap with a noise-ragged lower
    // edge, soft cast shadow, and a faint dark edge for definition.
    private static void Peak(TerrainPaintContext ctx, Vector2 apex, float baseY,
                             float halfWidth, float capDepth, float dim)
    {
        var snow  = new Color(0.90f, 0.93f, 0.97f);
        var baseL = new Vector2(apex.X - halfWidth * 1.05f, baseY);
        var baseR = new Vector2(apex.X + halfWidth * 0.95f, baseY);
        System.Func<Vector2, float> tri = p => Sdf.Triangle(p, apex, baseR, baseL);
        var bounds = new Rect2(baseL.X - 4f, apex.Y - 4f,
                               baseR.X - baseL.X + 8f, baseY - apex.Y + 8f);

        // A saturated, high-contrast rock ramp distinct from the ground fill, so
        // the peak separates from the terrain behind it instead of reading glassy.
        var rockRamp = ColorRamp.Painterly(
            ColorRamp.ShiftHsv(ctx.BaseColor, +0.02f, +0.14f, -0.04f), contrast: 1.5f);

        ctx.Painter.SoftShadow(tri, bounds, new Vector2(5f, 6f), 9f, 0.34f);
        ctx.Painter.FillSdf(tri, bounds, (p, d) =>
        {
            // Ridge runs from the apex down; the west face catches the sun hard,
            // the east face drops into shadow, crags break both.
            float u = (p.X - apex.X) / (halfWidth * 0.30f);
            float h = Mathf.Clamp((baseY - p.Y) / (baseY - apex.Y), 0f, 1f);
            float lit = Mathf.Lerp(0.94f, 0.12f, Mathf.SmoothStep(-1f, 1f, u));
            // Anisotropic crags: stretched vertically so the faces read as
            // gullied rock walls, not cloudy noise.
            lit += (NoiseField.Fbm(p.X / 9f, p.Y / 30f, ctx.Seed + 71, 3) - 0.5f) * 0.38f;
            lit += h * 0.10f; // summits catch a touch more light
            // Snow cap with a noise-ragged lower edge, shadow-split like the rock.
            float rag = (NoiseField.Fbm(p.X / 15f, 3.7f, ctx.Seed + 83, 2) - 0.5f) * 0.18f;
            float snowT = Mathf.SmoothStep(1f - capDepth + rag, 1f - capDepth * 0.45f + rag, h);
            var col = rockRamp.Sample(Mathf.Clamp(lit, 0f, 1f) * dim).Lerp(
                snow.Lerp(new Color(0.58f, 0.66f, 0.80f), Mathf.SmoothStep(-0.2f, 1f, u) * 0.55f),
                snowT);
            return col;
        });
        // Definition only on the shadow side: a dark east edge, no full outline.
        ctx.Painter.FillSdf(p => Sdf.Subtract(Mathf.Abs(tri(p)) - 1.2f,
                                              -(p.X - apex.X)), bounds,
                            ColorRamp.ShiftHsv(ctx.BaseColor, 0f, +0.06f, -0.55f) with { A = 0.45f });
    }

    public static void PaintTundra(TerrainPaintContext ctx)
    {
        var hf = GroundPainter.Paint(ctx, new GroundPainter.GroundStyle(
            Scale: 52f, Warp: 0.5f, Relief: 9f, Octaves: 4,
            AoStrength: 12f, AlbedoVar: 0.55f));

        // Frost patches on the high ground, dark mossy hollows in the low — the
        // two-tone patchwork that makes tundra read as frost-bitten, not washed.
        int S = ctx.Size;
        var frost = new Color(0.85f, 0.88f, 0.90f);
        var moss  = ColorRamp.ShiftHsv(ctx.BaseColor, +0.06f, +0.18f, -0.38f);
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float frostT = Mathf.SmoothStep(0.98f, 1.28f, hf[x, y]);
            if (frostT > 0f) ctx.Canvas.Blend(x, y, frost, frostT * 0.45f);
            float mossT = Mathf.SmoothStep(0.78f, 0.48f, hf[x, y]);
            if (mossT > 0f) ctx.Canvas.Blend(x, y, moss, mossT * 0.40f);
        }

        // Twiggy scrub: short dark leaning strokes.
        var scrubRamp = ColorRamp.Painterly(ColorRamp.ShiftHsv(ctx.BaseColor, +0.02f, +0.10f, -0.35f));
        int tufts = ctx.Rng.Range(8, 13);
        for (int i = 0; i < tufts; i++)
            if (ctx.TryPlace(12f, out var pos))
                TerrainProps.BladeTuft(ctx, pos, ctx.Rng.Range(4, 8), 5f, scrubRamp, spread: 7f);

        int rocks = ctx.Rng.Range(2, 5);
        for (int i = 0; i < rocks; i++)
            if (ctx.TryPlace(16f, out var pos))
                TerrainProps.Rock(ctx, pos, ctx.Rng.Range(3f, 7f));
    }

    public static void PaintSnow(TerrainPaintContext ctx)
    {
        // Gentle wind drifts; the cool hollow shadows come from Lighting.Shade's
        // hue-shifted shadow side plus drift AO.
        var hf = new HeightField(ctx.Size, ctx.Size);
        hf.AddFbm(ctx.Seed, 64f, 3.2f, 3, warp: 0.7f);
        hf.AddFbm(ctx.Seed + 31, 15f, 0.4f, 2);
        GroundPainter.Relight(ctx, hf,
            (x, y) => ctx.Ramp.Sample(0.55f + (hf[x, y] / 3.6f - 0.5f) * 0.35f),
            relief: 7f, aoRadius: 9, aoStrength: 10f);

        // Frost sparkle: tiny bright glints on the lit faces.
        int sparkles = ctx.Rng.Range(30, 45);
        for (int i = 0; i < sparkles; i++)
            if (ctx.TryPlace(8f, out var pos))
                ctx.Painter.FillSdf(p => Sdf.Circle(p, pos, ctx.Rng.Range(0.6f, 1.2f)),
                                    TerrainProps.CentredBounds(pos, 3f), Colors.White, 0.8f);

        int rocks = ctx.Rng.Range(1, 3);
        for (int i = 0; i < rocks; i++)
            if (ctx.TryPlace(18f, out var pos))
                TerrainProps.Rock(ctx, pos, ctx.Rng.Range(3f, 5f));
    }
}
