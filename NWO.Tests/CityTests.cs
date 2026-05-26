using Godot;
using NWO.Entities;
using Xunit;

namespace NWO.Tests;

public class CityTests
{
    private static Player MakePlayer() => new() { Id = 0, Name = "P0" };

    private static City MakeCity(int pop = 1, int foodYield = 4, int prodYield = 2)
    {
        var c = new City("Testopolis", MakePlayer(), new Vector2I(0, 0))
        {
            Population      = pop,
            FoodYield       = foodYield,
            ProductionYield = prodYield,
        };
        return c;
    }

    [Fact]
    public void ProcessFood_AccumulatesNetFood()
    {
        var c = MakeCity(pop: 1, foodYield: 5); // net = 5 - 1 = 4
        c.ProcessFood();
        Assert.Equal(4f, c.FoodAccumulated);
    }

    [Fact]
    public void ProcessFood_StarvationClampsToZero()
    {
        var c = MakeCity(pop: 5, foodYield: 1); // net = -4
        c.ProcessFood();
        Assert.Equal(0f, c.FoodAccumulated);
    }

    [Fact]
    public void ProcessFood_GrowsWhenThresholdHit()
    {
        var c = MakeCity(pop: 1, foodYield: 22); // threshold for pop 1 = 21; net = 21 -> just hits
        // Threshold formula: 15 + 6*Population = 15 + 6 = 21
        Assert.Equal(21, c.GrowthThreshold);
        bool grew = c.ProcessFood();
        Assert.True(grew);
        Assert.Equal(2, c.Population);
    }

    [Fact]
    public void ProcessFood_DoesNotGrowBelowThreshold()
    {
        var c = MakeCity(pop: 1, foodYield: 5);
        Assert.False(c.ProcessFood());
        Assert.Equal(1, c.Population);
    }

    [Fact]
    public void AdvanceProduction_NoItem_ReturnsNull()
    {
        var c = MakeCity();
        Assert.Null(c.AdvanceProduction(40));
        Assert.Equal(0, c.ProductionProgress);
    }

    [Fact]
    public void AdvanceProduction_AccumulatesUntilCost()
    {
        var c = MakeCity(prodYield: 10);
        c.ProductionItem = "unit:warrior";
        Assert.Null(c.AdvanceProduction(40));
        Assert.Equal(10, c.ProductionProgress);
        Assert.Null(c.AdvanceProduction(40));
        Assert.Null(c.AdvanceProduction(40));
        Assert.Equal(30, c.ProductionProgress);
        var done = c.AdvanceProduction(40);
        Assert.Equal("unit:warrior", done);
        Assert.Null(c.ProductionItem);
    }

    [Fact]
    public void AdvanceProduction_OverflowCarriesIntoNextItem()
    {
        var c = MakeCity(prodYield: 50);
        c.ProductionItem = "unit:warrior";
        var done = c.AdvanceProduction(40); // overflow of 10
        Assert.Equal("unit:warrior", done);
        Assert.Equal(10, c.ProductionProgress); // carries over
    }

    [Fact]
    public void NeedsAttention_TrueWhenNoProductionItem()
    {
        var c = MakeCity();
        Assert.True(c.NeedsAttention);
        c.ProductionItem = "unit:warrior";
        Assert.False(c.NeedsAttention);
    }
}
