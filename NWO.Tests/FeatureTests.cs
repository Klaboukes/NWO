using Godot;
using NWO.Core;
using NWO.Entities;
using NWO.Map;
using Xunit;

namespace NWO.Tests;

// Phase 14 terrain/feature split: the legality matrix, flag-additive yields, and
// the water rules (Lake and pack Ice vs land + naval movement).
public class FeatureTests
{
    // ── FeatureRules legality matrix ─────────────────────────────────────────────

    [Theory]
    [InlineData(TerrainType.Grassland, Feature.Forest, true)]
    [InlineData(TerrainType.Plains,    Feature.Forest, true)]
    [InlineData(TerrainType.Tundra,    Feature.Forest, true)]
    [InlineData(TerrainType.Desert,    Feature.Forest, false)]
    [InlineData(TerrainType.Snow,      Feature.Forest, false)]
    [InlineData(TerrainType.Grassland, Feature.Jungle, true)]
    [InlineData(TerrainType.Tundra,    Feature.Jungle, false)]
    [InlineData(TerrainType.Grassland, Feature.Marsh,  true)]
    [InlineData(TerrainType.Plains,    Feature.Marsh,  false)]
    [InlineData(TerrainType.Desert,    Feature.Oasis,  true)]
    [InlineData(TerrainType.Plains,    Feature.Oasis,  false)]
    [InlineData(TerrainType.Ocean,     Feature.Ice,    true)]
    [InlineData(TerrainType.Coast,     Feature.Ice,    true)]
    [InlineData(TerrainType.Lake,      Feature.Ice,    false)] // lakes never freeze over
    [InlineData(TerrainType.Mountain,  Feature.Hills,  false)]
    [InlineData(TerrainType.Ocean,     Feature.Hills,  false)]
    [InlineData(TerrainType.Snow,      Feature.Hills,  true)]
    public void IsLegal_SingleFeature(TerrainType terrain, Feature f, bool expected)
        => Assert.Equal(expected, FeatureRules.IsLegal(terrain, f));

    [Fact]
    public void IsLegal_Combinations()
    {
        // Hills stacks with the tree canopies, and with nothing else.
        Assert.True(FeatureRules.IsLegal(TerrainType.Grassland, Feature.Forest | Feature.Hills));
        Assert.True(FeatureRules.IsLegal(TerrainType.Plains,    Feature.Jungle | Feature.Hills));
        Assert.False(FeatureRules.IsLegal(TerrainType.Grassland, Feature.Marsh  | Feature.Hills));
        Assert.False(FeatureRules.IsLegal(TerrainType.Desert,    Feature.Oasis  | Feature.Hills));
        Assert.False(FeatureRules.IsLegal(TerrainType.Coast,     Feature.Ice    | Feature.Hills));
        // At most one vegetation overlay per tile.
        Assert.False(FeatureRules.IsLegal(TerrainType.Grassland, Feature.Forest | Feature.Jungle));
        // None is always legal.
        Assert.True(FeatureRules.IsLegal(TerrainType.Ocean, Feature.None));
    }

    // ── Flag-additive yields (calibrated to the pre-split terrain numbers) ───────

    [Fact]
    public void Yields_GrasslandForest_MatchesOldForestTerrain()
    {
        // Pre-split Forest terrain was 1F/2P, movement 2.
        var mask = Feature.Forest;
        Assert.Equal(1, TerrainYields.Food(TerrainType.Grassland) + FeatureYields.Food(mask));
        Assert.Equal(2, TerrainYields.Production(TerrainType.Grassland) + FeatureYields.Production(mask));
        Assert.Equal(2, TerrainYields.MovementCost(TerrainType.Grassland) + FeatureYields.MovementCost(mask));
    }

    [Fact]
    public void Yields_ForestHills_IsLumberHill()
    {
        // Grassland + Forest + Hills = 0F/3P, movement 3 (Civ5 lumber-hill feel).
        var mask = Feature.Forest | Feature.Hills;
        Assert.Equal(0, TerrainYields.Food(TerrainType.Grassland) + FeatureYields.Food(mask));
        Assert.Equal(3, TerrainYields.Production(TerrainType.Grassland) + FeatureYields.Production(mask));
        Assert.Equal(3, TerrainYields.MovementCost(TerrainType.Grassland) + FeatureYields.MovementCost(mask));
    }

    [Fact]
    public void Yields_DesertOasis_MatchesCiv5()
    {
        // Desert + Oasis = 3F/1P/1G (Civ5 oasis: 3F/1G; NWO desert carries 1P).
        Assert.Equal(3, TerrainYields.Food(TerrainType.Desert) + FeatureYields.Food(Feature.Oasis));
        Assert.Equal(1, TerrainYields.Production(TerrainType.Desert) + FeatureYields.Production(Feature.Oasis));
        Assert.Equal(1, TerrainYields.Gold(TerrainType.Desert) + FeatureYields.Gold(Feature.Oasis));
    }

    // ── Lake & Ice water rules ────────────────────────────────────────────────────

    [Fact]
    public void Lake_IsWaterButNotSea()
    {
        Assert.True(TerrainYields.IsWater(TerrainType.Lake));
        Assert.False(TerrainYields.IsSeaWater(TerrainType.Lake));
        Assert.True(TerrainYields.IsSeaWater(TerrainType.Coast));
        Assert.False(TerrainYields.CanFoundCityOn(TerrainType.Lake));
    }

    [Fact]
    public void Movement_LakeBlocksLandAndSea_IceBlocksSea()
    {
        var session = TestWorlds.StandardSession(out var human, out _);
        var state   = session.State;
        var lake    = new Vector2I(5, 5);
        var coast   = new Vector2I(7, 7);
        var iced    = new Vector2I(9, 9);
        state.Map.Tiles[lake]  = TerrainType.Lake;
        state.Map.Tiles[coast] = TerrainType.Coast;
        state.Map.Tiles[iced]  = TerrainType.Coast;
        state.Map.Features[iced] = Feature.Ice;

        var landUnit = new Unit(TestWorlds.Warrior(), human, new Vector2I(4, 5));
        var ship     = new Unit(new UnitData
        {
            Id = "galley", Name = "Galley", Attack = 6, Defense = 6,
            Movement = 3, Range = 1, Sight = 2, ProductionCost = 50, IsNaval = true,
        }, human, new Vector2I(6, 7));

        Assert.Equal(int.MaxValue, state.MovementCost(lake, landUnit)); // land can't enter water
        Assert.Equal(int.MaxValue, state.MovementCost(lake, ship));     // sea ships can't reach a lake
        Assert.Equal(1,            state.MovementCost(coast, ship));    // open coast is sailable
        Assert.Equal(int.MaxValue, state.MovementCost(iced, ship));     // pack ice blocks the lane
        Assert.Equal(int.MaxValue, state.MovementCost(coast, landUnit));
    }
}
