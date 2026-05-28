using System.Collections.Generic;
using Godot;
using NWO.Core;
using NWO.Entities;
using NWO.Map;
using Xunit;

namespace NWO.Tests;

public class TurnLoopTests
{
    private static GameState MakeState(out Player human, out Player ai)
    {
        var map = new MapData(10, 10);
        for (int q = 0; q < 10; q++)
        for (int r = 0; r < 10; r++)
            map.Tiles[new Vector2I(q, r)] = TerrainType.Plains;

        var catalog = new DataCatalog(new List<UnitData>(), new List<BuildingData>());
        var state   = new GameState(map, catalog);
        human       = state.AddPlayer(new Player { Id = 0, Name = "P0", IsHuman = true  });
        ai          = state.AddPlayer(new Player { Id = 1, Name = "P1", IsHuman = false });
        return state;
    }

    private static UnitData WarriorData() => new()
    {
        Id = "warrior", Name = "Warrior", Attack = 8, Defense = 8, Movement = 2, Range = 1,
    };

    [Fact]
    public void EndPlayerTurn_AdvancesPlayerIndex()
    {
        var state = MakeState(out _, out _);
        Assert.Equal(0, state.CurrentPlayerIndex);

        state.EndPlayerTurn(new List<GameState.ProductionCompletion>());

        Assert.Equal(1, state.CurrentPlayerIndex);
    }

    [Fact]
    public void EndPlayerTurn_OnlyResetsCurrentPlayerUnits()
    {
        var state = MakeState(out var human, out var ai);
        var humanUnit = new Unit(WarriorData(), human, new Vector2I(0, 0)) { MovementRemaining = 0 };
        var aiUnit    = new Unit(WarriorData(), ai,    new Vector2I(5, 5)) { MovementRemaining = 0 };
        state.Units.Add(humanUnit);
        state.Units.Add(aiUnit);

        state.EndPlayerTurn(new List<GameState.ProductionCompletion>());

        Assert.Equal(WarriorData().Movement, humanUnit.MovementRemaining);
        Assert.Equal(0,                       aiUnit.MovementRemaining);
    }

    [Fact]
    public void EndPlayerTurn_AdvancesTurnNumber_OnlyAfterAllPlayers()
    {
        var state = MakeState(out _, out _);
        int startTurn = state.TurnManager.TurnNumber;

        state.EndPlayerTurn(new List<GameState.ProductionCompletion>());
        Assert.Equal(startTurn, state.TurnManager.TurnNumber);

        state.EndPlayerTurn(new List<GameState.ProductionCompletion>());
        Assert.Equal(startTurn + 1, state.TurnManager.TurnNumber);
    }

    [Fact]
    public void EndPlayerTurn_WrapsBackToFirstPlayer()
    {
        var state = MakeState(out _, out _);
        state.EndPlayerTurn(new List<GameState.ProductionCompletion>());
        state.EndPlayerTurn(new List<GameState.ProductionCompletion>());

        Assert.Equal(0, state.CurrentPlayerIndex);
    }

    [Fact]
    public void EndPlayerTurn_ProcessesOnlyCurrentPlayerCities()
    {
        var state = MakeState(out var human, out var ai);
        var humanCity = new City("H", human, new Vector2I(2, 2)) { Population = 1, FoodYield = 5, ProductionYield = 1 };
        var aiCity    = new City("A", ai,    new Vector2I(7, 7)) { Population = 1, FoodYield = 5, ProductionYield = 1 };
        state.Cities.Add(humanCity);
        state.Cities.Add(aiCity);

        state.EndPlayerTurn(new List<GameState.ProductionCompletion>()); // human's turn ends

        Assert.True(humanCity.FoodAccumulated > 0);
        Assert.Equal(0, aiCity.FoodAccumulated);
    }
}
