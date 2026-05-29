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
}
