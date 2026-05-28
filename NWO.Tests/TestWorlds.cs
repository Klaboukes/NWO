using System.Collections.Generic;
using Godot;
using NWO.Core;
using NWO.Entities;
using NWO.Map;

namespace NWO.Tests;

// Shared deterministic world builders for session + scenario tests. Hand-built
// maps (no MapGenerator / FastNoiseLite) keep the tests free of Godot engine
// init and 100% reproducible across runs.
internal static class TestWorlds
{
    public static UnitData Warrior(int prodCost = 40) => new()
    {
        Id = "warrior", Name = "Warrior",
        Attack = 8, Defense = 8, Movement = 2, Range = 1, Sight = 2,
        ProductionCost = prodCost,
    };

    public static UnitData Settler() => new()
    {
        Id = "settler", Name = "Settler",
        Attack = 0, Defense = 0, Movement = 2, Range = 0, Sight = 2,
        ProductionCost = 100, Special = "found_city",
    };

    public static DataCatalog StandardCatalog()
        => new(new List<UnitData> { Warrior(), Settler() }, new List<BuildingData>());

    // All-Plains rectangular map. Walkable everywhere, foundable everywhere.
    public static MapData FlatMap(int width, int height)
    {
        var map = new MapData(width, height);
        for (int q = 0; q < width; q++)
        for (int r = 0; r < height; r++)
            map.Tiles[new Vector2I(q, r)] = TerrainType.Plains;
        return map;
    }

    // Returns a session with one human + one AI player, a flat map, and the
    // standard catalog. Combat seed is fixed so AI/combat outcomes are
    // deterministic across runs.
    public static GameSession StandardSession(
        out Player human,
        out Player ai,
        int width      = 20,
        int height     = 20,
        int combatSeed = 12345)
    {
        var state = new GameState(FlatMap(width, height), StandardCatalog(), combatSeed);
        human = state.AddPlayer(new Player { Id = 0, Name = "Player",     IsHuman = true  });
        ai    = state.AddPlayer(new Player { Id = 1, Name = "Barbarians", IsHuman = false });
        return new GameSession(state, human);
    }
}
