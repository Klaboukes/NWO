using System.Collections.Generic;
using Godot;

namespace NWO.Art;

// Procedural pixel-art generator for NWO unit billboard sprites (Phase 7 V7.3).
//
// STYLE  Same family as TerrainArtGenerator: 128px SNES-strategy / Civ V pixel art,
// Bayer-4×4 ordered dither for shadow depth, dark 1-px outline + white fill so
// Sprite3D.Modulate = owner.Color tints the whole sprite. RGBA8 transparent background.
//
// PIPELINE  per unit type:
//   1. Transparent fill.
//   2. White-filled silhouette for the unit type (unique shape per id).
//   3. Bayer-dithered grey shadow on the lower-right half for depth.
//   4. 1-px dark outline traced around the silhouette.
//
// DETERMINISM  Generate(unitId) is pure — same input → same Image bytes.
// Unknown ids fall back to a plain disc so new content never crashes the renderer.
public static class UnitArtGenerator
{
    public const int TileSize = 128;

    private static readonly Color White   = Colors.White;
    private static readonly Color Shadow  = new(0.70f, 0.70f, 0.70f);
    private static readonly Color Outline = new(0.10f, 0.10f, 0.10f);
    private static readonly Color Transp  = new(0f, 0f, 0f, 0f);

    // Same Bayer-4×4 matrix used by TerrainArtGenerator.
    private static readonly float[,] Bayer4 =
    {
        {  0f/16f,  8f/16f,  2f/16f, 10f/16f },
        { 12f/16f,  4f/16f, 14f/16f,  6f/16f },
        {  3f/16f, 11f/16f,  1f/16f,  9f/16f },
        { 15f/16f,  7f/16f, 13f/16f,  5f/16f },
    };

    public static Image Generate(string unitId)
    {
        var img = Image.CreateEmpty(TileSize, TileSize, false, Image.Format.Rgba8);
        img.Fill(Transp);

        switch (unitId)
        {
            case "scout":     DrawScout(img);      break;
            case "warrior":   DrawWarrior(img);   break;
            case "archer":    DrawArcher(img);     break;
            case "spearman":  DrawSpearman(img);   break;
            case "horseman":  DrawHorseman(img);   break;
            case "swordsman": DrawSwordsman(img);  break;
            case "catapult":  DrawCatapult(img);   break;
            case "settler":   DrawSettler(img);    break;
            case "worker":    DrawWorker(img);     break;
            default:          DrawDisc(img);       break;
        }

        ApplyOutline(img);
        return img;
    }

    // ── Silhouettes ────────────────────────────────────────────────────────────

    // Spyglass / telescope held on the lower-left → upper-right diagonal: a tapered
    // tube (narrow eyepiece → wide objective lens) with two collar bands and a round
    // lens cap. Reads instantly as "explorer / scout".
    private static void DrawScout(Image img)
    {
        const int cx = 64, cy = 64;
        const int steps = 84;
        float sx = cx - 38, sy = cy + 34;       // eyepiece end (lower-left)
        const float dx =  0.7071f, dy = -0.7071f; // up-right axis (unit length)
        const float px =  0.7071f, py =  0.7071f; // perpendicular (down-right)

        // Tapered tube: half-width grows from 4 (eyepiece) to 12 (objective).
        for (int t = 0; t <= steps; t++)
        {
            float ax = sx + dx * t, ay = sy + dy * t;
            int   hw = 4 + t * 8 / steps;
            for (int w = -hw; w <= hw; w++)
                Plot(img, Mathf.RoundToInt(ax + px * w), Mathf.RoundToInt(ay + py * w), White);
        }

        // Two collar bands (slightly proud of the tube) for a segmented read.
        foreach (float rf in new[] { 0.34f, 0.64f })
        {
            float ax = sx + dx * (rf * steps), ay = sy + dy * (rf * steps);
            int   hw = 4 + (int)(rf * 8) + 2;
            for (int w = -hw; w <= hw; w++)
                Plot(img, Mathf.RoundToInt(ax + px * w), Mathf.RoundToInt(ay + py * w), Shadow);
        }

        // Objective lens cap at the wide end (bright rim + dark glass centre).
        int ox = Mathf.RoundToInt(sx + dx * steps), oy = Mathf.RoundToInt(sy + dy * steps);
        FilledDisc(img, ox, oy, 13, White);
        FilledDisc(img, ox, oy, 6,  Shadow);
        ApplyShadowHalf(img, cx, cy);
    }

