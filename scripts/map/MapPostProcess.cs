using System.Collections.Generic;
using Godot;

namespace NWO.Map;

// Post-classification geography passes (Phase 14): operate on MapData after the
// noise layers have produced a provisional land/Ocean split, fixing water geography
// by ADJACENCY rather than by height bands. Pure static, no FastNoiseLite — fed
// only by MapData + a seed, so xUnit can drive it with hand-built maps.
public static class MapPostProcess
{
    // Civ5's lake constant: an enclosed water region up to this many tiles is a
    // Lake; anything larger stays sea (a navigable inland sea, Caspian-style).
    public const int LakeMaxArea = 9;

    // Flood-fills connected water regions; any region that does NOT touch the map
    // edge and is small enough becomes Lake. Edge water is by definition the world
    // ocean (the radial falloff guarantees ocean borders on every script).
    public static void FormLakes(MapData data)
    {
        var visited = new HashSet<Vector2I>();
        foreach (var (start, t) in data.Tiles)
        {
            if (!TerrainYields.IsWater(t) || !visited.Add(start)) continue;

            var region      = new List<Vector2I> { start };
            bool touchesEdge = OnMapEdge(data, start);
            var queue       = new Queue<Vector2I>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                foreach (var n in HexGrid.GetNeighbors(queue.Dequeue()))
                {
                    if (!data.Tiles.TryGetValue(n, out var nt)) continue;
                    if (!TerrainYields.IsWater(nt) || !visited.Add(n)) continue;
                    region.Add(n);
                    if (OnMapEdge(data, n)) touchesEdge = true;
                    queue.Enqueue(n);
                }
            }

            if (!touchesEdge && region.Count <= LakeMaxArea)
                foreach (var tile in region)
                    data.Tiles[tile] = TerrainType.Lake;
        }
    }

    // Relabels sea water by distance to land (Civ5 coastlines): every Ocean tile
    // with a land neighbour becomes Coast (the mandatory shelf), then each Ocean
    // tile adjacent to that ring rolls `shelfChance` to extend the shallows one
    // more ring. No third ring — open sea stays Ocean. Lakes are uniformly shallow
    // and are never touched. Deterministic per seed (per-tile hash, no RNG stream).
    public static void FormCoasts(MapData data, float shelfChance, int seed)
    {
        var ring1 = new List<Vector2I>();
        foreach (var (axial, t) in data.Tiles)
            if (t == TerrainType.Ocean && HasLandNeighbour(data, axial))
                ring1.Add(axial);
        foreach (var tile in ring1) data.Tiles[tile] = TerrainType.Coast;

        var ring2 = new List<Vector2I>();
        var ring1Set = new HashSet<Vector2I>(ring1);
        foreach (var (axial, t) in data.Tiles)
        {
            if (t != TerrainType.Ocean) continue;
            bool besideShelf = false;
            foreach (var n in HexGrid.GetNeighbors(axial))
                if (ring1Set.Contains(n)) { besideShelf = true; break; }
            if (besideShelf && Hash01(axial, seed) < shelfChance) ring2.Add(axial);
        }
        foreach (var tile in ring2) data.Tiles[tile] = TerrainType.Coast;
    }

    private static bool HasLandNeighbour(MapData data, Vector2I axial)
    {
        foreach (var n in HexGrid.GetNeighbors(axial))
            if (data.Tiles.TryGetValue(n, out var nt) && !TerrainYields.IsWater(nt))
                return true;
        return false;
    }

    // True if the tile lies on the rectangular map border (in even-q offset space —
    // the mirror of MapGenerator's offset→axial conversion).
    private static bool OnMapEdge(MapData data, Vector2I axial)
    {
        int col = axial.X;
        int row = axial.Y + (col - (col & 1)) / 2;
        return col == 0 || col == data.Width - 1 || row == 0 || row == data.Height - 1;
    }

    // Cheap deterministic per-tile hash → [0,1). Stable for a given (tile, seed),
    // independent of iteration order. Shared by FeaturePlacer's rolls and tests.
    public static float Hash01(Vector2I axial, int seed)
    {
        uint h = (uint)(axial.X * 73856093) ^ (uint)(axial.Y * 19349663) ^ (uint)(seed * 83492791);
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (h & 0xFFFF) / 65536f;
    }
}
