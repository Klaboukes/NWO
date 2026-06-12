using System;
using Godot;
using NWO.Art.Painterly;

namespace NWO.Art.Units;

// Non-humanoid land units: the catapult siege engine, the settler wagon, and
// the sci-fi recon drone. Shares the spoked-wheel sub-assembly.
public static class VehiclePainter
{
    public static void Catapult(UnitPaintContext ctx)
    {
        var frameC = new Vector2(118f, 168f);

        // Throwing arm first (behind the frame), angled up-right with the sling.
        var pivot = frameC + new Vector2(10f, -8f);
        var armTip = pivot + new Vector2(54f, -64f);
        ctx.Painter.FillShaded(p => Sdf.Capsule(p, pivot, armTip, 4f),
                               HumanoidPainter.Bounds(pivot, armTip, 8f), MaterialRamps.Wood, 4f);
        ctx.Painter.FillShaded(p => Sdf.Circle(p, armTip, 10f),
                               HumanoidPainter.Bounds(armTip, armTip, 14f), MaterialRamps.Wood, 6f);
        ctx.Painter.FillShaded(p => Sdf.Circle(p, armTip, 6f),
                               HumanoidPainter.Bounds(armTip, armTip, 9f), MaterialRamps.Stone, 5f);

        // Frame: two horizontal beams + a brace.
        ctx.Painter.FillShaded(
            p => Sdf.Box(p, frameC, new Vector2(46f, 7f), 3f),
            HumanoidPainter.Bounds(frameC - new Vector2(52f, 12f), frameC + new Vector2(52f, 12f), 6f),
            MaterialRamps.Wood, 6f);
        ctx.Painter.FillShaded(
            p => Sdf.Capsule(p, frameC + new Vector2(-30f, -4f), pivot + new Vector2(0f, 2f), 4f),
            HumanoidPainter.Bounds(frameC + new Vector2(-36f, -16f), pivot, 6f),
            MaterialRamps.Wood, 4f);
        // Crossbar upright.
        ctx.Painter.FillShaded(
            p => Sdf.Box(p, frameC + new Vector2(26f, -16f), new Vector2(5f, 14f), 2f),
            HumanoidPainter.Bounds(frameC + new Vector2(14f, -34f), frameC + new Vector2(38f, 2f), 4f),
            MaterialRamps.Wood, 4f);

        Wheel(ctx, frameC + new Vector2(-30f, 18f), 16f);
        Wheel(ctx, frameC + new Vector2(32f, 18f), 16f);
    }

    public static void SettlerWagon(UnitPaintContext ctx)
    {
        var boxC = new Vector2(128f, 162f);

        // Canvas cover: ellipse ∩ upper half-plane (a tall arch over the box).
        Func<Vector2, float> coverSdf = p => Sdf.Intersect(
            Sdf.Ellipse(p, boxC + new Vector2(0f, -8f), new Vector2(42f, 36f)),
            p.Y - (boxC.Y - 4f));
        ctx.Painter.FillShaded(coverSdf,
            HumanoidPainter.Bounds(boxC - new Vector2(48f, 48f), boxC + new Vector2(48f, 4f), 6f),
            MaterialRamps.Sail, 14f);
        // Cover ribs.
        for (int i = -1; i <= 1; i++)
        {
            float rx = boxC.X + i * 22f;
            ctx.Painter.FillSdf(
                p => Sdf.Intersect(coverSdf(p) + 1.5f, Mathf.Abs(p.X - rx) - 1f),
                HumanoidPainter.Bounds(boxC - new Vector2(48f, 48f), boxC + new Vector2(48f, 4f), 4f),
                new Color(0.62f, 0.57f, 0.47f, 0.7f));
        }

        // Wagon box.
        ctx.Painter.FillShaded(
            p => Sdf.Box(p, boxC, new Vector2(44f, 13f), 3f),
            HumanoidPainter.Bounds(boxC - new Vector2(50f, 18f), boxC + new Vector2(50f, 18f), 6f),
            MaterialRamps.Wood, 8f);

        // Yoke pole out the front-left.
        ctx.Painter.FillShaded(
            p => Sdf.Capsule(p, boxC + new Vector2(-44f, 8f), boxC + new Vector2(-70f, 16f), 2.4f),
            HumanoidPainter.Bounds(boxC + new Vector2(-76f, 0f), boxC + new Vector2(-40f, 22f), 5f),
            MaterialRamps.Wood, 2.4f);

        Wheel(ctx, boxC + new Vector2(-26f, 22f), 17f);
        Wheel(ctx, boxC + new Vector2(28f, 22f), 17f);
    }

