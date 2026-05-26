using System.Collections.Generic;
using System.Linq;
using Godot;
using NWO.Map;
using Xunit;

namespace NWO.Tests;

public class HexGridTests
{
    [Fact]
    public void Distance_SameTile_IsZero()
        => Assert.Equal(0, HexGrid.Distance(new Vector2I(3, -1), new Vector2I(3, -1)));

    [Fact]
    public void Distance_AdjacentTiles_IsOne()
    {
        var origin = new Vector2I(0, 0);
        foreach (var dir in HexGrid.Directions)
            Assert.Equal(1, HexGrid.Distance(origin, origin + dir));
    }

    [Fact]
    public void Distance_Symmetric()
    {
        var a = new Vector2I(2, 3);
        var b = new Vector2I(-1, 4);
        Assert.Equal(HexGrid.Distance(a, b), HexGrid.Distance(b, a));
    }

    [Fact]
    public void GetNeighbors_ReturnsSixUniqueAdjacentTiles()
    {
        var neighbors = HexGrid.GetNeighbors(new Vector2I(5, 5));
        Assert.Equal(6, neighbors.Length);
        Assert.Equal(6, neighbors.Distinct().Count());
        foreach (var n in neighbors)
            Assert.Equal(1, HexGrid.Distance(new Vector2I(5, 5), n));
    }

    [Fact]
    public void GetRing_RadiusZero_ReturnsCenter()
    {
        var center = new Vector2I(2, 2);
        var ring   = HexGrid.GetRing(center, 0);
        Assert.Single(ring);
        Assert.Equal(center, ring[0]);
    }

    [Theory]
    [InlineData(1, 6)]
    [InlineData(2, 12)]
    [InlineData(3, 18)]
    [InlineData(4, 24)]
    public void GetRing_ReturnsSixTimesRadiusTiles(int radius, int expectedCount)
    {
        var ring = HexGrid.GetRing(new Vector2I(0, 0), radius);
        Assert.Equal(expectedCount, ring.Count);
        Assert.All(ring, t => Assert.Equal(radius, HexGrid.Distance(new Vector2I(0, 0), t)));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 7)]
    [InlineData(2, 19)]
    [InlineData(3, 37)]
    public void GetRange_FilledDisc_HasCorrectTileCount(int radius, int expectedCount)
    {
        var range = HexGrid.GetRange(new Vector2I(0, 0), radius);
        Assert.Equal(expectedCount, range.Count);
        Assert.All(range, t => Assert.True(HexGrid.Distance(new Vector2I(0, 0), t) <= radius));
    }

    [Fact]
    public void FindPath_SameOriginAndDestination_ReturnsSingleTile()
    {
        var origin = new Vector2I(3, 3);
        var path   = HexGrid.FindPath(origin, origin, _ => 1);
        Assert.Single(path);
        Assert.Equal(origin, path[0]);
    }

    [Fact]
    public void FindPath_StartAndEndAreEndpoints()
    {
        var from = new Vector2I(0, 0);
        var to   = new Vector2I(5, -2);
        var path = HexGrid.FindPath(from, to, _ => 1);
        Assert.Equal(from, path[0]);
        Assert.Equal(to, path[^1]);
    }

    [Fact]
    public void FindPath_OnUniformGrid_IsMinimumLength()
    {
        var from = new Vector2I(0, 0);
        var to   = new Vector2I(4, -2);
        var path = HexGrid.FindPath(from, to, _ => 1);
        Assert.Equal(HexGrid.Distance(from, to) + 1, path.Count);
    }

    [Fact]
    public void FindPath_BlockedByWall_RoutesAround()
    {
        // 7x7 grid centered on origin; a partial wall along q=2 forces a detour.
        // Bounding the grid is required — A* on an unbounded passable plane would
        // explore forever when no path exists, even with the safety expansion cap.
        int Cost(Vector2I t)
        {
            if (System.Math.Abs(t.X) > 3 || System.Math.Abs(t.Y) > 3) return int.MaxValue;
            if (t.X == 2 && System.Math.Abs(t.Y) <= 1) return int.MaxValue;
            return 1;
        }
        var from = new Vector2I(0, 0);
        var to   = new Vector2I(3, 0);
        var path = HexGrid.FindPath(from, to, Cost);
        Assert.NotEmpty(path);
        Assert.Equal(from, path[0]);
        Assert.Equal(to,   path[^1]);
        Assert.All(path, t => Assert.False(t.X == 2 && System.Math.Abs(t.Y) <= 1));
    }

    [Fact]
    public void FindPath_NoPath_ReturnsEmpty()
    {
        // Completely walled in.
        int Cost(Vector2I t) => t == new Vector2I(0, 0) ? 1 : int.MaxValue;
        var path = HexGrid.FindPath(new Vector2I(0, 0), new Vector2I(3, 0), Cost);
        Assert.Empty(path);
    }

    [Fact]
    public void GetReachableTiles_FullBudget_CoversExpectedRange()
    {
        var origin    = new Vector2I(0, 0);
        var reachable = HexGrid.GetReachableTiles(origin, 2, _ => 1);
        // 2 moves on uniform terrain = filled disc minus center, distance ≤ 2
        Assert.All(reachable, t => Assert.True(HexGrid.Distance(origin, t) <= 2));
        Assert.DoesNotContain(origin, reachable);
        // Disc(2) has 19 tiles; minus center = 18
        Assert.Equal(18, reachable.Count);
    }

    [Fact]
    public void GetReachableTiles_RespectsHigherCosts()
    {
        var origin = new Vector2I(0, 0);
        // East tile costs 3, more than the budget of 2; should not be reachable directly
        int Cost(Vector2I t) => t == new Vector2I(1, 0) ? 3 : 1;
        var reachable = HexGrid.GetReachableTiles(origin, 2, Cost);
        Assert.DoesNotContain(new Vector2I(1, 0), reachable);
    }
}
