using Godot;
using NWO.Core;
using NWO.Entities;
using Xunit;

namespace NWO.Tests;

public class VictoryServiceTests
{
    private static void AdvanceToTurn(GameState state, int turn)
    {
        while (state.TurnManager.TurnNumber < turn)
            state.TurnManager.AdvanceTurn();
    }

    [Fact]
    public void Elimination_RequiresNoCityAndNoSettler()
    {
        var session = TestWorlds.StandardSession(out var human, out var ai);
        var state   = session.State;

        // A player with only a Settler (no city yet) is NOT eliminated.
        state.Units.Add(new Unit(TestWorlds.Settler(), ai, new Vector2I(5, 5)));
        Assert.False(VictoryService.IsEliminated(state, ai));

        // Lose the settler with no city to fall back on → eliminated.
        state.Units.Clear();
        Assert.True(VictoryService.IsEliminated(state, ai));

        // A city alone (no settler) still keeps a player in the game.
        state.Cities.Add(new City("Rome", human, new Vector2I(2, 2)));
        Assert.False(VictoryService.IsEliminated(state, human));
    }

    [Fact]
    public void Domination_FiresWhenLoneRivalIsWiped()
    {
        var session = TestWorlds.StandardSession(out var human, out var ai);
        var state   = session.State;

        // Human holds a city; the AI has nothing left at all.
        state.Cities.Add(new City("Rome", human, new Vector2I(5, 5)));

        var result = VictoryService.Evaluate(state);

        Assert.NotNull(result);
        Assert.Equal(VictoryService.VictoryType.Domination, result!.Type);
        Assert.Equal(human, result.Winner);
    }

    [Fact]
    public void OpeningTurns_WithBothSettlers_YieldNoResult()
    {
        var session = TestWorlds.StandardSession(out var human, out var ai);
        var state   = session.State;

        // Turn 1, nobody has founded yet — both still hold their starting Settler.
        state.Units.Add(new Unit(TestWorlds.Settler(), human, new Vector2I(4, 4)));
        state.Units.Add(new Unit(TestWorlds.Settler(), ai,    new Vector2I(8, 8)));

        Assert.Null(VictoryService.Evaluate(state));
    }

    [Fact]
    public void ScoreVictory_FiresAtTurnLimit_ForHigherScorer()
    {
        var session = TestWorlds.StandardSession(out var human, out var ai);
        var state   = session.State;

        // Both alive (so domination can't fire): human has a populous city, AI
        // only a settler. Human therefore out-scores the AI.
        var rome = new City("Rome", human, new Vector2I(5, 5)) { Population = 6 };
        state.Cities.Add(rome);
        state.Units.Add(new Unit(TestWorlds.Settler(), ai, new Vector2I(12, 12)));

        Assert.Null(VictoryService.Evaluate(state)); // not yet — turn limit not reached

        AdvanceToTurn(state, VictoryService.ScoreVictoryTurn);
        var result = VictoryService.Evaluate(state);

        Assert.NotNull(result);
        Assert.Equal(VictoryService.VictoryType.Score, result!.Type);
        Assert.Equal(human, result.Winner);
    }

    [Fact]
    public void ScoreService_RanksBiggerEmpireHigher()
    {
        var session = TestWorlds.StandardSession(out var human, out var ai);
        var state   = session.State;

        state.Cities.Add(new City("Rome",   human, new Vector2I(5, 5)) { Population = 5 });
        state.Cities.Add(new City("Athens", human, new Vector2I(9, 9)) { Population = 3 });
        state.Cities.Add(new City("Sparta", ai,    new Vector2I(2, 2)) { Population = 2 });

        Assert.True(ScoreService.Score(state, human) > ScoreService.Score(state, ai));
    }
}
