using System.Collections.Generic;
using Godot;
using NWO.Core;
using NWO.Entities;
using NWO.Map;
using Xunit;

namespace NWO.Tests;

public class FogOfWarTests
{
    private static MapData MakeMap(int w, int h)
    {
        var m = new MapData(w, h);
        for (int q = -w; q <= w; q++)
            for (int r = -h; r <= h; r++)
                m.Tiles[new Vector2I(q, r)] = TerrainType.Plains;
        return m;
    }

    private static UnitData ScoutDef(int sight = 2) => new()
    {
        Id = "scout", Name = "Scout", Movement = 2, Sight = sight,
    };

    [Fact]
    public void Recompute_NoUnitsOrCities_RevealsNothing()
    {
        var fog = new FogOfWar();
        fog.Recompute(new Player(), new List<Unit>(), new List<City>(), MakeMap(10, 10), 2);
        Assert.Empty(fog.Visible);
        Assert.Empty(fog.Discovered);
    }

    [Fact]
    public void Recompute_RevealsTilesInSightOfOwnedUnit()
    {
        var p = new Player { Id = 1 };
        var u = new Unit(ScoutDef(2), p, new Vector2I(0, 0));
        var fog = new FogOfWar();
        fog.Recompute(p, new[] { u }, new List<City>(), MakeMap(10, 10), 2);

        // Disc(2) has 19 tiles
        Assert.Equal(19, fog.Visible.Count);
        Assert.Contains(new Vector2I(0, 0), fog.Visible);
        Assert.Contains(new Vector2I(2, 0), fog.Visible);
        Assert.DoesNotContain(new Vector2I(3, 0), fog.Visible);
    }

    [Fact]
    public void Recompute_IgnoresOtherPlayersUnits()
    {
        var me  = new Player { Id = 0 };
        var foe = new Player { Id = 1 };
        var foeUnit = new Unit(ScoutDef(2), foe, new Vector2I(0, 0));
        var fog = new FogOfWar();
        fog.Recompute(me, new[] { foeUnit }, new List<City>(), MakeMap(10, 10), 2);
        Assert.Empty(fog.Visible);
    }

    [Fact]
    public void Recompute_DiscoveredIsMonotonic()
    {
        var p   = new Player { Id = 0 };
        var u   = new Unit(ScoutDef(1), p, new Vector2I(0, 0));
        var fog = new FogOfWar();
        var map = MakeMap(10, 10);

        fog.Recompute(p, new[] { u }, new List<City>(), map, 2);
        int afterFirst = fog.Discovered.Count;

        // Move unit far away; previously discovered tiles must remain discovered
        u.Position = new Vector2I(5, 0);
        fog.Recompute(p, new[] { u }, new List<City>(), map, 2);

        Assert.Contains(new Vector2I(0, 0), fog.Discovered);
        Assert.True(fog.Discovered.Count > afterFirst);
        // But Visible should reflect only new position's range
        Assert.DoesNotContain(new Vector2I(0, 0), fog.Visible);
    }

    [Fact]
    public void Recompute_AnimOverride_UsesOverridePosition()
    {
        var p   = new Player { Id = 0 };
        var u   = new Unit(ScoutDef(1), p, new Vector2I(0, 0));
        var fog = new FogOfWar();
        var animPos = new Vector2I(5, 0);
        var overrides = new Dictionary<Unit, Vector2I> { [u] = animPos };

        fog.Recompute(p, new[] { u }, new List<City>(), MakeMap(10, 10), 2, overrides);

        Assert.Contains(animPos, fog.Visible);
        // The unit's authoritative position (0,0) should NOT be revealed when the
        // animation override is in effect — only the visual position is what matters
        Assert.DoesNotContain(new Vector2I(0, 0), fog.Visible);
    }
}
