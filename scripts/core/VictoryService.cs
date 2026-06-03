using NWO.Entities;

namespace NWO.Core;

// Headless win-condition evaluation. Pure logic over GameState so it's unit-
// testable without a scene: GameSession.EndTurn calls Evaluate after the AI loop
// and surfaces the result; the scene routes to the victory screen when it's set.
//
// Three ways a game ends:
//   • Objective  — one player holds every key site ("Establish the New World Order").
//   • Domination — every rival has been eliminated; the lone survivor wins.
//   • Score      — at the turn cap the game is called and the highest score wins.
public static class VictoryService
{
    // The game is force-scored at the start of this turn if nobody has won by
    // objective or domination yet. Phase 10.5 shortened this from the MVP's 500 so
    // the faction war resolves decisively.
    public const int ScoreVictoryTurn = 250;

    public enum VictoryType { Objective, Domination, Score }

    public record GameResult(Player Winner, VictoryType Type, int Score);

    // A player is out of the game once they own no cities AND hold no unit that
    // could found one. The settler clause guards the opening turns, where nobody
    // has founded yet but everyone still has their starting Settler — without it
    // every game would "end" on turn 1.
    public static bool IsEliminated(GameState state, Player player)
    {
        foreach (var city in state.Cities)
            if (city.Owner == player) return false;
        foreach (var unit in state.Units)
            if (unit.Owner == player && unit.Data.Special == "found_city") return false;
        return true;
    }

    // Returns the triggered result, or null if the game continues. Domination is
    // checked first (it can fire any turn); the score limit only applies once no
    // single player is left standing.
    public static GameResult? Evaluate(GameState state)
    {
        // Objective victory can fire on any turn: one player holding every key site.
        foreach (var p in state.Players)
            if (KeySiteService.ControlsAll(state, p))
                return new GameResult(p, VictoryType.Objective, ScoreService.Score(state, p));

        if (state.Players.Count > 1)
        {
            Player? survivor = null;
            int alive = 0;
            foreach (var p in state.Players)
            {
                if (IsEliminated(state, p)) continue;
                alive++;
                survivor = p;
            }
            if (alive == 1 && survivor != null)
                return new GameResult(survivor, VictoryType.Domination, ScoreService.Score(state, survivor));
        }

        if (state.TurnManager.TurnNumber >= ScoreVictoryTurn)
        {
            Player? leader = null;
            int best = int.MinValue;
            foreach (var p in state.Players)
            {
                int score = ScoreService.Score(state, p);
                if (score > best) { best = score; leader = p; }
            }
            if (leader != null)
                return new GameResult(leader, VictoryType.Score, best);
        }

        return null;
    }
}
