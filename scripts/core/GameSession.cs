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
        unit.CurrentTask = null; // moving cancels an in-progress build

        var path = HexGrid.FindPath(unit.Position, dest, t => MoveCostFor(unit, t));
        if (path.Count < 2) return Failed();

        int cost = 0;
        for (int i = 1; i < path.Count; i++)
            cost += State.MovementCost(path[i], unit);

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
        return State.MovementCost(tile, unit);
    }

    // Only melee combat units (Attack > 0, not ranged) may capture a city, and
    // only if their type allows it — the Scout is a recon unit that can't take cities.
    public static bool CanCapture(Unit unit) =>
        unit.Data.Attack > 0 && unit.Data.Range < 2 && unit.Data.CanCaptureCities;

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

    // [H] order: fortify and keep sleeping until HP is full, then auto-wake (see
    // GameState.HealUnit). If already at full HP it's just a plain fortify.
    public void FortifyUntilHealed(Unit unit)
    {
        if (unit.Owner != Viewer) return;
        unit.Fortified         = true;
        unit.MovementRemaining = 0;
        unit.SleepUntilHealed  = unit.HP < Unit.MaxHP;
    }

    // Order a Worker to build an improvement on its current tile. Validates the
    // build (terrain/tech/duplicate) and commits the unit's move for this turn.
    public bool TryStartImprovement(Unit worker, ImprovementType type)
    {
        if (worker.Owner != Viewer)                        return false;
        if (worker.Data.Special != "build_improvement")    return false;
        if (!ImprovementService.CanBuild(State, Viewer, worker.Position, type)) return false;

        worker.Fortified        = false;
        worker.SleepUntilHealed = false;
        worker.CurrentTask      = new ImprovementTask(
            worker.Position, type, ImprovementService.BuildTurns(type));
        worker.MovementRemaining = 0; // committing to the build ends its move this turn
        return true;
    }

    // Board a land unit onto an adjacent friendly transport. The unit is removed
    // from the map and stored in transport.Cargo, costing the unit all remaining
    // movement. The transport must have capacity and be on a Coast/Ocean tile
    // adjacent to the unit (max distance 1).
    public bool TryLoad(Unit landUnit, Unit transport)
    {
        if (landUnit.Owner  != Viewer)                         return false;
        if (transport.Owner != Viewer)                         return false;
        if (landUnit.Data.IsNaval)                             return false;
        if (transport.Data.CargoCapacity <= 0)                 return false;
        if (transport.Cargo.Count >= transport.Data.CargoCapacity) return false;
        if (HexGrid.Distance(landUnit.Position, transport.Position) > 1) return false;
        if (!State.Map.Tiles.TryGetValue(transport.Position, out var tt)) return false;
        if (tt != TerrainType.Ocean && tt != TerrainType.Coast) return false;

        WakeIfFortified(landUnit);
        State.Units.Remove(landUnit);
        landUnit.MovementRemaining = 0;
        landUnit.ActedThisTurn     = true;
        transport.Cargo.Add(landUnit);
        return true;
    }

    // Disembark one cargo unit from a transport onto an adjacent land tile. The
    // unit is placed at destTile with 0 movement. The transport spends all its
    // remaining movement to complete the unload.
    public bool TryUnload(Unit transport, Unit cargoUnit, Vector2I destTile)
    {
        if (transport.Owner != Viewer)                return false;
        if (transport.Data.CargoCapacity <= 0)        return false;
        if (!transport.Cargo.Contains(cargoUnit))     return false;
        if (HexGrid.Distance(transport.Position, destTile) != 1) return false;
        if (!State.Map.Tiles.TryGetValue(destTile, out var dt)) return false;
        if (dt == TerrainType.Ocean || dt == TerrainType.Coast || dt == TerrainType.Mountain)
            return false;
        if (State.Units.Any(u => u.Position == destTile && u.Owner != Viewer)) return false;

        transport.Cargo.Remove(cargoUnit);
        cargoUnit.Position          = destTile;
        cargoUnit.MovementRemaining = 0;
        cargoUnit.ActedThisTurn     = true;
        State.Units.Add(cargoUnit);
        transport.MovementRemaining = 0;
        transport.ActedThisTurn     = true;
        State.RecomputeFog(Viewer);
        return true;
    }

    // Rush-buy the city's current production with gold. Fails if the city isn't
    // the viewer's, nothing is producing, or the treasury can't cover the cost.
    public bool TryBuyProduction(City city, out GameState.ProductionCompletion? completion)
    {
        completion = null;
        if (city.Owner != Viewer)       return false;
        if (city.ProductionItem == null) return false;

        int price = CivEconomyService.BuyCost(State, city);
        var civ   = State.Civ(Viewer);
        if (price <= 0 || civ.Treasury < price) return false;

        civ.Treasury -= price;
        completion    = State.RushProduction(city);
        return true;
    }

    // ── End-of-turn ──────────────────────────────────────────────────────────

    public record EndTurnSummary(
        List<GameEvent>                      Notifications,
        List<GameState.ProductionCompletion> Completions,
        VictoryService.GameResult?           Result);

    // Ends the viewer's turn, then runs every AI player's turn synchronously,
    // stopping when control returns to the viewer. After the loop it checks for a
    // win/loss; a non-null Result means the game is over and the caller should
    // route to the result screen.
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
        return new EndTurnSummary(notifications, completions, VictoryService.Evaluate(State));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void WakeIfFortified(Unit unit)
    {
        if (!unit.Fortified) return;
        unit.Fortified         = false;
        unit.SleepUntilHealed  = false; // a manual order overrides "sleep until healed"
        unit.MovementRemaining = unit.Data.Movement;
    }
}
