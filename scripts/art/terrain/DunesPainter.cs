using Godot;
using NWO.Art.Painterly;

namespace NWO.Art.Terrain;

// Desert: a real dune height field — asymmetric warped sine ridges with fine
// wind ripples — lit by the shared sun, so slip-face shadows fall out of the
// lighting pass instead of being painted on. Rocks and the odd saguaro on top.
public static class DunesPainter
{
    private const float DuneHeight = 7f;

    public static void Paint(TerrainPaintContext ctx)
    {
        int S = ctx.Size;
        var hf = new HeightField(S, S);
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float warp  = 36f * (NoiseField.Fbm(x / 90f, y / 90f, ctx.Seed + 5, 3) - 0.5f);
            float phase = (y + warp + 14f * Mathf.Sin(x * 0.013f)) * 0.052f;
            // Asymmetric profile: long windward back, steep slip face.
            float dune  = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(phase), 1.6f);
            hf[x, y] = dune * DuneHeight
                     + (NoiseField.Fbm(x / 13f, y / 13f, ctx.Seed + 19, 2) - 0.5f) * 0.9f;
        }

        GroundPainter.Relight(ctx, hf,
            (x, y) => ctx.Ramp.Sample(0.48f + hf[x, y] / DuneHeight * 0.20f),
            relief: 3.0f, aoRadius: 9, aoStrength: 6f);

        int rocks = ctx.Rng.Range(2, 5);
        for (int i = 0; i < rocks; i++)
            if (ctx.TryPlace(20f, out var pos))
                TerrainProps.Rock(ctx, pos, ctx.Rng.Range(4f, 8f));

        int cacti = ctx.Rng.Range(1, 3);
        for (int i = 0; i < cacti; i++)
            if (ctx.TryPlace(34f, out var pos))
                TerrainProps.Cactus(ctx, pos);
    }
}
