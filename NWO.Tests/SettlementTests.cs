using Godot;
using NWO.Core;
using NWO.Entities;
using NWO.Map;
using Xunit;

namespace NWO.Tests;

// Phase 10.4 — anti-micro settlement model: per-faction settle spacing and cost, and
// start-normalization (fertile, viable spawns).
public class SettlementTests
{
    private static DataCatalog Catalog() => new(
        new[]
        {
            new UnitData { Id = "settler", Name = "Settler", Movement = 2, Special = "found_city",
                           ProductionCost = 100 },
            new UnitData { Id = "scout",   Name = "Scout",   Movement = 3, Sight = 3 },
        },
        System.Array.Empty<BuildingData>(),
        null,
        new[]
        {
            // Free Settlements: tighter spacing, cheaper settlers.
            new FactionData { Id = "free", Name = "Free Settlements",
                              MinCityDistanceDelta = -1, SettleCostMult = 0.5 },
        });

    private static GameState FlatState() => new(TestWorlds.FlatMap(20, 20), Catalog(), 1);

    [Fact]
    public void EffectiveMinCityDistance_AppliesFactionDelta()
    {
        var s       = FlatState();
        var neutral = s.AddPlayer(new Player { Id = 0 });
        var free    = s.AddPlayer(new Player { Id = 1, FactionId = "free" });
        Assert.Equal(GameState.MinCityDistance, s.EffectiveMinCityDistance(neutral));
        Assert.Equal(GameState.MinCityDistance - 1, s.EffectiveMinCityDistance(free));
    }

    [Fact]
    public void TryFoundCity_FreeSettlements_CanSettleCloser()
    {
        bool CanFoundAtDistance2(string? faction)
        {
            var s = FlatState();
            var p = s.AddPlayer(new Player { Id = 0, FactionId = faction });
            s.Cities.Add(new City("Existing", p, new Vector2I(10, 10)));
            var settler = new Unit(s.Catalog.Unit("settler")!, p, new Vector2I(12, 10)); // distance 2
            s.Units.Add(settler);
            return s.TryFoundCity(settler, out _) == GameState.FoundCityResult.Success;
        }

        Assert.False(CanFoundAtDistance2(null));   // neutral needs distance 3
        Assert.True(CanFoundAtDistance2("free"));  // Free Settlements packs to 2
    }

    [Fact]
    public void EffectiveItemCost_DiscountsSettlersForFreeSettlements()
    {
        var s    = FlatState();
        var free = s.AddPlayer(new Player { Id = 0, FactionId = "free" });
        var norm = s.AddPlayer(new Player { Id = 1 });
        Assert.Equal(100, s.EffectiveItemCost(norm, "unit:settler"));
        Assert.Equal(50,  s.EffectiveItemCost(free, "unit:settler")); // 100 * 0.5
        // Non-settler units are unaffected.
        Assert.Equal(s.Catalog.ItemCost("unit:scout"), s.EffectiveItemCost(free, "unit:scout"));
    }

    [Fact]
    public void Populate_NormalizesViewerStartTowardFertility()
    {
        // Barren centre (desert: low yield) with a fertile grassland pocket within the
        // normalize radius. The viewer's start should slide onto the fertile ground.
        var map = TestWorlds.FlatMap(40, 30);
        foreach (var key in new System.Collections.Generic.List<Vector2I>(map.Tiles.Keys))
            map.Tiles[key] = TerrainType.Desert;
        // Grassland pocket next to the map centre (MapCenterAxial → (30,5)).
        foreach (var t in HexGrid.GetRange(new Vector2I(29, 5), 2))
            if (map.Tiles.ContainsKey(t)) map.Tiles[t] = TerrainType.Grassland;

        var state  = new GameState(map, Catalog(), 1);
        var viewer = GameFactory.Populate(state, new[] { new FactionChoice(null, true) });

        var start = state.Units.Find(u => u.Owner == viewer && u.Data.Id == "scout")!.Position;
        Assert.Equal(TerrainType.Grassland, state.Map.Tiles[start]);
    }
}
