using System.Collections.Generic;
using Godot;
using NWO.Core;
using NWO.Entities;
using NWO.Map;
using Xunit;

namespace NWO.Tests;

public class GameStateCombatTests
{
    private const int Seed = 12345;

    private static UnitData WarriorData() => new()
    {
        Id = "warrior", Name = "Warrior", Attack = 8, Defense = 8, Movement = 2, Range = 1,
    };

    private static UnitData ArcherData() => new()
    {
        Id = "archer", Name = "Archer", Attack = 7, Defense = 4, Movement = 2, Range = 2,
    };

    private static UnitData SettlerData() => new()
    {
        Id = "settler", Name = "Settler", Attack = 0, Defense = 0, Movement = 2, Range = 0,
        Special = "found_city",
    };

    private static GameState MakeState(out Player human, out Player ai)
    {
        var map = new MapData(10, 10);
        for (int q = 0; q < 10; q++)
        for (int r = 0; r < 10; r++)
            map.Tiles[new Vector2I(q, r)] = TerrainType.Plains;

        var catalog = new DataCatalog(new List<UnitData>(), new List<BuildingData>());
        var state   = new GameState(map, catalog, combatSeed: Seed);
        human       = state.AddPlayer(new Player { Id = 0, Name = "P0", IsHuman = true  });
        ai          = state.AddPlayer(new Player { Id = 1, Name = "P1", IsHuman = false });
        return state;
    }

    [Fact]
    public void TryAttack_AdjacentEnemy_AppliesDamage()
    {
        var state = MakeState(out var human, out var ai);
        var atk = new Unit(WarriorData(), human, new Vector2I(2, 2));
        var def = new Unit(WarriorData(), ai,    new Vector2I(3, 2));
        state.Units.Add(atk);
        state.Units.Add(def);

        var result = state.TryAttack(atk, def);

        Assert.Equal(GameState.AttackOutcome.Hit, result.Outcome);
        Assert.True(result.DefenderDmg > 0);
        Assert.True(result.AttackerDmg > 0);
        Assert.Equal(100 - result.AttackerDmg, atk.HP);
        Assert.Equal(100 - result.DefenderDmg, def.HP);
    }

    [Fact]
    public void TryAttack_OutOfRange_Invalid()
    {
        var state = MakeState(out var human, out var ai);
        var atk = new Unit(WarriorData(), human, new Vector2I(0, 0));
        var def = new Unit(WarriorData(), ai,    new Vector2I(5, 5));
        state.Units.Add(atk);
        state.Units.Add(def);

        var result = state.TryAttack(atk, def);

        Assert.Equal(GameState.AttackOutcome.Invalid, result.Outcome);
        Assert.Equal(100, atk.HP);
        Assert.Equal(100, def.HP);
    }

    [Fact]
    public void TryAttack_RangedAtRange2_HitsWithoutRetaliation()
    {
        var state = MakeState(out var human, out var ai);
        var atk = new Unit(ArcherData(),  human, new Vector2I(0, 0));
        var def = new Unit(WarriorData(), ai,    new Vector2I(2, 0));
        state.Units.Add(atk);
        state.Units.Add(def);

        var result = state.TryAttack(atk, def);

        Assert.Equal(GameState.AttackOutcome.Hit, result.Outcome);
        Assert.Equal(0, result.AttackerDmg);
        Assert.True(result.DefenderDmg > 0);
        Assert.Equal(100, atk.HP);
    }

    [Fact]
    public void TryAttack_KillsDefender_RemovesUnitAndReportsDefenderKilled()
    {
        var state = MakeState(out var human, out var ai);
        var atk = new Unit(WarriorData(), human, new Vector2I(0, 0));
        var def = new Unit(WarriorData(), ai,    new Vector2I(1, 0)) { HP = 1 };
        state.Units.Add(atk);
        state.Units.Add(def);

        var result = state.TryAttack(atk, def);

        Assert.Equal(GameState.AttackOutcome.DefenderKilled, result.Outcome);
        Assert.DoesNotContain(def, state.Units);
        Assert.Contains(atk, state.Units);
    }

    [Fact]
    public void TryAttack_SameOwner_Invalid()
    {
        var state = MakeState(out var human, out _);
        var atk = new Unit(WarriorData(), human, new Vector2I(0, 0));
        var def = new Unit(WarriorData(), human, new Vector2I(1, 0));
        state.Units.Add(atk);
        state.Units.Add(def);

        Assert.Equal(GameState.AttackOutcome.Invalid, state.TryAttack(atk, def).Outcome);
    }

    [Fact]
    public void TryAttack_ZerosAttackerMovement()
    {
        var state = MakeState(out var human, out var ai);
        var atk = new Unit(WarriorData(), human, new Vector2I(0, 0));
        var def = new Unit(WarriorData(), ai,    new Vector2I(1, 0));
        state.Units.Add(atk);
        state.Units.Add(def);
        Assert.True(atk.MovementRemaining > 0);

        state.TryAttack(atk, def);

        Assert.Equal(0, atk.MovementRemaining);
    }

    [Fact]
    public void TryAttack_NoMovementRemaining_Invalid()
    {
        var state = MakeState(out var human, out var ai);
        var atk = new Unit(WarriorData(), human, new Vector2I(0, 0)) { MovementRemaining = 0 };
        var def = new Unit(WarriorData(), ai,    new Vector2I(1, 0));
        state.Units.Add(atk);
        state.Units.Add(def);

        Assert.Equal(GameState.AttackOutcome.Invalid, state.TryAttack(atk, def).Outcome);
    }

    [Fact]
    public void TryAttack_NoAttackPower_Invalid()
    {
        var state = MakeState(out var human, out var ai);
        var atk = new Unit(SettlerData(),  human, new Vector2I(0, 0));
        var def = new Unit(WarriorData(),  ai,    new Vector2I(1, 0));
        state.Units.Add(atk);
        state.Units.Add(def);

        Assert.Equal(GameState.AttackOutcome.Invalid, state.TryAttack(atk, def).Outcome);
    }
}
