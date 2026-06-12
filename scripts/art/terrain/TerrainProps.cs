using System;
using Godot;
using NWO.Art.Painterly;

namespace NWO.Art.Terrain;

// Shared decorative props: every rock, tree, bush, cactus, palm, blade tuft and
// flower on any terrain comes from here, so props look like one family across
// the whole tile set. All shapes are SDF assemblies lit by the shared sun, with
// a soft cast shadow toward the lower-right (opposite the light).
public static class TerrainProps
{
    private static readonly Vector2 ShadowDir = new(2.6f, 3.2f);

    // Irregular shaded boulder.
    public static void Rock(TerrainPaintContext ctx, Vector2 c, float r)
    {
        var pts = new Vector2[6];
        for (int i = 0; i < 6; i++)
        {
            float a   = i / 6f * Mathf.Tau + ctx.Rng.Range(-0.25f, 0.25f);
            float rad = r * ctx.Rng.Range(0.72f, 1.15f);
            pts[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.85f) * rad;
        }
        Func<Vector2, float> sdf = p => Sdf.Polygon(p, pts);
        var bounds = CentredBounds(c, r * 1.3f);
        ctx.Painter.SoftShadow(sdf, bounds, ShadowDir, 4f, 0.32f);
        ctx.Painter.FillShaded(sdf, bounds, MaterialRamps.Stone, r * 0.8f, specular: 0.22f);
    }

    // A round-canopy tree: cast shadow, trunk, then a blob canopy of smooth-
    // unioned lobes so the crown reads lit-left / shaded-right under the sun.
    public static void Tree(TerrainPaintContext ctx, Vector2 c, float r, Color leaf)
    {
        var lobes = new Vector2[4];
        var lobeR = new float[4];
        lobes[0] = c;                                            lobeR[0] = r;
        for (int i = 1; i < 4; i++)
        {
            var dir = ctx.Rng.InUnitDisc() * (r * 0.55f);
            lobes[i] = c + dir - new Vector2(0f, r * 0.15f);
            lobeR[i] = r * ctx.Rng.Range(0.55f, 0.8f);
        }
        Func<Vector2, float> canopy = p =>
        {
            float d = Sdf.Circle(p, lobes[0], lobeR[0]);
            for (int i = 1; i < 4; i++)
                d = Sdf.SmoothUnion(d, Sdf.Circle(p, lobes[i], lobeR[i]), r * 0.45f);
            return d;
        };
        var bounds = CentredBounds(c, r * 2f);
        ctx.Painter.SoftShadow(canopy, bounds, ShadowDir * 1.4f, 6f, 0.38f);

        var trunkA = c + new Vector2(0f, r * 0.5f);
        var trunkB = c + new Vector2(ctx.Rng.Range(-2f, 2f), r + 7f);
        ctx.Painter.FillShaded(p => Sdf.Capsule(p, trunkA, trunkB, 2.4f),
                               CentredBounds(c + new Vector2(0f, r * 0.8f), r),
                               MaterialRamps.Wood, 2.4f);

        ctx.Painter.FillShaded(canopy, bounds, ColorRamp.Painterly(leaf), r * 0.55f,
                               specular: 0.12f);
    }

    // Low shrub: a flatter, smaller blob with no trunk. Acacias squash flatter.
    public static void Bush(TerrainPaintContext ctx, Vector2 c, float r, Color leaf,
                            bool acacia = false)
    {
        float squash = acacia ? 0.45f : 0.75f;
        Func<Vector2, float> sdf = p =>
        {
            float d = Sdf.Ellipse(p, c, new Vector2(r, r * squash));
            d = Sdf.SmoothUnion(d,
                Sdf.Ellipse(p, c + new Vector2(-r * 0.4f, -r * 0.18f),
                            new Vector2(r * 0.6f, r * squash * 0.7f)), r * 0.4f);
            return d;
        };
        var bounds = CentredBounds(c, r * 1.6f);
        ctx.Painter.SoftShadow(sdf, bounds, ShadowDir, 4f, 0.30f);
        ctx.Painter.FillShaded(sdf, bounds, ColorRamp.Painterly(leaf), r * 0.5f,
                               specular: 0.10f);
        if (acacia) // thin visible trunk under the flat crown
            ctx.Painter.FillShaded(p => Sdf.Capsule(p, c + new Vector2(0f, r * squash * 0.6f),
                                                    c + new Vector2(1.5f, r * squash + 8f), 1.6f),
                                   CentredBounds(c + new Vector2(0f, r * 0.8f), r),
                                   MaterialRamps.Wood, 1.6f);
    }

