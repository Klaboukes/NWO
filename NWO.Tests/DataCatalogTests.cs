using NWO.Core;
using NWO.Entities;
using Xunit;

namespace NWO.Tests;

public class DataCatalogTests
{
    private static DataCatalog Make() => new(
        new[]
        {
            new UnitData { Id = "warrior",  Name = "Warrior",  ProductionCost = 40 },
            new UnitData { Id = "settler",  Name = "Settler",  ProductionCost = 100 },
        },
        new[]
        {
            new BuildingData { Id = "granary", Name = "Granary", ProductionCost = 60 },
        });

    [Theory]
    [InlineData("unit:warrior",       "unit",     "warrior")]
    [InlineData("building:granary",   "building", "granary")]
    [InlineData("unprefixed",         "",         "unprefixed")]
    [InlineData("unit:multi:colon",   "unit",     "multi:colon")]
    public void SplitItem_ParsesKindAndId(string input, string expectedKind, string expectedId)
    {
        var (k, i) = DataCatalog.SplitItem(input);
        Assert.Equal(expectedKind, k);
        Assert.Equal(expectedId, i);
    }

    [Fact]
    public void ItemCost_KnownUnit_ReturnsCost()
    {
        var c = Make();
        Assert.Equal(40,  c.ItemCost("unit:warrior"));
        Assert.Equal(100, c.ItemCost("unit:settler"));
    }

    [Fact]
    public void ItemCost_KnownBuilding_ReturnsCost()
        => Assert.Equal(60, Make().ItemCost("building:granary"));

    [Fact]
    public void ItemCost_UnknownItem_ReturnsMaxValue()
    {
        var c = Make();
        Assert.Equal(int.MaxValue, c.ItemCost("unit:missing"));
        Assert.Equal(int.MaxValue, c.ItemCost("garbage"));
    }

    [Fact]
    public void ItemName_UnknownItem_FallsBackToId()
    {
        var c = Make();
        Assert.Equal("missing", c.ItemName("unit:missing"));
        Assert.Equal("Warrior", c.ItemName("unit:warrior"));
    }

    [Fact]
    public void Lookup_ById_ReturnsNullForUnknown()
    {
        var c = Make();
        Assert.NotNull(c.Unit("warrior"));
        Assert.Null(c.Unit("nope"));
        Assert.NotNull(c.Building("granary"));
        Assert.Null(c.Building("nope"));
    }

    // ── Factions ─────────────────────────────────────────────────────────────

    private static DataCatalog WithFactions() => new(
        new[] { new UnitData { Id = "spearman", Name = "Spearman", ProductionCost = 60 } },
        System.Array.Empty<BuildingData>(),
        null,
        new[]
        {
            new FactionData
            {
                Id = "dominion", Name = "The Dominion", CityDefenseBonus = 4,
                UnitVariants = new() { ["spearman"] = "palace_guard" },
            },
        });

    [Fact]
    public void Faction_KnownId_ReturnsFaction()
    {
        var f = WithFactions().Faction("dominion");
        Assert.NotNull(f);
        Assert.Equal("The Dominion", f!.Name);
    }

    [Fact]
    public void Faction_UnknownId_ReturnsNull()
        => Assert.Null(WithFactions().Faction("nope"));

    [Fact]
    public void FactionOf_KnownPlayer_ReturnsItsFaction()
    {
        var p = new Player { Id = 0, FactionId = "dominion" };
        Assert.Equal(4, WithFactions().FactionOf(p).CityDefenseBonus);
    }

    [Fact]
    public void FactionOf_NullOrUnknownFaction_ReturnsNeutral()
    {
        var c = WithFactions();
        Assert.Same(FactionData.Neutral, c.FactionOf(new Player { Id = 0, FactionId = null }));
        Assert.Same(FactionData.Neutral, c.FactionOf(new Player { Id = 1, FactionId = "ghost" }));
        // Neutral is all-identity: every hook is a no-op.
        Assert.Equal(1.0, FactionData.Neutral.CombatStrengthMult);
        Assert.Equal(0,   FactionData.Neutral.CityDefenseBonus);
    }

    [Fact]
    public void ResolveUnitForFaction_MapsVariantElseBase()
    {
        var c = WithFactions();
        var dominion = new Player { Id = 0, FactionId = "dominion" };
        var neutral  = new Player { Id = 1, FactionId = null };
        Assert.Equal("palace_guard", c.ResolveUnitForFaction("spearman", dominion));
        Assert.Equal("warrior",      c.ResolveUnitForFaction("warrior",  dominion)); // unmapped base
        Assert.Equal("spearman",     c.ResolveUnitForFaction("spearman", neutral));  // no faction
    }

    [Fact]
    public void IsFactionVariant_FlagsVariantsNotBases()
    {
        var c = WithFactions();
        Assert.True(c.IsFactionVariant("palace_guard")); // a faction's unique variant
        Assert.False(c.IsFactionVariant("spearman"));    // its base unit
        Assert.False(c.IsFactionVariant("warrior"));     // unrelated unit
    }
}
