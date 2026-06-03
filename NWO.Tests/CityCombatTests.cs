using System.Collections.Generic;
using Godot;
using NWO.Core;
using NWO.Entities;
using Xunit;

namespace NWO.Tests;

// M1 — city combat, capture-after-bombardment, garrison/Walls defense, healing,
// and the deterministic combat-odds preview.
public class CityCombatTests
{
    private static UnitData Archer() => new()
    {
        Id = "archer", Name = "Archer",
        Attack = 7, Defense = 4, Movement = 2, Range = 2, Sight = 2,
    };

    // ── CombatResolver.Expected (pure) ─────────────────────────────────────────

    [Fact]
    public void Expected_MeleeEqualStrength_DealsDamageScaleEach()
    {
        // DamageScale is 40 since Phase 10.5 (was 30).
        var e = CombatResolver.Expected(10, 100, 10, 100, isRanged: false);
        Assert.Equal(40, e.DefenderDamage);
        Assert.Equal(40, e.AttackerDamage);
    }

    [Fact]
    public void Expected_Ranged_TakesNoRetaliation()
    {
        var e = CombatResolver.Expected(10, 100, 10, 100, isRanged: true);
        Assert.Equal(40, e.DefenderDamage);
        Assert.Equal(0, e.AttackerDamage);
    }

    // ── City defense strength ──────────────────────────────────────────────────

    [Fact]
    public void Walls_RaiseCityDefenseStrength()
    {
        var city = new City("Rome", new Player { Id = 0 }, new Vector2I(0, 0)) { Population = 1 };
        int withoutWalls = city.CityDefenseStrength;
        city.Buildings.Add("walls");
        Assert.Equal(withoutWalls + 5, city.CityDefenseStrength);
    }

    [Fact]
    public void GarrisonDefense_ReturnsBestFriendlyUnitOnTile()
    {
        var session = TestWorlds.StandardSession(out var human, out _);
        var city    = new City("Rome", human, new Vector2I(5, 5));
        session.State.Cities.Add(city);
        Assert.Equal(0, session.State.GarrisonDefense(city));

        session.State.Units.Add(new Unit(TestWorlds.Warrior(), human, new Vector2I(5, 5))); // def 8
        Assert.Equal(8, session.State.GarrisonDefense(city));
    }

    // ── City assault + capture ───────────────────────────────────────────────────

    [Fact]
    public void TryAttackCity_DepletesHpAndBecomesConquerable()
    {
        var session  = TestWorlds.StandardSession(out var human, out var ai);
        var attacker = new Unit(TestWorlds.Warrior(), human, new Vector2I(5, 5));
        var city     = new City("Rome", ai, new Vector2I(6, 5)) { HP = 5 };
        session.State.Units.Add(attacker);
        session.State.Cities.Add(city);

        var r = session.TryAttackCity(attacker, city);

        Assert.True(r.Success);
        Assert.Equal(0, city.HP);
        Assert.True(r.CityConquerable);
        Assert.True(city.AttackedSinceTurn);
        Assert.Equal(0, attacker.MovementRemaining);
    }

    [Fact]
    public void TryAttackCity_RangedAttackerTakesNoRetaliation()
    {
        var session  = TestWorlds.StandardSession(out var human, out var ai);
        var archer   = new Unit(Archer(), human, new Vector2I(5, 5));
        var city     = new City("Rome", ai, new Vector2I(7, 5)) { HP = 100 }; // distance 2
        session.State.Units.Add(archer);
        session.State.Cities.Add(city);

        var r = session.TryAttackCity(archer, city);

        Assert.True(r.Success);
        Assert.Equal(100, archer.HP);     // no retaliation
        Assert.True(city.HP < 100);
    }

    [Fact]
    public void MoveOntoFullHpEnemyCity_IsBlocked()
    {
        var session  = TestWorlds.StandardSession(out var human, out var ai);
        var attacker = new Unit(TestWorlds.Warrior(), human, new Vector2I(5, 5));
        var city     = new City("Rome", ai, new Vector2I(6, 5)); // full HP → not conquerable
        session.State.Units.Add(attacker);
        session.State.Cities.Add(city);

        var move = session.TryMove(attacker, new Vector2I(6, 5));

        Assert.False(move.Success);
        Assert.Same(ai, city.Owner);
    }

    [Fact]
    public void MoveOntoConquerableCity_CapturesIt()
    {
        var session  = TestWorlds.StandardSession(out var human, out var ai);
        var attacker = new Unit(TestWorlds.Warrior(), human, new Vector2I(5, 5));
        var city     = new City("Rome", ai, new Vector2I(6, 5)) { HP = 0 }; // conquerable
        session.State.Units.Add(attacker);
        session.State.Cities.Add(city);

        var move = session.MoveAndResolve(attacker, new Vector2I(6, 5));

        Assert.True(move.Success);
        Assert.Same(human, city.Owner);
        Assert.Equal(City.MaxHP / 2, city.HP); // weakened on capture
    }

