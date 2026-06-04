using System.Collections.Generic;
using System.Linq;
using Godot;
using NWO.AI;
using NWO.Core;
using NWO.Entities;
using NWO.Map;
using Xunit;

namespace NWO.Tests;

// Cross-continent AI (Phase 13): the AI ferries land units across water with its
// transports — boarding stranded troops and landing them on enemy shores.
public class AINavalTests
{
    private static UnitData WarriorData() => new()
    {
        Id = "warrior", Name = "Warrior", Attack = 8, Defense = 8, Movement = 2, Range = 1, Sight = 2,
    };

    private static UnitData TransportData() => new()
    {
        Id = "transport", Name = "Transport", Attack = 0, Defense = 2, Movement = 4, Range = 0,
        Sight = 2, IsNaval = true, CargoCapacity = 2,
    };

    // Two land continents (cols 0–3 and 6–9) split by a water channel (cols 4–5).
    private static GameState TwoContinentState(out Player human, out Player ai)
    {
        var map = new MapData(10, 6);
        for (int q = 0; q < 10; q++)
        for (int r = 0; r < 6; r++)
            map.Tiles[new Vector2I(q, r)] =
                (q is 4 or 5) ? TerrainType.Coast : TerrainType.Plains;

        var catalog = new DataCatalog(new List<UnitData> { WarriorData(), TransportData() },
                                      new List<BuildingData>());
        var state = new GameState(map, catalog);
        human = state.AddPlayer(new Player { Id = 0, Name = "P0", IsHuman = true  });
        ai    = state.AddPlayer(new Player { Id = 1, Name = "P1", IsHuman = false });
        return state;
    }

    [Fact]
    public void Transport_AdjacentStrandedUnit_BoardsIt()
    {
        var state = TwoContinentState(out var human, out var ai);

        // AI warrior on the left shore; AI transport on the water beside it.
        var warrior   = new Unit(WarriorData(),  ai, new Vector2I(3, 2));
        var transport = new Unit(TransportData(), ai, new Vector2I(4, 2)); // E neighbour, water
        state.Units.Add(warrior);
        state.Units.Add(transport);
        // The only enemy is across the channel, so the warrior has no overland target.
        state.Cities.Add(new City("Enemyburg", human, new Vector2I(7, 2)));

        new AIController(state).TakeTurn(ai);

        Assert.DoesNotContain(warrior, state.Units);   // left the map…
        Assert.Contains(warrior, transport.Cargo);     // …and is aboard the transport
    }

    [Fact]
    public void LoadedTransport_AdjacentToEnemyShore_LandsTroops()
    {
        var state = TwoContinentState(out var human, out var ai);

        // Transport sits on the channel beside the right continent, carrying a warrior.
        var transport = new Unit(TransportData(), ai, new Vector2I(5, 2));
        var cargo     = new Unit(WarriorData(),   ai, new Vector2I(5, 2));
        transport.Cargo.Add(cargo);
        state.Units.Add(transport);
        // Enemy city on the right continent makes that landmass a valid landing target.
        state.Cities.Add(new City("Enemyburg", human, new Vector2I(8, 2)));

        new AIController(state).TakeTurn(ai);

        Assert.Empty(transport.Cargo);                                  // disembarked
        Assert.Contains(cargo, state.Units);                           // back on the map
        Assert.True(cargo.Position.X >= 6, $"landed on the enemy continent, at {cargo.Position}");
    }

    [Fact]
    public void EmptyTransport_SailsTowardStrandedFriendly()
    {
        var state = TwoContinentState(out var human, out var ai);

        // Stranded warrior on the left shore; empty transport out in the channel.
        var warrior   = new Unit(WarriorData(),  ai, new Vector2I(3, 2));
        var transport = new Unit(TransportData(), ai, new Vector2I(5, 0));
        state.Units.Add(warrior);
        state.Units.Add(transport);
        state.Cities.Add(new City("Enemyburg", human, new Vector2I(7, 2)));

        int before = HexGrid.Distance(transport.Position, warrior.Position);
        new AIController(state).TakeTurn(ai);
        int after = HexGrid.Distance(transport.Position, warrior.Position);

        Assert.True(after < before, $"transport should close on the stranded unit, {before} -> {after}");
    }

    [Fact]
    public void Transport_DoesNotLandOnAFriendlyOrEmptyShore()
    {
        var state = TwoContinentState(out _, out var ai);

        // Loaded transport beside the right continent, but no enemy anywhere on it.
        var transport = new Unit(TransportData(), ai, new Vector2I(5, 2));
        var cargo     = new Unit(WarriorData(),   ai, new Vector2I(5, 2));
        transport.Cargo.Add(cargo);
        state.Units.Add(transport);

        new AIController(state).TakeTurn(ai);

        // Nothing to land on (no enemy soil) — the troops stay aboard.
        Assert.Contains(cargo, transport.Cargo);
        Assert.DoesNotContain(cargo, state.Units);
    }
}
