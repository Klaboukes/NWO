using System.Collections.Generic;
using Godot;
using NWO.Core;
using NWO.Entities;
using NWO.Map;
using Xunit;

namespace NWO.Tests;

// M2a — strategic resources: per-civ reveal (tech-gated), access (controlling a
// resource tile), build-list gating, and the worked-tile production bonus.
public class ResourceTests
{
    private static TechData AnimalHusbandry() => new()
    {
        Id = "animal_husbandry", Name = "Animal Husbandry", ScienceCost = 35,
        Unlocks = new TechUnlocks { RevealedResources = new List<string> { "horses" } },
    };

    private static UnitData Horseman() => new()
    {
        Id = "horseman", Name = "Horseman", Attack = 12, Defense = 7, Movement = 4,
        Range = 1, Sight = 2, ProductionCost = 80, RequiredResource = "horses",
    };

    // Flat 20x20 Plains map, one human player, catalog with Warrior + Horseman
    // and the Animal Husbandry tech (reveals horses).
    private static GameState NewState(out Player human)
    {
        var map = new MapData(20, 20);
        for (int q = 0; q < 20; q++)
        for (int r = 0; r < 20; r++)
            map.Tiles[new Vector2I(q, r)] = TerrainType.Plains;

        var catalog = new DataCatalog(
            new List<UnitData> { TestWorlds.Warrior(), Horseman() },
            new List<BuildingData>(),
            new List<TechData> { AnimalHusbandry() });

        var state = new GameState(map, catalog);
        human = state.AddPlayer(new Player { Id = 0, Name = "P", IsHuman = true });
        return state;
    }

    // ── Id mapping ─────────────────────────────────────────────────────────────

    [Fact]
    public void FromId_RoundTrips()
    {
        Assert.Equal(ResourceType.Horses, ResourceService.FromId("horses"));
        Assert.Equal(ResourceType.Iron,   ResourceService.FromId("iron"));
        Assert.Equal(ResourceType.None,   ResourceService.FromId(null));
        Assert.Equal("horses", ResourceService.ToId(ResourceType.Horses));
    }

    // ── Reveal ──────────────────────────────────────────────────────────────────

    [Fact]
    public void IsRevealed_FalseBeforeTech_TrueAfter()
    {
        var state = NewState(out var human);
        Assert.False(ResourceService.IsRevealed(state, human, ResourceType.Horses));

        state.Civ(human).ResearchedTechs.Add("animal_husbandry");
        Assert.True(ResourceService.IsRevealed(state, human, ResourceType.Horses));
    }

    [Fact]
    public void IsRevealed_UngatedResource_AlwaysTrue()
    {
        var state = NewState(out var human);
        // Iron is not revealed by any tech in this catalog → always revealed.
        Assert.True(ResourceService.IsRevealed(state, human, ResourceType.Iron));
    }

    // ── Access ──────────────────────────────────────────────────────────────────

    [Fact]
    public void HasAccess_RequiresBothRevealAndControl()
    {
        var state = NewState(out var human);
        state.Map.Resources[new Vector2I(5, 5)] = ResourceType.Horses;
        state.Cities.Add(new City("Rome", human, new Vector2I(5, 5)));

        // Controls the tile, but hasn't revealed horses yet.
        Assert.False(ResourceService.HasAccess(state, human, ResourceType.Horses));

        state.Civ(human).ResearchedTechs.Add("animal_husbandry");
        Assert.True(ResourceService.HasAccess(state, human, ResourceType.Horses));
    }

    [Fact]
    public void HasAccess_FalseWhenNoCityControlsTheResource()
    {
        var state = NewState(out var human);
        state.Civ(human).ResearchedTechs.Add("animal_husbandry");
        state.Map.Resources[new Vector2I(15, 15)] = ResourceType.Horses; // far from any city
        state.Cities.Add(new City("Rome", human, new Vector2I(5, 5)));

        Assert.False(ResourceService.HasAccess(state, human, ResourceType.Horses));
    }

    // ── Build-list gate ───────────────────────────────────────────────────────────

    [Fact]
    public void Allows_GatesResourceUnit_ButNotPlainUnit()
    {
        var state = NewState(out var human);
        state.Civ(human).ResearchedTechs.Add("animal_husbandry");

        Assert.True(ResourceService.Allows(state, human, null));            // Warrior — no requirement
        Assert.False(ResourceService.Allows(state, human, "horses"));       // no access yet

        state.Map.Resources[new Vector2I(5, 5)] = ResourceType.Horses;
        state.Cities.Add(new City("Rome", human, new Vector2I(5, 5)));
        Assert.True(ResourceService.Allows(state, human, "horses"));        // now accessible
    }

    // ── Worked-tile production bonus ──────────────────────────────────────────────

    [Fact]
    public void RevealedResourceTile_AddsProductionWhenWorked()
    {
        var state = NewState(out var human);
        var city  = new City("Rome", human, new Vector2I(5, 5)) { Population = 1 };
        state.Cities.Add(city);

        var resourceTile = new Vector2I(6, 5);                 // distance 1, workable
        state.Map.Resources[resourceTile] = ResourceType.Horses;
        city.Workforce.Locked.Add(resourceTile);               // force the citizen onto it

        CityWorkforceService.Recompute(state, city);
        int withoutReveal = city.ProductionYield;

        state.Civ(human).ResearchedTechs.Add("animal_husbandry");
        CityWorkforceService.Recompute(state, city);
        int withReveal = city.ProductionYield;

        Assert.Equal(withoutReveal + 1, withReveal);
    }

    // ── Bonus resources (9.2) ─────────────────────────────────────────────────────

    [Fact]
    public void ResourceYields_TiersAndYields()
    {
        Assert.Equal(ResourceTier.Strategic, ResourceYields.Tier(ResourceType.Horses));
        Assert.Equal(ResourceTier.Bonus,     ResourceYields.Tier(ResourceType.Wheat));

        Assert.Equal(1, ResourceYields.Food(ResourceType.Wheat));   // food bonus
        Assert.Equal(0, ResourceYields.Production(ResourceType.Wheat));
        Assert.Equal(1, ResourceYields.Production(ResourceType.Sheep)); // prod bonus
        Assert.Equal(0, ResourceYields.Food(ResourceType.Sheep));
    }

    [Fact]
    public void BonusResourceId_RoundTrips()
    {
        Assert.Equal(ResourceType.Wheat,  ResourceService.FromId("wheat"));
        Assert.Equal("banana", ResourceService.ToId(ResourceType.Banana));
    }

    [Fact]
    public void BonusResourceTile_AddsFoodWhenWorked_NoTechNeeded()
    {
        var state = NewState(out var human);
        var city  = new City("Rome", human, new Vector2I(5, 5)) { Population = 1 };
        state.Cities.Add(city);

        var tile = new Vector2I(6, 5);          // distance 1, workable
        city.Workforce.Locked.Add(tile);        // force the citizen onto it

        CityWorkforceService.Recompute(state, city);
        int baseFood = city.FoodYield;

        // Wheat is a bonus resource — always revealed, no tech required.
        state.Map.Resources[tile] = ResourceType.Wheat;
        CityWorkforceService.Recompute(state, city);

        Assert.Equal(baseFood + 1, city.FoodYield);
    }
}
