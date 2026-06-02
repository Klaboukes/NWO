using Godot;
using NWO.Map;

namespace NWO.Art;

// Procedural pixel-art generator for map resource icons (Phase 9). Same family as
// HudIconGenerator / UnitArtGenerator: a transparent RGBA8 sprite with a dark 1-px
// outline, authored at 32px with chunky features so it stays crisp under
// nearest-neighbour filtering.
//
// Each resource draws a recognisable motif (fish, cow, sheep, wheat sheaf, gem, …)
// from the shared primitive toolkit below. A real
// res://assets/art/resources/<resource>.png drops in to override it with no code
// change (see ResourceIconRegistry / the add-art-asset skill).
//
// DETERMINISM  Generate(resource) is pure — same resource → same Image bytes.
public static class ResourceIconGenerator
{
    public const int IconSize = 32;

    private static readonly Color Transp  = new(0f, 0f, 0f, 0f);
    private static readonly Color Outline = new(0.10f, 0.10f, 0.12f);
    private static readonly Color Black   = new(0.14f, 0.13f, 0.16f);
    private static readonly Color White   = new(0.98f, 0.98f, 0.98f);

    public static Image Generate(ResourceType r)
    {
        var img = Image.CreateEmpty(IconSize, IconSize, false, Image.Format.Rgba8);
        img.Fill(Transp);

        switch (r)
        {
            case ResourceType.Fish:    Fish(img);    break;
            case ResourceType.Cattle:  Cattle(img);  break;
            case ResourceType.Sheep:   Sheep(img);   break;
            case ResourceType.Deer:    Deer(img);    break;
            case ResourceType.Wheat:   Wheat(img);   break;
            case ResourceType.Stone:   Stone(img);   break;
            case ResourceType.Banana:  Banana(img);  break;
            case ResourceType.Horses:  Horseshoe(img); break;
            case ResourceType.Iron:    Ore(img, new Color(0.38f, 0.43f, 0.55f)); break;
            case ResourceType.Gems:    Gem(img, new Color(0.22f, 0.80f, 0.72f)); break;
            case ResourceType.GoldOre: Nuggets(img, new Color(0.93f, 0.76f, 0.26f)); break;
            case ResourceType.Silver:  Nuggets(img, new Color(0.82f, 0.84f, 0.88f)); break;
            case ResourceType.Silk:    Spool(img, new Color(0.80f, 0.62f, 0.88f)); break;
            case ResourceType.Spices:  SpiceBowl(img); break;
            case ResourceType.Dyes:    DyePot(img, new Color(0.78f, 0.25f, 0.55f)); break;
            case ResourceType.Cotton:  Cotton(img);  break;
            case ResourceType.Incense: Incense(img); break;
            case ResourceType.Ivory:   Tusk(img);    break;
            case ResourceType.None:    return img;
        }

        ApplyOutline(img);
        return img;
    }

    // ── Bonus motifs ─────────────────────────────────────────────────────────────

    private static void Fish(Image img)
    {
        Color b = new(0.32f, 0.56f, 0.86f), belly = b.Lightened(0.30f), fin = b.Darkened(0.28f);
        Tri(img, 20, 16, 29, 9, 29, 23, b);              // tail fan
        Ellipse(img, 14, 16, 9, 6, b);                   // body
        Ellipse(img, 13, 18, 7, 3, belly);              // belly
        Tri(img, 9, 11, 17, 11, 13, 6, fin);             // dorsal fin
        Tri(img, 10, 21, 16, 21, 13, 25, fin);           // ventral fin
        Line(img, 9, 11, 9, 21, fin);                    // gill
        Disc(img, 8, 15, 2, White); Plot(img, 8, 15, Black); // eye
    }

    private static void Cattle(Image img)
    {
        Color hide = new(0.62f, 0.45f, 0.30f), cream = new(0.92f, 0.88f, 0.78f);
        Tri(img, 6, 13, 11, 13, 5, 6, cream);            // left horn
        Tri(img, 21, 13, 26, 13, 27, 6, cream);          // right horn
        Ellipse(img, 7, 17, 3, 2, hide);                 // ears
        Ellipse(img, 25, 17, 3, 2, hide);
        Ellipse(img, 16, 18, 8, 7, hide);                // head
        Ellipse(img, 16, 11, 5, 3, hide.Darkened(0.18f)); // forelock
        Ellipse(img, 16, 24, 6, 4, cream);               // snout
        Plot(img, 14, 25, Black); Plot(img, 18, 25, Black); // nostrils
        Disc(img, 12, 17, 1, Black); Disc(img, 20, 17, 1, Black); // eyes
    }

