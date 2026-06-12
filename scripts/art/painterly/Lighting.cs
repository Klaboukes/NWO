using Godot;

namespace NWO.Art.Painterly;

// The one light source every painter shares: a warm key light from the upper-left
// (image coords: x right, y DOWN, z out of the screen — so "up-left" is -x, -y).
// One sun direction across all 60+ assets is what makes the set read as a family.
public static class Lighting
{
    // Direction from a surface point TOWARD the light.
    public static readonly Vector3 SunDir = new Vector3(-0.45f, -0.62f, 0.645f).Normalized();

    private static readonly Vector3 ViewDir = new(0f, 0f, 1f);

    // Wrapped diffuse: softer terminator than hard Lambert, keeps shadow sides
    // readable (game art, not a renderer). Returns [0,1].
    public static float Lambert(Vector3 n)
    {
        const float wrap = 0.35f;
        return Mathf.Clamp((n.Dot(SunDir) + wrap) / (1f + wrap), 0f, 1f);
    }

    // Blinn specular against the screen-space view direction.
    public static float Specular(Vector3 n, float power)
    {
        var half = (SunDir + ViewDir).Normalized();
        return Mathf.Pow(Mathf.Max(n.Dot(half), 0f), power);
    }

    // Light an albedo by a normal + AO with painterly hue-shifted shading:
    // shadows lean cool/saturated, highlights warm/desaturated.
    public static Color Shade(Color albedo, Vector3 n, float ao = 1f)
    {
        float lit = Lambert(n) * ao;
        if (lit < 0.5f)
        {
            var shadow = ColorRamp.ShiftHsv(albedo, -0.045f, +0.16f, -0.52f);
            return shadow.Lerp(albedo, lit * 2f);
        }
        var high = ColorRamp.ShiftHsv(albedo, +0.025f, -0.10f, +0.30f);
        return albedo.Lerp(high, (lit - 0.5f) * 2f);
    }
}