    [Fact]
    public void RangedUnit_CannotCaptureConquerableCity()
    {
        var session = TestWorlds.StandardSession(out var human, out var ai);
        var archer  = new Unit(Archer(), human, new Vector2I(5, 5));
        var city    = new City("Rome", ai, new Vector2I(6, 5)) { HP = 0 };
        session.State.Units.Add(archer);
        session.State.Cities.Add(city);

        var move = session.MoveAndResolve(archer, new Vector2I(6, 5));

        Assert.False(move.Success);
        Assert.Same(ai, city.Owner);
    }

    // ── Healing & regen ──────────────────────────────────────────────────────────

    [Fact]
    public void EndTurn_HealsIdleDamagedUnit()
    {
        var session = TestWorlds.StandardSession(out var human, out _);
        var unit    = new Unit(TestWorlds.Warrior(), human, new Vector2I(5, 5)) { HP = 50 };
        session.State.Units.Add(unit);

        session.State.EndPlayerTurn(new List<GameState.ProductionCompletion>());

        Assert.Equal(60, unit.HP); // +10, no friendly city adjacent
    }

    [Fact]
    public void SkippedUnit_LeavesQueueButStillHeals()
    {
        var session = TestWorlds.StandardSession(out var human, out _);
        var unit    = new Unit(TestWorlds.Warrior(), human, new Vector2I(5, 5))
        {
            HP = 50,
            SkippedThisTurn = true, // as set by pressing Space on the unit
        };
        session.State.Units.Add(unit);

        Assert.False(unit.NeedsAttention); // won't re-block End Turn

        session.State.EndPlayerTurn(new List<GameState.ProductionCompletion>());

        Assert.Equal(60, unit.HP);             // skipping still heals
        Assert.False(unit.SkippedThisTurn);    // one-turn pass cleared
    }

    [Fact]
    public void FortifyUntilHealed_SleepsWhenDamaged_PlainFortifyWhenFull()
    {
        var session = TestWorlds.StandardSession(out var human, out _);
        var hurt    = new Unit(TestWorlds.Warrior(), human, new Vector2I(5, 5)) { HP = 50 };
        var full    = new Unit(TestWorlds.Warrior(), human, new Vector2I(6, 6)) { HP = 100 };
        session.State.Units.Add(hurt);
        session.State.Units.Add(full);

        session.FortifyUntilHealed(hurt);
        session.FortifyUntilHealed(full);

        Assert.True(hurt.Fortified);
        Assert.True(hurt.SleepUntilHealed);
        Assert.Equal(0, hurt.MovementRemaining);

        Assert.True(full.Fortified);
        Assert.False(full.SleepUntilHealed); // nothing to heal → ordinary fortify
    }

    [Fact]
    public void SleepingUnit_StaysAsleepUntilFull_ThenWakes()
    {
        var session = TestWorlds.StandardSession(out var human, out _);
        var unit    = new Unit(TestWorlds.Warrior(), human, new Vector2I(5, 5))
        {
            HP = 85, Fortified = true, SleepUntilHealed = true,
        };
        session.State.Units.Add(unit);

        // Full round: 85 → 95, still asleep.
        session.EndTurn();
        Assert.Equal(95, unit.HP);
        Assert.True(unit.SleepUntilHealed);
        Assert.True(unit.Fortified);

        // Next round: 95 → 100, wakes.
        session.EndTurn();
        Assert.Equal(100, unit.HP);
        Assert.False(unit.SleepUntilHealed);
        Assert.False(unit.Fortified);
    }

    [Fact]
    public void EndTurn_DoesNotHealUnitThatActed()
    {
        var session = TestWorlds.StandardSession(out var human, out _);
        var unit    = new Unit(TestWorlds.Warrior(), human, new Vector2I(5, 5)) { HP = 50, ActedThisTurn = true };
        session.State.Units.Add(unit);

        session.State.EndPlayerTurn(new List<GameState.ProductionCompletion>());

        Assert.Equal(50, unit.HP);
        Assert.False(unit.ActedThisTurn); // reset for next turn
    }

    [Fact]
    public void EndTurn_RegensCityWhenUnharassed_ButNotAfterAttack()
    {
        var session = TestWorlds.StandardSession(out var human, out _);
        var calm    = new City("Calm",    human, new Vector2I(3, 3)) { HP = 50 };
        var sieged  = new City("Sieged",  human, new Vector2I(8, 8)) { HP = 50, AttackedSinceTurn = true };
        session.State.Cities.Add(calm);
        session.State.Cities.Add(sieged);

        session.State.EndPlayerTurn(new List<GameState.ProductionCompletion>());

        Assert.Equal(60, calm.HP);   // +10 regen
        Assert.Equal(50, sieged.HP); // attacked → no regen this turn
        Assert.False(sieged.AttackedSinceTurn);
    }
}
