using System;
using Godot;
using NWO.Art.Painterly;

namespace NWO.Art.Terrain;

// The painterly ground fill every land terrain starts from: a domain-warped fBm
// height field lit by the shared sun, with the albedo wandering through the
// terrain's ramp. Replaces v1's Bayer-dithered noise — relief now comes from
// actual lighting, not dither density.
public static class GroundPainter
{
    // Scale = px per noise feature; Warp bends features organic; Relief converts
    // height gradient to normal steepness; AlbedoVar is how far (in ramp t) the
    // surface colour wanders around the midpoint.
    public readonly record struct GroundStyle(
        float Scale, float Warp, float Relief, int Octaves,
        float AoStrength, float AlbedoVar);

    public static HeightField Paint(TerrainPaintContext ctx, GroundStyle style)
    {
        var hf = new HeightField(ctx.Size, ctx.Size);
        hf.AddFbm(ctx.Seed, style.Scale, 1f, style.Octaves, style.Warp);
        hf.AddFbm(ctx.Seed + 31, style.Scale * 0.27f, 0.30f, 2); // fine grain
        float aScale = style.Scale * 1.6f;
        Relight(ctx, hf,
            (x, y) =>
            {
                float t = 0.5f + (NoiseField.Fbm(x / aScale, y / aScale, ctx.Seed + 57, 3) - 0.5f)
                               * style.AlbedoVar;
                return ctx.Ramp.Sample(t);
            },
            style.Relief, aoRadius: 7, style.AoStrength);
        return hf;
    }

    // Relight the whole canvas from a height field: albedo(x, y) shaded by the
    // field's normal + AO. Shared by dunes, the mountain massif, and snow drifts.
    public static void Relight(TerrainPaintContext ctx, HeightField hf,
                               Func<int, int, Color> albedo, float relief,
                               int aoRadius, float aoStrength)
    {
        for (int y = 0; y < ctx.Size; y++)
        for (int x = 0; x < ctx.Size; x++)
        {
            var n   = hf.Normal(x, y, relief);
            float ao = hf.Ao(x, y, aoRadius, aoStrength);
            ctx.Canvas.Set(x, y, Lighting.Shade(albedo(x, y), n, ao));
        }
    }
}
