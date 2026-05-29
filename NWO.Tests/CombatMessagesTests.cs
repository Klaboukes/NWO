using Godot;
using NWO.Core;
using NWO.Entities;
using NWO.Map;
using Xunit;

namespace NWO.Tests;

public class CombatMessagesTests
{
    private static readonly Player P0 = new() { Id = 0, Name = "Us" };
    private static readonly Player P1 = new() { Id = 1, Name = "Them" };

    private static Unit Warrior(Player owner)
        => new(TestWorlds.Warrior(), owner, new Vector2I(0, 0));

    [Fact]
    public void UnitAttack_DefenderKilled_ReadsAsAKill()
    {
        var msg = CombatMessages.ForUnitAttack(Warrior(P0), Warrior(P1),
            new GameState.AttackResult(GameState.AttackOutcome.DefenderKilled, AttackerDmg: 5, DefenderDmg: 30));
        Assert.Contains("killed", msg);
        Assert.Contains("(took 5)", msg);
    }

    [Fact]
    public void UnitAttack_AttackerKilled_ReadsAsADeath()
    {
        var msg = CombatMessages.ForUnitAttack(Warrior(P0), Warrior(P1),
            new GameState.AttackResult(GameState.AttackOutcome.AttackerKilled, AttackerDmg: 30, DefenderDmg: 7));
        Assert.Contains("died attacking", msg);
        Assert.Contains("dealt 7", msg);
    }

    [Fact]
    public void UnitAttack_BothKilled_MentionsBoth()
    {
        var msg = CombatMessages.ForUnitAttack(Warrior(P0), Warrior(P1),
            new GameState.AttackResult(GameState.AttackOutcome.BothKilled, 30, 30));
        Assert.Contains("destroyed each other", msg);
    }

    [Fact]
    public void UnitAttack_Hit_ShowsDamageBothWays()
    {
        var msg = CombatMessages.ForUnitAttack(Warrior(P0), Warrior(P1),
            new GameState.AttackResult(GameState.AttackOutcome.Hit, AttackerDmg: 4, DefenderDmg: 9));
        Assert.Contains("hits", msg);
        Assert.Contains("for 9", msg);
        Assert.Contains("(took 4)", msg);
    }

    [Fact]
    public void CityAttack_AttackerKilled_TakesPriority()
    {
        var city = new City("Rome", P1, new Vector2I(3, 3));
        var msg  = CombatMessages.ForCityAttack(Warrior(P0), city,
            new GameState.CityAttackResult(Success: true, CityDamage: 0, AttackerDamage: 40,
                CityConquerable: false, AttackerKilled: true));
        Assert.Contains("destroyed assaulting", msg);
    }

    [Fact]
    public void CityAttack_Breached_PromptsForCapture()
    {
        var city = new City("Rome", P1, new Vector2I(3, 3));
        var msg  = CombatMessages.ForCityAttack(Warrior(P0), city,
            new GameState.CityAttackResult(Success: true, CityDamage: 12, AttackerDamage: 3,
                CityConquerable: true, AttackerKilled: false));
        Assert.Contains("breached", msg);
    }

    [Fact]
    public void CityAttack_Hit_ShowsDamage()
    {
        var city = new City("Rome", P1, new Vector2I(3, 3));
        var msg  = CombatMessages.ForCityAttack(Warrior(P0), city,
            new GameState.CityAttackResult(Success: true, CityDamage: 12, AttackerDamage: 3,
                CityConquerable: false, AttackerKilled: false));
        Assert.Contains("for 12", msg);
        Assert.Contains("(took 3)", msg);
    }
}