    // Round kite shield with a central boss and cross-brace.
    private static void DrawWarrior(Image img)
    {
        const int cx = 64, cy = 60;
        FilledDisc(img, cx, cy, 34, White);
        // Boss (dark ring + bright centre)
        FilledDisc(img, cx, cy, 8, Shadow);
        FilledDisc(img, cx, cy, 5, White);
        // Cross brace
        for (int i = -4; i <= 4; i++) Plot(img, cx + i, cy, Shadow);
        for (int i = -4; i <= 4; i++) Plot(img, cx, cy + i, Shadow);
        ApplyShadowHalf(img, cx, cy);
    }

    // Left-facing bow (thick arc) with a vertical arrow on the string.
    private static void DrawArcher(Image img)
    {
        const int cx = 64, cy = 64;
        // Bow arc: annular sector facing left (dx <= 4)
        for (int dy = -36; dy <= 36; dy++)
        for (int dx = -36; dx <= 4;  dx++)
        {
            int d2 = dx * dx + dy * dy;
            if (d2 >= 22 * 22 && d2 <= 34 * 34) Plot(img, cx + dx, cy + dy, White);
        }
        // Bowstring (vertical, at inner edge)
        for (int y = cy - 34; y <= cy + 34; y++) Plot(img, cx + 4, y, White);
        // Arrow shaft
        for (int y = cy - 44; y <= cy + 30; y++) Plot(img, cx + 4, y, White);
        // Arrowhead (triangular, wide at base → narrows at tip going upward)
        for (int t = 0; t < 8; t++)
        {
            int w = 7 - t;
            for (int dx = -w; dx <= w; dx++) Plot(img, cx + 4 + dx, cy - 44 - t, White);
        }
        // Nock (forked tail)
        Plot(img, cx + 2, cy + 30, White);
        Plot(img, cx + 6, cy + 30, White);
        ApplyShadowHalf(img, cx, cy);
    }

    // Tall vertical spear: thin shaft with a diamond leaf-blade tip.
    private static void DrawSpearman(Image img)
    {
        const int cx = 64, cy = 64;
        // Shaft
        for (int y = 28; y <= 108; y++)
        for (int dx = -3; dx <= 3; dx++) Plot(img, cx + dx, y, White);
        // Diamond spearhead: widest at mid-point, pointed at both ends
        for (int t = 0; t < 20; t++)
        {
            int w = t < 10 ? t : 19 - t;
            for (int dx = -w; dx <= w; dx++) Plot(img, cx + dx, 28 - t, White);
        }
        // Butt cap
        FilledDisc(img, cx, 112, 5, White);
        ApplyShadowHalf(img, cx, cy);
    }

    // Simplified horse profile: horizontal body ellipse, head/neck extending upper-right.
    private static void DrawHorseman(Image img)
    {
        const int cx = 58, cy = 72;
        // Body
        FilledEllipse(img, cx, cy, 30, 16, White);
        // Neck (series of overlapping ellipses)
        for (int t = 0; t < 10; t++)
            FilledEllipse(img, cx + 14 + t, cy - 8 - t, 8, 5, White);
        // Head
        FilledEllipse(img, cx + 24, cy - 18, 14, 9, White);
        // Ear
        FilledDisc(img, cx + 28, cy - 26, 4, White);
        // Four legs (vertical sticks)
        int[] legX = { cx - 18, cx - 6, cx + 8, cx + 20 };
        foreach (int lx in legX)
            for (int dy = 0; dy <= 24; dy++) Plot(img, lx, cy + 14 + dy, White);
        ApplyShadowHalf(img, cx, cy);
    }

    // Long tapered blade, cross-guard, grip, and disc pommel.
    private static void DrawSwordsman(Image img)
    {
        const int cx = 64, cy = 64;
        // Blade (1px at tip, widens toward guard)
        for (int y = 10; y <= 78; y++)
        {
            int w = Mathf.Max(1, (y - 10) * 8 / 68);
            for (int dx = -w; dx <= w; dx++) Plot(img, cx + dx, y, White);
        }
        // Cross-guard (3px tall bar)
        for (int dy = 0; dy < 4; dy++)
        for (int dx = -22; dx <= 22; dx++) Plot(img, cx + dx, 78 + dy, White);
        // Grip
        for (int y = 83; y <= 100; y++)
        for (int dx = -5; dx <= 5; dx++) Plot(img, cx + dx, y, White);
        // Pommel disc
        FilledDisc(img, cx, 106, 7, White);
        ApplyShadowHalf(img, cx, cy);
    }

