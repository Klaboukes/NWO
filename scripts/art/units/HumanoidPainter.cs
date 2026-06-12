using System;
using Godot;
using NWO.Art.Painterly;

namespace NWO.Art.Units;

// The parametric soldier/civilian figure every infantry unit shares: one body
// plan (so the roster reads as a family), dressed per unit via material ramps
// and posed via hand positions. Painted back-to-front — cape, far arm + far
// weapon, legs, torso, head, near arm + near weapon — with FillShaded volumes
// so limbs and chest read as lit 3D forms.
public static class HumanoidPainter
{
    // Canvas-space anatomy constants (256px canvas, figure centred at x = 128).
    public const float GroundY = 212f;
    public static readonly Vector2 HeadC = new(128f, 86f);
    public const float HeadR = 15f;
    private const float ShoulderY = 108f;
    private const float HipY = 154f;

    public sealed class Figure
    {
        public int SkinVariant;
        public ColorRamp Torso = MaterialRamps.Leather;
        public ColorRamp Legs  = MaterialRamps.Cloth(new Color(0.42f, 0.38f, 0.32f));
        public ColorRamp? Helmet;
        public Color Hair = new(0.28f, 0.20f, 0.12f);
        public bool  Plume;                     // helmet crest (guards, legionaries)
        public Color PlumeColor = new(0.75f, 0.15f, 0.12f);
        public bool  Cape;
        public Color CapeColor = new(0.25f, 0.35f, 0.25f);
        public bool  Hat;                       // brimmed civilian hat
        public float TorsoSpecular;             // > 0 for metal chests
        public Vector2 FarHand  = new(92f, 158f);
        public Vector2 NearHand = new(164f, 158f);
    }

    public static void Draw(UnitPaintContext ctx, Figure fig,
                            Action<Vector2>? farWeapon = null,
                            Action<Vector2>? nearWeapon = null)
    {
        var skin = MaterialRamps.Skin(fig.SkinVariant);

        if (fig.Cape) Cape(ctx, fig.CapeColor);

        // Far arm + its weapon sit behind everything.
        farWeapon?.Invoke(fig.FarHand);
        Arm(ctx, new Vector2(112f, ShoulderY + 4f), fig.FarHand, fig.Torso, skin, bendSign: -1f);

        Legs(ctx, fig.Legs);
        Torso(ctx, fig);
        Head(ctx, fig, skin);

        Arm(ctx, new Vector2(144f, ShoulderY + 4f), fig.NearHand, fig.Torso, skin, bendSign: +1f);
        nearWeapon?.Invoke(fig.NearHand);
    }

    private static void Cape(UnitPaintContext ctx, Color col)
    {
        var pts = new[]
        {
            new Vector2(112f, ShoulderY),
            new Vector2(144f, ShoulderY),
            new Vector2(156f, HipY + 34f),
            new Vector2(104f, HipY + 38f),
        };
        var bounds = new Rect2(96f, ShoulderY - 6f, 70f, HipY - ShoulderY + 50f);
        ctx.Painter.FillShaded(p => Sdf.Polygon(p, pts), bounds,
                               ColorRamp.Painterly(col), 9f);
    }

    private static void Legs(UnitPaintContext ctx, ColorRamp ramp)
    {
        var boot = MaterialRamps.Leather;
        foreach (float side in new[] { -1f, 1f })
        {
            var hip  = new Vector2(128f + side * 9f, HipY);
            var knee = new Vector2(128f + side * 12f, HipY + 27f);
            var foot = new Vector2(128f + side * 15f, GroundY - 4f);
            Func<Vector2, float> leg = p => Sdf.Union(
                Sdf.Capsule(p, hip, knee, 6.5f), Sdf.Capsule(p, knee, foot, 5.5f));
            ctx.Painter.FillShaded(leg, Bounds(hip, foot, 10f), ramp, 6f);
            // Boot: short dark segment at the ankle plus the foot nub.
            ctx.Painter.FillShaded(
                p => Sdf.Capsule(p, foot - new Vector2(0f, 8f), foot + new Vector2(side * 4f, 2f), 5.5f),
                Bounds(foot - new Vector2(12f, 14f), foot + new Vector2(12f, 8f), 4f), boot, 5f);
        }
    }

    private static void Torso(UnitPaintContext ctx, Figure fig)
    {
        var pts = new[]
        {
            new Vector2(106f, ShoulderY),
            new Vector2(150f, ShoulderY),
            new Vector2(143f, HipY + 6f),
            new Vector2(113f, HipY + 6f),
        };
        var bounds = new Rect2(100f, ShoulderY - 8f, 56f, HipY - ShoulderY + 20f);
        ctx.Painter.FillShaded(p => Sdf.Polygon(p, pts), bounds, fig.Torso, 13f,
                               specular: fig.TorsoSpecular);
        // Belt.
        ctx.Painter.FillShaded(
            p => Sdf.Box(p, new Vector2(128f, HipY - 2f), new Vector2(17f, 4f), 2f),
            new Rect2(108f, HipY - 10f, 40f, 16f), MaterialRamps.Leather, 3f);
    }

