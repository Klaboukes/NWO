using System;
using Godot;

namespace NWO.Art.Painterly;

// Rasterizes SDF shapes onto a Canvas with anti-aliased coverage. Every op takes
// an explicit pixel-bounds Rect2 derived from the shape so a small prop never
// pays a full-canvas scan. FillShaded is the volumetric workhorse: it inflates
// the SDF interior into a rounded "pillow", derives normals, and lights them
// with the shared sun — flat shapes come out reading as 3D forms.
public sealed class Painter
{
    private readonly Canvas _c;

    public Painter(Canvas canvas) => _c = canvas;

    public Canvas Canvas => _c;

    public void FillSdf(Func<Vector2, float> sdf, Rect2 bounds, Color color, float aa = 1f)
        => FillSdf(sdf, bounds, (_, _) => color, aa);

    // shade(p, dist) lets callers vary colour across the interior (noise, gradients).
    public void FillSdf(Func<Vector2, float> sdf, Rect2 bounds,
                        Func<Vector2, float, Color> shade, float aa = 1f)
    {
        var (x0, y0, x1, y1) = Clip(bounds, aa);
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        {
            var p = new Vector2(x + 0.5f, y + 0.5f);
            float d = sdf(p);
            float cov = Coverage(d, aa);
            if (cov <= 0f) continue;
            _c.Blend(x, y, shade(p, d), cov);
        }
    }

    public void StrokeSdf(Func<Vector2, float> sdf, Rect2 bounds, Color color,
                          float width, float aa = 1f)
    {
        var (x0, y0, x1, y1) = Clip(bounds, width + aa);
        float hw = width * 0.5f;
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        {
            float d = Mathf.Abs(sdf(new Vector2(x + 0.5f, y + 0.5f))) - hw;
            float cov = Coverage(d, aa);
            if (cov <= 0f) continue;
            _c.Blend(x, y, color, cov);
        }
    }

    // Pillow-shade the SDF interior: height h rises from 0 at the edge to 1 at
    // `inflate` px deep (quarter-circle profile), normals come from the height
    // gradient, the shared sun lights them, and the lit value samples the ramp.
    public void FillShaded(Func<Vector2, float> sdf, Rect2 bounds, ColorRamp ramp,
                           float inflate, float aa = 1f, float specular = 0f)
    {
        var (x0, y0, x1, y1) = Clip(bounds, aa);
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        {
            var p = new Vector2(x + 0.5f, y + 0.5f);
            float d = sdf(p);
            float cov = Coverage(d, aa);
            if (cov <= 0f) continue;

            float hx = Pillow(sdf(p + Vector2.Right), inflate) - Pillow(sdf(p + Vector2.Left), inflate);
            float hy = Pillow(sdf(p + Vector2.Down),  inflate) - Pillow(sdf(p + Vector2.Up),   inflate);
            var n = new Vector3(-hx * inflate * 0.5f, -hy * inflate * 0.5f, 1f).Normalized();

            var col = ramp.Sample(Lighting.Lambert(n));
            if (specular > 0f)
                col = col.Lerp(Colors.White, Mathf.Clamp(Lighting.Specular(n, 24f) * specular, 0f, 1f));
            _c.Blend(x, y, col, cov);
        }
    }

    // Fill the SDF interior with a ramp swept along from→to.
    public void LinearGradient(Func<Vector2, float> sdf, Rect2 bounds,
                               ColorRamp ramp, Vector2 from, Vector2 to)
    {
        var axis = to - from;
        float len2 = axis.LengthSquared();
        FillSdf(sdf, bounds, (p, _) => ramp.Sample((p - from).Dot(axis) / len2));
    }

    // Soft drop shadow of a shape: full strength inside the offset silhouette,
    // fading to nothing over `blur` px outside it. Paint BEFORE the shape.
    public void SoftShadow(Func<Vector2, float> sdf, Rect2 bounds,
                           Vector2 offset, float blur, float strength)
    {
        var shifted = new Rect2(bounds.Position + offset, bounds.Size);
        var (x0, y0, x1, y1) = Clip(shifted, blur);
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        {
            float d = sdf(new Vector2(x + 0.5f, y + 0.5f) - offset);
            float a = strength * (1f - Mathf.SmoothStep(0f, blur, d));
            if (a <= 0f) continue;
            _c.Blend(x, y, new Color(0f, 0f, 0f, 1f), a);
        }
    }

    private static float Pillow(float d, float inflate)
    {
        float t = Mathf.Clamp(-d / inflate, 0f, 1f);
        return Mathf.Sqrt(1f - (1f - t) * (1f - t));
    }

    private static float Coverage(float d, float aa) => Mathf.Clamp(0.5f - d / aa, 0f, 1f);

    private (int x0, int y0, int x1, int y1) Clip(Rect2 bounds, float pad)
    {
        int x0 = Mathf.Max(0, Mathf.FloorToInt(bounds.Position.X - pad));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(bounds.Position.Y - pad));
        int x1 = Mathf.Min(_c.Width - 1,  Mathf.CeilToInt(bounds.End.X + pad));
        int y1 = Mathf.Min(_c.Height - 1, Mathf.CeilToInt(bounds.End.Y + pad));
        return (x0, y0, x1, y1);
    }
}
