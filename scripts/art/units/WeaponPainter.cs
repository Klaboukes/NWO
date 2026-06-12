using System;
using Godot;
using NWO.Art.Painterly;

namespace NWO.Art.Units;

// Shaded weapon/equipment assemblies, drawn relative to a hand (or anchor)
// position. The shape vocabulary carries over from v1's silhouettes; the
// rendering is painterly v2 — wood/steel material ramps with speculars.
public static class WeaponPainter
{
    private static Rect2 B(Vector2 a, Vector2 b, float pad) => HumanoidPainter.Bounds(a, b, pad);

    // Tall vertical spear gripped at the hand: shaft + leaf blade.
    public static void Spear(UnitPaintContext ctx, Vector2 hand, float len = 130f)
    {
        var butt = new Vector2(hand.X, hand.Y + 42f);
        var top  = new Vector2(hand.X, hand.Y + 42f - len);
        ctx.Painter.FillShaded(p => Sdf.Capsule(p, butt, top, 2.6f),
                               B(butt, top, 5f), MaterialRamps.Wood, 2.6f);
        var tip = top - new Vector2(0f, 16f);
        var blade = new[]
        {
            tip,
            top + new Vector2(6f, 2f),
            top + new Vector2(0f, 8f),
            top + new Vector2(-6f, 2f),
        };
        ctx.Painter.FillShaded(p => Sdf.Polygon(p, blade), B(tip, top + new Vector2(0f, 8f), 9f),
                               MaterialRamps.Steel, 3.5f, specular: 0.7f);
    }

