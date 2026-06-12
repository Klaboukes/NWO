using System;
using Godot;
using NWO.Art.Painterly;

namespace NWO.Art;

// Procedural owner-colour banner shown beside every unit / city sprite (painterly
// v2; see docs/ART_ASSETS.md "team-colour banners").
//
// One shared 256px sprite: a rippling swallow-tailed pennant on a dark pole. The
// cloth is painted in whites/greys (form shading only) so Sprite3D.Modulate =
// owner.Color dyes it cleanly to the player's colour; the pole is dark enough
// that the tint barely shifts it. A real PNG at res://assets/art/ui/banner.png
// overrides it with no code change (BannerTextureRegistry).
public static class BannerArtGenerator
{
    public const int TileSize = 256;

    public static Image Generate()
    {
        var canvas  = new Canvas(TileSize);
        var painter = new Painter(canvas);

        // Pole: dark wood with a small finial.
        var poleTop = new Vector2(96f, 52f);
        var poleBot = new Vector2(96f, 206f);
        painter.FillShaded(p => Sdf.Capsule(p, poleTop, poleBot, 5f),
                           new Rect2(84f, 40f, 26f, 180f),
                           ColorRamp.Painterly(new Color(0.22f, 0.16f, 0.11f)), 5f);
        painter.FillShaded(p => Sdf.Circle(p, poleTop - new Vector2(0f, 6f), 7f),
                           new Rect2(82f, 34f, 30f, 28f),
                           ColorRamp.Painterly(new Color(0.30f, 0.24f, 0.16f)), 5f,
                           specular: 0.4f);

        // Pennant: a rippling swallow-tail. Body = box with a wavy lower edge
        // (sine ripple carried by the SDF), notch carved from the fly end.
        var cloth = ColorRamp.Painterly(new Color(0.88f, 0.88f, 0.90f));
        Func<Vector2, float> flag = p =>
        {
            float ripple = Mathf.Sin(p.X * 0.085f) * 5f;
            var q = new Vector2(p.X, p.Y - ripple);
            float body = Sdf.Box(q, new Vector2(150f, 92f), new Vector2(50f, 31f), 5f);
            // Swallow-tail notch carved from the fly (right) end.
            float notchTri = Sdf.Triangle(q,
                new Vector2(168f, 92f), new Vector2(208f, 58f), new Vector2(208f, 126f));
            return Sdf.Subtract(body, notchTri);
        };
        var bounds = new Rect2(94f, 48f, 116f, 92f);
        painter.SoftShadow(flag, bounds, new Vector2(4f, 5f), 6f, 0.25f);
        painter.FillShaded(flag, bounds, cloth, 12f, specular: 0.08f);

        return canvas.ToImage();
    }
}
