using System;
using Godot;
using NWO.Art.Icons;
using NWO.Art.Painterly;
using NWO.Map;

namespace NWO.Art;

// Procedural painterly generator for map resource icons (Phase 9 / V7.5): 64px
// glossy full-colour motifs — shaded volumes, speculars, and the shared soft
// dark rim (IconFx) so they read on any terrain. One icon family, one hand.
//
// Each resource keeps its v1 motif (fish, cow head, wheat sheaf, gem, …). A real
// res://assets/art/resources/<resource>.png drops in to override with no code
// change (ResourceIconRegistry / the add-art-asset skill).
//
// DETERMINISM  Generate(resource) is pure — same resource → same Image bytes.
public static class ResourceIconGenerator
{
    public const int IconSize = 64;

    private static readonly Vector2 C = new(32f, 32f);

    public static Image Generate(ResourceType r)
    {
        var canvas  = new Canvas(IconSize);
        var painter = new Painter(canvas);

        switch (r)
        {
            case ResourceType.Fish:    Fish(painter);    break;
            case ResourceType.Cattle:  Cattle(painter);  break;
            case ResourceType.Sheep:   Sheep(painter);   break;
            case ResourceType.Deer:    Deer(painter);    break;
            case ResourceType.Wheat:   Wheat(painter);   break;
            case ResourceType.Stone:   Stone(painter);   break;
            case ResourceType.Banana:  Banana(painter);  break;
            case ResourceType.Horses:  Horseshoe(painter); break;
            case ResourceType.Iron:    Ore(painter, new Color(0.38f, 0.43f, 0.55f)); break;
            case ResourceType.Gems:    Gem(painter, new Color(0.20f, 0.78f, 0.72f)); break;
            case ResourceType.GoldOre: Nuggets(painter, new Color(0.93f, 0.76f, 0.26f)); break;
            case ResourceType.Silver:  Nuggets(painter, new Color(0.80f, 0.83f, 0.88f)); break;
            case ResourceType.Silk:    Spool(painter);   break;
            case ResourceType.Spices:  Spices(painter);  break;
            case ResourceType.Dyes:    DyePot(painter);  break;
            case ResourceType.Cotton:  Cotton(painter);  break;
            case ResourceType.Incense: Incense(painter); break;
            case ResourceType.Ivory:   Tusks(painter);   break;
            case ResourceType.None:    return canvas.ToImage();
        }

        IconFx.Finish(canvas);
        return canvas.ToImage();
    }

    private static Rect2 Full => new(2f, 2f, 60f, 60f);

    private static void Shaded(Painter p, Func<Vector2, float> sdf, Color baseCol,
                               float inflate, float spec = 0.2f)
        => p.FillShaded(sdf, Full, ColorRamp.Painterly(baseCol), inflate, specular: spec);

    // ── Bonus motifs ────────────────────────────────────────────────────────────

    private static void Fish(Painter p)
    {
        var body = C + new Vector2(-3f, 2f);
        Shaded(p, q => Sdf.Ellipse(q, body, new Vector2(17f, 9.5f)),
               new Color(0.36f, 0.58f, 0.66f), 8f, 0.45f);
        // Tail fin.
        Shaded(p, q => Sdf.Triangle(q, body + new Vector2(13f, 0f),
                                    body + new Vector2(25f, -9f), body + new Vector2(25f, 9f)),
               new Color(0.30f, 0.50f, 0.58f), 4f, 0.3f);
        // Eye.
        p.FillSdf(q => Sdf.Circle(q, body + new Vector2(-10f, -2.5f), 2.6f), Full, Colors.White);
        p.FillSdf(q => Sdf.Circle(q, body + new Vector2(-10f, -2.5f), 1.3f), Full,
                  new Color(0.12f, 0.12f, 0.14f));
    }

