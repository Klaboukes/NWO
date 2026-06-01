using Godot;

namespace NWO.Art;

// Procedural pixel-art generator for small HUD status icons (Phase 7 V7.4).
//
// STYLE  Same family as UnitArtGenerator (dark 1-px outline, transparent RGBA8
// background) but these icons are *not* Modulate-tinted, so they carry their own
// full-colour ramps: a gold coin for the treasury and a cyan flask for science.
// Authored at the display size (32px) with 1-2px features so they stay crisp under
// nearest-neighbour filtering.
//
// DETERMINISM  Generate(iconId) is pure — same id → same Image bytes. Unknown ids
// fall back to a neutral gold disc so new HUD icons never crash the UI.
public static class HudIconGenerator
{
    public const int IconSize = 32;

    private static readonly Color Transp  = new(0f, 0f, 0f, 0f);
    private static readonly Color Outline = new(0.10f, 0.10f, 0.12f);

    // Treasury gold ramp.
    private static readonly Color GoldDark  = new(0.62f, 0.45f, 0.10f);
    private static readonly Color GoldMid   = new(0.92f, 0.74f, 0.22f);
    private static readonly Color GoldLight = new(1.00f, 0.93f, 0.58f);

    // Science cyan ramp (glass + fluid).
    private static readonly Color Glass      = new(0.78f, 0.90f, 1.00f);
    private static readonly Color FluidMid   = new(0.28f, 0.66f, 0.94f);
    private static readonly Color FluidLight = new(0.55f, 0.85f, 1.00f);
    private static readonly Color Cork       = new(0.55f, 0.40f, 0.26f);

    public static Image Generate(string iconId)
    {
        var img = Image.CreateEmpty(IconSize, IconSize, false, Image.Format.Rgba8);
        img.Fill(Transp);

        switch (iconId)
        {
            case "gold":    DrawCoin(img);  break;
            case "science": DrawFlask(img); break;
            default:        FilledDisc(img, 16, 16, 12, GoldMid); break;
        }

        ApplyOutline(img);
        return img;
    }

    // Gold coin: filled disc, dark rim + inner ring, upper-left sheen, centre pip.
    private static void DrawCoin(Image img)
    {
        const int cx = 16, cy = 16, r = 13;
        FilledDisc(img, cx, cy, r, GoldMid);
        Ring(img, cx, cy, r, GoldDark);          // outer rim
        Ring(img, cx, cy, r - 4, GoldDark);      // inner ring (coin face edge)
        // Upper-left sheen.
        FilledDisc(img, cx - 4, cy - 4, 4, GoldLight);
        // Centre pip (small diamond stamp).
        for (int dy = -3; dy <= 3; dy++)
        for (int dx = -3; dx <= 3; dx++)
            if (Mathf.Abs(dx) + Mathf.Abs(dy) <= 3) Plot(img, cx + dx, cy + dy, GoldDark);
    }

    // Erlenmeyer flask: narrow neck widening to a triangular body, lower ~half
    // filled with cyan fluid (bright surface line), small cork at the top.
    private static void DrawFlask(Image img)
    {
        const int cx = 16;
        // Silhouette (glass) — neck then flared body.
        for (int y = 6; y <= 27; y++)
        {
            int hw = y <= 13
                ? 3                                   // neck
                : 3 + (y - 13) * 9 / 14;              // body flare → ~12 at base
            for (int dx = -hw; dx <= hw; dx++) Plot(img, cx + dx, y, Glass);
        }

        // Fluid fills the lower body.
        for (int y = 19; y <= 27; y++)
        {
            int hw = 3 + (y - 13) * 9 / 14;
            for (int dx = -hw; dx <= hw; dx++) Plot(img, cx + dx, y, FluidMid);
        }
        // Bright fluid surface line.
        for (int dx = -6; dx <= 6; dx++) Plot(img, cx + dx, 19, FluidLight);

        // Cork at the top of the neck.
        for (int y = 4; y <= 6; y++)
        for (int dx = -3; dx <= 3; dx++) Plot(img, cx + dx, y, Cork);
    }

    // ── Outline pass (mirrors UnitArtGenerator) ─────────────────────────────────

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

    // ── Drawing helpers ─────────────────────────────────────────────────────────

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

    private static void Ring(Image img, int cx, int cy, int r, Color c)
    {
        for (int dy = -r; dy <= r; dy++)
        for (int dx = -r; dx <= r; dx++)
        {
            int d2 = dx * dx + dy * dy;
            if (d2 <= r * r && d2 > (r - 1) * (r - 1)) Plot(img, cx + dx, cy + dy, c);
        }
    }
}
