using System.Collections.Generic;
using Godot;
using NWO.Core;
using NWO.Entities;
using NWO.Map;
using Xunit;

namespace NWO.Tests;

public class LandmassTests
{
    // Builds a 20×5 map split into two islands by a vertical ocean strip
    // at column 10. Walkable on both sides, no land bridge.
    private static GameState TwoIslandMap()
    {
        var map = new MapData(20, 5);
        for (int q = 0; q < 20; q++)
        for (int r = 0; r < 5; r++)
            map.Tiles[new Vector2I(q, r)] =
                q == 10 ? TerrainType.Ocean : TerrainType.Plains;

        return new GameState(map, TestWorlds.StandardCatalog());
    }

    [Fact]
    public void GetConnectedLandmass_ReturnsOnlyOwnIsland()
    {
        var state    = TwoIslandMap();
        var landmass = state.GetConnectedLandmass(new Vector2I(2, 2));

        // West island: columns 0-9, 5 rows = 50 tiles.
        Assert.Equal(50, landmass.Count);
        Assert.Contains(new Vector2I(0, 0), landmass);
        Assert.Contains(new Vector2I(9, 4), landmass);
        Assert.DoesNotContain(new Vector2I(10, 2), landmass); // ocean
        Assert.DoesNotContain(new Vector2I(15, 2), landmass); // east island
    }

    [Fact]
    public void GetConnectedLandmass_OnImpassableOrigin_ReturnsEmpty()
    {
        var state    = TwoIslandMap();
        var landmass = state.GetConnectedLandmass(new Vector2I(10, 2));

        Assert.Empty(landmass);
    }

    [Fact]
    public void GetConnectedLandmass_OnFlatMap_ReturnsEveryTile()
    {
        var session  = TestWorlds.StandardSession(out _, out _, width: 10, height: 10);
        var landmass = session.State.GetConnectedLandmass(new Vector2I(5, 5));

        Assert.Equal(100, landmass.Count);
    }
}
