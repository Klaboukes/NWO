using System;
using Godot;
using NWO.Art.Icons;
using NWO.Art.Painterly;

namespace NWO.Art;

// Procedural painterly generator for HUD status icons (Phase 7 V7.4 / V7.5):
// 64px glossy full-colour icons in the same family as the resource set (shared
// IconFx finish). Not Modulate-tinted — they carry their own ramps: a gold coin
// for the treasury, a cyan flask for science.
//
// DETERMINISM  Generate(iconId) is pure — same id → same Image bytes. Unknown
// ids fall back to a neutral gold disc so new HUD icons never crash the UI.
public static class HudIconGenerator
{
    public const int IconSize = 64;

    private static readonly Vector2 C = new(32f, 32f);
    private static readonly Rect2 Full = new(2f, 2f, 60f, 60f);

    public static Image Generate(string iconId)
    {
        var canvas  = new Canvas(IconSize);
        var painter = new Painter(canvas);

        switch (iconId)
        {
            case "gold":    Coin(painter);  break;
            case "science": Flask(painter); break;
            default:
                painter.FillShaded(q => Sdf.Circle(q, C, 24f), Full,
                                   ColorRamp.Painterly(new Color(0.92f, 0.74f, 0.22f)), 16f,
                                   specular: 0.6f);
                break;
        }

        IconFx.Finish(canvas);
        return canvas.ToImage();
    }

    // Gold coin: domed disc with a stamped inner ring + diamond pip, hot sheen.
    private static void Coin(Painter p)
    {
        var gold = new Color(0.92f, 0.74f, 0.22f);
        p.FillShaded(q => Sdf.Circle(q, C, 26f), Full, ColorRamp.Painterly(gold, contrast: 1.2f),
                     16f, specular: 0.7f);
        // Stamped face: inner ring + diamond pip.
        p.StrokeSdf(q => Sdf.Circle(q, C, 19f), Full,
                    new Color(0.55f, 0.40f, 0.10f, 0.85f), 2.2f);
        Func<Vector2, float> pip = q => Sdf.Box(q, C, new Vector2(7.4f, 7.4f), 1.5f);
        p.FillShaded(q => pip(new Vector2(C.X + (q.X - C.X) * 0.7071f + (q.Y - C.Y) * 0.7071f,
                                          C.Y - (q.X - C.X) * 0.7071f + (q.Y - C.Y) * 0.7071f)),
                     Full, ColorRamp.Painterly(new Color(0.70f, 0.52f, 0.14f)), 4f, specular: 0.5f);
        // Sheen arc upper-left.
        p.FillSdf(q => Sdf.Subtract(Sdf.Circle(q, C + new Vector2(-7f, -7f), 14f),
                                    Sdf.Circle(q, C + new Vector2(-3f, -3f), 13f)),
                  Full, new Color(1f, 0.96f, 0.75f, 0.75f));
    }

    // Erlenmeyer flask: pale glass silhouette, cyan fluid in the body, cork,
    // rising bubbles, and a vertical glass glint.
    private static void Flask(Painter p)
    {
        var glassPts = new[]
        {
            C + new Vector2(-5f, -24f), C + new Vector2(5f, -24f),
            C + new Vector2(5f, -6f),   C + new Vector2(17f, 22f),
            C + new Vector2(-17f, 22f), C + new Vector2(-5f, -6f),
        };
        Func<Vector2, float> glass = q => Sdf.Polygon(q, glassPts);
        p.FillShaded(glass, Full, ColorRamp.Painterly(new Color(0.80f, 0.90f, 0.97f), contrast: 0.6f),
                     8f, specular: 0.5f);
        // Fluid: glass ∩ lower half-plane (inside below the surface line).
        float fluidY = C.Y + 6f;
        p.FillShaded(q => Sdf.Intersect(glass(q) + 2f, fluidY - q.Y),
                     Full, ColorRamp.Painterly(new Color(0.24f, 0.62f, 0.92f)), 6f, specular: 0.4f);
        // Bright surface line.
        p.FillSdf(q => Sdf.Capsule(q, new Vector2(C.X - 10f, fluidY), new Vector2(C.X + 10f, fluidY), 1.1f),
                  Full, new Color(0.62f, 0.88f, 1f, 0.9f));
        // Bubbles.
        foreach (var (bx, by, br) in new[] { (-4f, 12f, 1.6f), (3f, 9f, 1.2f), (-1f, 16f, 1.1f) })
            p.FillSdf(q => Sdf.Circle(q, C + new Vector2(bx, by), br), Full,
                      new Color(0.78f, 0.94f, 1f, 0.85f));
        // Cork.
        p.FillShaded(q => Sdf.Box(q, C + new Vector2(0f, -26f), new Vector2(6f, 4f), 1.5f),
                     Full, ColorRamp.Painterly(new Color(0.55f, 0.40f, 0.26f)), 3.5f);
        // Glass glint.
        p.FillSdf(q => Sdf.Capsule(q, C + new Vector2(-8f, 0f), C + new Vector2(-11f, 16f), 1.3f),
                  Full, new Color(1f, 1f, 1f, 0.55f));
    }
}