    private static void Cattle(Painter p)
    {
        // Horns behind the head.
        var horn = new Color(0.90f, 0.86f, 0.74f);
        Shaded(p, q => Sdf.Capsule(q, C + new Vector2(-9f, -8f), C + new Vector2(-19f, -16f), 3.2f), horn, 3f, 0.3f);
        Shaded(p, q => Sdf.Capsule(q, C + new Vector2(9f, -8f), C + new Vector2(19f, -16f), 3.2f), horn, 3f, 0.3f);
        // Head + muzzle.
        Shaded(p, q => Sdf.Ellipse(q, C + new Vector2(0f, -2f), new Vector2(13f, 12f)),
               new Color(0.48f, 0.31f, 0.20f), 9f);
        Shaded(p, q => Sdf.Ellipse(q, C + new Vector2(0f, 9f), new Vector2(9f, 6f)),
               new Color(0.78f, 0.62f, 0.50f), 5f);
        // Nostrils + eyes.
        p.FillSdf(q => Sdf.Circle(q, C + new Vector2(-3.4f, 9f), 1.2f), Full, new Color(0.16f, 0.10f, 0.08f));
        p.FillSdf(q => Sdf.Circle(q, C + new Vector2(3.4f, 9f), 1.2f), Full, new Color(0.16f, 0.10f, 0.08f));
        p.FillSdf(q => Sdf.Circle(q, C + new Vector2(-5.5f, -5f), 1.5f), Full, new Color(0.12f, 0.10f, 0.10f));
        p.FillSdf(q => Sdf.Circle(q, C + new Vector2(5.5f, -5f), 1.5f), Full, new Color(0.12f, 0.10f, 0.10f));
    }

    private static void Sheep(Painter p)
    {
        // Wool: smooth-unioned puffs.
        Func<Vector2, float> wool = q =>
        {
            float d = Sdf.Circle(q, C + new Vector2(0f, -1f), 12f);
            d = Sdf.SmoothUnion(d, Sdf.Circle(q, C + new Vector2(-10f, 2f), 8f), 6f);
            d = Sdf.SmoothUnion(d, Sdf.Circle(q, C + new Vector2(10f, 2f), 8f), 6f);
            d = Sdf.SmoothUnion(d, Sdf.Circle(q, C + new Vector2(0f, -10f), 7f), 6f);
            return d;
        };
        Shaded(p, wool, new Color(0.92f, 0.90f, 0.84f), 9f, 0.1f);
        // Dark head poking out the left, with an ear.
        Shaded(p, q => Sdf.Ellipse(q, C + new Vector2(-14f, -6f), new Vector2(6.5f, 5.5f)),
               new Color(0.22f, 0.18f, 0.16f), 4f);
        p.FillSdf(q => Sdf.Circle(q, C + new Vector2(-16f, -7.5f), 1.2f), Full, Colors.White);
    }

    private static void Deer(Painter p)
    {
        // Antlers: branching capsules.
        var antler = new Color(0.82f, 0.74f, 0.58f);
        foreach (float s in new[] { -1f, 1f })
        {
            var root = C + new Vector2(s * 5f, -8f);
            var mid  = root + new Vector2(s * 7f, -10f);
            Shaded(p, q => Sdf.Capsule(q, root, mid, 2f), antler, 2f, 0.2f);
            Shaded(p, q => Sdf.Capsule(q, mid, mid + new Vector2(s * 6f, -4f), 1.7f), antler, 2f, 0.2f);
            Shaded(p, q => Sdf.Capsule(q, root + (mid - root) * 0.45f,
                                       root + (mid - root) * 0.45f + new Vector2(s * 2f, -7f), 1.7f),
                   antler, 2f, 0.2f);
        }
        // Head.
        Shaded(p, q => Sdf.Ellipse(q, C + new Vector2(0f, 4f), new Vector2(9f, 12f)),
               new Color(0.62f, 0.44f, 0.28f), 7f);
        Shaded(p, q => Sdf.Ellipse(q, C + new Vector2(0f, 13f), new Vector2(5f, 4f)),
               new Color(0.45f, 0.30f, 0.20f), 3f);
        p.FillSdf(q => Sdf.Circle(q, C + new Vector2(-4f, 1f), 1.4f), Full, new Color(0.12f, 0.10f, 0.10f));
        p.FillSdf(q => Sdf.Circle(q, C + new Vector2(4f, 1f), 1.4f), Full, new Color(0.12f, 0.10f, 0.10f));
    }