    private static void Sheep(Image img)
    {
        Color wool = new(0.93f, 0.92f, 0.88f), face = new(0.32f, 0.31f, 0.35f);
        Disc(img, 13, 11, 4, wool); Disc(img, 19, 11, 4, wool);
        Disc(img, 10, 16, 5, wool); Disc(img, 22, 16, 5, wool);
        Disc(img, 16, 14, 7, wool);                      // wool body
        VLine(img, 13, 25, 29, face); VLine(img, 19, 25, 29, face); // legs
        Ellipse(img, 11, 20, 2, 3, face); Ellipse(img, 21, 20, 2, 3, face); // ears
        Ellipse(img, 16, 21, 4, 4, face);                // face
        Plot(img, 14, 21, White); Plot(img, 18, 21, White); // eyes
    }

    private static void Deer(Image img)
    {
        Color hide = new(0.64f, 0.42f, 0.25f), horn = new(0.38f, 0.26f, 0.14f);
        // Antlers (branched).
        Line(img, 12, 14, 9, 4, horn); Line(img, 10, 9, 6, 6, horn); Line(img, 10, 6, 7, 3, horn);
        Line(img, 20, 14, 23, 4, horn); Line(img, 22, 9, 26, 6, horn); Line(img, 22, 6, 25, 3, horn);
        Ellipse(img, 9, 16, 2, 3, hide); Ellipse(img, 23, 16, 2, 3, hide); // ears
        Ellipse(img, 16, 20, 5, 7, hide);                // face
        Disc(img, 16, 26, 2, horn);                      // nose
        Disc(img, 13, 19, 1, Black); Disc(img, 19, 19, 1, Black); // eyes
    }

    private static void Wheat(Image img)
    {
        Color g = new(0.86f, 0.70f, 0.24f), gd = g.Darkened(0.30f);
        foreach (int x in new[] { 11, 16, 21 })
        {
            VLine(img, x, 12, 27, g);                    // stalk
            Ellipse(img, x, 9, 2, 5, g);                 // grain head
            for (int k = 0; k < 5; k++)
            {
                Line(img, x, 6 + 2 * k, x - 3, 7 + 2 * k, gd); // seeds
                Line(img, x, 6 + 2 * k, x + 3, 7 + 2 * k, gd);
            }
        }
        HLine(img, 9, 23, 23, gd); HLine(img, 9, 23, 24, gd); // tie band
    }

    private static void Stone(Image img)
    {
        Color s = new(0.60f, 0.60f, 0.58f);
        Disc(img, 11, 20, 6, s); Disc(img, 21, 21, 6, s); Disc(img, 16, 15, 5, s);
        Disc(img, 9, 18, 2, s.Lightened(0.30f));         // highlights
        Disc(img, 19, 19, 2, s.Lightened(0.30f));
        Disc(img, 15, 13, 2, s.Lightened(0.30f));
    }

    private static void Banana(Image img)
    {
        Color y = new(0.93f, 0.81f, 0.24f), yd = y.Darkened(0.30f);
        // Two crescents (fill a disc, carve an offset disc away).
        Disc(img, 13, 17, 10, y);  ClearDisc(img, 8, 24, 11);
        Disc(img, 17, 14, 8, y);   ClearDisc(img, 13, 20, 9);
        VLine(img, 21, 6, 9, yd);                        // stem
        Plot(img, 5, 14, yd); Plot(img, 24, 10, yd);     // tips
    }

    // ── Strategic motifs ─────────────────────────────────────────────────────────

    private static void Horseshoe(Image img)
    {
        Color m = new(0.62f, 0.64f, 0.68f);
        Disc(img, 16, 16, 9, m);
        ClearDisc(img, 16, 16, 5);                       // ring
        Rect(img, 11, 3, 21, 15, Transp);                // open the top → U
        foreach (var (x, yy) in new[] { (16, 24), (10, 21), (22, 21), (8, 16), (24, 16) })
            Plot(img, x, yy, Black);                     // nail holes
    }

