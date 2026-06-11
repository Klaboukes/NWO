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

    // Terrain features (e.g. Hills) overlaying the base terrain, keyed by axial
    // coord. Sparse — only featured tiles appear. Set by MapGenerator.
    public Dictionary<Vector2I, Feature> Features { get; } = new();

    // River edges (Phase 9.4): each entry is a tile + a HexGrid.Directions index,
    // naming the hex edge between that tile and its neighbour in that direction.
    // An edge is stored once (from one side); IsRiverAdjacent checks both sides.
    public HashSet<(Vector2I Tile, int Dir)> Rivers { get; } = new();

    // Contested objective sites (Phase 10.5): controlling them wins the match
    // ("Establish the New World Order"). Placed by GameFactory away from spawns;
    // control is derived from nearby city ownership (see KeySiteService).
    public List<Vector2I> KeySites { get; } = new();

    public MapData(int width, int height)
    {
        Width  = width;
        Height = height;
        Tiles  = new(width * height);
    }

    // True if any of the tile's six edges carries a river (matched from either side).
    public bool IsRiverAdjacent(Vector2I axial)
    {
        for (int d = 0; d < 6; d++)
        {
            if (Rivers.Contains((axial, d))) return true;
            var n = axial + HexGrid.Directions[d];
            if (Rivers.Contains((n, (d + 3) % 6))) return true; // edge stored on neighbour's side
        }
        return false;
    }

    public ResourceType ResourceAt(Vector2I axial)
        => Resources.GetValueOrDefault(axial, ResourceType.None);

    public ImprovementType ImprovementAt(Vector2I axial)
        => Improvements.GetValueOrDefault(axial, ImprovementType.None);

    public Feature FeatureAt(Vector2I axial)
        => Features.GetValueOrDefault(axial, Feature.None);

    // Feature is a flag mask (Forest+Hills etc.), so membership is a bit test.
    public bool HasFeature(Vector2I axial, Feature f) => (FeatureAt(axial) & f) != 0;

    public bool IsHill(Vector2I axial) => HasFeature(axial, Feature.Hills);
}
