using Godot;
using NWO.Core;
using NWO.Entities;
using NWO.Map;
using Xunit;

namespace NWO.Tests;

// M2c — per-tile trade gold and gold rush-buy of production.
public class EconomyTests
{
    // ── Per-tile gold ──────────────────────────────────────────────────────────

    [Fact]
    public void GoldPerTurn_IncludesWorkedCoastTile()
    {
        var session = TestWorlds.StandardSession(out var human, out _);
        var state   = session.State;
        var coast   = new Vector2I(6, 5);
        state.Map.Tiles[coast] = TerrainType.Coast;

        var city = new City("Rome", human, new Vector2I(5, 5)) { Population = 1 };
        state.Cities.Add(city);
        city.Workforce.Locked.Add(coast);
        CityWorkforceService.Recompute(state, city);

        // No units (no maintenance), no buildings → income is purely the coast tile.
        Assert.Equal(1, CivEconomyService.GoldPerTurn(state, human));
    }

    // ── Buy cost ────────────────────────────────────────────────────────────────

    [Fact]
    public void BuyCost_ScalesWithRemainingProduction()
    {
        var session = TestWorlds.StandardSession(out var human, out _);
        var city    = new City("Rome", human, new Vector2I(5, 5)) { ProductionItem = "unit:warrior" };
        session.State.Cities.Add(city);

        // Warrior costs 40; 40 * GoldPerProduction(4) = 160 at zero progress.
        Assert.Equal(160, CivEconomyService.BuyCost(session.State, city));

        city.ProductionProgress = 10; // remaining 30
        Assert.Equal(120, CivEconomyService.BuyCost(session.State, city));
    }

    [Fact]
    public void BuyCost_ZeroWhenIdle()
    {
        var session = TestWorlds.StandardSession(out var human, out _);
        var city    = new City("Rome", human, new Vector2I(5, 5));
        session.State.Cities.Add(city);
        Assert.Equal(0, CivEconomyService.BuyCost(session.State, city));
    }

    // ── Rush-buy ────────────────────────────────────────────────────────────────

    [Fact]
    public void TryBuyProduction_SpawnsUnitAndDeductsGold()
    {
        var session = TestWorlds.StandardSession(out var human, out _);
        var state   = session.State;
        var city    = new City("Rome", human, new Vector2I(5, 5)) { ProductionItem = "unit:warrior" };
        state.Cities.Add(city);
        state.Civ(human).Treasury = 200;

        Assert.True(session.TryBuyProduction(city, out var completion));
        Assert.NotNull(completion);
        Assert.Equal(40, state.Civ(human).Treasury);                 // 200 - 160
        Assert.Null(city.ProductionItem);                            // queue cleared
        Assert.Contains(state.Units, u => u.Owner == human && u.Position == city.Position && u.Data.Id == "warrior");
    }

    [Fact]
    public void TryBuyProduction_FailsWhenTooPoor()
    {
        var session = TestWorlds.StandardSession(out var human, out _);
        var city    = new City("Rome", human, new Vector2I(5, 5)) { ProductionItem = "unit:warrior" };
        session.State.Cities.Add(city);
        session.State.Civ(human).Treasury = 100; // need 160

        Assert.False(session.TryBuyProduction(city, out _));
        Assert.Equal(100, session.State.Civ(human).Treasury);        // unchanged
        Assert.Equal("unit:warrior", city.ProductionItem);           // still queued
    }

    [Fact]
    public void TryBuyProduction_FailsWhenIdle()
    {
        var session = TestWorlds.StandardSession(out var human, out _);
        var city    = new City("Rome", human, new Vector2I(5, 5));
        session.State.Cities.Add(city);
        session.State.Civ(human).Treasury = 500;
        Assert.False(session.TryBuyProduction(city, out _));
    }

    // ── Building effects ────────────────────────────────────────────────────────

    // A state whose catalog carries the Barracks (XP effect tag) and Monument
    // (culture yield), mirroring data/buildings.json.
    private static GameState StateWithBuildings(out Player human)
    {
        var buildings = new System.Collections.Generic.List<BuildingData>
        {
            new() { Id = "barracks", Name = "Barracks", ProductionCost = 100,
                    Effect = "new_units_bonus_xp_15" },
            new() { Id = "monument", Name = "Monument", ProductionCost = 60,
                    Yields = new BuildingYields { Culture = 2 } },
        };
        var catalog = new DataCatalog(
            new System.Collections.Generic.List<UnitData> { TestWorlds.Warrior() }, buildings);
        var state = new GameState(TestWorlds.FlatMap(10, 10), catalog);
        human = state.AddPlayer(new Player { Id = 0, Name = "P0", IsHuman = true });
        return state;
    }

    [Fact]
    public void Barracks_GrantsBonusXpToNewUnits()
    {
        var state = StateWithBuildings(out var human);
        var city  = new City("Rome", human, new Vector2I(5, 5)) { ProductionItem = "unit:warrior" };
        city.Buildings.Add("barracks");
        state.Cities.Add(city);

        Assert.NotNull(state.RushProduction(city));

        var trained = Assert.Single(state.Units);
        Assert.Equal(15, trained.Experience); // new_units_bonus_xp_15 → level 1 out of the gate
        Assert.Equal(1, trained.Level);
    }

    [Fact]
    public void Monument_CultureAccumulatesAndFeedsScore()
    {
        var state = StateWithBuildings(out var human);
        var city  = new City("Rome", human, new Vector2I(5, 5));
        city.Buildings.Add("monument");
        state.Cities.Add(city);

        // City base 1 + Monument 2.
        Assert.Equal(3, CivEconomyService.CulturePerTurn(state, human));

        state.EndPlayerTurn(new System.Collections.Generic.List<GameState.ProductionCompletion>());
        Assert.Equal(3, state.Civ(human).CultureAccumulated);
        Assert.Equal(3, city.CultureAccumulated); // also banks toward border growth

        int before = ScoreService.Score(state, human);
        state.Civ(human).CultureAccumulated += 10; // 1 score per CultureDivisor (5)
        Assert.Equal(before + 2, ScoreService.Score(state, human));
    }

    [Fact]
    public void Culture_ExpandsCityBordersAtThreshold()
    {
        var state = StateWithBuildings(out var human);
        var city  = new City("Rome", human, new Vector2I(5, 5));
        city.Buildings.Add("monument"); // 1 base + 2 monument = 3 culture/turn
        state.Cities.Add(city);

        Assert.Equal(City.InitialBorderRadius, city.BorderRadius);
        var far = new Vector2I(8, 5); // distance 3 — outside the initial ring
        Assert.DoesNotContain(far, CityWorkforceService.Workable(state, city));

        // NextBorderCost at radius 2 is 30 → ten turns at 3 culture/turn.
        for (int i = 0; i < 10; i++)
            state.EndPlayerTurn(new System.Collections.Generic.List<GameState.ProductionCompletion>());

        Assert.Equal(City.MaxBorderRadius, city.BorderRadius);
        Assert.Equal(0, city.CultureAccumulated); // 30 banked, 30 spent on the ring
        Assert.Contains(far, CityWorkforceService.Workable(state, city));
    }
}