    private static void Wheat(Painter p)
    {
        var stalkCol = new Color(0.80f, 0.62f, 0.22f);
        var grainCol = new Color(0.94f, 0.78f, 0.34f);
        foreach (var (dx, lean) in new[] { (-9f, -0.18f), (0f, 0f), (9f, 0.18f) })
        {
            var baseP = C + new Vector2(dx, 22f);
            var top   = C + new Vector2(dx + lean * 30f, -16f);
            Shaded(p, q => Sdf.Capsule(q, baseP, top, 1.6f), stalkCol, 1.6f);
            // Grains: paired ellipses up the head.
            for (int g = 0; g < 4; g++)
            {
                var gp = top.Lerp(baseP, g * 0.09f);
                Shaded(p, q => Sdf.Ellipse(q, gp + new Vector2(-3f, g * 1.2f), new Vector2(3.2f, 2.2f)), grainCol, 2.4f, 0.3f);
                Shaded(p, q => Sdf.Ellipse(q, gp + new Vector2(3f, g * 1.2f), new Vector2(3.2f, 2.2f)), grainCol, 2.4f, 0.3f);
            }
        }
    }

    private static void Stone(Painter p)
    {
        Shaded(p, q => Sdf.Box(q, C + new Vector2(0f, 8f), new Vector2(16f, 8f), 2f),
               new Color(0.58f, 0.56f, 0.52f), 6f, 0.25f);
        Shaded(p, q => Sdf.Box(q, C + new Vector2(-5f, -7f), new Vector2(10f, 7f), 2f),
               new Color(0.64f, 0.62f, 0.58f), 5f, 0.25f);
        Shaded(p, q => Sdf.Box(q, C + new Vector2(11f, -5f), new Vector2(6f, 5f), 1.5f),
               new Color(0.52f, 0.50f, 0.47f), 4f, 0.25f);
    }

    private static void Banana(Painter p)
    {
        // A slim diagonal crescent — stem high-left, tip low-right, belly bowed
        // out to the lower-left.
        var a = C + new Vector2(-6f, -17f);
        var b = C + new Vector2(15f, 9f);
        var ctrl = C + new Vector2(-18f, 14f);
        Shaded(p, q => Sdf.QuadBezier(q, a, ctrl, b) - 4.2f,
               new Color(0.95f, 0.82f, 0.25f), 4.2f, 0.35f);
        // Stem + tip.
        p.FillSdf(q => Sdf.Circle(q, a, 2.2f), Full, new Color(0.42f, 0.30f, 0.14f));
        p.FillSdf(q => Sdf.Circle(q, b, 1.8f), Full, new Color(0.36f, 0.26f, 0.12f));
    }

    // ── Strategic motifs ────────────────────────────────────────────────────────

    private static void Horseshoe(Painter p)
    {
        var steel = new Color(0.62f, 0.66f, 0.72f);
        // U: ring minus inner disc, opened at the top.
        Func<Vector2, float> shoe = q =>
        {
            float ring = Sdf.Subtract(Sdf.Circle(q, C, 16f), Sdf.Circle(q, C, 9f));
            return Sdf.Subtract(ring, Sdf.Box(q, C + new Vector2(0f, -14f), new Vector2(7f, 8f)));
        };
        Shaded(p, shoe, steel, 4f, 0.55f);
        // Nail studs.
        foreach (float a in new[] { 2.6f, 2.0f, 1.2f, 0.5f, -0.1f })
        {
            var sp = C + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 12.5f;
            p.FillSdf(q => Sdf.Circle(q, sp, 1.3f), Full, new Color(0.30f, 0.32f, 0.36f));
        }
    }

    private static void Ore(Painter p, Color col)
    {
        var pts = new[]
        {
            C + new Vector2(-15f, 6f), C + new Vector2(-7f, -12f), C + new Vector2(6f, -14f),
            C + new Vector2(16f, -2f), C + new Vector2(11f, 12f), C + new Vector2(-4f, 14f),
        };
        Shaded(p, q => Sdf.Polygon(q, pts), col, 8f, 0.5f);
        // Glinting facet.
        Shaded(p, q => Sdf.Triangle(q, C + new Vector2(-6f, -10f), C + new Vector2(4f, -12f),
                                    C + new Vector2(-1f, -2f)),
               ColorRamp.ShiftHsv(col, 0f, -0.05f, +0.45f), 3f, 0.7f);
    }

