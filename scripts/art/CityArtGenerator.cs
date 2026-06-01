using System.Collections.Generic;
using Godot;

namespace NWO.Art;

// Procedural pixel-art generator for NWO city billboard sprites (Phase 7 V7.3).
//
// Same style as UnitArtGenerator and TerrainArtGenerator: 128px, RGBA8 transparent
// background, white fill + dark outline so Sprite3D.Modulate tints by owner colour,
// Bayer-4×4 shadow on the lower-right half for depth. SNES-strategy / Civ V vibe.
//
// Two variants: regular city (3-tower castle silhouette) and capital (same + crown).
public static class CityArtGenerator
{
    public const int TileSize = 128;

    private static readonly Color White   = Colors.White;
    private static readonly Color Shadow  = new(0.70f, 0.70f, 0.70f);
    private static readonly Color Outline = new(0.10f, 0.10f, 0.10f);
    private static readonly Color Transp  = new(0f, 0f, 0f, 0f);

    private static readonly float[,] Bayer4 =
    {
        {  0f/16f,  8f/16f,  2f/16f, 10f/16f },
        { 12f/16f,  4f/16f, 14f/16f,  6f/16f },
        {  3f/16f, 11f/16f,  1f/16f,  9f/16f },
        { 15f/16f,  7f/16f, 13f/16f,  5f/16f },
    };

    public static Image Generate(bool isCapital)
    {
        var img = Image.CreateEmpty(TileSize, TileSize, false, Image.Format.Rgba8);
        img.Fill(Transp);

        DrawCastle(img);
        if (isCapital) DrawCrown(img);
        ApplyShadowHalf(img, 64, 60);
        ApplyOutline(img);
        return img;
    }

    // ── Castle silhouette ───────────────────────────────────────────────────────

    // Three-tower castle: wide curtain wall, flanking towers, taller central keep.
    private static void DrawCastle(Image img)
    {
        // Curtain wall (wide horizontal base)
        FillRect(img, 14, 70, 100, 32, White);

        // Gate arch cut into base (removes pixels — draw Transp)
        for (int dy = -14; dy <= 0; dy++)
        for (int dx = -12; dx <= 12; dx++)
        {
            float ex = (float)dx / 12f, ey = (float)dy / 14f;
            if (ex * ex + ey * ey <= 1f) Plot(img, 64 + dx, 102 + dy, Transp);
        }

        // Flanking towers (slightly taller than wall)
        FillRect(img, 14, 46, 24, 26, White);   // left tower
        FillRect(img, 90, 46, 24, 26, White);   // right tower

        // Central keep (tallest)
        FillRect(img, 46, 28, 36, 44, White);

        // Crenellations on left tower (alternating merlons)
        Crenellate(img, 14, 46, 24, merlon: 5, gap: 4);
        // Crenellations on right tower
        Crenellate(img, 90, 46, 24, merlon: 5, gap: 4);
        // Crenellations on keep
        Crenellate(img, 46, 28, 36, merlon: 5, gap: 5);

        // Arrow-slit windows on keep
        for (int wy = 36; wy <= 56; wy += 14)
        {
            Plot(img, 64, wy,     Shadow);
            Plot(img, 64, wy + 1, Shadow);
            Plot(img, 64, wy + 2, Shadow);
        }
    }

    // Draw alternating merlons (raised) and gaps along the top of a rect.
    // x0 = left edge, topY = top of crenellation zone, width = rect width.
    private static void Crenellate(Image img, int x0, int topY, int width, int merlon, int gap)
    {
        int x = x0;
        bool onMerlon = true;
        while (x < x0 + width)
        {
            int blockW = onMerlon ? merlon : gap;
            if (onMerlon)
            {
                for (int bx = x; bx < x + blockW && bx < x0 + width; bx++)
                for (int by = topY - 6; by < topY; by++) Plot(img, bx, by, White);
            }
            else
            {
                // Gap: remove pixels (transparency already there)
                for (int bx = x; bx < x + blockW && bx < x0 + width; bx++)
                for (int by = topY - 6; by < topY; by++) Plot(img, bx, by, Transp);
            }
            x += blockW;
            onMerlon = !onMerlon;
        }
    }

    // ── Crown (capital only) ────────────────────────────────────────────────────

    // A simple five-point crown above the central keep.
    private static void DrawCrown(Image img)
    {
        const int baseY = 22;
        // Crown band
        FillRect(img, 40, baseY - 10, 48, 10, White);
        // Five points (triangles sticking up from the band)
        int[] pointsX = { 42, 50, 64, 78, 86 };
        foreach (int px in pointsX)
        {
            for (int t = 0; t < 9; t++)
            {
                int w = 3 - t * 3 / 9;
                for (int dx = -w; dx <= w; dx++) Plot(img, px + dx, baseY - 10 - t, White);
            }
        }
        // Gem dots on band
        foreach (int gx in new[] { 50, 64, 78 }) FilledDisc(img, gx, baseY - 5, 2, Shadow);
    }

    // ── Shadow + outline (same helpers as UnitArtGenerator) ─────────────────────

    private static void ApplyShadowHalf(Image img, int cx, int cy)
    {
        for (int y = 0; y < TileSize; y++)
        for (int x = 0; x < TileSize; x++)
        {
            if (img.GetPixel(x, y).A < 0.5f) continue;
            float bias = (x - cx) / 56f + (y - cy) / 56f;
            if (bias <= 0f) continue;
            float t = Mathf.Clamp(bias * 0.7f, 0f, 1f);
            if (t > Bayer4[x & 3, y & 3]) img.SetPixel(x, y, Shadow);
        }
    }

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

    // ── Drawing helpers ─────────────────────────────────────────────────────────

    private static void Plot(Image img, int x, int y, Color c)
    {
        if (x >= 0 && x < TileSize && y >= 0 && y < TileSize) img.SetPixel(x, y, c);
    }

    private static void FillRect(Image img, int x0, int y0, int w, int h, Color c)
    {
        for (int y = y0; y < y0 + h; y++)
        for (int x = x0; x < x0 + w; x++) Plot(img, x, y, c);
    }

    private static void FilledDisc(Image img, int cx, int cy, int r, Color c)
    {
        for (int dy = -r; dy <= r; dy++)
        for (int dx = -r; dx <= r; dx++)
            if (dx * dx + dy * dy <= r * r) Plot(img, cx + dx, cy + dy, c);
    }
}
