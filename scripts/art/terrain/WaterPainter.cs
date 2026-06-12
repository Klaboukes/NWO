using Godot;
using NWO.Art.Painterly;

namespace NWO.Art.Terrain;

// Ocean / Coast / Lake: a subsurface depth gradient under a relit wave-height
// field, with sun glints on slopes facing the light. Coast adds foam streaks
// along the wave crests; Lake is calmer and more mirror-like.
public static class WaterPainter
{
    public enum Kind { Ocean, Coast, Lake }

    private static readonly Color Glint = new(0.97f, 0.99f, 1f);
    private static readonly Color Foam  = new(0.93f, 0.96f, 0.97f);

    public static void Paint(TerrainPaintContext ctx, Kind kind)
    {
        int S = ctx.Size;
        float amp  = kind == Kind.Lake ? 0.8f : 1.5f;
        float freq = kind == Kind.Lake ? 0.045f : 0.07f;

        // Two advected sine layers + fine chop = the wave height field.
        var hf = new HeightField(S, S);
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float w1 = NoiseField.Fbm(x / 52f, y / 52f, ctx.Seed + 11, 3);
            float w2 = NoiseField.Fbm(x / 37f, y / 37f, ctx.Seed + 23, 3);
            float h  = Mathf.Sin((y + 26f * w1) * freq) * 0.65f
                     + Mathf.Sin((x * 0.35f + y * 0.8f + 34f * w2) * freq * 1.7f) * 0.35f;
            hf[x, y] = h * amp
                     + (NoiseField.Fbm(x / 9f, y / 9f, ctx.Seed + 41, 2) - 0.5f) * 0.55f;
        }

        // Depth bias: ocean deep and dark, coast bright shallows, lake between.
        float bias = kind switch
        {
            Kind.Ocean => 0.38f,
            Kind.Coast => 0.60f,
            _          => 0.50f,
        };
        float glintGain = kind == Kind.Lake ? 0.55f : 0.78f;

        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float depth = NoiseField.Fbm(x / 110f, y / 110f, ctx.Seed + 3, 3, lacunarity: 2.3f);
            var albedo  = ctx.Ramp.Sample(bias + (depth - 0.5f) * 0.34f);
            var n       = hf.Normal(x, y, kind == Kind.Lake ? 5f : 9f);
            var col     = Lighting.Shade(albedo, n);
            float spec  = Lighting.Specular(n, 42f) * glintGain;
            if (spec > 0.02f) col = col.Lerp(Glint, Mathf.Clamp(spec, 0f, 1f));
            ctx.Canvas.Set(x, y, col);
        }

        if (kind == Kind.Coast) PaintFoam(ctx, hf);
    }

    // White streaks where the wave crests peak, broken up by a noise mask so the
    // foam reads windblown rather than striped.
    private static void PaintFoam(TerrainPaintContext ctx, HeightField hf)
    {
        int S = ctx.Size;
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float crest = Mathf.SmoothStep(0.95f, 1.45f, hf[x, y]);
            if (crest <= 0f) continue;
            float mask = NoiseField.Fbm(x / 14f, y / 14f, ctx.Seed + 67, 2);
            float a = crest * Mathf.SmoothStep(0.45f, 0.75f, mask) * 0.85f;
            if (a > 0.01f) ctx.Canvas.Blend(x, y, Foam, a);
        }
    }
}