    // Saguaro: vertical body capsule with two raised arms.
    public static void Cactus(TerrainPaintContext ctx, Vector2 c)
    {
        float h = ctx.Rng.Range(16f, 24f);
        var top = c - new Vector2(0f, h);
        var armY = c - new Vector2(0f, h * 0.55f);
        Func<Vector2, float> sdf = p =>
        {
            float d = Sdf.Capsule(p, c, top, 3.2f);
            d = Sdf.Union(d, Sdf.Capsule(p, armY + new Vector2(-2f, 0f),
                                         armY + new Vector2(-8f, -7f), 2.4f));
            d = Sdf.Union(d, Sdf.Capsule(p, armY + new Vector2(2f, -3f),
                                         armY + new Vector2(9f, -10f), 2.4f));
            return d;
        };
        var bounds = new Rect2(c.X - 14f, c.Y - h - 14f, 28f, h + 18f);
        ctx.Painter.SoftShadow(sdf, bounds, ShadowDir, 3f, 0.30f);
        ctx.Painter.FillShaded(sdf, bounds,
                               ColorRamp.Painterly(new Color(0.30f, 0.52f, 0.28f)), 3f);
    }

    // A palm: curved trunk + drooping fronds radiating from the crown.
    public static void Palm(TerrainPaintContext ctx, Vector2 baseP, Color leaf)
    {
        float h    = ctx.Rng.Range(18f, 26f);
        float lean = ctx.Rng.Range(-7f, 7f);
        var crown  = baseP + new Vector2(lean, -h);
        var mid    = baseP + new Vector2(lean * 0.3f, -h * 0.5f);

        ctx.Painter.FillShaded(
            p => Sdf.Union(Sdf.Capsule(p, baseP, mid, 2.2f), Sdf.Capsule(p, mid, crown, 1.8f)),
            CentredBounds(baseP + new Vector2(lean * 0.5f, -h * 0.5f), h),
            MaterialRamps.Wood, 2f);

        var frondRamp = ColorRamp.Painterly(leaf);
        int fronds = ctx.Rng.Range(5, 8);
        for (int i = 0; i < fronds; i++)
        {
            float a    = Mathf.Tau * i / fronds + ctx.Rng.Range(-0.2f, 0.2f);
            float len  = ctx.Rng.Range(10f, 15f);
            var dir    = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.6f);
            var tipDir = dir + new Vector2(0f, 0.8f); // droop
            var elbow  = crown + dir * (len * 0.6f);
            var tip    = crown + dir * len + tipDir * (len * 0.25f);
            ctx.Painter.FillShaded(
                p => Sdf.Union(Sdf.Capsule(p, crown, elbow, 1.8f), Sdf.Capsule(p, elbow, tip, 1.2f)),
                CentredBounds(crown, len * 1.5f), frondRamp, 1.8f);
        }
    }

    // A tuft of leaning grass blades clustered round a point.
    public static void BladeTuft(TerrainPaintContext ctx, Vector2 centre, int blades,
                                 float len, ColorRamp ramp, float spread = 11f)
    {
        for (int i = 0; i < blades; i++)
        {
            var p0   = centre + ctx.Rng.InUnitDisc() * spread;
            float l  = len * ctx.Rng.Range(0.7f, 1.3f);
            var p1   = p0 + new Vector2(ctx.Rng.Range(-0.45f, 0.45f) * l, -l);
            // Mostly shadow-side greens so blades read as texture against the lit
            // ground; the occasional sunlit blade keeps the patch lively.
            var col  = ramp.Sample(ctx.Rng.Chance(0.3f)
                ? ctx.Rng.Range(0.70f, 0.88f)
                : ctx.Rng.Range(0.18f, 0.42f));
            var lo   = new Vector2(Mathf.Min(p0.X, p1.X), Mathf.Min(p0.Y, p1.Y));
            var size = new Vector2(Mathf.Abs(p1.X - p0.X), Mathf.Abs(p1.Y - p0.Y));
            ctx.Painter.FillSdf(p => Sdf.Capsule(p, p0, p1, 1.1f),
                                new Rect2(lo, size).Grow(2f), col, 0.8f);
        }
    }

    // A small bright flower: petal disc + warm centre dot.
    public static void Flower(TerrainPaintContext ctx, Vector2 c, Color petal)
    {
        var bounds = CentredBounds(c, 4f);
        ctx.Painter.FillSdf(p => Sdf.Circle(p, c, 2.0f), bounds, petal, 0.8f);
        ctx.Painter.FillSdf(p => Sdf.Circle(p, c, 0.9f), bounds,
                            new Color(0.98f, 0.85f, 0.35f), 0.6f);
    }

    public static Rect2 CentredBounds(Vector2 c, float halfExtent)
        => new(c - new Vector2(halfExtent, halfExtent), new Vector2(halfExtent * 2f, halfExtent * 2f));
}
