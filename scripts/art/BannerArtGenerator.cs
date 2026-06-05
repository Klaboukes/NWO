using System.Collections.Generic;
using Godot;

namespace NWO.Art;

// Procedural placeholder for the owner-colour banner shown beside full-colour unit /
// city art (Phase 7 follow-up; see docs/ART_ASSETS.md "team-colour banners").
//
// One shared 128px sprite, white fill + dark outline, drawn so Sprite3D.Modulate =
// owner.Color tints the whole pennant to that player's colour. A real PNG at
// res://assets/art/ui/banner.png overrides it with no code change (BannerTextureRegistry).
// A swallow-tailed pennant on a short pole — small and unobtrusive at the unit's base.
public static class BannerArtGenerator
{
    public const int TileSize = 128;

    private static readonly Color White   = Colors.White;
    private static readonly Color Outline = new(0.10f, 0.10f, 0.10f);
    private static readonly Color Transp  = new(0f, 0f, 0f, 0f);

    public static Image Generate()
    {
        var img = Image.CreateEmpty(TileSize, TileSize, false, Image.Format.Rgba8);
        img.Fill(Transp);

        // Pole (vertical staff).
        FillRect(img, 46, 28, 6, 74, White);

        // Flag body to the right of the pole.
        FillRect(img, 52, 32, 40, 30, White);

        // Swallow-tail notch: carve a triangle out of the flag's right edge.
        for (int dy = 0; dy < 30; dy++)
        {
            int cut = 14 - Mathf.Abs(15 - dy);          // widest at vertical centre
            if (cut <= 0) continue;
            for (int dx = 0; dx < cut; dx++) Plot(img, 91 - dx, 32 + dy, Transp);
        }

        ApplyOutline(img);
        return img;
    }

    // ── helpers (same style as UnitArtGenerator / CityArtGenerator) ──────────────

    private static void ApplyOutline(Image img)
    {
        var candidates = new List<(int x, int y)>();
        for (int y = 0; y < TileSize; y++)
        for (int x = 0; x < TileSize; x++)
        {
            if (img.GetPixel(x, y).A > 0.5f) continue;
            if (HasFilledNeighbour(img, x, y)) candidates.Add((x, y));
        }
        foreach (var (x, y) in candidates) img.SetPixel(x, y, Outline);
    }

    private static bool HasFilledNeighbour(Image img, int x, int y)
    {
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dy == 0) continue;
            int nx = x + dx, ny = y + dy;
            if (nx < 0 || nx >= TileSize || ny < 0 || ny >= TileSize) continue;
            if (img.GetPixel(nx, ny).A > 0.5f) return true;
        }
        return false;
    }

    private static void Plot(Image img, int x, int y, Color c)
    {
        if (x >= 0 && x < TileSize && y >= 0 && y < TileSize) img.SetPixel(x, y, c);
    }

    private static void FillRect(Image img, int x0, int y0, int w, int h, Color c)
    {
        for (int y = y0; y < y0 + h; y++)
        for (int x = x0; x < x0 + w; x++) Plot(img, x, y, c);
    }
}
