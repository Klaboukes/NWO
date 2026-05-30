using Godot;
using NWO.Map;
using Xunit;

namespace NWO.Tests;

// Guards the baked-2.5D projection (docs/ROADMAP.md V7.1). The vertical
// foreshortening lives inside AxialToWorld, so WorldToAxial must invert it
// exactly — otherwise the tilt would silently break mouse picking and the
// movement animation, which both round-trip through these two methods.
public class ProjectionTests
{
    [Fact]
    public void WorldToAxial_InvertsAxialToWorld_AcrossGrid()
    {
        for (int q = -12; q <= 12; q++)
        for (int r = -12; r <= 12; r++)
        {
            var axial = new Vector2I(q, r);
            var round = WorldRenderer.WorldToAxial(WorldRenderer.AxialToWorld(axial));
            Assert.Equal(axial, round);
        }
    }

    [Fact]
    public void AxialToWorld_ForeshortensVerticalAxis()
    {
        // Same row spacing in axial maps to a squashed vertical span in world space.
        float spanY = WorldRenderer.AxialToWorld(new Vector2I(0, 1)).Y
                    - WorldRenderer.AxialToWorld(new Vector2I(0, 0)).Y;
        float unscaled = WorldRenderer.HexSize * Mathf.Sqrt(3f);
        Assert.Equal(unscaled * WorldRenderer.VerticalScale, spanY, 0.001f);
        Assert.True(WorldRenderer.VerticalScale < 1f);
    }

    [Fact]
    public void ElevationLift_RaisesTallTerrainMore()
    {
        Assert.Equal(0f, WorldRenderer.ElevationLift(TerrainType.Grassland));
        Assert.True(WorldRenderer.ElevationLift(TerrainType.Forest)
                  < WorldRenderer.ElevationLift(TerrainType.Hills));
        Assert.True(WorldRenderer.ElevationLift(TerrainType.Hills)
                  < WorldRenderer.ElevationLift(TerrainType.Mountain));
    }
}