    private static void Ore(Image img, Color c)
    {
        Disc(img, 12, 19, 6, c); Disc(img, 21, 18, 6, c); Disc(img, 16, 13, 5, c);
        foreach (var (x, yy) in new[] { (12, 17), (20, 17), (16, 12), (14, 21), (23, 20) })
            Plot(img, x, yy, c.Lightened(0.40f));        // metallic specks
        Disc(img, 11, 21, 2, c.Darkened(0.30f));
    }

    // ── Luxury motifs ──────────────────────────────────────────────────────────-─

    private static void Gem(Image img, Color c)
    {
        Color lit = c.Lightened(0.30f), dark = c.Darkened(0.28f);
        for (int y = 9; y <= 14; y++)                    // crown (table → girdle)
            HLine(img, 12 - (y - 9), 20 + (y - 9), y, y % 2 == 0 ? c : lit);
        Tri(img, 6, 14, 26, 14, 16, 27, c);              // pavilion
        Tri(img, 6, 14, 16, 14, 16, 27, lit);            // lit left facet
        Line(img, 12, 9, 16, 27, dark); Line(img, 20, 9, 16, 27, dark); // facet seams
        Plot(img, 13, 11, White); Plot(img, 14, 11, White); // sparkle
    }

    private static void Nuggets(Image img, Color c)
    {
        Disc(img, 12, 19, 6, c); Disc(img, 21, 19, 6, c); Disc(img, 16, 13, 5, c);
        foreach (var (x, yy) in new[] { (12, 18), (20, 18), (16, 12) })
            Star(img, x, yy, White);                     // sparkles
        Disc(img, 10, 21, 2, c.Lightened(0.30f));
    }

    private static void Spool(Image img, Color c)
    {
        Rect(img, 11, 9, 21, 23, c);                     // body
        for (int y = 11; y <= 21; y += 3) HLine(img, 11, 21, y, c.Lightened(0.30f)); // thread
        Ellipse(img, 16, 9, 7, 2, c.Darkened(0.22f));    // flanges
        Ellipse(img, 16, 23, 7, 2, c.Darkened(0.22f));
        Line(img, 21, 14, 26, 18, c.Lightened(0.25f));   // loose thread
        Line(img, 26, 18, 24, 22, c.Lightened(0.25f));
    }

    private static void SpiceBowl(Image img)
    {
        Color bowl = new(0.55f, 0.40f, 0.28f), spice = new(0.86f, 0.46f, 0.18f);
        Disc(img, 16, 18, 7, spice); Rect(img, 6, 20, 26, 27, Transp); // mound (top half)
        for (int y = 19; y <= 25; y++)                   // bowl (lower ellipse)
            for (int x = 7; x <= 25; x++)
                if (Inside(x, y, 16, 19, 9, 6)) Plot(img, x, y, bowl);
        Ellipse(img, 16, 19, 9, 2, bowl.Lightened(0.20f)); // rim
        foreach (var (x, yy) in new[] { (13, 15), (18, 14), (16, 17), (20, 16) })
            Plot(img, x, yy, spice.Darkened(0.30f));     // specks
    }

    private static void DyePot(Image img, Color c)
    {
        Rect(img, 10, 14, 22, 25, c);                    // pot
        Ellipse(img, 16, 14, 7, 2, c.Darkened(0.25f));   // rim
        VLine(img, 13, 16, 24, c.Lightened(0.30f));      // highlight
        Disc(img, 25, 21, 2, c); Disc(img, 26, 26, 2, c); // drip + drop
    }

    private static void Cotton(Image img)
    {
        Color w = new(0.96f, 0.96f, 0.93f), bract = new(0.45f, 0.32f, 0.18f);
        VLine(img, 16, 22, 29, bract);                   // stem
        Tri(img, 11, 21, 21, 21, 16, 27, bract);         // bracts (calyx)
        Disc(img, 11, 16, 4, w); Disc(img, 21, 16, 4, w); Disc(img, 16, 17, 5, w);
        Disc(img, 16, 12, 6, w);                         // bolls
        Plot(img, 14, 11, w.Darkened(0.10f));
    }

