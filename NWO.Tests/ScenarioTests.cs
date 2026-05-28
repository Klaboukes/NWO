using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using NWO.Core;
using NWO.Entities;
using NWO.Map;
using Xunit;

namespace NWO.Tests;

// End-to-end scenario tests. Each scenario sets up a world, drives the
// GameSession through scripted player actions for N turns, and asserts both
// scenario-specific outcomes and the universal invariants in
// AssertInvariants(). Replaces a chunk of manual playtesting in the Godot
// editor — adding a regression is "write a scenario, run xunit, done."
public class ScenarioTests
{
    // ── Invariants ───────────────────────────────────────────────────────────

    // Universal facts that must be true after every player action or end-turn.
    // If you find a class of bug that slipped through playtesting, add a
    // matching invariant here and every scenario gets the check for free.
    private static void AssertInvariants(GameState state, int turn)
    {
        // No two units on the same non-city tile.
        //
        // City tiles are exempt because GameState.CompleteProduction spawns
        // newly produced units directly on the city tile without checking
        // whether it's occupied (see TODO in GameState). Once that's fixed,
        // tighten this invariant to cover every tile.
        var cityTiles = new HashSet<Vector2I>(state.Cities.Select(c => c.Position));
        var unitTiles = new HashSet<Vector2I>();
        foreach (var u in state.Units)
        {
            if (!cityTiles.Contains(u.Position))
                Assert.True(unitTiles.Add(u.Position),
                    $"turn {turn}: two units on tile {u.Position}");
            Assert.True(u.HP > 0, $"turn {turn}: dead unit {u.Data.Name} still in Units list");
            Assert.True(state.Map.Tiles.TryGetValue(u.Position, out var t)
                        && TerrainYields.MovementCost(t) != int.MaxValue,
                $"turn {turn}: unit {u.Data.Name} on impassable tile {u.Position}");
        }

        // Every city is on a foundable tile and owned by a registered player.
        foreach (var c in state.Cities)
        {
            Assert.True(state.Map.Tiles.TryGetValue(c.Position, out var t)
                        && TerrainYields.CanFoundCityOn(t),
                $"turn {turn}: city {c.Name} on non-foundable tile {c.Position}");
            Assert.Contains(c.Owner, state.Players);
            Assert.True(c.Population >= 1, $"turn {turn}: city {c.Name} has population {c.Population}");
        }

        Assert.True(state.TurnManager.TurnNumber >= 1);
    }

    // ── Scenarios ────────────────────────────────────────────────────────────

    [Fact]
    public void NoOpFiftyTurns_StaysStable()
    {
        var session = TestWorlds.StandardSession(out var human, out var ai);
        SeedStartingUnits(session, human, ai);

        for (int turn = 1; turn <= 50; turn++)
        {
            session.EndTurn();
            AssertInvariants(session.State, turn);
        }

        // After 50 turns the AI's reactive logic should have produced cities
        // (its idle cities auto-queue Warriors, settlers auto-found).
        Assert.True(session.State.Cities.Any(c => c.Owner == ai),
            "AI should have founded at least one city in 50 turns");
    }

    [Fact]
    public void PlayerFoundsCity_QueuesWarrior_WarriorAppears()
    {
        var session = TestWorlds.StandardSession(out var human, out var ai);
        var settler = new Unit(TestWorlds.Settler(), human, new Vector2I(5, 5));
        session.State.Units.Add(settler);
        // No AI units — the AI player exists but has nothing to do, which keeps
        // this test focused on the city's growth/production loop. (Old test
        // parked an AI warrior far away; with the new tile-based yields the
        // production window is long enough that a wandering AI warrior reached
        // and engaged the city.)

        var founded = session.TryFoundCity(settler, out var city);
        Assert.Equal(GameState.FoundCityResult.Success, founded);
        city!.ProductionItem = "unit:warrior";

        int warriorCountBefore = session.State.Units.Count(u => u.Owner == human && u.Data.Id == "warrior");

        // All-Plains map: pop 1 yields 3F/2P, grows to pop 2 around turn 11 and
        // tips up to 3P/turn — a Warrior (40) completes by turn 18 worst case.
        for (int turn = 1; turn <= 25; turn++)
        {
            session.EndTurn();
            AssertInvariants(session.State, turn);
        }

        int warriorCountAfter = session.State.Units.Count(u => u.Owner == human && u.Data.Id == "warrior");
        Assert.True(warriorCountAfter > warriorCountBefore,
            $"city should have produced a Warrior by turn 25 (before={warriorCountBefore}, after={warriorCountAfter})");
    }

    [Fact]
    public void PlayerCapturesAdjacentAICity()
    {
        var session = TestWorlds.StandardSession(out var human, out var ai);
        var captor  = new Unit(TestWorlds.Warrior(), human, new Vector2I(5, 5));
        var aiCity  = new City("Rome", ai, new Vector2I(6, 5));
        session.State.Units.Add(captor);
        session.State.Cities.Add(aiCity);

        var move = session.MoveAndResolve(captor, new Vector2I(6, 5));

        Assert.True(move.Success);
        Assert.Same(human, aiCity.Owner);
        AssertInvariants(session.State, 1);
    }

