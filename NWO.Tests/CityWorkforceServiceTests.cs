using System.Linq;
using Godot;
using NWO.Core;
using NWO.Entities;
using NWO.Map;
using Xunit;

namespace NWO.Tests;

public class CityWorkforceServiceTests
{
    private static (GameState state, Player player) FlatState(int w = 12, int h = 12)
    {
        var state  = new GameState(TestWorlds.FlatMap(w, h), TestWorlds.StandardCatalog(), combatSeed: 1);
        var player = state.AddPlayer(new Player { Id = 0, Name = "P0", IsHuman = true });
        return (state, player);
    }

    private static City Found(GameState state, Player owner, Vector2I pos)
    {
        var city = new City(state.NextCityName(), owner, pos) { Population = 1 };
        state.Cities.Add(city);
        CityWorkforceService.Recompute(state, city);
        return city;
    }

    [Fact]
    public void CityCenterFloor_AppliesEvenOnDesert()
    {
        var (state, player) = FlatState();
        // Replace the founding tile with Desert (0F, 1P).
        state.Map.Tiles[new Vector2I(5, 5)] = TerrainType.Desert;
        var city = new City("X", player, new Vector2I(5, 5)) { Population = 0 };
        state.Cities.Add(city);
        CityWorkforceService.Recompute(state, city);

        // Pop 0 → no workers, only the center. Floor guarantees 2F/1P.
        Assert.Equal(2, city.FoodYield);
        Assert.Equal(1, city.ProductionYield);
    }

    [Fact]
    public void RiverAdjacentWorkedTile_GainsFloodplainFood()
    {
        var (state, player) = FlatState();
        var city = new City("X", player, new Vector2I(5, 5)) { Population = 1 };
        state.Cities.Add(city);

        var tile = new Vector2I(6, 5);     // distance 1, workable Plains (1F)
        city.Workforce.Locked.Add(tile);   // force the citizen onto it

        CityWorkforceService.Recompute(state, city);
        int baseFood = city.FoodYield;

        // Put a river on the E edge of (6,5) — touches only the worked tile, not
        // the city centre, so this isolates the worked-tile floodplain bonus.
        state.Map.Rivers.Add((new Vector2I(6, 5), 0));
        CityWorkforceService.Recompute(state, city);

        Assert.Equal(baseFood + 1, city.FoodYield);
    }

    [Fact]
    public void RiverAdjacentCityCenter_GainsFloodplainFood()
    {
        var (state, player) = FlatState();
        var city = new City("X", player, new Vector2I(5, 5)) { Population = 0 };
        state.Cities.Add(city);

        CityWorkforceService.Recompute(state, city);
        int baseFood = city.FoodYield; // population 0 → centre yield only

        // River on the E edge of the city centre makes the centre a floodplain.
        state.Map.Rivers.Add((new Vector2I(5, 5), 0));
        CityWorkforceService.Recompute(state, city);

        Assert.Equal(baseFood + 1, city.FoodYield);
    }

    [Fact]
    public void HillsFeature_TradesFoodForProductionOnWorkedTile()
    {
        var (state, player) = FlatState();
        var city = new City("X", player, new Vector2I(5, 5)) { Population = 1 };
        state.Cities.Add(city);

        var tile = new Vector2I(6, 5);     // Plains: 1F / 1P
        city.Workforce.Locked.Add(tile);
        CityWorkforceService.Recompute(state, city);
        int baseFood = city.FoodYield, baseProd = city.ProductionYield;

        state.Map.Features[tile] = Feature.Hills; // → 0F / 2P
        CityWorkforceService.Recompute(state, city);

        Assert.Equal(baseFood - 1, city.FoodYield);
        Assert.Equal(baseProd + 1, city.ProductionYield);
    }

