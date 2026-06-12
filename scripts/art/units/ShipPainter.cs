using System;
using Godot;
using NWO.Art.Painterly;

namespace NWO.Art.Units;

// Side-profile sailing vessels sharing one hull recipe: a sheer-line bezier hull
// with plank shading, masts + bulged sails, and per-class fittings (ram + oars,
// gunports, deck cargo, stern castle). Parametrized so all four naval units read
// as one shipyard's work.
public static class ShipPainter
{
    public sealed class ShipSpec
    {
        public float HullHalf = 78f;      // half-length of the hull
        public float DeckY    = 150f;     // deck line height on the canvas
        public float HullDepth = 30f;     // deck → keel
        public int   Masts = 1;
        public float SailScale = 1f;
        public bool  Ram;
        public bool  Oars;
        public int   Gunports;
        public bool  Cargo;
        public bool  SternCastle;
    }

    public static ShipSpec Galley    => new() { HullHalf = 80f, HullDepth = 22f, Masts = 1, SailScale = 0.85f, Ram = true, Oars = true };
    public static ShipSpec Frigate   => new() { HullHalf = 78f, HullDepth = 30f, Masts = 3, SailScale = 0.95f, Gunports = 5 };
    public static ShipSpec Transport => new() { HullHalf = 74f, HullDepth = 32f, Masts = 1, SailScale = 0.9f, Cargo = true };
    public static ShipSpec Galleon   => new() { HullHalf = 82f, HullDepth = 36f, Masts = 3, SailScale = 1.15f, SternCastle = true };

    public static void Draw(UnitPaintContext ctx, ShipSpec s)
    {
        float cx = 128f;
        var bowTip   = new Vector2(cx - s.HullHalf, s.DeckY - 8f);     // bow on the left
        var sternTip = new Vector2(cx + s.HullHalf, s.DeckY - 6f);
        float keelY  = s.DeckY + s.HullDepth;

        // Masts + sails first (behind the hull rim), back-to-front by x.
        for (int m = 0; m < s.Masts; m++)
        {
            float t  = s.Masts == 1 ? 0f : m / (float)(s.Masts - 1) - 0.5f;
            float mx = cx + t * s.HullHalf * 1.05f;
            float mastH = (110f - Mathf.Abs(t) * 26f) * s.SailScale;
            Mast(ctx, new Vector2(mx, s.DeckY), mastH, s.SailScale);
        }

        // Hull: deck sheer curve down to the keel.
        var hull = new[]
        {
            bowTip,
            new Vector2(cx - s.HullHalf * 0.5f, s.DeckY),
            new Vector2(cx + s.HullHalf * 0.55f, s.DeckY),
            sternTip,
            new Vector2(cx + s.HullHalf * 0.62f, keelY),
            new Vector2(cx - s.HullHalf * 0.55f, keelY),
        };
        Func<Vector2, float> hullSdf = p => Sdf.Polygon(p, hull);
        var hb = new Rect2(bowTip.X - 8f, s.DeckY - 20f, s.HullHalf * 2f + 16f, s.HullDepth + 28f);
        ctx.Painter.FillShaded(hullSdf, hb, MaterialRamps.Wood, 11f);
        // Plank lines.
        for (int i = 1; i <= 2; i++)
        {
            float py = s.DeckY + s.HullDepth * i / 3f;
            ctx.Painter.FillSdf(p => Sdf.Intersect(hullSdf(p) + 2f,
                                                   Mathf.Abs(p.Y - py) - 0.9f),
                                hb, new Color(0.30f, 0.20f, 0.11f, 0.55f));
        }
        // Rail along the deck edge.
        ctx.Painter.FillShaded(
            p => Sdf.Box(p, new Vector2(cx + 2f, s.DeckY - 3f), new Vector2(s.HullHalf * 0.72f, 2.6f), 2f),
            hb, MaterialRamps.Wood, 2.6f);

        if (s.SternCastle)
            ctx.Painter.FillShaded(
                p => Sdf.Box(p, new Vector2(cx + s.HullHalf * 0.62f, s.DeckY - 12f),
                             new Vector2(s.HullHalf * 0.22f, 10f), 3f),
                hb.Grow(16f), MaterialRamps.Wood, 6f);

        if (s.Ram)
            ctx.Painter.FillShaded(
                p => Sdf.Capsule(p, bowTip + new Vector2(2f, 4f), bowTip + new Vector2(-16f, 8f), 3.4f),
                new Rect2(bowTip.X - 24f, bowTip.Y - 6f, 36f, 22f),
                MaterialRamps.Bronze, 3.4f, specular: 0.6f);

        if (s.Oars)
            for (int i = 0; i < 6; i++)
            {
                float ox = cx - s.HullHalf * 0.55f + i * (s.HullHalf * 1.1f / 5f);
                var a = new Vector2(ox, s.DeckY + s.HullDepth * 0.45f);
                var b = a + new Vector2(-7f, 22f);
                ctx.Painter.FillShaded(p => Sdf.Capsule(p, a, b, 1.7f),
                                       HumanoidPainter.Bounds(a, b, 4f), MaterialRamps.Wood, 1.7f);
            }

        if (s.Gunports > 0)
            for (int i = 0; i < s.Gunports; i++)
            {
                float gx = cx - s.HullHalf * 0.5f + i * (s.HullHalf / (s.Gunports - 1));
                var gc = new Vector2(gx, s.DeckY + s.HullDepth * 0.42f);
                ctx.Painter.FillSdf(p => Sdf.Box(p, gc, new Vector2(3.4f, 3f), 1f),
                                    HumanoidPainter.Bounds(gc, gc, 6f),
                                    new Color(0.12f, 0.09f, 0.06f));
            }

        if (s.Cargo)
        {
            var crateRamp = MaterialRamps.Wood;
            foreach (var (dx, w) in new[] { (-26f, 11f), (-4f, 9f), (16f, 12f) })
            {
                var cc = new Vector2(cx + dx, s.DeckY - 9f);
                float cw = w;
                ctx.Painter.FillShaded(p => Sdf.Box(p, cc, new Vector2(cw, 8f), 1.5f),
                                       HumanoidPainter.Bounds(cc, cc, cw + 6f), crateRamp, 5f);
            }
        }

    }

