using System.Collections.Generic;
using Godot;

namespace NWO.Map;

public class MapData
{
    public int Width  { get; init; }
    public int Height { get; init; }

    // Keyed by axial (q, r) coordinates
    public Dictionary<Vector2I, TerrainType> Tiles { get; } = new();
}
