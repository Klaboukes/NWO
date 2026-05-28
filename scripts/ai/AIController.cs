using System.Collections.Generic;
using System.Linq;
using Godot;
using NWO.Core;
using NWO.Entities;
using NWO.Map;

namespace NWO.AI;

// Reactive AI: each unit tries to attack an in-range enemy, otherwise settles
// (if it's a Settler on a foundable tile), otherwise steps toward the nearest
// enemy unit or city. Cities auto-queue Warriors when idle.
//
// MVP simplifications:
//   - AI sees through fog (iterates GameState.Units directly).
//     TODO: gate by per-AI FogOfWar when difficulty levels are added.
//   - AI moves resolve instantly — no MovementAnimator interleave.
public class AIController
{
    private readonly GameState _state;

    public AIController(GameState state) => _state = state;

    public void TakeTurn(Player ai)
    {
        // Snapshot the unit list — combat can remove units mid-iteration.
        foreach (var unit in _state.Units.Where(u => u.Owner == ai).ToList())
        {
            if (!_state.Units.Contains(unit)) continue; // killed earlier this loop
            Act(ai, unit);
        }

        foreach (var city in _state.Cities)
            if (city.Owner == ai && city.ProductionItem == null)
                city.ProductionItem = "unit:warrior";
    }

    private void Act(Player ai, Unit unit)
    {
        // 1. Enemy in attack range? attack.
        var target = NearestEnemyInRange(ai, unit);
        if (target != null)
        {
            _state.TryAttack(unit, target);
            return;
        }

        // 2. Settler? try to found on the current tile.
        if (unit.Data.Special == "found_city")
        {
            if (_state.TryFoundCity(unit, out _) == GameState.FoundCityResult.Success)
                return;
            // fall through: walk toward an enemy or the map centre to look for a site
        }

        // 3. Otherwise step toward the nearest enemy unit/city.
        var goal = NearestEnemyOrCityPosition(ai, unit.Position);
        if (goal.HasValue) StepToward(unit, goal.Value);
    }

    private Unit? NearestEnemyInRange(Player ai, Unit unit)
    {
        if (unit.Data.Attack <= 0) return null;

        Unit? best = null;
        int   bestDist = int.MaxValue;
        foreach (var other in _state.Units)
        {
            if (other.Owner == ai) continue;
            int d = HexGrid.Distance(unit.Position, other.Position);
            if (d <= 0 || d > unit.Data.Range) continue;
            if (d < bestDist) { best = other; bestDist = d; }
        }
        return best;
    }

    private Vector2I? NearestEnemyOrCityPosition(Player ai, Vector2I from)
    {
        Vector2I? best = null;
        int       bestDist = int.MaxValue;

        foreach (var other in _state.Units)
        {
            if (other.Owner == ai) continue;
            int d = HexGrid.Distance(from, other.Position);
            if (d < bestDist) { best = other.Position; bestDist = d; }
        }
        foreach (var city in _state.Cities)
        {
            if (city.Owner == ai) continue;
            int d = HexGrid.Distance(from, city.Position);
            if (d < bestDist) { best = city.Position; bestDist = d; }
        }
        return best;
    }

    private void StepToward(Unit unit, Vector2I goal)
    {
        if (unit.MovementRemaining <= 0) return;
        if (unit.Position == goal)       return;

        var path = HexGrid.FindPath(unit.Position, goal, _state.MovementCost);
        if (path.Count < 2) return;

        int budget = unit.MovementRemaining;
        Vector2I last = unit.Position;
        int spent = 0;

        for (int i = 1; i < path.Count; i++)
        {
            int cost = _state.MovementCost(path[i]);
            if (cost == int.MaxValue) break;

            // Don't walk onto another unit's tile (enemies blocked, friendlies overlap forbidden).
            if (_state.Units.Any(u => u != unit && u.Position == path[i])) break;

            if (spent + cost > budget) break;
            spent += cost;
            last   = path[i];
        }

        unit.Position          = last;
        unit.MovementRemaining = Mathf.Max(0, unit.MovementRemaining - spent);
    }
}
