using System.Collections.Generic;
using Godot;

namespace NWO.Entities;

public enum CityFocus { Balanced, Food, Production }

// Per-city tile-assignment state. Owned by City; mutated by CityWorkforceService.
public sealed class CityWorkforce
{
    public CityFocus         Focus    { get; set; } = CityFocus.Balanced;
    public HashSet<Vector2I> Locked   { get; }      = new(); // player-locked tiles
    public HashSet<Vector2I> Assigned { get; }      = new(); // tiles worked this recompute
}
