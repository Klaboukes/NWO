using System;

namespace NWO.Core;

// Seeded RNG for combat jitter that counts every draw, so save/load can replay
// the stream back to the exact position it was at. Without this a reload
// re-bases the RNG at the seed and upcoming combat results change
// (reload-scumming). SaveSerializer persists Draws; the constructor fast-forwards.
public sealed class CombatRng
{
    private readonly Random _rng;

    // Draws consumed so far. Monotonic; serialized in saves.
    public int Draws { get; private set; }

    public CombatRng(int seed, int draws = 0)
    {
        _rng = new Random(seed);
        for (int i = 0; i < draws; i++) _rng.NextDouble();
        Draws = draws;
    }

    public double NextDouble()
    {
        Draws++;
        return _rng.NextDouble();
    }
}
