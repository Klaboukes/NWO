using System.Collections.Generic;
using Godot;

namespace NWO.Map;

// The hex map: terrain keyed by axial (q, r) coordinate. Populated by
// MapGenerator and read (never structurally mutated) during play.
public class MapData
{
    public int Width  { get; }
    public int Height { get; }

    // Keyed by axial (q, r) coordinates — pre-allocated to avoid resizing
    public Dictionary<Vector2I, TerrainType> Tiles { get; }

    // Strategic resources, keyed by axial coord. Sparse — only resource tiles
    // appear. Populated by MapGenerator; read (never structurally mutated) in play.
    public Dictionary<Vector2I, ResourceType> Resources { get; } = new();

    // Worker-built tile improvements, keyed by axial coord. Sparse. Mutated at
    // runtime when a Worker completes a build task (see GameState).
    public Dictionary<Vector2I, ImprovementType> Improvements { get; } = new();

    public MapData(int width, int height)
    {
        Width  = width;
        Height = height;
        Tiles  = new(width * height);
    }

    public ResourceType ResourceAt(Vector2I axial)
        => Resources.GetValueOrDefault(axial, ResourceType.None);

    public ImprovementType ImprovementAt(Vector2I axial)
        => Improvements.GetValueOrDefault(axial, ImprovementType.None);
}