    // ── Luxury motifs ───────────────────────────────────────────────────────────

    private static void Gem(Painter p, Color col)
    {
        var crown = new[]
        {
            C + new Vector2(-16f, -4f), C + new Vector2(-8f, -13f),
            C + new Vector2(8f, -13f),  C + new Vector2(16f, -4f),
            C + new Vector2(0f, 17f),
        };
        Shaded(p, q => Sdf.Polygon(q, crown), col, 7f, 0.8f);
        // Table facet, brighter.
        Shaded(p, q => Sdf.Polygon(q, new[]
               {
                   C + new Vector2(-8f, -13f), C + new Vector2(8f, -13f),
                   C + new Vector2(5f, -5f),   C + new Vector2(-5f, -5f),
               }),
               ColorRamp.ShiftHsv(col, 0f, -0.18f, +0.55f), 3f, 0.9f);
        // Sparkle.
        p.FillSdf(q => Sdf.Circle(q, C + new Vector2(-6f, -9f), 1.6f), Full, Colors.White);
    }

    private static void Nuggets(Painter p, Color col)
    {
        Shaded(p, q => Sdf.Circle(q, C + new Vector2(-8f, 7f), 9f), col, 7f, 0.65f);
        Shaded(p, q => Sdf.Circle(q, C + new Vector2(9f, 9f), 7f), col, 5f, 0.65f);
        Shaded(p, q => Sdf.Circle(q, C + new Vector2(2f, -7f), 8f),
               ColorRamp.ShiftHsv(col, 0f, 0f, +0.12f), 6f, 0.65f);
        p.FillSdf(q => Sdf.Circle(q, C + new Vector2(-1f, -10f), 1.7f), Full, Colors.White);
    }

    private static void Spool(Painter p)
    {
        var silk = new Color(0.74f, 0.55f, 0.86f);
        // Thread body.
        Shaded(p, q => Sdf.Box(q, C, new Vector2(11f, 12f), 4f), silk, 8f, 0.5f);
        // Thread wraps.
        for (int i = -2; i <= 2; i++)
        {
            float yy = C.Y + i * 4.6f;
            p.FillSdf(q => Sdf.Capsule(q, new Vector2(C.X - 10f, yy + 1.5f),
                                       new Vector2(C.X + 10f, yy - 1.5f), 0.9f),
                      Full, ColorRamp.ShiftHsv(silk, 0f, +0.08f, -0.22f));
        }
        // Spool ends.
        Shaded(p, q => Sdf.Box(q, C + new Vector2(0f, -14f), new Vector2(14f, 3f), 2f), MaterialWood(), 3f);
        Shaded(p, q => Sdf.Box(q, C + new Vector2(0f, 14f), new Vector2(14f, 3f), 2f), MaterialWood(), 3f);
    }

    private static void Spices(Painter p)
    {
        // Bowl.
        Shaded(p, q => Sdf.Intersect(Sdf.Circle(q, C + new Vector2(0f, 4f), 15f), q.Y - (C.Y + 4f)),
               new Color(0.46f, 0.32f, 0.22f), 6f);
        // Heaped spice mound.
        Shaded(p, q => Sdf.Intersect(Sdf.Circle(q, C + new Vector2(0f, 6f), 12.5f), (C.Y + 5f) - q.Y),
               new Color(0.82f, 0.38f, 0.12f), 7f, 0.15f);
        // Scattered grains.
        foreach (var (gx, gy) in new[] { (-6f, -4f), (1f, -7f), (7f, -3f), (-2f, -1f) })
            p.FillSdf(q => Sdf.Circle(q, C + new Vector2(gx, gy), 1.4f), Full,
                      new Color(0.95f, 0.62f, 0.20f));
    }

