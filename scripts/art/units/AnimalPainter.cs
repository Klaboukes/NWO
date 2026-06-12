using System;
using Godot;
using NWO.Art.Painterly;

namespace NWO.Art.Units;

// Mounted units: a side-profile horse with an armoured rider. Built from
// capsule/ellipse volumes so the body reads rounded under the shared sun.
public static class AnimalPainter
{
    public static void HorseWithRider(UnitPaintContext ctx, ColorRamp riderTorso)
    {
        var coat = ColorRamp.Painterly(new Color(0.50f, 0.33f, 0.20f));
        var mane = ColorRamp.Painterly(new Color(0.20f, 0.13f, 0.08f));
        var bodyC = new Vector2(118f, 158f);

        // Far legs first (slightly darker read comes from the AO pass).
        Legs(ctx, coat, bodyC, far: true);

        // Tail.
        var tailTop = bodyC + new Vector2(-40f, -8f);
        ctx.Painter.FillShaded(
            p => Sdf.QuadBezier(p, tailTop, tailTop + new Vector2(-16f, 14f), tailTop + new Vector2(-10f, 36f)) - 4f,
            HumanoidPainter.Bounds(tailTop + new Vector2(-26f, 0f), tailTop + new Vector2(4f, 42f), 6f),
            mane, 4f);

        // Body + neck + head.
        ctx.Painter.FillShaded(p => Sdf.Ellipse(p, bodyC, new Vector2(42f, 21f)),
                               HumanoidPainter.Bounds(bodyC - new Vector2(48f, 28f), bodyC + new Vector2(48f, 28f), 6f),
                               coat, 16f);
        var neckTop = bodyC + new Vector2(46f, -34f);
        ctx.Painter.FillShaded(p => Sdf.Capsule(p, bodyC + new Vector2(30f, -8f), neckTop, 11f),
                               HumanoidPainter.Bounds(bodyC + new Vector2(16f, -50f), neckTop + new Vector2(16f, 10f), 8f),
                               coat, 9f);
        var headC = neckTop + new Vector2(13f, 2f);
        ctx.Painter.FillShaded(p => Sdf.Ellipse(p, headC, new Vector2(15f, 8.5f)),
                               HumanoidPainter.Bounds(headC - new Vector2(20f, 14f), headC + new Vector2(20f, 14f), 5f),
                               coat, 7f);
        // Ear + mane ridge.
        ctx.Painter.FillShaded(p => Sdf.Capsule(p, neckTop + new Vector2(2f, -8f), neckTop + new Vector2(5f, -16f), 2.6f),
                               HumanoidPainter.Bounds(neckTop - new Vector2(6f, 22f), neckTop + new Vector2(12f, 0f), 4f),
                               coat, 2.6f);
        ctx.Painter.FillShaded(
            p => Sdf.QuadBezier(p, neckTop + new Vector2(-4f, -6f), bodyC + new Vector2(34f, -28f), bodyC + new Vector2(22f, -16f)) - 3.4f,
            HumanoidPainter.Bounds(bodyC + new Vector2(14f, -48f), neckTop + new Vector2(6f, 2f), 6f),
            mane, 3.4f);

        // Near legs.
        Legs(ctx, coat, bodyC, far: false);

        // Rider: compact torso + head above the horse's back, lance in hand.
        var seat = bodyC + new Vector2(-2f, -24f);
        var rTorso = new[]
        {
            seat + new Vector2(-11f, -26f), seat + new Vector2(11f, -26f),
            seat + new Vector2(9f, 2f),     seat + new Vector2(-9f, 2f),
        };
        ctx.Painter.FillShaded(p => Sdf.Polygon(p, rTorso),
                               HumanoidPainter.Bounds(seat - new Vector2(16f, 32f), seat + new Vector2(16f, 8f), 5f),
                               riderTorso, 8f, specular: 0.3f);
        var rHead = seat + new Vector2(0f, -35f);
        ctx.Painter.FillShaded(p => Sdf.Circle(p, rHead, 9f),
                               HumanoidPainter.Bounds(rHead, rHead, 12f),
                               MaterialRamps.Skin(0), 7f);
        ctx.Painter.FillShaded(
            p => Sdf.Intersect(Sdf.Circle(p, rHead - new Vector2(0f, 1f), 10.5f), p.Y - rHead.Y),
            HumanoidPainter.Bounds(rHead - new Vector2(14f, 14f), rHead + new Vector2(14f, 2f), 4f),
            MaterialRamps.Steel, 5f, specular: 0.5f);

        // Lance angled up-right, couched at the rider's side.
        var grip = seat + new Vector2(12f, -10f);
        var lanceTip = grip + new Vector2(46f, -58f);
        var lanceButt = grip + new Vector2(-18f, 22f);
        ctx.Painter.FillShaded(p => Sdf.Capsule(p, lanceButt, lanceTip, 2.4f),
                               HumanoidPainter.Bounds(lanceButt, lanceTip, 6f), MaterialRamps.Wood, 2.4f);
        var tipPts = new[]
        {
            lanceTip + new Vector2(8f, -10f), lanceTip + new Vector2(4f, 2f), lanceTip + new Vector2(-4f, -2f),
        };
        ctx.Painter.FillShaded(p => Sdf.Polygon(p, tipPts),
                               HumanoidPainter.Bounds(lanceTip - new Vector2(8f, 14f), lanceTip + new Vector2(12f, 6f), 4f),
                               MaterialRamps.Steel, 2.5f, specular: 0.7f);
    }

    private static void Legs(UnitPaintContext ctx, ColorRamp coat, Vector2 bodyC, bool far)
    {
        float off = far ? -7f : 5f;
        foreach (float lx in new[] { -28f, 24f })
        {
            var hip  = bodyC + new Vector2(lx + off * 0.3f, 12f);
            var knee = hip + new Vector2(off * 0.4f, 22f);
            var hoof = knee + new Vector2(off * 0.5f, 26f);
            Func<Vector2, float> leg = p => Sdf.Union(
                Sdf.Capsule(p, hip, knee, 5f), Sdf.Capsule(p, knee, hoof, 3.6f));
            ctx.Painter.FillShaded(leg, HumanoidPainter.Bounds(hip, hoof, 8f), coat, 4.5f);
            ctx.Painter.FillShaded(p => Sdf.Capsule(p, hoof, hoof + new Vector2(1f, 3f), 3.8f),
                                   HumanoidPainter.Bounds(hoof - new Vector2(8f, 2f), hoof + new Vector2(8f, 8f), 3f),
                                   ColorRamp.Painterly(new Color(0.16f, 0.12f, 0.09f)), 3f);
        }
    }
}
