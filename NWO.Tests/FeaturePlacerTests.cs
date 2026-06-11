using System.Collections.Generic;
using System.Linq;
using Godot;
using NWO.Map;
using Xunit;

namespace NWO.Tests;

// Phase 14.3 feature placement, driven by synthetic climate fields (the placer
// takes plain data, so no noise engine is needed).
public class FeaturePlacerTests
{
    private static readonly MapScriptParams P = MapScriptParams.For(MapScript.Continents);

    private static MapData UniformMap(int size, TerrainType terrain)
    {
        var map = new MapData(size, size);
        for (int q = 0; q < size; q++)
        for (int r = 0; r < size; r++)
            map.Tiles[new Vector2I(q, r)] = terrain;
        return map;
    }

    private static Dictionary<Vector2I, ClimateSample> UniformClimate(MapData map, ClimateSample c)
        => map.Tiles.Keys.ToDictionary(k => k, _ => c);

    [Fact]
    public void Jungle_OnlyInTheEquatorialBand()
    {
        var map = UniformMap(10, TerrainType.Grassland);
        var equatorial = new ClimateSample(Moisture: 0.7f, Temperature: 0.8f, Latitude: 0.1f, ForestNoise: 0f);
        FeaturePlacer.Place(map, UniformClimate(map, equatorial), P, seed: 1);
        Assert.All(map.Tiles.Keys, k => Assert.True(map.HasFeature(k, Feature.Jungle)));

        var temperate = UniformMap(10, TerrainType.Grassland);
        var midLat = equatorial with { Latitude = 0.45f };
        FeaturePlacer.Place(temperate, UniformClimate(temperate, midLat), P, seed: 1);
        Assert.All(temperate.Tiles.Keys, k => Assert.False(temperate.HasFeature(k, Feature.Jungle)));
    }

    [Fact]
    public void Forest_FollowsTheClumpField()
    {
        // Strong forest signal → woods; zero signal → open ground.
        var map = UniformMap(10, TerrainType.Plains);
        var wooded = new ClimateSample(0.6f, 0.5f, 0.45f, ForestNoise: 1f); // 0.55+0.27 > 0.62
        FeaturePlacer.Place(map, UniformClimate(map, wooded), P, seed: 1);
        Assert.All(map.Tiles.Keys, k => Assert.True(map.HasFeature(k, Feature.Forest)));

        var open = UniformMap(10, TerrainType.Plains);
        FeaturePlacer.Place(open, UniformClimate(open, wooded with { ForestNoise = 0f }), P, seed: 1);
        Assert.All(open.Tiles.Keys, k => Assert.False(open.HasFeature(k, Feature.Forest)));

        // Desert never grows forest no matter the signal.
        var desert = UniformMap(10, TerrainType.Desert);
        FeaturePlacer.Place(desert, UniformClimate(desert, wooded), P, seed: 1);
        Assert.All(desert.Tiles.Keys, k => Assert.False(desert.HasFeature(k, Feature.Forest)));
    }

    [Fact]
    public void Forest_StacksWithExistingHills()
    {
        var map = UniformMap(6, TerrainType.Grassland);
        var hill = new Vector2I(3, 3);
        map.Features[hill] = Feature.Hills;
        var wooded = new ClimateSample(0.6f, 0.5f, 0.45f, 1f);
        FeaturePlacer.Place(map, UniformClimate(map, wooded), P, seed: 1);
        Assert.Equal(Feature.Forest | Feature.Hills, map.FeatureAt(hill));
    }

    [Fact]
    public void Ice_CapsTheSeaButNeverLakes()
    {
        var sea = UniformMap(10, TerrainType.Ocean);
        var polar = new ClimateSample(0.5f, 0.05f, Latitude: 0.95f, ForestNoise: 0f);
        FeaturePlacer.Place(sea, UniformClimate(sea, polar), P, seed: 1);
        Assert.All(sea.Tiles.Keys, k => Assert.True(sea.HasFeature(k, Feature.Ice)));

        var lakes = UniformMap(10, TerrainType.Lake);
        FeaturePlacer.Place(lakes, UniformClimate(lakes, polar), P, seed: 1);
        Assert.All(lakes.Tiles.Keys, k => Assert.False(lakes.HasFeature(k, Feature.Ice)));

        // Equatorial sea stays open.
        var warm = UniformMap(10, TerrainType.Coast);
        FeaturePlacer.Place(warm, UniformClimate(warm, polar with { Latitude = 0.3f }), P, seed: 1);
        Assert.All(warm.Tiles.Keys, k => Assert.False(warm.HasFeature(k, Feature.Ice)));
    }

    [Fact]
    public void Oasis_IsRareAndIsolated()
    {
        var map = UniformMap(30, TerrainType.Desert);
        // A lake in the middle of the erg: its neighbours must stay oasis-free.
        var lake = new Vector2I(15, 15);
        map.Tiles[lake] = TerrainType.Lake;
        var dry = new ClimateSample(0.1f, 0.8f, 0.35f, 0f);
        FeaturePlacer.Place(map, UniformClimate(map, dry), P, seed: 5);

        var oases = map.Features.Where(kv => (kv.Value & Feature.Oasis) != 0).Select(kv => kv.Key).ToList();
        Assert.NotEmpty(oases);                              // a big desert earns some springs
        Assert.True(oases.Count < map.Tiles.Count / 10);     // but they stay rare
        foreach (var o in oases)
        {
            foreach (var n in HexGrid.GetNeighbors(o))
            {
                Assert.DoesNotContain(n, oases);             // never adjacent to another oasis
                if (map.Tiles.TryGetValue(n, out var nt))
                    Assert.False(TerrainYields.IsWater(nt)); // never beside water
            }
        }
    }

    [Fact]
    public void EveryPlacementIsLegal()
    {
        // A mixed map with every terrain + pre-set hills, extreme climates.
        var map = UniformMap(12, TerrainType.Grassland);
        var terrains = System.Enum.GetValues<TerrainType>();
        int i = 0;
        foreach (var k in map.Tiles.Keys.ToList())
        {
            map.Tiles[k] = terrains[i++ % terrains.Length];
            if (i % 3 == 0 && FeatureRules.IsLegal(map.Tiles[k], Feature.Hills))
                map.Features[k] = Feature.Hills;
        }
        var climate = map.Tiles.Keys.ToDictionary(
            k => k,
            k => new ClimateSample(
                Moisture:    MapPostProcess.Hash01(k, 11),
                Temperature: MapPostProcess.Hash01(k, 22),
                Latitude:    MapPostProcess.Hash01(k, 33),
                ForestNoise: MapPostProcess.Hash01(k, 44)));

        FeaturePlacer.Place(map, climate, P, seed: 9);

        foreach (var (k, t) in map.Tiles)
            Assert.True(FeatureRules.IsLegal(t, map.FeatureAt(k)), $"illegal mask {map.FeatureAt(k)} on {t}");
    }
}
