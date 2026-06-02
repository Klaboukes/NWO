using System.Collections.Generic;
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
            var map    = MapGenerator.Generate(width, height, seed);
            var counts = new Dictionary<TerrainType, int>();
            foreach (var (_, t) in map.Tiles)
                counts[t] = counts.GetValueOrDefault(t) + 1;

            int total = map.Tiles.Count;
            int water = counts.GetValueOrDefault(TerrainType.Ocean) + counts.GetValueOrDefault(TerrainType.Coast);
            int land  = total - water;

            GD.Print($"── seed {seed}  ({total} tiles, {land} land) ──");
            foreach (TerrainType t in System.Enum.GetValues<TerrainType>())
            {
                int c = counts.GetValueOrDefault(t);
                // Percent of land for land biomes; percent of all tiles for water.
                bool isWater = t is TerrainType.Ocean or TerrainType.Coast;
                float pct = isWater ? 100f * c / total : (land > 0 ? 100f * c / land : 0f);
                GD.Print($"  {t,-10} {c,4}  {pct,5:0.0}%{(isWater ? " (of map)" : " (of land)")}");
            }
            GD.Print($"  rivers(edges)={map.Rivers.Count}  resources={map.Resources.Count}");
        }
        GetTree().Quit();
    }
}
