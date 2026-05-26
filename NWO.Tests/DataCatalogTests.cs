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
}
