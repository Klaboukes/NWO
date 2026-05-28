using System.Collections.Generic;
using Godot;
using NWO.Core;
using NWO.Entities;
using NWO.Map;
using Xunit;

namespace NWO.Tests;

public class CityCaptureTests
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
    public void CaptureCity_ChangesOwner()
    {
        var state = MakeState(out var human, out var ai);
        var city  = new City("Rome", ai, new Vector2I(3, 3));
        var captor = new Unit(WarriorData(), human, new Vector2I(3, 3));
        state.Cities.Add(city);

        state.CaptureCity(captor, city);

        Assert.Same(human, city.Owner);
    }

    [Fact]
    public void CaptureCity_ClearsProduction()
    {
        var state = MakeState(out var human, out var ai);
        var city  = new City("Rome", ai, new Vector2I(3, 3))
        {
            ProductionItem     = "unit:warrior",
            ProductionProgress = 15,
        };
        var captor = new Unit(WarriorData(), human, new Vector2I(3, 3));
        state.Cities.Add(city);

        state.CaptureCity(captor, city);

        Assert.Null(city.ProductionItem);
        Assert.Equal(0, city.ProductionProgress);
    }
}
