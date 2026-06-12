using System;
using Godot;
using NWO.Art;
using NWO.Core;
using NWO.Map;

namespace NWO.Tools;

// Headless one-shot tool: bakes EVERY procedural art asset to its registry path,
// then quits — terrain tiles, unit sprites (every id in data/units.json), city
// sprites, resource icons, HUD icons, and the banner. Replaces BakeTerrainTiles.
//
// Run it via the generate-terrain-art skill (bake.ps1), or directly:
//   godot --headless --path <repo> res://scenes/tools/BakeAllArt.tscn
//
// Re-run after changing any generator. Output is deterministic: re-baking with
// no code change produces byte-identical PNGs, so committed art is stable in git
// and runtime placeholders always match the baked files.
public partial class BakeAllArt : Node
{
    public override void _Ready()
    {
        int count = 0;
        try
        {
            // Terrain tiles: every legal (terrain, vegetation-feature) combo.
            foreach (var (terrain, veg) in FeatureRules.TextureCombos())
                count += Save($"res://assets/art/tiles/{TerrainTextureRegistry.Stem(terrain, veg)}.png",
                              TerrainArtGenerator.Generate(terrain, veg));

            // Unit sprites: every unit id the game data defines.
            foreach (var unit in DataLoader.LoadUnits())
                count += Save($"res://assets/art/units/{unit.Id}.png",
                              UnitArtGenerator.Generate(unit.Id));

            // City sprites.
            count += Save("res://assets/art/cities/city.png",    CityArtGenerator.Generate(false));
            count += Save("res://assets/art/cities/capital.png", CityArtGenerator.Generate(true));

            // Resource icons: every named resource.
            foreach (ResourceType r in Enum.GetValues<ResourceType>())
            {
                if (r == ResourceType.None) continue;
                count += Save($"res://assets/art/resources/{r.ToString().ToLowerInvariant()}.png",
                              ResourceIconGenerator.Generate(r));
            }

            // HUD icons + the shared owner banner.
            count += Save("res://assets/art/ui/gold.png",    HudIconGenerator.Generate("gold"));
            count += Save("res://assets/art/ui/science.png", HudIconGenerator.Generate("science"));
            count += Save("res://assets/art/ui/banner.png",  BannerArtGenerator.Generate());
        }
        catch (Exception e)
        {
            GD.PrintErr($"BakeAllArt: {e}");
            GetTree().Quit(1);
            return;
        }

        GD.Print($"BakeAllArt: baked {count} assets under res://assets/art/");
        GetTree().Quit();
    }

    private int Save(string path, Image img)
    {
        DirAccess.MakeDirRecursiveAbsolute(path.GetBaseDir());
        Error err = img.SavePng(path);
        if (err != Error.Ok)
            throw new InvalidOperationException($"failed to write {path}: {err}");
        GD.Print($"baked {path}");
        return 1;
    }
}
