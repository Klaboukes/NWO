using System.Collections.Generic;
using Godot;
using NWO.AI;
using NWO.Core;
using NWO.Entities;
using NWO.Map;
using Xunit;

namespace NWO.Tests;

public class AIControllerTests
{
    private static UnitData WarriorData() => new()
    {
        Id = "warrior", Name = "Warrior", Attack = 8, Defense = 8, Movement = 2, Range = 1,
    };

    private static UnitData SettlerData() => new()
    {
        Id = "settler", Name = "Settler", Attack = 0, Defense = 0, Movement = 2,
        Range = 0, Special = "found_city",
    };

    private static List<UnitData> Units() => new() { WarriorData(), SettlerData() };

    private static List<TechData> Techs() => new()
    {
        new TechData { Id = "pottery", Name = "Pottery", ScienceCost = 35 },
        new TechData { Id = "mining",  Name = "Mining",  ScienceCost = 35 },
    };

    private static GameState MakeState(out Player human, out Player ai)
    {
        var map = new MapData(10, 10);
        for (int q = 0; q < 10; q++)
        for (int r = 0; r < 10; r++)
            map.Tiles[new Vector2I(q, r)] = TerrainType.Plains;

        var catalog = new DataCatalog(Units(), new List<BuildingData>(), Techs());
        var state   = new GameState(map, catalog);
        human       = state.AddPlayer(new Player { Id = 0, Name = "P0", IsHuman = true  });
        ai          = state.AddPlayer(new Player { Id = 1, Name = "P1", IsHuman = false });
        return state;
    }

    [Fact]
    public void AI_ResearchFallback_PicksTechOutsidePrefsList()
    {
        // Catalog holds only a tech that is NOT in AIController.ResearchPrefs —
        // the fallback must still pick it so science is never wasted.
        var techs   = new List<TechData> { new() { Id = "calendar", Name = "Calendar", ScienceCost = 55 } };
        var catalog = new DataCatalog(Units(), new List<BuildingData>(), techs);
        var state   = new GameState(TestWorlds.FlatMap(10, 10), catalog);
        var ai      = state.AddPlayer(new Player { Id = 0, Name = "AI", IsHuman = false });

        new AIController(state).TakeTurn(ai);

        Assert.Equal("calendar", state.Civ(ai).CurrentResearch);
    }

    [Fact]
    public void AI_BuildsEconomyBuildingWhenSafeAndArmed()
    {
        // Catalog with no settler/worker so production falls through to the
        // economy step; the AI is garrisoned, unthreatened, and fielding an army.
        var catalog = new DataCatalog(
            new List<UnitData> { WarriorData() },
            new List<BuildingData>
            {
                new() { Id = "granary", Name = "Granary", ProductionCost = 80,
                        Yields = new BuildingYields { Food = 2 } },
            },
            Techs());
        var state = new GameState(TestWorlds.FlatMap(10, 10), catalog);
        var ai    = state.AddPlayer(new Player { Id = 0, Name = "AI", IsHuman = false });
        var city  = new City("Rome", ai, new Vector2I(5, 5));
        state.Cities.Add(city);
        state.Units.Add(new Unit(WarriorData(), ai, new Vector2I(5, 5))); // garrison
        state.Units.Add(new Unit(WarriorData(), ai, new Vector2I(7, 5))); // field army

        new AIController(state).TakeTurn(ai);

        Assert.Equal("building:granary", city.ProductionItem);
    }

    [Fact]
    public void AI_DeclaresWarWhenOverwhelminglyStronger()
    {
        var state = MakeState(out var human, out var ai);
        state.Diplomacy.Set(human.Id, ai.Id, DiplomaticStance.Peace);
        state.Units.Add(new Unit(WarriorData(), ai, new Vector2I(0, 0)));
        state.Units.Add(new Unit(WarriorData(), ai, new Vector2I(2, 0)));
        // The human is disarmed — an irresistible target.

        new AIController(state).TakeTurn(ai);

        Assert.Equal(DiplomaticStance.War, state.Diplomacy.Between(ai.Id, human.Id));
    }

    [Fact]
    public void AI_DoesNotBreakNonAggressionPactOrMarchOnItsPartner()
    {
        var state = MakeState(out var human, out var ai);
        state.Diplomacy.Set(human.Id, ai.Id, DiplomaticStance.NonAggression);
        var aiUnit = new Unit(WarriorData(), ai, new Vector2I(0, 0));
        state.Units.Add(aiUnit);
        state.Units.Add(new Unit(WarriorData(), human, new Vector2I(5, 0)));

        new AIController(state).TakeTurn(ai);

        // Signed pacts hold even against a weaker partner, and a peaceful
        // player's units are not a march target.
        Assert.Equal(DiplomaticStance.NonAggression, state.Diplomacy.Between(ai.Id, human.Id));
        Assert.Equal(new Vector2I(0, 0), aiUnit.Position);
    }

