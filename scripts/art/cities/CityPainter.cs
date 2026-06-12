using System;
using Godot;
using NWO.Art.Painterly;
using NWO.Art.Units;

namespace NWO.Art.Cities;

// The walled-settlement billboard, painted back-to-front for aerial depth:
// a muted back row of houses, the central keep, then the curtain wall with
// gate and crenellations, and corner towers in front. The capital grows a
// taller keep with a gold roof and pennant. Same post-pass chain as units
// (UnitPaintContext.Finish) so cities sit in the same art family.
public static class CityPainter
{
    private const float GroundY = 206f;

    private static readonly ColorRamp Clay = ColorRamp.Painterly(new Color(0.62f, 0.28f, 0.18f));

    public static void Draw(UnitPaintContext ctx, bool isCapital)
    {
        // Back row: small houses, desaturated + darkened for distance.
        var mutedRoof = ColorRamp.Painterly(new Color(0.45f, 0.26f, 0.20f).Lerp(new Color(0.45f, 0.45f, 0.50f), 0.35f));
        var mutedWall = ColorRamp.Painterly(new Color(0.55f, 0.52f, 0.46f).Lerp(new Color(0.45f, 0.45f, 0.50f), 0.35f));
        House(ctx, new Vector2(92f, 142f), 13f, 12f, mutedWall, mutedRoof);
        House(ctx, new Vector2(126f, 138f), 15f, 13f, mutedWall, mutedRoof);
        House(ctx, new Vector2(166f, 142f), 12f, 11f, mutedWall, mutedRoof);

        Keep(ctx, isCapital);
        Wall(ctx);
        Tower(ctx, 70f, isCapital ? 96f : 88f);
        Tower(ctx, 186f, isCapital ? 96f : 88f);

        ctx.Finish(new Vector2(128f, GroundY), new Vector2(82f, 13f), shadowStrength: 0.38f);
    }

    // A small gabled house: box body + triangle roof.
    private static void House(UnitPaintContext ctx, Vector2 baseC, float halfW, float h,
                              ColorRamp wall, ColorRamp roof)
    {
        var bodyC = baseC - new Vector2(0f, h * 0.5f);
        ctx.Painter.FillShaded(p => Sdf.Box(p, bodyC, new Vector2(halfW, h * 0.5f), 1.5f),
                               HumanoidPainter.Bounds(baseC - new Vector2(halfW + 4f, h + 4f),
                                                      baseC + new Vector2(halfW + 4f, 4f), 3f),
                               wall, 5f);
        var roofPts = new[]
        {
            baseC + new Vector2(-halfW - 3f, -h),
            baseC + new Vector2(0f, -h - halfW * 0.9f),
            baseC + new Vector2(halfW + 3f, -h),
        };
        ctx.Painter.FillShaded(p => Sdf.Polygon(p, roofPts),
                               HumanoidPainter.Bounds(baseC - new Vector2(halfW + 6f, h + halfW + 6f),
                                                      baseC + new Vector2(halfW + 6f, -h + 4f), 3f),
                               roof, 4f);
    }

    // The central keep: tall stone block, clay (city) or gold (capital) roof,
    // capital adds a pennant.
    private static void Keep(UnitPaintContext ctx, bool isCapital)
    {
        float h = isCapital ? 96f : 74f;
        var baseC = new Vector2(128f, 188f);
        var bodyC = baseC - new Vector2(0f, h * 0.5f);
        float halfW = isCapital ? 23f : 20f;

        ctx.Painter.FillShaded(p => Sdf.Box(p, bodyC, new Vector2(halfW, h * 0.5f), 2f),
                               HumanoidPainter.Bounds(baseC - new Vector2(halfW + 5f, h + 6f),
                                                      baseC + new Vector2(halfW + 5f, 4f), 4f),
                               MaterialRamps.Stone, 10f);
        // Window slits.
        foreach (float wy in new[] { h * 0.45f, h * 0.68f })
            ctx.Painter.FillSdf(p => Sdf.Box(p, baseC - new Vector2(0f, wy), new Vector2(2f, 4.5f), 1.5f),
                                HumanoidPainter.Bounds(baseC - new Vector2(8f, wy + 8f),
                                                       baseC + new Vector2(8f, -wy + 8f), 2f),
                                new Color(0.13f, 0.11f, 0.10f, 0.9f));

        var roofRamp = isCapital ? MaterialRamps.Gold : Clay;
        var apex = baseC - new Vector2(0f, h + halfW * 0.95f);
        var roofPts = new[]
        {
            baseC + new Vector2(-halfW - 4f, -h),
            apex,
            baseC + new Vector2(halfW + 4f, -h),
        };
        ctx.Painter.FillShaded(p => Sdf.Polygon(p, roofPts),
                               HumanoidPainter.Bounds(apex - new Vector2(halfW + 8f, 6f),
                                                      baseC + new Vector2(halfW + 8f, -h + 6f), 4f),
                               roofRamp, 6f, specular: isCapital ? 0.5f : 0.1f);

        if (isCapital)
        {
            var poleTop = apex - new Vector2(0f, 16f);
            ctx.Painter.FillShaded(p => Sdf.Capsule(p, apex, poleTop, 1.5f),
                                   HumanoidPainter.Bounds(apex, poleTop, 4f), MaterialRamps.Wood, 1.5f);
            var flag = new[]
            {
                poleTop, poleTop + new Vector2(18f, 5f), poleTop + new Vector2(0f, 10f),
            };
            ctx.Painter.FillShaded(p => Sdf.Polygon(p, flag),
                                   HumanoidPainter.Bounds(poleTop, poleTop + new Vector2(22f, 12f), 3f),
                                   ColorRamp.Painterly(new Color(0.80f, 0.20f, 0.16f)), 3f);
        }
    }

