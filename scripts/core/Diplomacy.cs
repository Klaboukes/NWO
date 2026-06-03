using System.Collections.Generic;

namespace NWO.Core;

public enum DiplomaticStance { War, Peace, NonAggression, Alliance }

// Minimal pairwise diplomacy (Phase 10.6). Stances are symmetric and keyed by the
// unordered player-id pair. The default is War, preserving the MVP's all-vs-all
// footing; players may de-escalate to Peace/NonAggression/Alliance, which blocks
// attacks between them (see GameState.TryAttack / AIController target selection).
public class Diplomacy
{
    private readonly Dictionary<(int, int), DiplomaticStance> _stances = new();

    private static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);

    public DiplomaticStance Between(int a, int b)
        => a == b ? DiplomaticStance.Peace : _stances.GetValueOrDefault(Key(a, b), DiplomaticStance.War);

    public void Set(int a, int b, DiplomaticStance stance)
    {
        if (a == b) return;
        _stances[Key(a, b)] = stance;
    }

    // Combat is only permitted between players actively at War.
    public bool CanAttack(int a, int b) => a != b && Between(a, b) == DiplomaticStance.War;

    public bool AreAllied(int a, int b) => a != b && Between(a, b) == DiplomaticStance.Alliance;

    // Non-default stances, for save/load.
    public IEnumerable<(int A, int B, DiplomaticStance Stance)> Entries()
    {
        foreach (var ((a, b), stance) in _stances)
            yield return (a, b, stance);
    }
}
