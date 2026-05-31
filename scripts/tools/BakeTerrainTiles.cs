using Godot;
using NWO.Art;
using NWO.Map;

namespace NWO.Tools;

// Headless one-shot tool: bakes every TerrainType's procedural tile to
// res://assets/art/tiles/<terrain>.png, then quits. These are the "real" terrain
// tiles V7.2 calls for; once committed, TerrainTextureRegistry loads them instead of
// re-synthesizing, and a human can hand-edit any PNG to override the generator.
//
// Run it via the generate-terrain-art skill (bake.ps1), or directly:
//   godot --headless --path <repo> res://scenes/tools/BakeTerrainTiles.tscn
//
// Re-run after changing TerrainArtGenerator (or a terrain's base colour) to refresh
// the committed art. The output is deterministic, so re-baking with no code change
// produces byte-identical PNGs.
public partial class BakeTerrainTiles : Node
{
    private const string OutDir = "res://assets/art/tiles";

    public override void _Ready()
    {
        DirAccess.MakeDirRecursiveAbsolute(OutDir);

        int count = 0;
        foreach (TerrainType terrain in System.Enum.GetValues<TerrainType>())
        {
            string name = terrain.ToString().ToLowerInvariant();
            string path = $"{OutDir}/{name}.png";
            Error err = TerrainArtGenerator.Generate(terrain).SavePng(path);
            if (err != Error.Ok)
            {
                GD.PrintErr($"BakeTerrainTiles: failed to write {path}: {err}");
                GetTree().Quit(1);
                return;
            }
            GD.Print($"baked {path}");
            count++;
        }

        GD.Print($"BakeTerrainTiles: baked {count} tiles to {OutDir}");
        GetTree().Quit();
    }
}
