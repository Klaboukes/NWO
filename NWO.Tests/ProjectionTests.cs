using Godot;
using NWO.Map;
using Xunit;

namespace NWO.Tests;

// Guards the axial-hex ↔ 3D-world projection (docs/ROADMAP.md V7.1). Mouse picking
// casts a ray onto the ground plane and feeds the hit to WorldToAxial, while the
// movement animation round-trips axial → world → axial — so the inverse must land
// back on the tile AxialToWorld placed.
public class ProjectionTests
{
    [Fact]
    public void WorldToAxial_InvertsAxialToWorld_AcrossGrid()
    {
        for (int q = -12; q <= 12; q++)
        for (int r = -12; r <= 12; r++)
        {
            var axial = new Vector2I(q, r);
            var round = HexProjection.WorldToAxial(HexProjection.AxialToWorld(axial));
            Assert.Equal(axial, round);
        }
    }

    [Fact]
    public void AxialToWorld_LiesOnGroundPlane()
    {
        // The projection itself places tiles on Y = 0; elevation is added by the
        // renderer via TopHeight, not by AxialToWorld.
        Assert.Equal(0f, HexProjection.AxialToWorld(new Vector2I(3, -2)).Y);
    }

    [Fact]
    public void WorldToAxial_IgnoresElevation()
    {
        // A point above a tile (any Y) still picks that tile — picking happens on
        // the ground footprint regardless of prism height.
        var axial   = new Vector2I(4, 1);
        var ground  = HexProjection.AxialToWorld(axial);
        var raised  = ground + new Vector3(0f, 999f, 0f);
        Assert.Equal(axial, HexProjection.WorldToAxial(raised));
    }

    [Fact]
    public void TopHeight_RaisesTallTerrainMore()
    {
        Assert.Equal(0f, HexProjection.Elevation(TerrainType.Grassland));
        Assert.True(HexProjection.TopHeight(TerrainType.Grassland)
                  < HexProjection.TopHeight(TerrainType.Forest));
        Assert.True(HexProjection.TopHeight(TerrainType.Forest)
                  < HexProjection.TopHeight(TerrainType.Mountain));
        // A Hills feature raises a tile above its flat (non-hill) self.
        Assert.True(HexProjection.TopHeight(TerrainType.Grassland, hill: false)
                  < HexProjection.TopHeight(TerrainType.Grassland, hill: true));
    }
}
