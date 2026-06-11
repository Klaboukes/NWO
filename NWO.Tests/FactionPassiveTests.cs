using System.Collections.Generic;
using System.Linq;
using Godot;
using NWO.Core;
using NWO.Entities;
using NWO.Map;
using Xunit;

namespace NWO.Tests;

// Phase 10.3 — verifies each faction signature passive measurably changes core
// output through its one service hook, plus the unit-XP/veterancy subsystem.
public class FactionPassiveTests
{
    // ── Catalog / state builders ─────────────────────────────────────────────

    private static UnitData U(string id, int atk, int def, int move = 2, int range = 1,
                              int sight = 2, string? special = null, bool ignoresTerrain = false)
        => new()
        {
            Id = id, Name = id, Attack = atk, Defense = def, Movement = move, Range = range,
            Sight = sight, ProductionCost = 40, Special = special, IgnoresTerrainCost = ignoresTerrain,
        };

    private static DataCatalog Catalog() => new(
        new[]
        {
            U("warrior", 8, 8),
            U("spearman", 6, 12),
            U("scout", 6, 6, move: 3, sight: 3, ignoresTerrain: false),
            U("settler", 0, 0, special: "found_city"),
            U("palace_guard", 6, 16),
        },
        System.Array.Empty<BuildingData>(),
        null,
        new[]
        {
            new FactionData { Id = "iron_pact", Name = "Iron Pact", CombatStrengthMult = 1.5,
                              XpGainMult = 2.0, HealInEnemyLand = true },
            new FactionData { Id = "syndicate", Name = "Syndicate", GoldMult = 2.0, RushBuyDiscount = 0.5 },
            new FactionData { Id = "cognate", Name = "Cognate", ScienceMult = 2.0 },
            new FactionData { Id = "voyagers", Name = "Voyagers", SightBonus = 2, TerrainCostMult = 0.5 },
            new FactionData { Id = "dominion", Name = "Dominion", CityDefenseBonus = 10,
                              CapitalProductionBonus = 3, UnitVariants = new() { ["spearman"] = "palace_guard" } },
            new FactionData { Id = "settlers", Name = "Free Settlements", Traits = new() { "new_city_defender" } },
        });

    private static GameState FlatState(int seed = 5)
        => new(TestWorlds.FlatMap(20, 20), Catalog(), seed);

    private static Player Add(GameState s, int id, string? faction, bool human = true)
        => s.AddPlayer(new Player { Id = id, Name = $"P{id}", IsHuman = human, FactionId = faction });

    // ── Combat strength ──────────────────────────────────────────────────────

    [Fact]
    public void CombatStrengthMult_MakesAttackerHitHarder()
    {
        int Damage(string? faction)
        {
            var s   = FlatState();
            var atk = Add(s, 0, faction);
            var def = Add(s, 1, null, human: false);
            var a   = new Unit(s.Catalog.Unit("warrior")!, atk, new Vector2I(5, 5));
            var d   = new Unit(s.Catalog.Unit("warrior")!, def, new Vector2I(6, 5));
            s.Units.Add(a); s.Units.Add(d);
            return s.TryAttack(a, d).DefenderDmg;
        }

        Assert.True(Damage("iron_pact") > Damage(null),
            "Iron Pact's combat-strength multiplier should raise damage dealt.");
    }

    [Fact]
    public void Veterancy_RaisesEffectiveStrength()
    {
        int Damage(int xp)
        {
            var s   = FlatState();
            var atk = Add(s, 0, null);
            var def = Add(s, 1, null, human: false);
            var a   = new Unit(s.Catalog.Unit("warrior")!, atk, new Vector2I(5, 5)) { Experience = xp };
            var d   = new Unit(s.Catalog.Unit("warrior")!, def, new Vector2I(6, 5));
            s.Units.Add(a); s.Units.Add(d);
            return s.TryAttack(a, d).DefenderDmg;
        }

        Assert.True(Damage(100) > Damage(0), "A veteran (level 3) should out-hit a green unit.");
    }

