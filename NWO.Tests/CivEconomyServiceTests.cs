using System.Collections.Generic;
using Godot;
using NWO.Core;
using NWO.Entities;
using NWO.Map;
using Xunit;

namespace NWO.Tests;

public class CivEconomyServiceTests
{
    // ── Test world plumbing ─────────────────────────────────────────────────

    private static UnitData Warrior(int prodCost = 40, int maintenance = 1) => new()
    {
        Id = "warrior", Name = "Warrior",
        Attack = 8, Defense = 8, Movement = 2, Range = 1, Sight = 2,
        ProductionCost = prodCost, MaintenanceGold = maintenance,
    };

    private static UnitData Scout(int prodCost = 25) => new()
    {
        Id = "scout", Name = "Scout",
        Attack = 4, Defense = 4, Movement = 2, Range = 1, Sight = 3,
        ProductionCost = prodCost, MaintenanceGold = 1,
    };

    private static BuildingData Library() => new()
    {
        Id = "library", Name = "Library", ProductionCost = 90,
        RequiredTech = "writing",
        Yields = new BuildingYields { Science = 3 },
    };

    private static BuildingData Market() => new()
    {
        Id = "market", Name = "Market", ProductionCost = 120,
        Yields = new BuildingYields { Gold = 4 },
    };

    private static TechData Pottery(int cost = 35) => new()
    {
        Id = "pottery", Name = "Pottery", ScienceCost = cost,
    };

    private static TechData Writing() => new()
    {
        Id = "writing", Name = "Writing", ScienceCost = 55,
        Prerequisites = new List<string> { "pottery" },
    };

    private static DataCatalog Catalog(params TechData[] techs)
        => new(
            new[] { Warrior(), Scout() },
            new[] { Library(), Market() },
            techs);

    private static (GameState state, Player p) MakeState(DataCatalog catalog)
    {
        var state = new GameState(TestWorlds.FlatMap(10, 10), catalog, combatSeed: 1);
        var p     = state.AddPlayer(new Player { Id = 0, Name = "P", IsHuman = true });
        return (state, p);
    }

    private static City Found(GameState state, Player owner, Vector2I pos)
    {
        var city = new City("X", owner, pos) { Population = 1 };
        state.Cities.Add(city);
        CityWorkforceService.Recompute(state, city);
        return city;
    }

    // ── Science / research ─────────────────────────────────────────────────

    [Fact]
    public void Science_AccumulatesFromCityCenter()
    {
        var (state, p) = MakeState(Catalog(Pottery()));
        Found(state, p, new Vector2I(5, 5));

        Assert.Equal(1, CivEconomyService.SciencePerTurn(state, p));
    }

    [Fact]
    public void Science_LibraryAdds3()
    {
        var (state, p) = MakeState(Catalog(Pottery(), Writing()));
        var city       = Found(state, p, new Vector2I(5, 5));
        city.Buildings.Add("library");

        Assert.Equal(1 + 3, CivEconomyService.SciencePerTurn(state, p));
    }

    [Fact]
    public void Research_CompletesAndFiresNotification()
    {
        var (state, p) = MakeState(Catalog(Pottery(cost: 3)));
        Found(state, p, new Vector2I(5, 5));

        Assert.Equal(CivEconomyService.SetResearchResult.Ok,
            CivEconomyService.SetResearch(state, p, "pottery"));

        var notifs = new List<string>();
        // 1 science/turn * 3 turns = 3 → completes on turn 3.
        for (int i = 0; i < 3; i++)
            CivEconomyService.ProcessEndOfTurn(state, p, notifs);

        var civ = state.Civ(p);
        Assert.Contains("pottery", civ.ResearchedTechs);
        Assert.Null(civ.CurrentResearch);
        Assert.Contains("Researched Pottery!", notifs);
    }

    [Fact]
    public void SetResearch_RejectsTechWithUnmetPrereqs()
    {
        var (state, p) = MakeState(Catalog(Pottery(), Writing()));
        Assert.Equal(CivEconomyService.SetResearchResult.MissingPrereq,
            CivEconomyService.SetResearch(state, p, "writing"));
    }

    [Fact]
    public void SetResearch_AcceptsTechWhenPrereqsMet()
    {
        var (state, p) = MakeState(Catalog(Pottery(), Writing()));
        state.Civ(p).ResearchedTechs.Add("pottery");

        Assert.Equal(CivEconomyService.SetResearchResult.Ok,
            CivEconomyService.SetResearch(state, p, "writing"));
        Assert.Equal("writing", state.Civ(p).CurrentResearch);
    }

