using Godot;
using NWO.Core;
using NWO.Entities;
using Xunit;

namespace NWO.Tests;

public class EventVisibilityFilterTests
{
    private static readonly Vector2I OwnCityPos   = new(2, 2);
    private static readonly Vector2I EnemyCityPos = new(12, 12);

    // A session with the human's own city and an enemy city placed on the map.
    private static GameState WorldWithCities(out Player human)
    {
        var session = TestWorlds.StandardSession(out human, out var ai);
        var state   = session.State;
        state.Cities.Add(new City("Home",  human, OwnCityPos));
        state.Cities.Add(new City("Enemy", ai,    EnemyCityPos));
        return state;
    }

    [Fact]
    public void TilelessEvent_PassesThrough()
    {
        var state = WorldWithCities(out var human);
        var result = EventVisibilityFilter.ForViewer(
            new[] { new GameEvent("Researched Pottery!") }, state, human, state.Fog(human));

        Assert.Single(result);
        Assert.Null(result[0].Focus);
    }

    [Fact]
    public void OwnCityEvent_PassesThroughWithFocus()
    {
        var state = WorldWithCities(out var human);
        var result = EventVisibilityFilter.ForViewer(
            new[] { new GameEvent("Home grew!", OwnCityPos, GameEventKind.CityGrew) },
            state, human, state.Fog(human));

        Assert.Single(result);
        Assert.Equal(OwnCityPos, result[0].Focus);
    }

    [Fact]
    public void EnemyCityGrowth_IsHidden()
    {
        var state = WorldWithCities(out var human);
        state.Fog(human).Discovered.Add(EnemyCityPos); // even if seen, growth stays hidden

        var result = EventVisibilityFilter.ForViewer(
            new[] { new GameEvent("Enemy grew!", EnemyCityPos, GameEventKind.CityGrew) },
            state, human, state.Fog(human));

        Assert.Empty(result);
    }

    [Fact]
    public void EnemyCityEvent_Undiscovered_StripsFocus()
    {
        var state = WorldWithCities(out var human); // EnemyCityPos not discovered

        var result = EventVisibilityFilter.ForViewer(
            new[] { new GameEvent("Enemy completed Warrior!", EnemyCityPos, GameEventKind.CityProduced) },
            state, human, state.Fog(human));

        Assert.Single(result);
        Assert.Null(result[0].Focus); // shown, but not clickable to its location
    }

    [Fact]
    public void EnemyCityEvent_Discovered_KeepsFocus()
    {
        var state = WorldWithCities(out var human);
        state.Fog(human).Discovered.Add(EnemyCityPos);

        var result = EventVisibilityFilter.ForViewer(
            new[] { new GameEvent("Enemy completed Warrior!", EnemyCityPos, GameEventKind.CityProduced) },
            state, human, state.Fog(human));

        Assert.Single(result);
        Assert.Equal(EnemyCityPos, result[0].Focus);
    }
}
