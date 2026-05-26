using System.Collections.Generic;
using Godot;

namespace NWO.Map;

public class MapData
{
    public int Width  { get; }
    public int Height { get; }

    // Keyed by axial (q, r) coordinates — pre-allocated to avoid resizing
    public Dictionary<Vector2I, TerrainType> Tiles { get; }

    public MapData(int width, int height)
    {
        Width  = width;
        Height = height;
        Tiles  = new(width * height);
    }
}
