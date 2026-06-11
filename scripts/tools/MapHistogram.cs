using System.Collections.Generic;
using System.Linq;
using Godot;
using NWO.Map;

namespace NWO.Tools;

// Headless diagnostic tool: runs MapGenerator over a span of seeds and prints a
// terrain histogram + river/resource counts, so map-generation tuning can be judged
// without a display (FastNoiseLite needs the Godot runtime, so this can't be an
// xUnit test). Dev-only — not wired into the game, same as BakeTerrainTiles.
//
// Run via the tune-map-generation skill (histogram.ps1), or directly:
//   godot --headless --path <repo> res://scenes/tools/MapHistogram.tscn -- --seeds 5 --size 60x40
//
// Optional user args (after the `--`):
//   --seeds N      number of seeds 0..N-1 to sample (default 5)
//   --size WxH     map dimensions (default 60x40)
public partial class MapHistogram : Node
{
    // Connected river systems that never touch water — should always be 0 (the
    // tracer carves a Lake when a river bottoms out inland). Edges are grouped
    // into components via shared corner vertices; a component "reaches water"
    // if any tile meeting at any of its corners is Ocean/Coast/Lake.
    private static int DryRivers(MapData map)
    {
        var edges = new List<(Vector2I Tile, int Dir)>(map.Rivers);
        if (edges.Count == 0) return 0;

        // A corner vertex = the sorted triple of tiles meeting there.
        static (Vector2I, Vector2I, Vector2I) Vertex(Vector2I a, Vector2I b, Vector2I c)
        {
            var arr = new[] { a, b, c };
            System.Array.Sort(arr, (u, v) => u.X != v.X ? u.X - v.X : u.Y - v.Y);
            return (arr[0], arr[1], arr[2]);
        }

        var vertexEdges = new Dictionary<(Vector2I, Vector2I, Vector2I), List<int>>();
        var edgeVerts   = new List<(Vector2I, Vector2I, Vector2I)[]>(edges.Count);
        for (int i = 0; i < edges.Count; i++)
        {
            var p  = edges[i].Tile;
            var q  = p + HexGrid.Directions[edges[i].Dir];
            var nb = new HashSet<Vector2I>(HexGrid.GetNeighbors(q));
            var vs = new List<(Vector2I, Vector2I, Vector2I)>(2);
            foreach (var c in HexGrid.GetNeighbors(p))
                if (nb.Contains(c)) vs.Add(Vertex(p, q, c)); // the edge's two corners
            edgeVerts.Add(vs.ToArray());
            foreach (var v in vs)
                (vertexEdges.TryGetValue(v, out var list) ? list : vertexEdges[v] = new()).Add(i);
        }

        bool VertexTouchesWater((Vector2I, Vector2I, Vector2I) v)
            => new[] { v.Item1, v.Item2, v.Item3 }.Any(t =>
                map.Tiles.TryGetValue(t, out var tt) && TerrainYields.IsWater(tt));

        int dry = 0;
        var seen = new bool[edges.Count];
        for (int i = 0; i < edges.Count; i++)
        {
            if (seen[i]) continue;
            bool wet = false;
            var queue = new Queue<int>();
            queue.Enqueue(i); seen[i] = true;
            while (queue.Count > 0)
            {
                int e = queue.Dequeue();
                foreach (var v in edgeVerts[e])
                {
                    if (VertexTouchesWater(v)) wet = true;
                    foreach (var other in vertexEdges[v])
                        if (!seen[other]) { seen[other] = true; queue.Enqueue(other); }
                }
            }
            if (!wet) dry++;
        }
        return dry;
    }

    public override void _Ready()
    {
        int seeds = 5, width = 60, height = 40;

        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--seeds") int.TryParse(args[i + 1], out seeds);
            else if (args[i] == "--size")
            {
                var parts = args[i + 1].ToLowerInvariant().Split('x');
                if (parts.Length == 2 && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h))
                    (width, height) = (w, h);
            }
        }

        for (int seed = 0; seed < seeds; seed++)
        {
            var map       = MapGenerator.Generate(width, height, seed);
            var counts    = new Dictionary<TerrainType, int>();
            var featCounts = new Dictionary<Feature, int>();
            int illegal   = 0;
            foreach (var (pos, t) in map.Tiles)
            {
                counts[t] = counts.GetValueOrDefault(t) + 1;
                var mask = map.FeatureAt(pos);
                foreach (var f in FeatureRules.Flags)
                    if ((mask & f) != 0) featCounts[f] = featCounts.GetValueOrDefault(f) + 1;
                if (!FeatureRules.IsLegal(t, mask)) illegal++;
            }

            int total = map.Tiles.Count;
            int water = counts.GetValueOrDefault(TerrainType.Ocean)
                      + counts.GetValueOrDefault(TerrainType.Coast)
                      + counts.GetValueOrDefault(TerrainType.Lake);
            int land  = total - water;

            GD.Print($"── seed {seed}  ({total} tiles, {land} land) ──");
            foreach (TerrainType t in System.Enum.GetValues<TerrainType>())
            {
                int c = counts.GetValueOrDefault(t);
                // Percent of land for land biomes; percent of all tiles for water.
                bool isWater = TerrainYields.IsWater(t);
                float pct = isWater ? 100f * c / total : (land > 0 ? 100f * c / land : 0f);
                GD.Print($"  {t,-10} {c,4}  {pct,5:0.0}%{(isWater ? " (of map)" : " (of land)")}");
            }
            // Features overlay the biomes above (Ice over water, the rest over land).
            foreach (var f in FeatureRules.Flags)
            {
                int c = featCounts.GetValueOrDefault(f);
                bool onWater = f == Feature.Ice;
                int basis = onWater ? water : land;
                GD.Print($"  +{f,-9}(feat) {c,4}  {(basis > 0 ? 100f * c / basis : 0f),5:0.0}% (of {(onWater ? "water" : "land")})");
            }
            GD.Print($"  rivers(edges)={map.Rivers.Count}  resources={map.Resources.Count}"
                   + $"  legality-violations={illegal}  rivers-not-reaching-water={DryRivers(map)}");
        }
        GetTree().Quit();
    }
}
