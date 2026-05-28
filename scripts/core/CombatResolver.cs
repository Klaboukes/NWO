using System;
using NWO.Entities;

namespace NWO.Core;

// Pure combat formula. No Godot dependency, no state — caller passes the two
// units and an RNG, gets back the damage each side took.
//
// Formula (docs/MECHANICS.md):
//   attacker_roll = attack  * (HP/100) * jitter(0.85..1.15)
//   defender_roll = defense * (HP/100) * jitter(0.85..1.15)
//   damage_to_defender = (attacker_roll / defender_roll) * 30
//   damage_to_attacker = (defender_roll / attacker_roll) * 30   (0 if ranged)
public static class CombatResolver
{
    public readonly record struct CombatResult(int AttackerDamage, int DefenderDamage);

    public static CombatResult Resolve(Unit attacker, Unit defender, Random rng, bool isRanged)
    {
        double aRoll = attacker.Data.Attack  * (attacker.HP / 100.0) * Jitter(rng);
        double dRoll = defender.Data.Defense * (defender.HP / 100.0) * Jitter(rng);

        if (aRoll <= 0.0) aRoll = 0.0001;
        if (dRoll <= 0.0) dRoll = 0.0001;

        int dmgToDefender = (int)Math.Round((aRoll / dRoll) * 30.0);
        int dmgToAttacker = isRanged ? 0 : (int)Math.Round((dRoll / aRoll) * 30.0);

        return new CombatResult(dmgToAttacker, dmgToDefender);
    }

    private static double Jitter(Random rng) => 0.85 + rng.NextDouble() * 0.30;
}
