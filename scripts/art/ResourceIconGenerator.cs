using Godot;
using NWO.Map;

namespace NWO.Art;

// Procedural pixel-art generator for map resource icons (Phase 9). Same family as
// HudIconGenerator / UnitArtGenerator: a transparent RGBA8 sprite with a dark 1-px
// outline, authored at the display size (32px) with chunky features so it stays crisp
// under nearest-neighbour filtering.
//
// The SHAPE encodes the resource tier (bonus = round token, strategic = ingot bar,
// luxury = faceted gem) and the COLOUR is per-resource, so every resource has a
// distinct placeholder. A real res://assets/art/resources/<resource>.png drops in to
// override it with no code change (see ResourceIconRegistry / the add-art-asset skill).
//
// DETERMINISM  Generate(resource) is pure — same resource → same Image bytes.
public static class ResourceIconGenerator
{
    public const int IconSize = 32;

    private static readonly Color Transp  = new(0f, 0f, 0f, 0f);
    private static readonly Color Outline = new(0.10f, 0.10f, 0.12f);

    public static Image Generate(ResourceType r)
    {
        var img = Image.CreateEmpty(IconSize, IconSize, false, Image.Format.Rgba8);
        img.Fill(Transp);
        if (r == ResourceType.None) return img;

        Color baseC = BaseColor(r);
        switch (ResourceYields.Tier(r))
        {
            case ResourceTier.Luxury:    DrawGem(img, baseC);   break;
            case ResourceTier.Strategic: DrawIngot(img, baseC); break;
            default:                     DrawToken(img, baseC); break;
        }

        ApplyOutline(img);
        return img;
    }

    // Per-resource base hue. Shape (above) carries the tier; this carries identity.
    private static Color BaseColor(ResourceType r) => r switch
    {
        // Strategic
        ResourceType.Horses  => new Color(0.55f, 0.40f, 0.25f),
        ResourceType.Iron    => new Color(0.55f, 0.57f, 0.62f),
        // Bonus
        ResourceType.Wheat   => new Color(0.85f, 0.72f, 0.28f),
        ResourceType.Fish    => new Color(0.55f, 0.70f, 0.85f),
        ResourceType.Cattle  => new Color(0.70f, 0.50f, 0.35f),
        ResourceType.Sheep   => new Color(0.88f, 0.86f, 0.80f),
        ResourceType.Deer    => new Color(0.65f, 0.40f, 0.25f),
        ResourceType.Stone   => new Color(0.60f, 0.60f, 0.58f),
        ResourceType.Banana  => new Color(0.90f, 0.80f, 0.25f),
        // Luxury
        ResourceType.Gems    => new Color(0.20f, 0.80f, 0.70f),
        ResourceType.GoldOre => new Color(0.95f, 0.80f, 0.25f),
        ResourceType.Silver  => new Color(0.80f, 0.82f, 0.85f),
        ResourceType.Silk    => new Color(0.80f, 0.65f, 0.85f),
        ResourceType.Spices  => new Color(0.85f, 0.45f, 0.20f),
        ResourceType.Dyes    => new Color(0.75f, 0.25f, 0.55f),
        ResourceType.Cotton  => new Color(0.92f, 0.92f, 0.88f),
        ResourceType.Incense => new Color(0.80f, 0.55f, 0.30f),
        ResourceType.Ivory   => new Color(0.92f, 0.88f, 0.75f),
        _                    => new Color(0.70f, 0.70f, 0.70f),
    };

    // ── Tier shapes ──────────────────────────────────────────────────────────────

    // Bonus: a round token, sun-side sheen + shaded base.
    private static void DrawToken(Image img, Color c)
    {
        const int cx = 16, cy = 16, rad = 11;
        FilledDisc(img, cx, cy, rad, c);
        FilledDisc(img, cx - 3, cy - 3, 4, c.Lightened(0.35f)); // sheen
        ArcShade(img, cx, cy, rad, c.Darkened(0.30f));          // shaded lower rim
    }

    // Strategic: a stubby ingot bar (trapezoid), lit top face + dark base.
    private static void DrawIngot(Image img, Color c)
    {
        Color top = c.Lightened(0.25f), bot = c.Darkened(0.30f);
        for (int y = 11; y <= 21; y++)
        {
            int inset = Mathf.Abs(y - 16) <= 2 ? 0 : (Mathf.Abs(y - 16) - 2);
            int x0 = 5 + inset, x1 = 26 - inset;
            for (int x = x0; x <= x1; x++)
                Plot(img, x, y, y <= 15 ? top : y >= 18 ? bot : c);
        }
    }

    // Luxury: a faceted gem (diamond), bright left facet + dark right + top sparkle.
    private static void DrawGem(Image img, Color c)
    {
        const int cx = 16, cy = 16, rad = 11;
        Color litF = c.Lightened(0.30f), darkF = c.Darkened(0.28f);
        for (int dy = -rad; dy <= rad; dy++)
        for (int dx = -rad; dx <= rad; dx++)
        {
            if (Mathf.Abs(dx) + Mathf.Abs(dy) > rad) continue; // diamond
            Color f = dx + dy < -2 ? litF : dx + dy > 3 ? darkF : c;
            Plot(img, cx + dx, cy + dy, f);
        }
        Plot(img, cx - 2, cy - 5, Colors.White); // top sparkle
        Plot(img, cx - 1, cy - 5, Colors.White);
    }

    private static void ArcShade(Image img, int cx, int cy, int rad, Color c)
    {
        for (int dy = 1; dy <= rad; dy++)
        for (int dx = -rad; dx <= rad; dx++)
        {
            int d2 = dx * dx + dy * dy;
            if (d2 <= rad * rad && d2 > (rad - 2) * (rad - 2)) Plot(img, cx + dx, cy + dy, c);
        }
    }

    // ── Outline + drawing helpers (mirror HudIconGenerator) ──────────────────────

    private static void ApplyOutline(Image img)
    {
        var pts = new System.Collections.Generic.List<(int x, int y)>();
        for (int y = 0; y < IconSize; y++)
        for (int x = 0; x < IconSize; x++)
        {
            if (img.GetPixel(x, y).A > 0.5f) continue;
            if (HasFilledNeighbour(img, x, y)) pts.Add((x, y));
        }
        foreach (var (x, y) in pts) img.SetPixel(x, y, Outline);
    }

    private static bool HasFilledNeighbour(Image img, int x, int y)
    {
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dy == 0) continue;
            int nx = x + dx, ny = y + dy;
            if (nx < 0 || nx >= IconSize || ny < 0 || ny >= IconSize) continue;
            if (img.GetPixel(nx, ny).A > 0.5f) return true;
        }
        return false;
    }

    private static void Plot(Image img, int x, int y, Color c)
    {
        if (x >= 0 && x < IconSize && y >= 0 && y < IconSize) img.SetPixel(x, y, c);
    }

    private static void FilledDisc(Image img, int cx, int cy, int r, Color c)
    {
        for (int dy = -r; dy <= r; dy++)
        for (int dx = -r; dx <= r; dx++)
            if (dx * dx + dy * dy <= r * r) Plot(img, cx + dx, cy + dy, c);
    }
}
