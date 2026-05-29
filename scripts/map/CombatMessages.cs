using NWO.Core;
using NWO.Entities;

namespace NWO.Map;

// Pure player-facing combat result text. Extracted from WorldMap so the wording
// can be unit-tested without a scene running (WorldMap just shows the string).
public static class CombatMessages
{
    public static string ForUnitAttack(Unit attacker, Unit target, GameState.AttackResult r) => r.Outcome switch
    {
        GameState.AttackOutcome.DefenderKilled =>
            $"{attacker.Data.Name} killed {target.Data.Name}! (took {r.AttackerDmg})",
        GameState.AttackOutcome.AttackerKilled =>
            $"{attacker.Data.Name} died attacking {target.Data.Name} (dealt {r.DefenderDmg})",
        GameState.AttackOutcome.BothKilled =>
            $"{attacker.Data.Name} and {target.Data.Name} destroyed each other!",
        _ =>
            $"{attacker.Data.Name} hits {target.Data.Name} for {r.DefenderDmg} (took {r.AttackerDmg})",
    };

    public static string ForCityAttack(Unit attacker, City city, GameState.CityAttackResult r)
    {
        if (r.AttackerKilled)
            return $"{attacker.Data.Name} was destroyed assaulting {city.Name}!";
        if (r.CityConquerable)
            return $"{city.Name} breached! Move a melee unit in to capture it.";
        return $"{attacker.Data.Name} hits {city.Name} for {r.CityDamage} (took {r.AttackerDamage})";
    }
}