    // Catapult: rectangular frame with two wheels, angled throwing arm, sling bucket.
    private static void DrawCatapult(Image img)
    {
        const int fx = 50, fy = 82; // frame centre
        // Horizontal frame beam
        for (int y = fy - 5; y <= fy + 5; y++)
        for (int x = fx - 24; x <= fx + 36; x++) Plot(img, x, y, White);
        // Wheels
        FilledDisc(img, fx - 18, fy + 16, 13, White);
        FilledDisc(img, fx + 30, fy + 16, 13, White);
        // Throwing arm (diagonal up-right from pivot at frame centre)
        for (int t = 0; t <= 46; t++)
        {
            int ax = fx + 16 + t;
            int ay = fy - 5 - t;
            Plot(img, ax,     ay,     White);
            Plot(img, ax,     ay - 1, White);
            Plot(img, ax + 1, ay,     White);
        }
        // Sling bucket at top of arm
        FilledDisc(img, fx + 62, fy - 51, 9, White);
        ApplyShadowHalf(img, fx + 20, fy);
    }

    // Covered wagon: rectangular body, arched canvas top, two spoke wheels.
    private static void DrawSettler(Image img)
    {
        const int cx = 64, cy = 76;
        // Wagon box
        for (int y = cy - 8; y <= cy + 10; y++)
        for (int x = cx - 28; x <= cx + 28; x++) Plot(img, x, y, White);
        // Canvas cover (half-ellipse arch)
        for (int dy = -30; dy <= 0; dy++)
        {
            float t = (float)(dy + 30) / 30f;            // 0 at top, 1 at base
            int   hw = Mathf.RoundToInt(28f * Mathf.Sin(t * Mathf.Pi * 0.5f));
            for (int dx = -hw; dx <= hw; dx++) Plot(img, cx + dx, cy - 8 + dy, White);
        }
        // Wheels
        FilledDisc(img, cx - 20, cy + 16, 13, White);
        FilledDisc(img, cx + 20, cy + 16, 13, White);
        // Wheel spokes (drawn as lighter cross, shadowed)
        foreach (int wx in new[] { cx - 20, cx + 20 })
        {
            for (int i = -10; i <= 10; i++) Plot(img, wx + i, cy + 16, Shadow);
            for (int i = -10; i <= 10; i++) Plot(img, wx,     cy + 16 + i, Shadow);
        }
        ApplyShadowHalf(img, cx, cy);
    }

    // Pickaxe: diagonal handle with a T-shaped iron head.
    private static void DrawWorker(Image img)
    {
        const int cx = 64, cy = 64;
        // Handle: diagonal from lower-left to upper-right
        for (int t = 0; t <= 60; t++)
        {
            int hx = cx - 26 + t;
            int hy = cy + 30 - t;
            for (int d = -2; d <= 2; d++) Plot(img, hx, hy + d, White);
        }
        // Pick head (horizontal bar at upper-right end of handle)
        int headX = cx + 34, headY = cy - 30;
        for (int dy = -6; dy <= 6; dy++)
        for (int dx = -16; dx <= 16; dx++) Plot(img, headX + dx, headY + dy, White);
        // Pick point (right taper)
        for (int t = 1; t <= 12; t++)
        {
            int w = Mathf.Max(0, 6 - t / 2);
            for (int dy = -w; dy <= w; dy++) Plot(img, headX + 16 + t, headY + dy, White);
        }
        // Adze (left blunt end, slightly broader)
        for (int t = 1; t <= 8; t++)
        {
            int w = Mathf.Max(0, 6 - t / 3);
            for (int dy = -w; dy <= w; dy++) Plot(img, headX - 16 - t, headY + dy, White);
        }
        ApplyShadowHalf(img, cx, cy);
    }

    // Generic fallback: white disc with dark ring (mirrors old MakeDiscToken).
    private static void DrawDisc(Image img)
    {
        FilledDisc(img, 64, 64, 38, White);
        ApplyShadowHalf(img, 64, 64);
    }

    // ── Shadow + outline passes ─────────────────────────────────────────────────

    // Bayer-dithered grey shadow on the lower-right half of the silhouette.
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

    // Trace a 1-px dark outline on transparent pixels adjacent to any filled pixel.
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

    private static void FilledDisc(Image img, int cx, int cy, int r, Color c)
    {
        for (int dy = -r; dy <= r; dy++)
        for (int dx = -r; dx <= r; dx++)
            if (dx * dx + dy * dy <= r * r) Plot(img, cx + dx, cy + dy, c);
    }

    private static void FilledEllipse(Image img, int cx, int cy, int rx, int ry, Color c)
    {
        for (int dy = -ry; dy <= ry; dy++)
        for (int dx = -rx; dx <= rx; dx++)
        {
            float ex = (float)dx / rx, ey = (float)dy / ry;
            if (ex * ex + ey * ey <= 1f) Plot(img, cx + dx, cy + dy, c);
        }
    }
}
