using System;
using NWO.Entities;

namespace NWO.Core;

// Pure combat formula. No Godot dependency, no state — caller passes the two
// combatants (as strength + HP) and an RNG, gets back the damage each side took.
//
// Formula (docs/MECHANICS.md):
//   attacker_roll = attackStrength  * (atkHp/100) * jitter(0.85..1.15)
//   defender_roll = defenderStrength * (defHp/100) * jitter(0.85..1.15)
//   damage_to_defender = (attacker_roll / defender_roll) * 30
//   damage_to_attacker = (defender_roll / attacker_roll) * 30   (0 if ranged)
//
// Works for unit-vs-unit and unit-vs-city alike: a city is just a defender whose
// strength is its CityDefenseStrength (+ garrison) and whose HP is its City.HP.
public static class CombatResolver
{
    public readonly record struct CombatResult(int AttackerDamage, int DefenderDamage);

    private const double DamageScale = 30.0;

    // ── Resolved (random) combat ───────────────────────────────────────────────

    public static CombatResult Resolve(Unit attacker, Unit defender, Random rng, bool isRanged)
        => Resolve(attacker.Data.Attack, attacker.HP, defender.Data.Defense, defender.HP, rng, isRanged);

    public static CombatResult Resolve(
        int atkStrength, int atkHp, int defStrength, int defHp, Random rng, bool isRanged)
    {
        double aRoll = Roll(atkStrength, atkHp, Jitter(rng));
        double dRoll = Roll(defStrength, defHp, Jitter(rng));
        return Damage(aRoll, dRoll, isRanged);
    }

    // ── Expected (deterministic) outcome — for the combat-odds preview ──────────
    // Jitter averages 1.0, so the expected rolls drop the random term.

    public static CombatResult Expected(Unit attacker, Unit defender, bool isRanged)
        => Expected(attacker.Data.Attack, attacker.HP, defender.Data.Defense, defender.HP, isRanged);

    public static CombatResult Expected(
        int atkStrength, int atkHp, int defStrength, int defHp, bool isRanged)
    {
        double aRoll = Roll(atkStrength, atkHp, 1.0);
        double dRoll = Roll(defStrength, defHp, 1.0);
        return Damage(aRoll, dRoll, isRanged);
    }

    // ── Shared math ─────────────────────────────────────────────────────────────

    private static double Roll(int strength, int hp, double jitter)
        => strength * (hp / 100.0) * jitter;

    private static CombatResult Damage(double aRoll, double dRoll, bool isRanged)
    {
        if (aRoll <= 0.0) aRoll = 0.0001;
        if (dRoll <= 0.0) dRoll = 0.0001;

        int dmgToDefender = (int)Math.Round((aRoll / dRoll) * DamageScale);
        int dmgToAttacker = isRanged ? 0 : (int)Math.Round((dRoll / aRoll) * DamageScale);
        return new CombatResult(dmgToAttacker, dmgToDefender);
    }

    private static double Jitter(Random rng) => 0.85 + rng.NextDouble() * 0.30;
}
