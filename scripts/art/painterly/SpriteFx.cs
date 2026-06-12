using Godot;

namespace NWO.Art.Painterly;

// Alpha-aware post passes for sprite canvases (units, cities, icons). Applied
// after the shape layers: AO settles the forms, rim light pops the silhouette,
// DarkRim keeps it readable on any terrain, ContactShadow grounds it, and
// AlphaBleed makes the result safe for Linear filtering.
public static class SpriteFx
{
    // Cool light caught on silhouette edges that face `lightDir` (unit vector,
    // image coords). Reads from an alpha snapshot so the rim can't feed on itself.
    public static void RimLight(Canvas c, Vector2 lightDir, Color rim,
                                float width, float strength)
    {
        var a = SnapshotAlpha(c);
        var probe = lightDir.Normalized() * width;
        for (int y = 0; y < c.Height; y++)
        for (int x = 0; x < c.Width; x++)
        {
            float body = a[y * c.Width + x];
            if (body < 0.3f) continue;
            int px = Mathf.RoundToInt(x + probe.X);
            int py = Mathf.RoundToInt(y + probe.Y);
            float outside = (px < 0 || px >= c.Width || py < 0 || py >= c.Height)
                ? 0f : a[py * c.Width + px];
            if (outside >= 0.4f) continue;
            c.Blend(x, y, rim, strength * body * (1f - outside));
        }
    }

    // Soft form darkening where alpha coverage thins out — silhouette borders,
    // notches, crevices. Separable box blur of alpha drives the occlusion.
    public static void AmbientOcclusionFromAlpha(Canvas c, int radius, float strength)
    {
        var blurred = BoxBlurAlpha(c, radius);
        for (int y = 0; y < c.Height; y++)
        for (int x = 0; x < c.Width; x++)
        {
            if (c.Alpha(x, y) < 0.05f) continue;
            float occ = strength * (1f - blurred[y * c.Width + x]);
            if (occ > 0f) c.ScaleRgb(x, y, 1f - occ);
        }
    }

    // Soft elliptical ground shadow painted UNDER the sprite (dst-over), so the
    // body always sits on top of it.
    public static void ContactShadow(Canvas c, Vector2 centre, Vector2 radii, float strength)
    {
        int x0 = Mathf.Max(0, Mathf.FloorToInt(centre.X - radii.X));
        int x1 = Mathf.Min(c.Width - 1, Mathf.CeilToInt(centre.X + radii.X));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(centre.Y - radii.Y));
        int y1 = Mathf.Min(c.Height - 1, Mathf.CeilToInt(centre.Y + radii.Y));
        var shadow = new Color(0.05f, 0.04f, 0.08f, 1f);
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        {
            float ex = (x + 0.5f - centre.X) / radii.X;
            float ey = (y + 0.5f - centre.Y) / radii.Y;
            float e = ex * ex + ey * ey;
            if (e >= 1f) continue;
            c.BlendUnder(x, y, shadow, strength * Mathf.Pow(1f - e, 1.5f));
        }
    }

    // Soft dark outline painted UNDER the silhouette — the painterly replacement
    // for v1's hard 1px ApplyOutline. Keeps sprites readable on any terrain.
    public static void DarkRim(Canvas c, Color rim, float width)
    {
        var a = SnapshotAlpha(c);
        int r = Mathf.CeilToInt(width);
        for (int y = 0; y < c.Height; y++)
        for (int x = 0; x < c.Width; x++)
        {
            if (a[y * c.Width + x] >= 0.98f) continue;
            float best = 0f;
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || nx >= c.Width || ny < 0 || ny >= c.Height) continue;
                float na = a[ny * c.Width + nx];
                if (na <= 0.05f) continue;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                if (dist > width + 0.5f) continue;
                best = Mathf.Max(best, na * (1f - dist / (width + 1f)));
            }
            if (best > 0f) c.BlendUnder(x, y, rim, best);
        }
    }

    // Dilate RGB into fully-transparent pixels so Linear filtering interpolates
    // toward neighbouring body colour instead of black (the halo fix). Alpha
    // stays 0 — only the hidden RGB changes.
    public static void AlphaBleed(Canvas c, int passes = 8)
    {
        int w = c.Width, h = c.Height;
        var hasColor = new bool[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            hasColor[y * w + x] = c.Alpha(x, y) > 0.001f;

        for (int pass = 0; pass < passes; pass++)
        {
            var grew = new System.Collections.Generic.List<(int x, int y, Color c)>();
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (hasColor[y * w + x]) continue;
                float r = 0f, g = 0f, b = 0f; int n = 0;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                    if (!hasColor[ny * w + nx]) continue;
                    var col = c.Get(nx, ny);
                    r += col.R; g += col.G; b += col.B; n++;
                }
                if (n > 0) grew.Add((x, y, new Color(r / n, g / n, b / n, 0f)));
            }
            if (grew.Count == 0) break;
            foreach (var (x, y, col) in grew)
            {
                c.Set(x, y, col);
                hasColor[y * w + x] = true;
            }
        }
    }

    private static float[] SnapshotAlpha(Canvas c)
    {
        var a = new float[c.Width * c.Height];
        for (int y = 0; y < c.Height; y++)
        for (int x = 0; x < c.Width; x++)
            a[y * c.Width + x] = c.Alpha(x, y);
        return a;
    }

    // Two-pass separable box blur of the alpha channel (AO occlusion source).
    private static float[] BoxBlurAlpha(Canvas c, int radius)
    {
        int w = c.Width, h = c.Height;
        var src = SnapshotAlpha(c);
        var tmp = new float[w * h];
        var dst = new float[w * h];
        float inv = 1f / (2 * radius + 1);

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float sum = 0f;
            for (int k = -radius; k <= radius; k++)
                sum = sum + src[y * w + Mathf.Clamp(x + k, 0, w - 1)];
            tmp[y * w + x] = sum * inv;
        }
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float sum = 0f;
            for (int k = -radius; k <= radius; k++)
                sum = sum + tmp[Mathf.Clamp(y + k, 0, h - 1) * w + x];
            dst[y * w + x] = sum * inv;
        }
        return dst;
    }
}
