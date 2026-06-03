using Godot;
using NWO.Core;
using NWO.Entities;
using Xunit;

namespace NWO.Tests;

// Phase 10.5 — the "Establish the New World Order" objective victory: holding every
// key site wins, regardless of the turn or whether rivals survive.
public class KeySiteVictoryTests
{
    [Fact]
    public void Controller_IsNearestCityOwnerWithinRadius()
    {
        var session = TestWorlds.StandardSession(out var human, out var ai);
        var state   = session.State;
        var site    = new Vector2I(10, 10);
        state.Map.KeySites.Add(site);

        Assert.Null(KeySiteService.Controller(state, site)); // no city near → uncontested

        state.Cities.Add(new City("Far", ai, new Vector2I(0, 0)));         // out of range
        Assert.Null(KeySiteService.Controller(state, site));

        state.Cities.Add(new City("Near", human, new Vector2I(11, 10)));   // within radius
        Assert.Equal(human, KeySiteService.Controller(state, site));
    }

    [Fact]
    public void ObjectiveVictory_FiresWhenOnePlayerHoldsAllSites()
    {
        var session = TestWorlds.StandardSession(out var human, out var ai);
        var state   = session.State;

        // Keep the AI alive (a city) so domination can't fire and mask the objective.
        state.Cities.Add(new City("AICity", ai, new Vector2I(0, 0)));

        state.Map.KeySites.Add(new Vector2I(10, 10));
        state.Map.KeySites.Add(new Vector2I(14, 12));
        // Human cities adjacent to both sites.
        state.Cities.Add(new City("A", human, new Vector2I(10, 11)));
        state.Cities.Add(new City("B", human, new Vector2I(14, 13)));

        var result = VictoryService.Evaluate(state);

        Assert.NotNull(result);
        Assert.Equal(VictoryService.VictoryType.Objective, result!.Type);
        Assert.Equal(human, result.Winner);
    }

    [Fact]
    public void ObjectiveVictory_DoesNotFireWhileSitesAreSplit()
    {
        var session = TestWorlds.StandardSession(out var human, out var ai);
        var state   = session.State;

        state.Map.KeySites.Add(new Vector2I(10, 10));
        state.Map.KeySites.Add(new Vector2I(14, 12));
        state.Cities.Add(new City("H", human, new Vector2I(10, 11))); // human holds one
        state.Cities.Add(new City("A", ai,    new Vector2I(14, 13))); // AI holds the other

        var result = VictoryService.Evaluate(state);
        Assert.True(result == null || result.Type != VictoryService.VictoryType.Objective);
    }

    [Fact]
    public void KeySiteControl_AddsToScore()
    {
        var session = TestWorlds.StandardSession(out var human, out _);
        var state   = session.State;
        var city    = new City("Cap", human, new Vector2I(10, 11));
        state.Cities.Add(city);

        int before = ScoreService.Score(state, human);
        state.Map.KeySites.Add(new Vector2I(10, 10)); // now controlled by the city
        int after  = ScoreService.Score(state, human);

        Assert.Equal(before + ScoreService.PerKeySite, after);
    }
}