    private static void Incense(Image img)
    {
        Color pot = new(0.70f, 0.50f, 0.28f), smoke = new(0.82f, 0.84f, 0.88f);
        for (int y = 6; y <= 20; y++)                    // rising smoke
        {
            int x = 16 + Mathf.RoundToInt(3f * Mathf.Sin(y * 0.6f));
            Plot(img, x, y, smoke); Plot(img, x + 1, y, smoke);
        }
        Disc(img, 16, 22, 3, new Color(0.95f, 0.55f, 0.20f)); // ember
        Ellipse(img, 16, 25, 7, 3, pot);                 // bowl
        Ellipse(img, 16, 23, 7, 2, pot.Lightened(0.20f)); // rim
    }

    private static void Tusk(Image img)
    {
        Color iv = new(0.93f, 0.90f, 0.78f);
        Disc(img, 20, 20, 10, iv);
        ClearDisc(img, 26, 12, 12);                      // carve into a curved tusk
        Disc(img, 12, 26, 2, iv.Darkened(0.18f));        // thick base
    }

    // ── Primitive toolkit ────────────────────────────────────────────────────────

    private static bool Inside(int x, int y, int cx, int cy, float rx, float ry)
    {
        float nx = (x - cx) / rx, ny = (y - cy) / ry;
        return nx * nx + ny * ny <= 1f;
    }

    private static void Plot(Image img, int x, int y, Color c)
    {
        if (x >= 0 && x < IconSize && y >= 0 && y < IconSize) img.SetPixel(x, y, c);
    }

    private static void Disc(Image img, int cx, int cy, int r, Color c)
    {
        for (int dy = -r; dy <= r; dy++)
        for (int dx = -r; dx <= r; dx++)
            if (dx * dx + dy * dy <= r * r) Plot(img, cx + dx, cy + dy, c);
    }

    private static void ClearDisc(Image img, int cx, int cy, int r)
    {
        for (int dy = -r; dy <= r; dy++)
        for (int dx = -r; dx <= r; dx++)
            if (dx * dx + dy * dy <= r * r) Plot(img, cx + dx, cy + dy, Transp);
    }

    private static void Ellipse(Image img, int cx, int cy, int rx, int ry, Color c)
    {
        for (int dy = -ry; dy <= ry; dy++)
        for (int dx = -rx; dx <= rx; dx++)
            if (Inside(cx + dx, cy + dy, cx, cy, rx, ry)) Plot(img, cx + dx, cy + dy, c);
    }

    private static void Rect(Image img, int x0, int y0, int x1, int y1, Color c)
    {
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
            Plot(img, x, y, c);
    }

    private static void HLine(Image img, int x0, int x1, int y, Color c)
    {
        for (int x = x0; x <= x1; x++) Plot(img, x, y, c);
    }

    private static void VLine(Image img, int x, int y0, int y1, Color c)
    {
        for (int y = y0; y <= y1; y++) Plot(img, x, y, c);
    }

    private static void Line(Image img, int x0, int y0, int x1, int y1, Color c)
    {
        int dx = Mathf.Abs(x1 - x0), dy = -Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
        while (true)
        {
            Plot(img, x0, y0, c);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    private static void Tri(Image img, int ax, int ay, int bx, int by, int cx, int cy, Color col)
    {
        int minX = Mathf.Min(ax, Mathf.Min(bx, cx)), maxX = Mathf.Max(ax, Mathf.Max(bx, cx));
        int minY = Mathf.Min(ay, Mathf.Min(by, cy)), maxY = Mathf.Max(ay, Mathf.Max(by, cy));
        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++)
        {
            int d1 = (x - bx) * (ay - by) - (ax - bx) * (y - by);
            int d2 = (x - cx) * (by - cy) - (bx - cx) * (y - cy);
            int d3 = (x - ax) * (cy - ay) - (cx - ax) * (y - ay);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            if (!(neg && pos)) Plot(img, x, y, col);
        }
    }

    private static void Star(Image img, int cx, int cy, Color c)
    {
        Plot(img, cx, cy, c);
        Plot(img, cx - 1, cy, c); Plot(img, cx + 1, cy, c);
        Plot(img, cx, cy - 1, c); Plot(img, cx, cy + 1, c);
    }

    // ── Outline pass (mirrors HudIconGenerator) ─────────────────────────────────-─

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
}
