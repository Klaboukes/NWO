using System.Collections.Generic;
using Godot;
using NWO.AI;
using NWO.Core;
using NWO.Entities;
using NWO.Map;
using Xunit;

namespace NWO.Tests;

public class AIControllerTests
{
    private static UnitData WarriorData() => new()
    {
        Id = "warrior", Name = "Warrior", Attack = 8, Defense = 8, Movement = 2, Range = 1,
    };

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

    [Fact]
    public void AI_WithEnemyInRange_Attacks()
    {
        var state = MakeState(out var human, out var ai);
        var aiUnit = new Unit(WarriorData(), ai,    new Vector2I(0, 0));
        var hUnit  = new Unit(WarriorData(), human, new Vector2I(1, 0));
        state.Units.Add(aiUnit);
        state.Units.Add(hUnit);

        new AIController(state).TakeTurn(ai);

        // Either it dealt damage or killed the defender.
        Assert.True(hUnit.HP < 100 || !state.Units.Contains(hUnit));
    }

    [Fact]
    public void AI_WithIdleCity_QueuesWarrior()
    {
        var state = MakeState(out _, out var ai);
        var city  = new City("Rome", ai, new Vector2I(5, 5));
        state.Cities.Add(city);

        new AIController(state).TakeTurn(ai);

        Assert.Equal("unit:warrior", city.ProductionItem);
    }

    [Fact]
    public void AI_WithBusyCity_DoesNotOverwriteProduction()
    {
        var state = MakeState(out _, out var ai);
        var city  = new City("Rome", ai, new Vector2I(5, 5)) { ProductionItem = "unit:archer" };
        state.Cities.Add(city);

        new AIController(state).TakeTurn(ai);

        Assert.Equal("unit:archer", city.ProductionItem);
    }

    [Fact]
    public void AI_DoesNotMutateHumanUnits()
    {
        var state = MakeState(out var human, out var ai);
        var hUnit = new Unit(WarriorData(), human, new Vector2I(0, 0));
        state.Units.Add(hUnit);

        new AIController(state).TakeTurn(ai);

        Assert.Equal(new Vector2I(0, 0), hUnit.Position);
        Assert.Equal(WarriorData().Movement, hUnit.MovementRemaining);
    }

    [Fact]
    public void AI_WithNoEnemyInRange_StepsTowardNearestEnemy()
    {
        var state = MakeState(out var human, out var ai);
        var aiUnit = new Unit(WarriorData(), ai,    new Vector2I(0, 0));
        var hUnit  = new Unit(WarriorData(), human, new Vector2I(5, 0));
        state.Units.Add(aiUnit);
        state.Units.Add(hUnit);

        new AIController(state).TakeTurn(ai);

        int distAfter = HexGrid.Distance(aiUnit.Position, hUnit.Position);
        Assert.True(distAfter < 5, $"AI should have closed the gap, distance = {distAfter}");
    }
}