    [Fact]
    public void Veterancy_LevelsCrossThresholds()
    {
        var green   = new Unit(Catalog().Unit("warrior")!, new Player { Id = 0 }, Vector2I.Zero);
        var veteran = new Unit(Catalog().Unit("warrior")!, new Player { Id = 0 }, Vector2I.Zero) { Experience = 50 };
        Assert.Equal(0, green.Level);
        Assert.Equal(2, veteran.Level); // crossed 15 and 45, not 90
        Assert.Equal(1.2, veteran.VeterancyMult, 3);
    }

    [Fact]
    public void CombatXp_AccruesOnSurvival_FasterForIronPact()
    {
        int Xp(string? faction)
        {
            var s   = FlatState();
            var atk = Add(s, 0, faction);
            var def = Add(s, 1, null, human: false);
            // Beefy defender so the attacker survives and earns XP.
            var a   = new Unit(s.Catalog.Unit("warrior")!, atk, new Vector2I(5, 5));
            var d   = new Unit(s.Catalog.Unit("spearman")!, def, new Vector2I(6, 5));
            s.Units.Add(a); s.Units.Add(d);
            s.TryAttack(a, d);
            return a.Experience;
        }

        Assert.True(Xp(null) > 0, "Surviving combat should grant XP.");
        Assert.True(Xp("iron_pact") > Xp(null), "Iron Pact accrues veterancy faster.");
    }

    [Fact]
    public void HealInEnemyLand_LetsIronPactHealAfterActing()
    {
        int HpAfterTurn(string? faction)
        {
            var s = FlatState();
            var p = Add(s, 0, faction);
            var u = new Unit(s.Catalog.Unit("warrior")!, p, new Vector2I(5, 5))
            { HP = 50, ActedThisTurn = true };
            s.Units.Add(u);
            s.EndPlayerTurn(new List<GameState.ProductionCompletion>());
            return u.HP;
        }

        Assert.Equal(50, HpAfterTurn(null));            // ordinary unit that acted doesn't heal
        Assert.True(HpAfterTurn("iron_pact") > 50);     // Iron Pact heals despite acting
    }

    // ── Economy ──────────────────────────────────────────────────────────────

    private static City CityWith(GameState s, Player owner)
    {
        // Coast tiles in the work radius carry trade gold (plains carry none), so the
        // worked-tile income is non-zero and gold multipliers are observable.
        foreach (var t in new[] { new Vector2I(5, 6), new Vector2I(6, 5), new Vector2I(4, 5) })
            s.Map.Tiles[t] = TerrainType.Coast;
        var city = new City("C", owner, new Vector2I(5, 5)) { IsCapital = true, Population = 3 };
        s.Cities.Add(city);
        CityWorkforceService.Recompute(s, city);
        return city;
    }

    [Fact]
    public void GoldMult_RaisesIncome()
    {
        int Gold(string? faction)
        {
            var s = FlatState();
            var p = Add(s, 0, faction);
            CityWith(s, p);
            return CivEconomyService.GoldPerTurn(s, p);
        }
        Assert.True(Gold("syndicate") > Gold(null));
    }

    [Fact]
    public void ScienceMult_RaisesResearch()
    {
        int Science(string? faction)
        {
            var s = FlatState();
            var p = Add(s, 0, faction);
            CityWith(s, p);
            return CivEconomyService.SciencePerTurn(s, p);
        }
        Assert.True(Science("cognate") > Science(null));
    }

    [Fact]
    public void RushBuyDiscount_LowersCost()
    {
        int Cost(string? faction)
        {
            var s = FlatState();
            var p = Add(s, 0, faction);
            var c = CityWith(s, p);
            c.ProductionItem = "unit:warrior"; // cost 40, no progress
            return CivEconomyService.BuyCost(s, c);
        }
        Assert.True(Cost("syndicate") < Cost(null));
        Assert.True(Cost("syndicate") > 0);
    }

    // ── Sight & movement ───────────────────────────────────────────────────────

