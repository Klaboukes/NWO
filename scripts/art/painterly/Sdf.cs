using Godot;

namespace NWO.Art.Painterly;

// 2D signed-distance functions, in pixels: negative inside, positive outside.
// Painters compose these (Union/SmoothUnion/Subtract) and hand the composite to
// Painter.FillSdf / FillShaded, which rasterizes with anti-aliased coverage.
public static class Sdf
{
    public static float Circle(Vector2 p, Vector2 c, float r) => (p - c).Length() - r;

    // Scaled-space approximation: exact on circles, good enough on game-art
    // ellipses (error grows with eccentricity, stays visually irrelevant here).
    public static float Ellipse(Vector2 p, Vector2 c, Vector2 radii)
    {
        var q = (p - c) / radii;
        return (q.Length() - 1f) * Mathf.Min(radii.X, radii.Y);
    }

    public static float Segment(Vector2 p, Vector2 a, Vector2 b)
    {
        var pa = p - a; var ba = b - a;
        float h = Mathf.Clamp(pa.Dot(ba) / ba.Dot(ba), 0f, 1f);
        return (pa - ba * h).Length();
    }

    public static float Capsule(Vector2 p, Vector2 a, Vector2 b, float r) => Segment(p, a, b) - r;

    public static float Box(Vector2 p, Vector2 c, Vector2 half, float round = 0f)
    {
        var d = (p - c).Abs() - half + new Vector2(round, round);
        return new Vector2(Mathf.Max(d.X, 0f), Mathf.Max(d.Y, 0f)).Length()
             + Mathf.Min(Mathf.Max(d.X, d.Y), 0f) - round;
    }

    public static float Triangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        => Polygon(p, new[] { a, b, c });

    // Exact signed distance to a simple polygon (winding-sign interior test).
    public static float Polygon(Vector2 p, Vector2[] pts)
    {
        float d = (p - pts[0]).LengthSquared();
        float s = 1f;
        for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i, i++)
        {
            var e = pts[j] - pts[i];
            var w = p - pts[i];
            var b = w - e * Mathf.Clamp(w.Dot(e) / e.Dot(e), 0f, 1f);
            d = Mathf.Min(d, b.LengthSquared());
            bool c1 = p.Y >= pts[i].Y, c2 = p.Y < pts[j].Y, c3 = e.X * w.Y > e.Y * w.X;
            if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) s = -s;
        }
        return s * Mathf.Sqrt(d);
    }

    // Distance to a quadratic bezier, approximated by a fine polyline — exact
    // enough at sprite scale and far simpler than the closed-form cubic solve.
    public static float QuadBezier(Vector2 p, Vector2 a, Vector2 ctrl, Vector2 b)
    {
        const int segs = 16;
        float d = float.MaxValue;
        var prev = a;
        for (int i = 1; i <= segs; i++)
        {
            float t = (float)i / segs;
            var pt = a.Lerp(ctrl, t).Lerp(ctrl.Lerp(b, t), t);
            d = Mathf.Min(d, Segment(p, prev, pt));
            prev = pt;
        }
        return d;
    }

    // Flat-top hexagon (same orientation as the terrain tiles): three edge-normal
    // axes, apothem = 0.866 * circumradius. Negative inside.
    public static float Hexagon(Vector2 p, Vector2 c, float circumradius)
    {
        float dx = p.X - c.X, dy = p.Y - c.Y;
        float m = Mathf.Max(Mathf.Abs(dy),
                  Mathf.Max(Mathf.Abs(0.8660254f * dx + 0.5f * dy),
                            Mathf.Abs(-0.8660254f * dx + 0.5f * dy)));
        return m - 0.8660254f * circumradius;
    }

    public static float Union(float a, float b) => Mathf.Min(a, b);

    public static float Subtract(float a, float b) => Mathf.Max(a, -b);

    public static float Intersect(float a, float b) => Mathf.Max(a, b);

    // Polynomial smooth-min: organic merges for canopy blobs, muscle masses,
    // melted snow. k is the blend radius in pixels.
    public static float SmoothUnion(float a, float b, float k)
    {
        float h = Mathf.Clamp(0.5f + 0.5f * (b - a) / k, 0f, 1f);
        return Mathf.Lerp(b, a, h) - k * h * (1f - h);
    }
}
