using System.Collections.Generic;
using System.Linq;
using Godot;
using NWO.Map;
using Xunit;

namespace NWO.Tests;

// Phase 14.2 post-classification geography: lake flood-fill and adjacency
// coastlines, driven by hand-built maps (no noise engine).
public class MapPostProcessTests
{
    // Mirrors MapGenerator.EvenQOffsetToAxial so tests address tiles in the same
    // offset space MapPostProcess uses for map-edge detection.
    private static Vector2I Axial(int col, int row)
        => new(col, row - (col - (col & 1)) / 2);

    // A width×height all-Plains rectangle in even-q offset space.
    private static MapData OffsetRectMap(int width, int height)
    {
        var map = new MapData(width, height);
        for (int col = 0; col < width; col++)
        for (int row = 0; row < height; row++)
            map.Tiles[Axial(col, row)] = TerrainType.Plains;
        return map;
    }

    private static void PaintOcean(MapData map, params (int Col, int Row)[] cells)
    {
        foreach (var (c, r) in cells) map.Tiles[Axial(c, r)] = TerrainType.Ocean;
    }

    [Fact]
    public void FormLakes_SmallEnclosedBasinBecomesLake()
    {
        var map = OffsetRectMap(11, 11);
        PaintOcean(map, (5, 5), (5, 6), (6, 5));

        MapPostProcess.FormLakes(map);

        Assert.Equal(TerrainType.Lake, map.Tiles[Axial(5, 5)]);
        Assert.Equal(TerrainType.Lake, map.Tiles[Axial(5, 6)]);
        Assert.Equal(TerrainType.Lake, map.Tiles[Axial(6, 5)]);
    }

    [Fact]
    public void FormLakes_LargeEnclosedSeaStaysSea()
    {
        // A 4×3 enclosed basin (12 tiles) exceeds LakeMaxArea = 9 → inland sea.
        var map = OffsetRectMap(14, 14);
        var basin = new List<(int, int)>();
        for (int c = 4; c < 8; c++)
        for (int r = 4; r < 7; r++)
            basin.Add((c, r));
        PaintOcean(map, basin.ToArray());

        MapPostProcess.FormLakes(map);

        Assert.All(basin, cell => Assert.Equal(TerrainType.Ocean, map.Tiles[Axial(cell.Item1, cell.Item2)]));
    }

    [Fact]
    public void FormLakes_EdgeTouchingWaterIsNeverALake()
    {
        var map = OffsetRectMap(11, 11);
        PaintOcean(map, (0, 4), (1, 4), (2, 4)); // touches col 0 → world ocean

        MapPostProcess.FormLakes(map);

        Assert.Equal(TerrainType.Ocean, map.Tiles[Axial(0, 4)]);
        Assert.Equal(TerrainType.Ocean, map.Tiles[Axial(2, 4)]);
    }

    [Fact]
    public void FormCoasts_LandIsRingedByCoast_OpenSeaStaysOcean()
    {
        // All-ocean map with one land tile in the middle; shelfChance 0 → exactly
        // the six neighbours become Coast, everything else stays Ocean.
        var map = OffsetRectMap(11, 11);
        foreach (var k in map.Tiles.Keys.ToList()) map.Tiles[k] = TerrainType.Ocean;
        var land = Axial(5, 5);
        map.Tiles[land] = TerrainType.Grassland;

        MapPostProcess.FormCoasts(map, shelfChance: 0f, seed: 42);

        var ring = HexGrid.GetNeighbors(land).ToHashSet();
        foreach (var (axial, t) in map.Tiles)
        {
            if (axial == land)                Assert.Equal(TerrainType.Grassland, t);
            else if (ring.Contains(axial))    Assert.Equal(TerrainType.Coast, t);
            else                              Assert.Equal(TerrainType.Ocean, t);
        }
    }

    [Fact]
    public void FormCoasts_FullShelfChanceExtendsExactlyOneMoreRing()
    {
        var map = OffsetRectMap(13, 13);
        foreach (var k in map.Tiles.Keys.ToList()) map.Tiles[k] = TerrainType.Ocean;
        var land = Axial(6, 6);
        map.Tiles[land] = TerrainType.Grassland;

        MapPostProcess.FormCoasts(map, shelfChance: 1f, seed: 42);

        foreach (var (axial, t) in map.Tiles)
        {
            if (axial == land) continue;
            int d = HexGrid.Distance(land, axial);
            if (d <= 2) Assert.Equal(TerrainType.Coast, t); // ring 1 + full ring 2
            else        Assert.Equal(TerrainType.Ocean, t); // never a third ring
        }
    }

    [Fact]
    public void FormCoasts_IsDeterministicPerSeed()
    {
        MapData Build()
        {
            var m = OffsetRectMap(13, 13);
            foreach (var k in m.Tiles.Keys.ToList()) m.Tiles[k] = TerrainType.Ocean;
            m.Tiles[Axial(6, 6)] = TerrainType.Grassland;
            return m;
        }

        var a = Build();
        var b = Build();
        MapPostProcess.FormCoasts(a, shelfChance: 0.5f, seed: 7);
        MapPostProcess.FormCoasts(b, shelfChance: 0.5f, seed: 7);

        foreach (var (axial, t) in a.Tiles) Assert.Equal(t, b.Tiles[axial]);
        // And a partial shelf actually mixes Coast and Ocean in ring 2.
        var ring2 = a.Tiles.Keys.Where(k => HexGrid.Distance(Axial(6, 6), k) == 2).ToList();
        Assert.Contains(ring2, k => a.Tiles[k] == TerrainType.Coast);
        Assert.Contains(ring2, k => a.Tiles[k] == TerrainType.Ocean);
    }

    [Fact]
    public void FormCoasts_LakesAreLeftAlone()
    {
        var map = OffsetRectMap(11, 11);
        PaintOcean(map, (5, 5));
        MapPostProcess.FormLakes(map); // 1-tile basin → Lake

        MapPostProcess.FormCoasts(map, shelfChance: 1f, seed: 1);

        Assert.Equal(TerrainType.Lake, map.Tiles[Axial(5, 5)]);
    }
}
