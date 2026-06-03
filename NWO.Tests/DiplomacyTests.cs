using Godot;
using NWO.Core;
using NWO.Entities;
using Xunit;

namespace NWO.Tests;

// Phase 10.6 — pairwise diplomacy (stances block attacks) and the Syndicate's
// hire-Reavers gold play.
public class DiplomacyTests
{
    private static DataCatalog Catalog() => new(
        new[]
        {
            new UnitData { Id = "warrior",   Name = "Warrior",   Attack = 8, Defense = 8, Movement = 2, Range = 1 },
            new UnitData { Id = "mercenary", Name = "Mercenary", Attack = 9, Defense = 9, Movement = 2, Range = 1 },
        },
        System.Array.Empty<BuildingData>(),
        null,
        new[]
        {
            new FactionData { Id = "syndicate", Name = "Syndicate",
                              Traits = new() { "can_hire_reavers" },
                              UnitVariants = new() { ["warrior"] = "mercenary" } },
        });

    private static GameState FlatState() => new(TestWorlds.FlatMap(20, 20), Catalog(), 1);

    [Fact]
    public void DefaultStance_IsWar_AndBlocksOnceDeEscalated()
    {
        var d = new Diplomacy();
        Assert.True(d.CanAttack(0, 1));            // all-vs-all by default
        d.Set(0, 1, DiplomaticStance.Alliance);
        Assert.False(d.CanAttack(0, 1));
        Assert.True(d.AreAllied(0, 1));
        Assert.False(d.CanAttack(1, 1));           // never attack self
    }

    [Fact]
    public void TryAttack_BlockedBetweenAlliedPlayers()
    {
        var s = FlatState();
        var a = s.AddPlayer(new Player { Id = 0 });
        var b = s.AddPlayer(new Player { Id = 1 });
        var au = new Unit(s.Catalog.Unit("warrior")!, a, new Vector2I(5, 5));
        var bu = new Unit(s.Catalog.Unit("warrior")!, b, new Vector2I(6, 5));
        s.Units.Add(au); s.Units.Add(bu);

        s.Diplomacy.Set(0, 1, DiplomaticStance.Alliance);
        Assert.Equal(GameState.AttackOutcome.Invalid, s.TryAttack(au, bu).Outcome);

        s.Diplomacy.Set(0, 1, DiplomaticStance.War);
        au.MovementRemaining = 2; // reset after the blocked attempt (no moves were spent anyway)
        Assert.NotEqual(GameState.AttackOutcome.Invalid, s.TryAttack(au, bu).Outcome);
    }

    [Fact]
    public void TryAttackCity_BlockedUnderNonAggression()
    {
        var s = FlatState();
        var a = s.AddPlayer(new Player { Id = 0 });
        var b = s.AddPlayer(new Player { Id = 1 });
        var au = new Unit(s.Catalog.Unit("warrior")!, a, new Vector2I(5, 5));
        s.Units.Add(au);
        var city = new City("B", b, new Vector2I(6, 5));
        s.Cities.Add(city);

        s.Diplomacy.Set(0, 1, DiplomaticStance.NonAggression);
        Assert.False(s.TryAttackCity(au, city).Success);
    }

    [Fact]
    public void HireReaver_OnlySyndicate_SpendsGoldAndSpawns()
    {
        var s   = FlatState();
        var syn = s.AddPlayer(new Player { Id = 0, FactionId = "syndicate" });
        var non = s.AddPlayer(new Player { Id = 1 });
        s.Civ(syn).Treasury = 100;
        s.Civ(non).Treasury = 100;

        // Wrong faction can't hire.
        Assert.Null(CivEconomyService.HireReaver(s, non, new Vector2I(3, 3)));

        var hired = CivEconomyService.HireReaver(s, syn, new Vector2I(3, 3));
        Assert.NotNull(hired);
        Assert.Equal("mercenary", hired!.Data.Id);                       // Syndicate variant
        Assert.Equal(100 - CivEconomyService.HireReaverCost, s.Civ(syn).Treasury);
        Assert.Contains(hired, s.Units);
    }

    [Fact]
    public void HireReaver_InsufficientGold_Fails()
    {
        var s   = FlatState();
        var syn = s.AddPlayer(new Player { Id = 0, FactionId = "syndicate" });
        s.Civ(syn).Treasury = CivEconomyService.HireReaverCost - 1;
        Assert.False(CivEconomyService.CanHireReaver(s, syn));
        Assert.Null(CivEconomyService.HireReaver(s, syn, new Vector2I(3, 3)));
    }
}
