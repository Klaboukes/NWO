using System.Collections.Generic;
using System.Linq;
using Godot;
using NWO.AI;
using NWO.Entities;
using NWO.Map;

namespace NWO.Core;

// Headless gameplay driver. Owns the GameState + AIController + end-of-turn
// orchestration that's shared between the live scene (WorldMap) and scenario
// tests. No Godot scene concerns live here — no animation, camera, input, or UI.
//
// Two contracts:
//   1. Player actions (Move, Attack, FoundCity) mutate state immediately and
//      return a result the caller can inspect. Animation is the caller's
//      problem; tests skip it, the scene drives MovementAnimator on top.
//   2. EndTurn() advances the turn loop: ends the viewer's turn, runs each
//      AI player's turn synchronously, and stops when control returns to the
//      viewer. Returns the notification stream so callers can display or
//      assert on it.
public class GameSession
{
    public GameState    State  { get; }
    public Player       Viewer { get; }
    public AIController AI     { get; }

    public FogOfWar ViewerFog => State.Fog(Viewer);

    public GameSession(GameState state, Player viewer)
    {
        State  = state;
        Viewer = viewer;
        AI     = new AIController(state);
    }

    // ── Player actions ───────────────────────────────────────────────────────

    public record MoveResult(
        bool           Success,
        List<Vector2I> Path,
        City?          CapturedOnArrival);

    // Mirrors WorldMap.HandleRightPress's move branch. Computes a path treating
    // enemy units as blockers, deducts movement cost, applies the final
    // position, and identifies an enemy city the unit landed on. The capture
    // itself is applied via ResolveCapture so the live scene can defer it to
    // animation-complete; tests can use MoveAndResolve to fold both into one
    // call.
    public MoveResult TryMove(Unit unit, Vector2I dest)
    {
        if (unit.Owner != Viewer)               return Failed();
        if (!State.Map.Tiles.ContainsKey(dest)) return Failed();

        WakeIfFortified(unit);

        var path = HexGrid.FindPath(unit.Position, dest, t => MoveCostFor(unit, t));
        if (path.Count < 2) return Failed();

        int cost = 0;
        for (int i = 1; i < path.Count; i++)
            cost += State.MovementCost(path[i]);

        unit.MovementRemaining = Mathf.Max(0, unit.MovementRemaining - cost);
        unit.Position          = path[^1];
        unit.ActedThisTurn     = true;
        State.RecomputeFog(Viewer);

        // Only a conquerable (HP-depleted) enemy city is captured on arrival.
        var pendingCapture = State.Cities.Find(
            c => c.Position == dest && c.Owner != Viewer && c.IsConquerable);
        return new MoveResult(true, path, pendingCapture);

        static MoveResult Failed() => new(false, new List<Vector2I>(), null);
    }

    // Cost to enter `tile` for `unit`, or int.MaxValue if blocked. Enemy units
    // block; an enemy city blocks unless it's conquerable and `unit` is a melee
    // unit that can capture it. Shared by the move path, the reachable overlay,
    // and the path preview so they all agree.
    public int MoveCostFor(Unit unit, Vector2I tile)
    {
        if (State.Units.Any(u => u.Position == tile && u.Owner != unit.Owner))
            return int.MaxValue;
        var enemyCity = State.Cities.Find(c => c.Position == tile && c.Owner != unit.Owner);
        if (enemyCity != null && !(enemyCity.IsConquerable && CanCapture(unit)))
            return int.MaxValue;
        return State.MovementCost(tile);
    }

    // Only melee combat units (Attack > 0, not ranged) may capture a city.
    public static bool CanCapture(Unit unit) => unit.Data.Attack > 0 && unit.Data.Range < 2;

    // Applied by the scene when the move animation finishes; tests call inline.
    public void ResolveCapture(Unit captor, City city)
        => State.CaptureCity(captor, city);

    // Test convenience: move + immediate capture in one call.
    public MoveResult MoveAndResolve(Unit unit, Vector2I dest)
    {
        var r = TryMove(unit, dest);
        if (r.Success && r.CapturedOnArrival != null)
            ResolveCapture(unit, r.CapturedOnArrival);
        return r;
    }

    public GameState.AttackResult TryAttack(Unit attacker, Unit target)
    {
        if (attacker.Owner != Viewer) return Invalid();

        WakeIfFortified(attacker);
        var result = State.TryAttack(attacker, target);
        if (result.Outcome != GameState.AttackOutcome.Invalid)
            State.RecomputeFog(Viewer);
        return result;

        static GameState.AttackResult Invalid()
            => new(GameState.AttackOutcome.Invalid, 0, 0);
    }

    public GameState.CityAttackResult TryAttackCity(Unit attacker, City city)
    {
        if (attacker.Owner != Viewer)
            return new GameState.CityAttackResult(false, 0, 0, false, false);

        WakeIfFortified(attacker);
        var result = State.TryAttackCity(attacker, city);
        if (result.Success)
            State.RecomputeFog(Viewer);
        return result;
    }

    public GameState.FoundCityResult TryFoundCity(Unit settler, out City? city)
    {
        city = null;
        if (settler.Owner != Viewer) return GameState.FoundCityResult.BadTerrain;
        WakeIfFortified(settler);
        var r = State.TryFoundCity(settler, out city);
        if (r == GameState.FoundCityResult.Success)
            State.RecomputeFog(Viewer);
        return r;
    }

    public void Fortify(Unit unit)
    {
        if (unit.Owner != Viewer) return;
        unit.Fortified         = true;
        unit.MovementRemaining = 0;
    }

    // ── End-of-turn ──────────────────────────────────────────────────────────

    public record EndTurnSummary(
        List<string>                         Notifications,
        List<GameState.ProductionCompletion> Completions);

    // Ends the viewer's turn, then runs every AI player's turn synchronously,
    // stopping when control returns to the viewer.
    public EndTurnSummary EndTurn()
    {
        var completions   = new List<GameState.ProductionCompletion>();
        var notifications = State.EndPlayerTurn(completions);

        while (!State.CurrentPlayer.IsHuman)
        {
            AI.TakeTurn(State.CurrentPlayer);
            notifications.AddRange(State.EndPlayerTurn(completions));
        }

        State.RecomputeFog(Viewer);
        return new EndTurnSummary(notifications, completions);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void WakeIfFortified(Unit unit)
    {
        if (!unit.Fortified) return;
        unit.Fortified         = false;
        unit.MovementRemaining = unit.Data.Movement;
    }
}