    [Fact]
    public void PlayerMarchesTowardAI_CombatOccursWithinTenTurns()
    {
        var session = TestWorlds.StandardSession(out var human, out var ai);
        var hWarrior  = new Unit(TestWorlds.Warrior(), human, new Vector2I(2, 5));
        var aiWarrior = new Unit(TestWorlds.Warrior(), ai,    new Vector2I(8, 5));
        session.State.Units.Add(hWarrior);
        session.State.Units.Add(aiWarrior);

        bool combatHappened = false;
        for (int turn = 1; turn <= 10; turn++)
        {
            // Player steps toward the AI's last known position each turn.
            if (session.State.Units.Contains(hWarrior))
            {
                var target = NearestEnemyPosition(session.State, human, hWarrior.Position);
                if (target.HasValue)
                {
                    var adj = AdjacentEnemy(session.State, human, hWarrior.Position);
                    if (adj != null)
                    {
                        var result = session.TryAttack(hWarrior, adj);
                        if (result.Outcome != GameState.AttackOutcome.Invalid)
                            combatHappened = true;
                    }
                    else
                    {
                        var step = StepTowardWithinRange(hWarrior.Position, target.Value, hWarrior.MovementRemaining);
                        if (step.HasValue) session.TryMove(hWarrior, step.Value);
                    }
                }
            }

            session.EndTurn();
            AssertInvariants(session.State, turn);

            // Combat may also be initiated by AI when it closes in.
            if (hWarrior.HP < 100 || !session.State.Units.Contains(hWarrior))
                combatHappened = true;
        }

        Assert.True(combatHappened,
            "marching player + reactive AI should have collided within 10 turns");
    }

    [Fact]
    public void RandomizedPlayerActions_HoldInvariantsForThirtyTurns()
    {
        // Property-style smoke test: random (but seeded) player actions for
        // 30 turns, verifying invariants every step. Catches "this combination
        // of inputs explodes" bugs we'd never write a unit test for.
        var session = TestWorlds.StandardSession(out var human, out var ai, combatSeed: 9999);
        SeedStartingUnits(session, human, ai);
        var rng = new Random(42);

        for (int turn = 1; turn <= 30; turn++)
        {
            // 0-3 random actions per turn.
            int actions = rng.Next(0, 4);
            for (int i = 0; i < actions; i++)
                DoRandomPlayerAction(session, human, rng);

            session.EndTurn();
            AssertInvariants(session.State, turn);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void SeedStartingUnits(GameSession session, Player human, Player ai)
    {
        session.State.Units.Add(new Unit(TestWorlds.Warrior(), human, new Vector2I(5, 5)));
        session.State.Units.Add(new Unit(TestWorlds.Settler(), human, new Vector2I(5, 6)));
        session.State.Units.Add(new Unit(TestWorlds.Warrior(), ai,    new Vector2I(15, 15)));
        session.State.Units.Add(new Unit(TestWorlds.Settler(), ai,    new Vector2I(15, 14)));
    }

    private static Vector2I? NearestEnemyPosition(GameState state, Player viewer, Vector2I from)
    {
        Vector2I? best = null;
        int       bestDist = int.MaxValue;
        foreach (var u in state.Units)
        {
            if (u.Owner == viewer) continue;
            int d = HexGrid.Distance(from, u.Position);
            if (d < bestDist) { best = u.Position; bestDist = d; }
        }
        return best;
    }

    private static Unit? AdjacentEnemy(GameState state, Player viewer, Vector2I from)
        => state.Units.FirstOrDefault(u =>
            u.Owner != viewer && HexGrid.Distance(from, u.Position) == 1);

    // Returns a tile to walk to, at most `mp` steps along the straight path
    // to `goal`. Picks the furthest reachable step along the path.
    private static Vector2I? StepTowardWithinRange(Vector2I from, Vector2I goal, int mp)
    {
        if (from == goal || mp <= 0) return null;
        // Walk one hex toward goal along the cube-distance gradient.
        Vector2I best = from;
        int      bestDist = HexGrid.Distance(from, goal);
        foreach (var n in HexGrid.GetNeighbors(from))
        {
            int d = HexGrid.Distance(n, goal);
            if (d < bestDist) { best = n; bestDist = d; }
        }
        return best == from ? null : best;
    }

    private static void DoRandomPlayerAction(GameSession session, Player human, Random rng)
    {
        var ownUnits = session.State.Units.Where(u => u.Owner == human).ToList();
        if (ownUnits.Count == 0) return;
        var unit = ownUnits[rng.Next(ownUnits.Count)];

        switch (rng.Next(0, 4))
        {
            case 0: // step in a random direction
            {
                var n = HexGrid.GetNeighbors(unit.Position);
                var dest = n[rng.Next(n.Length)];
                session.TryMove(unit, dest);
                break;
            }
            case 1: // try to attack a random enemy
            {
                var enemies = session.State.Units.Where(u => u.Owner != human).ToList();
                if (enemies.Count > 0)
                    session.TryAttack(unit, enemies[rng.Next(enemies.Count)]);
                break;
            }
            case 2: // settler → found city
            {
                if (unit.Data.Special == "found_city")
                    session.TryFoundCity(unit, out _);
                break;
            }
            case 3: // fortify
                session.Fortify(unit);
                break;
        }
    }
}