    private static void Mast(UnitPaintContext ctx, Vector2 deck, float h, float sailScale)
    {
        var top = deck - new Vector2(0f, h);
        ctx.Painter.FillShaded(p => Sdf.Capsule(p, deck, top, 2.4f),
                               HumanoidPainter.Bounds(deck, top, 5f), MaterialRamps.Wood, 2.4f);
        // Yard + bulged square sail hanging from it.
        float yw = 34f * sailScale;
        var yardY = top + new Vector2(0f, 10f);
        ctx.Painter.FillShaded(
            p => Sdf.Capsule(p, yardY - new Vector2(yw, 0f), yardY + new Vector2(yw, 0f), 1.8f),
            HumanoidPainter.Bounds(yardY - new Vector2(yw, 4f), yardY + new Vector2(yw, 4f), 4f),
            MaterialRamps.Wood, 1.8f);
        // Billowed square sail: a well-rounded box reads as taut canvas at
        // sprite scale, and FillShaded's pillow normals give it the bulge.
        float drop = h * 0.52f;
        Func<Vector2, float> sailBox = p => Sdf.Box(
            p, yardY + new Vector2(0f, drop * 0.52f), new Vector2(yw * 0.95f, drop * 0.5f), 10f);
        var sb = HumanoidPainter.Bounds(yardY - new Vector2(yw * 1.3f, 4f),
                                        yardY + new Vector2(yw * 1.3f, drop + 8f), 6f);
        ctx.Painter.FillShaded(sailBox, sb, MaterialRamps.Sail, 13f, specular: 0.06f);
    }
}