    public static void Drone(UnitPaintContext ctx)
    {
        // Hovers: body sits high, contact shadow far below (set by the catalog).
        var c = new Vector2(128f, 120f);

        // Rotor pylons + blurred rotor discs.
        foreach (float side in new[] { -1f, 1f })
        {
            var hub = c + new Vector2(side * 40f, -12f);
            ctx.Painter.FillShaded(p => Sdf.Capsule(p, c + new Vector2(side * 16f, -4f), hub, 3f),
                                   HumanoidPainter.Bounds(c, hub, 6f), MaterialRamps.Steel, 3f,
                                   specular: 0.5f);
            ctx.Painter.FillSdf(p => Sdf.Ellipse(p, hub + new Vector2(0f, -4f), new Vector2(22f, 4f)),
                                HumanoidPainter.Bounds(hub - new Vector2(26f, 12f), hub + new Vector2(26f, 4f), 4f),
                                new Color(0.75f, 0.80f, 0.86f, 0.45f));
        }

        // Lenticular body.
        ctx.Painter.FillShaded(p => Sdf.Ellipse(p, c, new Vector2(34f, 16f)),
                               HumanoidPainter.Bounds(c - new Vector2(40f, 22f), c + new Vector2(40f, 22f), 6f),
                               MaterialRamps.Steel, 12f, specular: 0.7f);

        // Glowing sensor eye: bright cyan core + soft halo.
        var eye = c + new Vector2(0f, 6f);
        ctx.Painter.FillSdf(p => Sdf.Circle(p, eye, 9f),
                            HumanoidPainter.Bounds(eye, eye, 12f),
                            new Color(0.20f, 0.85f, 0.95f, 0.35f));
        ctx.Painter.FillSdf(p => Sdf.Circle(p, eye, 5f),
                            HumanoidPainter.Bounds(eye, eye, 8f),
                            new Color(0.55f, 0.97f, 1f, 0.95f));
        ctx.Painter.FillSdf(p => Sdf.Circle(p, eye + new Vector2(-1.5f, -1.5f), 1.8f),
                            HumanoidPainter.Bounds(eye, eye, 5f), Colors.White);
    }

    // Spoked wooden wheel with an iron hub.
    public static void Wheel(UnitPaintContext ctx, Vector2 c, float r)
    {
        var b = HumanoidPainter.Bounds(c, c, r + 5f);
        ctx.Painter.FillShaded(p => Sdf.Circle(p, c, r), b, MaterialRamps.Wood, r * 0.4f);
        ctx.Painter.FillSdf(p => Sdf.Circle(p, c, r * 0.62f), b,
                            new Color(0.16f, 0.11f, 0.07f, 0.85f));
        for (int s = 0; s < 4; s++)
        {
            float a = s * Mathf.Pi / 4f;
            var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (r * 0.62f);
            ctx.Painter.FillSdf(p => Sdf.Capsule(p, c - d, c + d, 1.6f), b,
                                new Color(0.52f, 0.36f, 0.20f));
        }
        ctx.Painter.FillShaded(p => Sdf.Circle(p, c, r * 0.2f), b,
                               MaterialRamps.Steel, r * 0.16f, specular: 0.5f);
    }
}
