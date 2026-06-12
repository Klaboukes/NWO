using Godot;

namespace NWO.Art.Painterly;

// A shadow→highlight gradient sampled by lighting. Painterly() builds the
// signature v2 ramp: shadows lean cool and saturated, highlights warm and
// desaturated — the way pigment reads — replacing v1's discrete 6-tone ramps.
public readonly struct ColorRamp
{
    private readonly (float T, Color C)[] _stops;

    public ColorRamp(params (float T, Color C)[] stops) => _stops = stops;

    public Color Sample(float t)
    {
        t = Mathf.Clamp(t, 0f, 1f);
        if (t <= _stops[0].T) return _stops[0].C;
        for (int i = 1; i < _stops.Length; i++)
        {
            if (t > _stops[i].T) continue;
            var (t0, c0) = _stops[i - 1];
            var (t1, c1) = _stops[i];
            return c0.Lerp(c1, (t - t0) / (t1 - t0));
        }
        return _stops[^1].C;
    }

    // Five hue-shifted stops centred on the base colour at t = 0.5.
    public static ColorRamp Painterly(Color baseColor,
        float shadowHueShift = -0.045f, float highlightHueShift = +0.025f, float contrast = 1f)
    {
        return new ColorRamp(
            (0.00f, ShiftHsv(baseColor, shadowHueShift,          +0.18f, -0.58f * contrast)),
            (0.25f, ShiftHsv(baseColor, shadowHueShift * 0.6f,   +0.10f, -0.34f * contrast)),
            (0.50f, baseColor),
            (0.75f, ShiftHsv(baseColor, highlightHueShift * 0.6f, -0.07f, +0.20f * contrast)),
            (1.00f, ShiftHsv(baseColor, highlightHueShift,        -0.14f, +0.42f * contrast)));
    }

    // Nudge a colour in HSV: dh shifts hue (wrapping), ds adds saturation,
    // dv scales value. Shared by Lighting and all material palettes.
    public static Color ShiftHsv(Color c, float dh, float ds, float dv)
    {
        float h = Mathf.PosMod(c.H + dh, 1f);
        float s = Mathf.Clamp(c.S + ds, 0f, 1f);
        float v = Mathf.Clamp(c.V * (1f + dv), 0f, 1f);
        return Color.FromHsv(h, s, v, c.A);
    }
}

// Shared material palettes for units, cities, and props — one set of ramps keeps
// every sprite in the same colour family (the cross-asset consistency rule).
public static class MaterialRamps
{
    public static ColorRamp Skin(int variant)
    {
        Color[] tones =
        {
            new(0.87f, 0.65f, 0.48f),
            new(0.72f, 0.50f, 0.35f),
            new(0.55f, 0.38f, 0.26f),
        };
        return ColorRamp.Painterly(tones[((variant % tones.Length) + tones.Length) % tones.Length]);
    }

    public static ColorRamp Leather  => ColorRamp.Painterly(new Color(0.48f, 0.32f, 0.18f));
    public static ColorRamp Steel    => ColorRamp.Painterly(new Color(0.62f, 0.66f, 0.72f), -0.02f, +0.01f, 1.3f);
    public static ColorRamp Bronze   => ColorRamp.Painterly(new Color(0.71f, 0.48f, 0.22f), -0.03f, +0.02f, 1.2f);
    public static ColorRamp Wood     => ColorRamp.Painterly(new Color(0.45f, 0.30f, 0.17f));
    public static ColorRamp Sail     => ColorRamp.Painterly(new Color(0.88f, 0.84f, 0.74f));
    public static ColorRamp Stone    => ColorRamp.Painterly(new Color(0.58f, 0.56f, 0.52f));
    public static ColorRamp Gold     => ColorRamp.Painterly(new Color(0.92f, 0.74f, 0.25f), -0.02f, +0.02f, 1.3f);

    public static ColorRamp Cloth(Color dye) => ColorRamp.Painterly(dye);
}
