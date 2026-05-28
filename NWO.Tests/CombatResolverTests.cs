using System;
using Godot;
using NWO.Core;
using NWO.Entities;
using Xunit;

namespace NWO.Tests;

public class CombatResolverTests
{
    private static Player MakePlayer(int id = 0) => new() { Id = id, Name = $"P{id}" };

    private static Unit MakeUnit(int attack, int defense, int range = 1, int hp = 100)
    {
        var data = new UnitData { Id = "u", Name = "U", Attack = attack, Defense = defense, Range = range };
        return new Unit(data, MakePlayer(), new Vector2I(0, 0)) { HP = hp };
    }

    [Fact]
    public void StrongerAttacker_DealsMoreDamageThanItTakes()
    {
        var rng      = new Random(42);
        var attacker = MakeUnit(attack: 15, defense: 5);
        var defender = MakeUnit(attack: 5,  defense: 5);

        var result = CombatResolver.Resolve(attacker, defender, rng, isRanged: false);

        Assert.True(result.DefenderDamage > result.AttackerDamage);
    }

    [Fact]
    public void RangedAttacker_TakesNoDamage()
    {
        var rng      = new Random(1);
        var attacker = MakeUnit(attack: 7, defense: 4, range: 2);
        var defender = MakeUnit(attack: 8, defense: 8);

        var result = CombatResolver.Resolve(attacker, defender, rng, isRanged: true);

        Assert.Equal(0, result.AttackerDamage);
        Assert.True(result.DefenderDamage > 0);
    }

    [Fact]
    public void WoundedUnit_HitsSofter()
    {
        var rng = new Random(7);
        var fullHpAttacker    = MakeUnit(attack: 10, defense: 5, hp: 100);
        var defender1         = MakeUnit(attack: 5,  defense: 5);
        var wounded           = MakeUnit(attack: 10, defense: 5, hp: 25);
        var defender2         = MakeUnit(attack: 5,  defense: 5);

        // Use a fresh RNG sequence per resolve so the jitter is comparable.
        var r1 = CombatResolver.Resolve(fullHpAttacker, defender1, new Random(100), isRanged: false);
        var r2 = CombatResolver.Resolve(wounded,        defender2, new Random(100), isRanged: false);

        Assert.True(r2.DefenderDamage < r1.DefenderDamage);
    }

    [Fact]
    public void DamageIsNonNegative_StatisticallyOver1000Rolls()
    {
        var rng      = new Random(2026);
        var attacker = MakeUnit(attack: 8, defense: 8);
        var defender = MakeUnit(attack: 8, defense: 8);

        for (int i = 0; i < 1000; i++)
        {
            var result = CombatResolver.Resolve(attacker, defender, rng, isRanged: false);
            Assert.True(result.AttackerDamage >= 0);
            Assert.True(result.DefenderDamage >= 0);
        }
    }

    [Fact]
    public void EvenMatch_DealsRoughly30DamageBothWays()
    {
        var rng      = new Random(99);
        var attacker = MakeUnit(attack: 10, defense: 10);
        var defender = MakeUnit(attack: 10, defense: 10);

        int totalAtk = 0, totalDef = 0;
        const int N = 500;
        for (int i = 0; i < N; i++)
        {
            var result = CombatResolver.Resolve(attacker, defender, rng, isRanged: false);
            totalAtk += result.AttackerDamage;
            totalDef += result.DefenderDamage;
        }

        double avgAtk = totalAtk / (double)N;
        double avgDef = totalDef / (double)N;

        // With equal strength and HP, average damage should be near 30 on both sides.
        Assert.InRange(avgAtk, 25.0, 40.0);
        Assert.InRange(avgDef, 25.0, 40.0);
    }
}