    // Sword raised at an angle (radians, 0 = up). Long blade, guard, pommel.
    public static void Sword(UnitPaintContext ctx, Vector2 hand, float angle = 0.45f,
                             float bladeLen = 78f)
    {
        var dir   = new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle));
        var perp  = new Vector2(-dir.Y, dir.X);
        var guard = hand + dir * 8f;
        var tip   = guard + dir * bladeLen;
        // Blade: tapered — drawn as two capsule strokes, thick base / thin tip.
        ctx.Painter.FillShaded(p => Sdf.Capsule(p, guard, guard + dir * (bladeLen * 0.6f), 4f),
                               B(guard, tip, 8f), MaterialRamps.Steel, 4f, specular: 0.75f);
        ctx.Painter.FillShaded(p => Sdf.Capsule(p, guard + dir * (bladeLen * 0.5f), tip, 2.6f),
                               B(guard, tip, 8f), MaterialRamps.Steel, 2.6f, specular: 0.75f);
        ctx.Painter.FillShaded(p => Sdf.Capsule(p, guard - perp * 9f, guard + perp * 9f, 2.6f),
                               B(guard - perp * 12f, guard + perp * 12f, 5f),
                               MaterialRamps.Bronze, 2.6f, specular: 0.5f);
        ctx.Painter.FillShaded(p => Sdf.Circle(p, hand - dir * 9f, 4f),
                               B(hand - dir * 14f, hand, 6f), MaterialRamps.Bronze, 3f,
                               specular: 0.5f);
    }

    // Short stabbing sword (legionary).
    public static void Gladius(UnitPaintContext ctx, Vector2 hand)
        => Sword(ctx, hand, angle: 0.30f, bladeLen: 48f);

    // Heavy wooden club, thick end up.
    public static void Club(UnitPaintContext ctx, Vector2 hand)
    {
        var dir = new Vector2(Mathf.Sin(0.5f), -Mathf.Cos(0.5f));
        var top = hand + dir * 52f;
        ctx.Painter.FillShaded(p => Sdf.Capsule(p, hand - dir * 8f, top, 4f),
                               B(hand - dir * 12f, top, 10f), MaterialRamps.Wood, 4f);
        ctx.Painter.FillShaded(p => Sdf.Circle(p, top, 9f),
                               B(top, top, 12f), MaterialRamps.Wood, 7f);
    }

    // Bow held out by the far hand, arrow nocked horizontally.
    public static void Bow(UnitPaintContext ctx, Vector2 hand)
    {
        var top    = hand + new Vector2(6f, -52f);
        var bottom = hand + new Vector2(6f, 52f);
        var ctrl   = hand + new Vector2(-26f, 0f);
        ctx.Painter.FillShaded(p => Sdf.QuadBezier(p, top, ctrl, bottom) - 2.6f,
                               B(top, bottom, 32f), MaterialRamps.Wood, 2.6f);
        // String.
        ctx.Painter.FillSdf(p => Sdf.Segment(p, top, bottom) - 0.7f, B(top, bottom, 3f),
                            new Color(0.85f, 0.82f, 0.74f), 0.7f);
        // Arrow on the string, pointing left (drawn direction).
        var nock = hand + new Vector2(6f, 0f);
        var head = hand + new Vector2(-34f, 0f);
        ctx.Painter.FillShaded(p => Sdf.Capsule(p, nock, head, 1.6f),
                               B(nock, head, 6f), MaterialRamps.Wood, 1.6f);
        var tipPts = new[]
        {
            head + new Vector2(-9f, 0f), head + new Vector2(2f, -4f), head + new Vector2(2f, 4f),
        };
        ctx.Painter.FillShaded(p => Sdf.Polygon(p, tipPts), B(head + new Vector2(-12f, 0f), head, 7f),
                               MaterialRamps.Steel, 2.5f, specular: 0.6f);
    }

    // Round shield centred on the far hand.
    public static void RoundShield(UnitPaintContext ctx, Vector2 c, float r, Color face)
    {
        ctx.Painter.FillShaded(p => Sdf.Circle(p, c, r), B(c, c, r + 4f),
                               ColorRamp.Painterly(face), r * 0.55f, specular: 0.18f);
        ctx.Painter.FillShaded(p => Sdf.Circle(p, c, r * 0.28f), B(c, c, r * 0.5f),
                               MaterialRamps.Steel, r * 0.22f, specular: 0.6f);
        ctx.Painter.StrokeSdf(p => Sdf.Circle(p, c, r - 1.5f), B(c, c, r + 4f),
                              new Color(0.2f, 0.16f, 0.12f, 0.8f), 2.2f);
    }

    // Tall rectangular legionary shield.
    public static void Scutum(UnitPaintContext ctx, Vector2 c)
    {
        var half = new Vector2(16f, 34f);
        Func<Vector2, float> sdf = p => Sdf.Box(p, c, half, 7f);
        ctx.Painter.FillShaded(sdf, B(c - half, c + half, 6f),
                               ColorRamp.Painterly(new Color(0.62f, 0.16f, 0.12f)), 9f,
                               specular: 0.2f);
        // Vertical gold spine + boss.
        ctx.Painter.FillShaded(p => Sdf.Box(p, c, new Vector2(2.4f, half.Y - 4f), 2f),
                               B(c - half, c + half, 4f), MaterialRamps.Gold, 2.4f, specular: 0.5f);
        ctx.Painter.FillShaded(p => Sdf.Circle(p, c, 6f), B(c, c, 9f),
                               MaterialRamps.Gold, 4.5f, specular: 0.6f);
    }

    // Kite shield (swordsman).
    public static void KiteShield(UnitPaintContext ctx, Vector2 c, Color face)
    {
        var pts = new[]
        {
            c + new Vector2(-15f, -22f), c + new Vector2(15f, -22f),
            c + new Vector2(12f, 6f),    c + new Vector2(0f, 30f),
            c + new Vector2(-12f, 6f),
        };
        ctx.Painter.FillShaded(p => Sdf.Polygon(p, pts), B(c - new Vector2(20f, 26f), c + new Vector2(20f, 34f), 4f),
                               ColorRamp.Painterly(face), 9f, specular: 0.2f);
        ctx.Painter.FillShaded(p => Sdf.Circle(p, c - new Vector2(0f, 6f), 5f),
                               B(c - new Vector2(8f, 14f), c + new Vector2(8f, 2f), 3f),
                               MaterialRamps.Steel, 4f, specular: 0.6f);
    }

    // Halberd: long shaft, axe blade + top spike (palace guard).
    public static void Halberd(UnitPaintContext ctx, Vector2 hand)
    {
        var butt = new Vector2(hand.X, hand.Y + 48f);
        var top  = new Vector2(hand.X, hand.Y - 92f);
        ctx.Painter.FillShaded(p => Sdf.Capsule(p, butt, top, 2.6f),
                               B(butt, top, 5f), MaterialRamps.Wood, 2.6f);
        var spike = top - new Vector2(0f, 14f);
        ctx.Painter.FillShaded(p => Sdf.Capsule(p, top, spike, 2f),
                               B(top, spike, 5f), MaterialRamps.Steel, 2f, specular: 0.7f);
        var bladeC = top + new Vector2(8f, 8f);
        var blade = new[]
        {
            top + new Vector2(2f, -2f), bladeC + new Vector2(10f, -6f),
            bladeC + new Vector2(12f, 10f), top + new Vector2(2f, 18f),
        };
        ctx.Painter.FillShaded(p => Sdf.Polygon(p, blade),
                               B(top + new Vector2(0f, -6f), bladeC + new Vector2(16f, 14f), 5f),
                               MaterialRamps.Steel, 4f, specular: 0.7f);
    }

    // Pickaxe held diagonally across the body (worker).
    public static void Pickaxe(UnitPaintContext ctx, Vector2 hand)
    {
        var headP = hand + new Vector2(26f, -44f);
        var butt  = hand + new Vector2(-18f, 30f);
        ctx.Painter.FillShaded(p => Sdf.Capsule(p, butt, headP, 2.8f),
                               B(butt, headP, 6f), MaterialRamps.Wood, 2.8f);
        // Curved iron head: two arcs meeting at the eye.
        var left  = headP + new Vector2(-22f, 8f);
        var right = headP + new Vector2(20f, 6f);
        ctx.Painter.FillShaded(p => Sdf.QuadBezier(p, left, headP + new Vector2(0f, -10f), right) - 3.2f,
                               B(left, right, 16f), MaterialRamps.Steel, 3.2f, specular: 0.55f);
    }

    // Rifle held across the body (ranger).
    public static void Rifle(UnitPaintContext ctx, Vector2 hand)
    {
        var muzzle = hand + new Vector2(44f, -26f);
        var stock  = hand + new Vector2(-30f, 18f);
        ctx.Painter.FillShaded(p => Sdf.Capsule(p, stock, hand + new Vector2(8f, -5f), 4f),
                               B(stock, hand, 7f), MaterialRamps.Wood, 4f);
        ctx.Painter.FillShaded(p => Sdf.Capsule(p, hand + new Vector2(2f, -2f), muzzle, 2f),
                               B(hand, muzzle, 5f), MaterialRamps.Steel, 2f, specular: 0.6f);
    }

    // Spyglass raised toward the upper-right (scout).
    public static void Spyglass(UnitPaintContext ctx, Vector2 hand)
    {
        var dir = new Vector2(0.7071f, -0.7071f);
        var eye = hand - dir * 6f;
        var objv = hand + dir * 34f;
        ctx.Painter.FillShaded(p => Sdf.Capsule(p, eye, hand + dir * 16f, 3.2f),
                               B(eye, objv, 8f), MaterialRamps.Bronze, 3.2f, specular: 0.5f);
        ctx.Painter.FillShaded(p => Sdf.Capsule(p, hand + dir * 14f, objv, 4.6f),
                               B(eye, objv, 9f), MaterialRamps.Bronze, 4.6f, specular: 0.5f);
        ctx.Painter.FillShaded(p => Sdf.Circle(p, objv, 5.2f),
                               B(objv, objv, 8f), MaterialRamps.Steel, 3f, specular: 0.8f);
    }

    // Walking staff with a small banner pennant (pioneer survey pole).
    public static void SurveyPole(UnitPaintContext ctx, Vector2 hand)
    {
        var butt = new Vector2(hand.X, hand.Y + 46f);
        var top  = new Vector2(hand.X, hand.Y - 78f);
        ctx.Painter.FillShaded(p => Sdf.Capsule(p, butt, top, 2.2f),
                               B(butt, top, 5f), MaterialRamps.Wood, 2.2f);
        var flag = new[]
        {
            top + new Vector2(2f, 2f), top + new Vector2(26f, 8f), top + new Vector2(2f, 16f),
        };
        ctx.Painter.FillShaded(p => Sdf.Polygon(p, flag), B(top, top + new Vector2(30f, 18f), 4f),
                               ColorRamp.Painterly(new Color(0.85f, 0.30f, 0.20f)), 3f);
    }
}