    [Fact]
    public void AI_MakesStalematePeaceWithAnotherAIButNeverWithTheHuman()
    {
        var state = MakeState(out var human, out var ai);
        var ai2   = state.AddPlayer(new Player { Id = 2, Name = "P2", IsHuman = false });
        // Evenly matched forces all around (one warrior each).
        state.Units.Add(new Unit(WarriorData(), human, new Vector2I(0, 9)));
        state.Units.Add(new Unit(WarriorData(), ai,    new Vector2I(0, 0)));
        state.Units.Add(new Unit(WarriorData(), ai2,   new Vector2I(9, 9)));

        new AIController(state).TakeTurn(ai);

        // AI-AI stalemate settles into peace; the war with the human is never
        // ended unilaterally (that's the player's call via the diplomacy UI).
        Assert.Equal(DiplomaticStance.Peace, state.Diplomacy.Between(ai.Id, ai2.Id));
        Assert.Equal(DiplomaticStance.War,   state.Diplomacy.Between(ai.Id, human.Id));
    }

    [Fact]
    public void AI_WithEnemyInRange_Attacks()
    {
        var state = MakeState(out var human, out var ai);
        var aiUnit = new Unit(WarriorData(), ai,    new Vector2I(0, 0));
        var hUnit  = new Unit(WarriorData(), human, new Vector2I(1, 0));
        state.Units.Add(aiUnit);
        state.Units.Add(hUnit);

        new AIController(state).TakeTurn(ai);

        // Either it dealt damage or killed the defender.
        Assert.True(hUnit.HP < 100 || !state.Units.Contains(hUnit));
    }

    [Fact]
    public void AI_WithIdleCity_QueuesWarrior()
    {
        var state = MakeState(out _, out var ai);
        var city  = new City("Rome", ai, new Vector2I(5, 5));
        state.Cities.Add(city);

        new AIController(state).TakeTurn(ai);

        Assert.Equal("unit:warrior", city.ProductionItem);
    }

    [Fact]
    public void AI_WithBusyCity_DoesNotOverwriteProduction()
    {
        var state = MakeState(out _, out var ai);
        var city  = new City("Rome", ai, new Vector2I(5, 5)) { ProductionItem = "unit:archer" };
        state.Cities.Add(city);

        new AIController(state).TakeTurn(ai);

        Assert.Equal("unit:archer", city.ProductionItem);
    }

    [Fact]
    public void AI_DoesNotMutateHumanUnits()
    {
        var state = MakeState(out var human, out var ai);
        var hUnit = new Unit(WarriorData(), human, new Vector2I(0, 0));
        state.Units.Add(hUnit);

        new AIController(state).TakeTurn(ai);

        Assert.Equal(new Vector2I(0, 0), hUnit.Position);
        Assert.Equal(WarriorData().Movement, hUnit.MovementRemaining);
    }

    [Fact]
    public void AI_WithNoEnemyInRange_StepsTowardNearestEnemy()
    {
        var state = MakeState(out var human, out var ai);
        var aiUnit = new Unit(WarriorData(), ai,    new Vector2I(0, 0));
        var hUnit  = new Unit(WarriorData(), human, new Vector2I(5, 0));
        state.Units.Add(aiUnit);
        state.Units.Add(hUnit);

        new AIController(state).TakeTurn(ai);

        int distAfter = HexGrid.Distance(aiUnit.Position, hUnit.Position);
        Assert.True(distAfter < 5, $"AI should have closed the gap, distance = {distAfter}");
    }

    [Fact]
    public void AI_WhenIdleResearch_PicksATech()
    {
        var state = MakeState(out _, out var ai);
        state.Cities.Add(new City("Rome", ai, new Vector2I(5, 5)));

        new AIController(state).TakeTurn(ai);

        Assert.Equal("pottery", state.Civ(ai).CurrentResearch);
    }

    [Fact]
    public void AI_WithSettlerOnGoodSite_FoundsCity()
    {
        var state   = MakeState(out _, out var ai);
        var settler = new Unit(SettlerData(), ai, new Vector2I(5, 5));
        state.Units.Add(settler);

        new AIController(state).TakeTurn(ai);

        Assert.Contains(state.Cities, c => c.Owner == ai && c.Position == new Vector2I(5, 5));
        Assert.DoesNotContain(settler, state.Units); // consumed by founding
    }

    [Fact]
    public void AI_DeclinesASuicidalAttack()
    {
        var state  = MakeState(out var human, out var ai);
        var aiUnit = new Unit(WarriorData(), ai,    new Vector2I(0, 0)) { HP = 20 };
        var hUnit  = new Unit(WarriorData(), human, new Vector2I(1, 0)) { HP = 100 };
        state.Units.Add(aiUnit);
        state.Units.Add(hUnit);

        new AIController(state).TakeTurn(ai);

        // A wounded warrior must not throw itself at a full-health one it can't kill.
        Assert.Equal(100, hUnit.HP);
        Assert.Contains(aiUnit, state.Units);
    }

    [Fact]
    public void AI_MovesToGarrisonAThreatenedCity()
    {
        var state = MakeState(out var human, out var ai);
        var city  = new City("Rome", ai, new Vector2I(5, 5));
        state.Cities.Add(city);

        // Enemy within the threat radius of the (undefended) city.
        state.Units.Add(new Unit(WarriorData(), human, new Vector2I(5, 8)));
        // Friendly warrior in range but too far to engage the enemy this turn.
        var defender = new Unit(WarriorData(), ai, new Vector2I(5, 1));
        state.Units.Add(defender);

        int before = HexGrid.Distance(defender.Position, city.Position);
        new AIController(state).TakeTurn(ai);
        int after  = HexGrid.Distance(defender.Position, city.Position);

        Assert.True(after < before, $"defender should close on its city, {before} -> {after}");
    }
}
