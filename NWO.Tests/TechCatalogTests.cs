using System.Collections.Generic;
using NWO.Core;
using NWO.Entities;
using Xunit;

namespace NWO.Tests;

public class TechCatalogTests
{
    private static DataCatalog Make() => new(
        new[]
        {
            new UnitData     { Id = "warrior",  Name = "Warrior",  ProductionCost = 40 },
            new UnitData     { Id = "horseman", Name = "Horseman", ProductionCost = 80, RequiredTech = "horseback_riding" },
            new UnitData     { Id = "catapult", Name = "Catapult", ProductionCost = 100, Range = 2, RequiredTech = "iron_working" },
        },
        new[]
        {
            new BuildingData { Id = "granary",  Name = "Granary",  ProductionCost = 60, RequiredTech = "pottery" },
            new BuildingData { Id = "library",  Name = "Library",  ProductionCost = 90, RequiredTech = "writing" },
        },
        new[]
        {
            new TechData
            {
                Id = "pottery", Name = "Pottery", ScienceCost = 35,
                Unlocks = new TechUnlocks { Buildings = new List<string> { "granary" } },
            },
            new TechData
            {
                Id = "writing", Name = "Writing", ScienceCost = 55,
                Prerequisites = new List<string> { "pottery" },
                Unlocks = new TechUnlocks { Buildings = new List<string> { "library" } },
            },
            new TechData
            {
                Id = "horseback_riding", Name = "Horseback Riding", ScienceCost = 100,
                Prerequisites = new List<string> { "pottery" },
                Unlocks = new TechUnlocks { Units = new List<string> { "horseman" } },
            },
            new TechData
            {
                Id = "iron_working", Name = "Iron Working", ScienceCost = 100,
                Unlocks = new TechUnlocks { Units = new List<string> { "swordsman", "catapult" } },
            },
        });

    [Fact]
    public void Tech_LookupById_ReturnsNullForUnknown()
    {
        var c = Make();
        Assert.NotNull(c.Tech("pottery"));
        Assert.Null(c.Tech("nope"));
    }

    [Fact]
    public void UnlockingTech_FindsTechByUnitItem()
    {
        var c = Make();
        var tech = c.UnlockingTech("unit:horseman");
        Assert.NotNull(tech);
        Assert.Equal("horseback_riding", tech!.Id);
    }

    [Fact]
    public void UnlockingTech_FindsTechByBuildingItem()
    {
        var c = Make();
        Assert.Equal("pottery", c.UnlockingTech("building:granary")!.Id);
        Assert.Equal("writing", c.UnlockingTech("building:library")!.Id);
    }

    [Fact]
    public void UnlockingTech_ReturnsNullForFreeItems()
        => Assert.Null(Make().UnlockingTech("unit:warrior"));

    [Fact]
    public void UnlockingTech_FindsTech_WhenManyUnitsShareOneTech()
        => Assert.Equal("iron_working", Make().UnlockingTech("unit:catapult")!.Id);

    [Fact]
    public void Catalog_DefaultsToEmptyTechs_WhenNotProvided()
    {
        var c = new DataCatalog(new List<UnitData>(), new List<BuildingData>());
        Assert.Empty(c.Techs);
        Assert.Null(c.Tech("pottery"));
    }
}