    // Curtain wall across the front with crenellations and a gate arch.
    private static void Wall(UnitPaintContext ctx)
    {
        float topY = 160f;
        var wallC = new Vector2(128f, (topY + GroundY) * 0.5f);
        var half  = new Vector2(62f, (GroundY - topY) * 0.5f);
        var b = HumanoidPainter.Bounds(wallC - half - new Vector2(6f, 14f),
                                       wallC + half + new Vector2(6f, 6f), 4f);

        ctx.Painter.FillShaded(p => Sdf.Box(p, wallC, half, 2f), b, MaterialRamps.Stone, 9f);

        // Crenellations: merlons along the wall top.
        for (int i = 0; i < 7; i++)
        {
            float mx = 128f - 54f + i * 18f;
            ctx.Painter.FillShaded(p => Sdf.Box(p, new Vector2(mx, topY - 4f), new Vector2(5f, 5f), 1f),
                                   HumanoidPainter.Bounds(new Vector2(mx - 9f, topY - 13f),
                                                          new Vector2(mx + 9f, topY + 4f), 2f),
                                   MaterialRamps.Stone, 4f);
        }

        // Gate: dark round-topped arch with a wood door inset.
        var gateC = new Vector2(128f, GroundY - 1f);
        Func<Vector2, float> arch = p => Sdf.Union(
            Sdf.Box(p, gateC - new Vector2(0f, 9f), new Vector2(11f, 9f)),
            Sdf.Circle(p, gateC - new Vector2(0f, 18f), 11f));
        ctx.Painter.FillSdf(arch, HumanoidPainter.Bounds(gateC - new Vector2(16f, 34f),
                                                         gateC + new Vector2(16f, 4f), 3f),
                            new Color(0.10f, 0.08f, 0.07f));
        ctx.Painter.FillShaded(p => arch(p) + 2.5f,
                               HumanoidPainter.Bounds(gateC - new Vector2(14f, 32f),
                                                      gateC + new Vector2(14f, 2f), 3f),
                               MaterialRamps.Wood, 4f);
    }

    // Round corner tower with a conical clay roof.
    private static void Tower(UnitPaintContext ctx, float x, float h)
    {
        var baseC = new Vector2(x, GroundY);
        var bodyC = baseC - new Vector2(0f, h * 0.5f);
        const float r = 12f;

        ctx.Painter.FillShaded(p => Sdf.Box(p, bodyC, new Vector2(r, h * 0.5f), 5f),
                               HumanoidPainter.Bounds(baseC - new Vector2(r + 5f, h + 6f),
                                                      baseC + new Vector2(r + 5f, 4f), 4f),
                               MaterialRamps.Stone, 8f);
        ctx.Painter.FillSdf(p => Sdf.Box(p, baseC - new Vector2(0f, h * 0.55f), new Vector2(2f, 4f), 1.5f),
                            HumanoidPainter.Bounds(baseC - new Vector2(7f, h * 0.55f + 8f),
                                                   baseC + new Vector2(7f, -h * 0.55f + 8f), 2f),
                            new Color(0.13f, 0.11f, 0.10f, 0.9f));

        var apex = baseC - new Vector2(0f, h + 17f);
        var roofPts = new[]
        {
            baseC + new Vector2(-r - 4f, -h), apex, baseC + new Vector2(r + 4f, -h),
        };
        ctx.Painter.FillShaded(p => Sdf.Polygon(p, roofPts),
                               HumanoidPainter.Bounds(apex - new Vector2(r + 8f, 5f),
                                                      baseC + new Vector2(r + 8f, -h + 5f), 4f),
                               Clay, 5f);
    }
}
