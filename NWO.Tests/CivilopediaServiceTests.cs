using System;
using System.Linq;
using NWO.Core;
using NWO.Entities;
using NWO.Map;
using Xunit;

namespace NWO.Tests;

public class CivilopediaServiceTests
{
    private static DataCatalog Catalog() => new(
        new[]
        {
            new UnitData { Id = "warrior", Name = "Warrior", ProductionCost = 40, Attack = 8, Defense = 8, Movement = 2 },
            new UnitData { Id = "settler", Name = "Settler", ProductionCost = 100, Movement = 2, Special = "found_city" },
            new UnitData { Id = "legionary", Name = "Legionary", ProductionCost = 45, Attack = 11, Defense = 11, Movement = 2 },
        },
        new[] { new BuildingData { Id = "granary", Name = "Granary", ProductionCost = 80, RequiredTech = "pottery", Yields = new BuildingYields { Food = 2 } } },
        new[] { new TechData { Id = "pottery", Name = "Pottery", ScienceCost = 35 } },
        new[]
        {
            new FactionData
            {
                Id = "iron_pact", Name = "The Iron Pact", Tree = "Honor",
                CombatStrengthMult = 1.15, XpGainMult = 2.0, HealInEnemyLand = true,
                UnitVariants = new() { ["warrior"] = "legionary" },
            },
        });

    private static CivilopediaContent Content() => new()
    {
        Prose = new()
        {
            ["faction:iron_pact"] = "Strength decides who is right.",
            ["unit:warrior"]      = "The backbone of every army.",
        },
        Articles =
        {
            new CivilopediaArticle { Id = "world:sundering", Category = "world", Title = "The Sundering", Body = "Society fractured." },
            new CivilopediaArticle { Id = "mech:combat", Category = "mechanics", Title = "Combat", Body = "Compare strengths." },
        },
    };

    [Fact]
    public void Categories_ExposeTheFullOrderedSet()
    {
        var svc = new CivilopediaService(Catalog(), Content());
        Assert.Equal(
            new[] { "world", "factions", "units", "buildings", "techs", "terrain", "features", "resources", "mechanics" },
            svc.Categories.Select(c => c.Id).ToArray());
    }

    [Fact]
    public void EveryCatalogEntity_ProducesAnEntry()
    {
        var cat = Catalog();
        var svc = new CivilopediaService(cat, Content());
        Assert.Equal(cat.Units.Count,     Category(svc, "units").Entries.Count);
        Assert.Equal(cat.Buildings.Count, Category(svc, "buildings").Entries.Count);
        Assert.Equal(cat.Techs.Count,     Category(svc, "techs").Entries.Count);
        Assert.Equal(cat.Factions.Count,  Category(svc, "factions").Entries.Count);
    }

    [Fact]
    public void TerrainCategory_CoversEveryEnumValue()
    {
        var svc = new CivilopediaService(Catalog(), Content());
        Assert.Equal(Enum.GetValues<TerrainType>().Length, Category(svc, "terrain").Entries.Count);
        // One entry per feature flag (None is not a flag).
        Assert.Equal(FeatureRules.Flags.Length, Category(svc, "features").Entries.Count);
        // ResourceType.None is excluded; every other value gets an entry.
        Assert.Equal(Enum.GetValues<ResourceType>().Length - 1, Category(svc, "resources").Entries.Count);
    }

    [Fact]
    public void Prose_IsAppendedToTheStatsBlock()
    {
        var svc = new CivilopediaService(Catalog(), Content());
        var warrior = Entry(svc, "units", "unit:warrior");
        Assert.Contains("Strength: 8 attack / 8 defense", warrior.Detail); // live stats
        Assert.Contains("The backbone of every army.",     warrior.Detail); // authored prose
    }

    [Fact]
    public void MissingProse_FallsBackToStatsOnly()
    {
        // Same catalog, but no prose/articles at all.
        var svc = new CivilopediaService(Catalog(), CivilopediaContent.Empty);
        var settler = Entry(svc, "units", "unit:settler");
        Assert.Contains("Founds new cities", settler.Detail);
        Assert.DoesNotContain("backbone", settler.Detail);
    }

    [Fact]
    public void FactionModifiers_RenderAsReadableLines()
    {
        var svc = new CivilopediaService(Catalog(), Content());
        var d = Entry(svc, "factions", "faction:iron_pact").Detail;
        Assert.Contains("Ideology: Honor", d);
        Assert.Contains("+15% combat strength", d);
        Assert.Contains("+100% veterancy XP gain", d);
        Assert.Contains("Units heal in enemy territory", d);
        Assert.Contains("Unique unit: Legionary (replaces Warrior)", d);
        Assert.Contains("Strength decides who is right.", d); // prose
    }

    [Fact]
    public void Articles_AreGroupedIntoTheirCategory()
    {
        var svc = new CivilopediaService(Catalog(), Content());
        var world = Category(svc, "world");
        Assert.Single(world.Entries);
        Assert.Equal("The Sundering", world.Entries[0].Title);
        Assert.Equal("Society fractured.", world.Entries[0].Detail);

        Assert.Single(Category(svc, "mechanics").Entries);
    }

    private static CivilopediaCategory Category(CivilopediaService svc, string id)
        => svc.Categories.First(c => c.Id == id);

    private static CivilopediaEntry Entry(CivilopediaService svc, string categoryId, string entryId)
        => Category(svc, categoryId).Entries.First(e => e.Id == entryId);
}