    private static void DyePot(Painter p)
    {
        var dye = new Color(0.66f, 0.20f, 0.52f);
        // Pot body.
        Shaded(p, q => Sdf.Ellipse(q, C + new Vector2(0f, 6f), new Vector2(13f, 11f)),
               new Color(0.55f, 0.42f, 0.32f), 9f, 0.25f);
        // Dye pooling at the rim + a drip down the side.
        Shaded(p, q => Sdf.Ellipse(q, C + new Vector2(0f, -4f), new Vector2(10.5f, 4.5f)), dye, 4f, 0.55f);
        Shaded(p, q => Sdf.Capsule(q, C + new Vector2(7f, -3f), C + new Vector2(9f, 7f), 2f), dye, 2f, 0.4f);
        p.FillSdf(q => Sdf.Circle(q, C + new Vector2(-4f, -5f), 1.5f), Full,
                  new Color(0.92f, 0.70f, 0.92f));
    }

    private static void Cotton(Painter p)
    {
        // Stem + leaf first.
        Shaded(p, q => Sdf.Capsule(q, C + new Vector2(0f, 6f), C + new Vector2(2f, 22f), 1.6f),
               new Color(0.36f, 0.44f, 0.24f), 1.6f);
        Shaded(p, q => Sdf.Ellipse(q, C + new Vector2(-6f, 16f), new Vector2(6f, 3f)),
               new Color(0.40f, 0.50f, 0.28f), 2.5f);
        // Fluffy boll.
        Func<Vector2, float> boll = q =>
        {
            float d = Sdf.Circle(q, C + new Vector2(0f, -6f), 9f);
            d = Sdf.SmoothUnion(d, Sdf.Circle(q, C + new Vector2(-9f, -2f), 7f), 5f);
            d = Sdf.SmoothUnion(d, Sdf.Circle(q, C + new Vector2(9f, -2f), 7f), 5f);
            return d;
        };
        Shaded(p, boll, new Color(0.96f, 0.95f, 0.92f), 7f, 0.1f);
    }

    private static void Incense(Painter p)
    {
        // Smoke wisp.
        var smoke = new Color(0.80f, 0.82f, 0.88f, 0.7f);
        p.FillSdf(q => Sdf.QuadBezier(q, C + new Vector2(2f, -22f), C + new Vector2(-8f, -12f),
                                      C + new Vector2(2f, -2f)) - 1.8f, Full, smoke);
        // Burner: dome + base with cut vents.
        Shaded(p, q => Sdf.Intersect(Sdf.Circle(q, C + new Vector2(0f, 8f), 13f), q.Y - (C.Y - 2f)),
               MaterialGold(), 8f, 0.6f);
        Shaded(p, q => Sdf.Intersect(Sdf.Circle(q, C + new Vector2(0f, 4f), 11f), (C.Y + 2f) - q.Y),
               MaterialGold(), 6f, 0.6f);
        Shaded(p, q => Sdf.Box(q, C + new Vector2(0f, 20f), new Vector2(9f, 2.5f), 2f),
               new Color(0.55f, 0.40f, 0.18f), 2.5f, 0.4f);
        p.FillSdf(q => Sdf.Circle(q, C + new Vector2(0f, 0f), 1.6f), Full, new Color(0.30f, 0.18f, 0.10f));
    }

    private static void Tusks(Painter p)
    {
        var ivory = new Color(0.94f, 0.90f, 0.80f);
        Shaded(p, q => Sdf.QuadBezier(q, C + new Vector2(-14f, 16f), C + new Vector2(-16f, -8f),
                                      C + new Vector2(2f, -16f)) - 3.6f, ivory, 3.6f, 0.35f);
        Shaded(p, q => Sdf.QuadBezier(q, C + new Vector2(14f, 16f), C + new Vector2(16f, -8f),
                                      C + new Vector2(-2f, -16f)) - 3.6f,
               ColorRamp.ShiftHsv(ivory, 0f, 0f, -0.08f), 3.6f, 0.35f);
    }

    private static Color MaterialWood() => new(0.45f, 0.30f, 0.17f);
    private static Color MaterialGold() => new(0.86f, 0.66f, 0.24f);
}
