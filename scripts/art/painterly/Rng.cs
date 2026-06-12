using Godot;

namespace NWO.Art.Painterly;

// Deterministic xorshift64 PRNG — the same algorithm TerrainArtGenerator v1 used,
// promoted to the shared painterly library so every generator places props from
// one reproducible stream. Seed from the asset's identity only (terrain/unit id),
// never from time or System.Random, so bakes stay byte-identical in git.
public sealed class Rng
{
    private ulong _s;

    public Rng(ulong seed) => _s = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;

    private ulong Next()
    {
        _s ^= _s << 13; _s ^= _s >> 7; _s ^= _s << 17;
        return _s;
    }

    public float Float() => (Next() >> 40) / (float)(1 << 24);

    public float Range(float min, float max) => min + Float() * (max - min);

    public int Range(int minIncl, int maxExcl) => minIncl + (int)(Next() % (ulong)(maxExcl - minIncl));

    public bool Chance(float p) => Float() < p;

    // Uniform point in the unit disc (for organic cluster scatter): sqrt keeps the
    // radial density uniform instead of bunching at the centre.
    public Vector2 InUnitDisc()
    {
        float a = Float() * Mathf.Tau;
        float r = Mathf.Sqrt(Float());
        return new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
    }
}