    private static void Head(UnitPaintContext ctx, Figure fig, ColorRamp skin)
    {
        // Neck, then face.
        ctx.Painter.FillShaded(
            p => Sdf.Capsule(p, HeadC + new Vector2(0f, HeadR - 4f), new Vector2(128f, ShoulderY + 2f), 6f),
            Bounds(HeadC, new Vector2(128f, ShoulderY + 8f), 8f), skin, 6f);
        ctx.Painter.FillShaded(p => Sdf.Circle(p, HeadC, HeadR),
                               Bounds(HeadC, HeadC, HeadR + 3f), skin, HeadR * 0.8f);

        if (fig.Helmet is { } helm)
        {
            // Dome over the upper head (circle ∩ half-plane) + a short nose guard.
            Func<Vector2, float> dome = p => Sdf.Intersect(
                Sdf.Circle(p, HeadC - new Vector2(0f, 2f), HeadR + 3f),
                p.Y - (HeadC.Y + 1f)); // negative above the cut line = inside
            var hb = Bounds(HeadC - new Vector2(0f, HeadR + 8f), HeadC + new Vector2(0f, 6f), HeadR + 6f);
            ctx.Painter.FillShaded(dome, hb, helm, 7f, specular: 0.5f);
            ctx.Painter.FillShaded(
                p => Sdf.Box(p, HeadC + new Vector2(0f, 4f), new Vector2(2.2f, 7f), 1.5f),
                Bounds(HeadC, HeadC + new Vector2(0f, 12f), 6f), helm, 2f, specular: 0.4f);
            if (fig.Plume)
            {
                var top = HeadC - new Vector2(0f, HeadR + 2f);
                ctx.Painter.FillShaded(
                    p => Sdf.Ellipse(p, top - new Vector2(0f, 6f), new Vector2(5f, 11f)),
                    Bounds(top - new Vector2(10f, 20f), top + new Vector2(10f, 4f), 4f),
                    ColorRamp.Painterly(fig.PlumeColor), 4f);
            }
        }
        else if (fig.Hat)
        {
            var hatRamp = MaterialRamps.Leather;
            var crownC = HeadC - new Vector2(0f, HeadR - 1f);
            ctx.Painter.FillShaded(
                p => Sdf.Ellipse(p, crownC + new Vector2(0f, 1f), new Vector2(HeadR + 9f, 4.5f)),
                Bounds(crownC - new Vector2(28f, 8f), crownC + new Vector2(28f, 8f), 4f), hatRamp, 3f);
            ctx.Painter.FillShaded(
                p => Sdf.Ellipse(p, crownC - new Vector2(0f, 5f), new Vector2(10f, 7f)),
                Bounds(crownC - new Vector2(14f, 14f), crownC + new Vector2(14f, 2f), 4f), hatRamp, 5f);
        }
        else
        {
            // Hair: crescent over the crown.
            Func<Vector2, float> hair = p => Sdf.Subtract(
                Sdf.Circle(p, HeadC - new Vector2(0f, 3f), HeadR + 1f),
                Sdf.Circle(p, HeadC + new Vector2(0f, 6f), HeadR + 2f));
            ctx.Painter.FillShaded(hair, Bounds(HeadC, HeadC, HeadR + 4f),
                                   ColorRamp.Painterly(fig.Hair), 4f);
        }
    }

    private static void Arm(UnitPaintContext ctx, Vector2 shoulder, Vector2 hand,
                            ColorRamp sleeve, ColorRamp skin, float bendSign)
    {
        // Elbow bows perpendicular to the shoulder→hand line.
        var axis = hand - shoulder;
        var perp = new Vector2(-axis.Y, axis.X).Normalized() * (8f * bendSign);
        var elbow = shoulder + axis * 0.5f + perp;
        ctx.Painter.FillShaded(p => Sdf.Capsule(p, shoulder, elbow, 6f),
                               Bounds(shoulder, elbow, 9f), sleeve, 6f);
        ctx.Painter.FillShaded(p => Sdf.Capsule(p, elbow, hand, 5f),
                               Bounds(elbow, hand, 8f), skin, 5f);
        // Hand.
        ctx.Painter.FillShaded(p => Sdf.Circle(p, hand, 5f),
                               Bounds(hand, hand, 8f), skin, 4f);
    }

    public static Rect2 Bounds(Vector2 a, Vector2 b, float pad)
    {
        var lo = new Vector2(Mathf.Min(a.X, b.X) - pad, Mathf.Min(a.Y, b.Y) - pad);
        var hi = new Vector2(Mathf.Max(a.X, b.X) + pad, Mathf.Max(a.Y, b.Y) + pad);
        return new Rect2(lo, hi - lo);
    }
}