    [Fact]
    public void SightBonus_RevealsMoreTiles()
    {
        int Seen(string? faction)
        {
            var s = FlatState();
            var p = Add(s, 0, faction);
            s.Units.Add(new Unit(s.Catalog.Unit("scout")!, p, new Vector2I(10, 10)));
            s.RecomputeFog(p);
            return s.Fog(p).Visible.Count;
        }
        Assert.True(Seen("voyagers") > Seen(null));
    }

    [Fact]
    public void TerrainCostMult_CheapensRoughTerrain()
    {
        var s = FlatState();
        s.Map.Features[new Vector2I(7, 7)] = Feature.Forest; // Plains + Forest: cost 2
        var voyager = Add(s, 0, "voyagers");
        var plain   = Add(s, 1, null, human: false);
        var vu = new Unit(s.Catalog.Unit("warrior")!, voyager, new Vector2I(6, 7));
        var pu = new Unit(s.Catalog.Unit("warrior")!, plain,   new Vector2I(6, 7));
        Assert.Equal(2, s.MovementCost(new Vector2I(7, 7), pu));
        Assert.Equal(1, s.MovementCost(new Vector2I(7, 7), vu)); // round(2 * 0.5)
    }

    // ── Fortress capital (Dominion) ────────────────────────────────────────────

    [Fact]
    public void CapitalDefenseBonus_AppliesToCapitalOnly()
    {
        var s = FlatState();
        var p = Add(s, 0, "dominion");
        var capital = new City("Cap", p, new Vector2I(5, 5)) { IsCapital = true };
        var second  = new City("Two", p, new Vector2I(9, 9)) { IsCapital = false };
        s.Cities.Add(capital); s.Cities.Add(second);

        Assert.Equal(capital.CityDefenseStrength + 10, s.CityDefenseTotal(capital));
        Assert.Equal(second.CityDefenseStrength,       s.CityDefenseTotal(second));
    }

    [Fact]
    public void CapitalProductionBonus_RaisesCapitalYield()
    {
        int Prod(string? faction, bool capital)
        {
            var s = FlatState();
            var p = Add(s, 0, faction);
            var c = new City("C", p, new Vector2I(5, 5)) { IsCapital = capital, Population = 3 };
            s.Cities.Add(c);
            CityWorkforceService.Recompute(s, c);
            return c.ProductionYield;
        }
        Assert.Equal(Prod(null, true) + 3, Prod("dominion", true));
        Assert.Equal(Prod(null, false),    Prod("dominion", false)); // non-capital unaffected
    }

    [Fact]
    public void CapitalRegen_ScalesByMultiplier()
    {
        var c = new City("C", new Player { Id = 0 }, Vector2I.Zero) { HP = 50 };
        c.RegenIfUnharassed(1.5);
        Assert.Equal(65, c.HP); // 50 + round(10 * 1.5)
    }

    // ── Unique units & new-city defender ───────────────────────────────────────

    [Fact]
    public void UnitVariant_SwapsBaseForFactionVariant()
    {
        var s = FlatState();
        var p = Add(s, 0, "dominion");
        var c = new City("C", p, new Vector2I(5, 5));
        s.Cities.Add(c);
        c.ProductionItem = "unit:spearman";
        s.RushProduction(c);
        Assert.Contains(s.Units, u => u.Data.Id == "palace_guard" && u.Owner == p);
        Assert.DoesNotContain(s.Units, u => u.Data.Id == "spearman");
    }

    [Fact]
    public void NewCityDefender_SpawnsForFreeSettlements()
    {
        int DefendersAfterFounding(string? faction)
        {
            var s = FlatState();
            var p = Add(s, 0, faction);
            var settler = new Unit(s.Catalog.Unit("settler")!, p, new Vector2I(5, 5));
            s.Units.Add(settler);
            s.TryFoundCity(settler, out _);
            return s.Units.Count(u => u.Owner == p && u.Data.Defense > 0);
        }
        Assert.Equal(0, DefendersAfterFounding(null));
        Assert.Equal(1, DefendersAfterFounding("settlers"));
    }
}