    [Fact]
    public void FocusProduction_PrefersHighProdTiles()
    {
        var (state, player) = FlatState();
        // A Hills feature on Plains: 1F/1P → 0F/2P, the highest-production workable tile.
        state.Map.Features[new Vector2I(6, 5)] = Feature.Hills;
        var city = Found(state, player, new Vector2I(5, 5));    // Plains center
        Assert.Equal(1, city.Population);

        city.Workforce.Focus = CityFocus.Production;
        CityWorkforceService.Recompute(state, city);

        Assert.Contains(new Vector2I(6, 5), city.Workforce.Assigned);
    }

    [Fact]
    public void LockedTile_PersistsAcrossRecompute()
    {
        var (state, player) = FlatState();
        var city = Found(state, player, new Vector2I(5, 5));
        var pinned = new Vector2I(7, 5); // a workable Plains tile two hexes east

        city.Workforce.Locked.Add(pinned);
        CityWorkforceService.Recompute(state, city);

        Assert.Contains(pinned, city.Workforce.Assigned);
        Assert.Contains(pinned, city.Workforce.Locked);

        // Trigger another recompute — locked tile stays in.
        CityWorkforceService.Recompute(state, city);
        Assert.Contains(pinned, city.Workforce.Assigned);
    }

    [Fact]
    public void LockedTile_DroppedWhenNoLongerWorkable()
    {
        var (state, player) = FlatState();
        var city = Found(state, player, new Vector2I(5, 5));
        var pinned = new Vector2I(7, 5);
        city.Workforce.Locked.Add(pinned);
        CityWorkforceService.Recompute(state, city);
        Assert.Contains(pinned, city.Workforce.Locked);

        // Spawn an enemy unit on the pinned tile — blocks workability.
        var enemy = state.AddPlayer(new Player { Id = 1, Name = "E", IsHuman = false });
        state.Units.Add(new Unit(TestWorlds.Warrior(), enemy, pinned));
        CityWorkforceService.Recompute(state, city);

        Assert.DoesNotContain(pinned, city.Workforce.Locked);
        Assert.DoesNotContain(pinned, city.Workforce.Assigned);
    }

    [Fact]
    public void ControllingCity_EarlierCityWinsTies()
    {
        var (state, player) = FlatState();
        var first  = Found(state, player, new Vector2I(5, 5));
        var second = Found(state, player, new Vector2I(9, 5)); // dist 4 → no overlap with radius-2

        // A tile equidistant from both cities — there isn't one in this layout
        // (distance differs). Instead test a tile inside only `first`'s reach.
        Assert.Same(first, CityWorkforceService.ControllingCity(state, new Vector2I(6, 5)));
        Assert.Same(second, CityWorkforceService.ControllingCity(state, new Vector2I(8, 5)));

        // Now bring `second` close enough to overlap with `first` and create a
        // contested tile — earlier-founded wins.
        var third = new City("T", player, new Vector2I(7, 5)) { Population = 0 };
        state.Cities.Add(third);
        // Tile (6, 5): dist to first=1, dist to third=1 → tie → first (earlier).
        Assert.Same(first, CityWorkforceService.ControllingCity(state, new Vector2I(6, 5)));
    }

    [Fact]
    public void EnemyOnWorkedTile_BlockadesIt()
    {
        var (state, player) = FlatState();
        var enemy = state.AddPlayer(new Player { Id = 1, Name = "E", IsHuman = false });
        var city  = Found(state, player, new Vector2I(5, 5));

        // Pre-blockade: pop 1 picks one workable tile, prod ≥ center floor + 1.
        int prodBefore = city.ProductionYield;
        Assert.True(city.Workforce.Assigned.Count == 1);
        var worked = city.Workforce.Assigned.First();

        state.Units.Add(new Unit(TestWorlds.Warrior(), enemy, worked));
        CityWorkforceService.Recompute(state, city);

        Assert.DoesNotContain(worked, city.Workforce.Assigned);
        // Auto-reassigned to another adjacent Plains tile of equal value — totals stay the same.
        Assert.Equal(prodBefore, city.ProductionYield);
    }
}