    [Fact]
    public void SetResearch_RejectsAlreadyResearched()
    {
        var (state, p) = MakeState(Catalog(Pottery()));
        state.Civ(p).ResearchedTechs.Add("pottery");
        Assert.Equal(CivEconomyService.SetResearchResult.AlreadyResearched,
            CivEconomyService.SetResearch(state, p, "pottery"));
    }

    [Fact]
    public void SetResearch_RejectsUnknownTech()
    {
        var (state, p) = MakeState(Catalog(Pottery()));
        Assert.Equal(CivEconomyService.SetResearchResult.UnknownTech,
            CivEconomyService.SetResearch(state, p, "atomic_theory"));
    }

    // ── Gold / maintenance / disband ───────────────────────────────────────

    [Fact]
    public void Gold_MarketAddsIncome()
    {
        var (state, p) = MakeState(Catalog());
        var city       = Found(state, p, new Vector2I(5, 5));
        city.Buildings.Add("market");

        Assert.Equal(4, CivEconomyService.GoldPerTurn(state, p));
    }

    [Fact]
    public void Gold_UnitMaintenanceSubtracts_AfterFreeAllowance()
    {
        var (state, p) = MakeState(Catalog());
        state.Units.Add(new Unit(Warrior(maintenance: 2), p, new Vector2I(0, 0)));
        state.Units.Add(new Unit(Warrior(maintenance: 3), p, new Vector2I(1, 0)));

        // Total maintenance = 5; free allowance = 2 → paid = 3 → net = -3.
        Assert.Equal(-3, CivEconomyService.GoldPerTurn(state, p));
    }

    [Fact]
    public void Gold_FreeAllowance_CoversOpeningUnits()
    {
        var (state, p) = MakeState(Catalog());
        // Two 1-gold-maintenance warriors: total 2, free allowance 2 → 0 paid.
        state.Units.Add(new Unit(Warrior(maintenance: 1), p, new Vector2I(0, 0)));
        state.Units.Add(new Unit(Warrior(maintenance: 1), p, new Vector2I(1, 0)));

        Assert.Equal(0, CivEconomyService.GoldPerTurn(state, p));
    }

    [Fact]
    public void StartingTreasury_AppliesOnAddPlayer()
    {
        var (state, p) = MakeState(Catalog());
        Assert.Equal(CivEconomyService.StartingTreasury, state.Civ(p).Treasury);
    }

    [Fact]
    public void Treasury_DeficitDisbandsCheapestUnitFirst()
    {
        var (state, p) = MakeState(Catalog());
        var scout      = new Unit(Scout(prodCost: 25),    p, new Vector2I(0, 0));
        var warrior    = new Unit(Warrior(prodCost: 40),  p, new Vector2I(1, 0));
        state.Units.Add(scout);
        state.Units.Add(warrior);
        state.Civ(p).Treasury = -1;

        var notifs = new List<string>();
        CivEconomyService.ProcessEndOfTurn(state, p, notifs);

        // GoldPerTurn = -2 (two units × 1 maint). Treasury starts -1 → -3 → disband scout (refund 1) → -2 → disband warrior (refund 1) → -1 → no units left.
        Assert.DoesNotContain(scout, state.Units);
        Assert.Contains("Treasury depleted — disbanded Scout.", notifs);
    }

    [Fact]
    public void Treasury_TerminatesWhenAllUnitsDisbanded()
    {
        var (state, p) = MakeState(Catalog());
        state.Units.Add(new Unit(Scout(), p, new Vector2I(0, 0)));
        state.Civ(p).Treasury = -100;

        var notifs = new List<string>();
        CivEconomyService.ProcessEndOfTurn(state, p, notifs);

        Assert.Empty(state.Units);
        Assert.True(state.Civ(p).Treasury < 0); // still negative, but no infinite loop
    }

    [Fact]
    public void Treasury_PositiveIncome_NoDisband()
    {
        var (state, p) = MakeState(Catalog());
        state.Civ(p).Treasury = 0; // start from a clean slate, not the founding stipend
        var city = Found(state, p, new Vector2I(5, 5));
        city.Buildings.Add("market");
        state.Units.Add(new Unit(Warrior(maintenance: 1), p, new Vector2I(0, 0)));

        var notifs = new List<string>();
        CivEconomyService.ProcessEndOfTurn(state, p, notifs);

        // +4 market, 1 maint fully covered by free allowance → +4 net.
        Assert.Equal(4, state.Civ(p).Treasury);
        Assert.Single(state.Units);
        Assert.DoesNotContain(notifs, n => n.Contains("disbanded"));
    }
}
