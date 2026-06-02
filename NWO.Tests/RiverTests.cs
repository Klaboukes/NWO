using Godot;
using NWO.Map;
using Xunit;

namespace NWO.Tests;

// Rivers are stored as an edge-set of (tile, dir) and traced by walking a graph of
// hex corners downhill, one shared edge per step. Both the tracer and the renderer
// rely on a single topology fact: an edge (tile, dir) is a *shared* boundary whose
// two endpoint corners are also corners of the neighbour across it. If that holds,
// river segments lie on the correct side and consecutive edges meet at a vertex —
// the continuous channel the feature needs. MapGenerator.Generate itself can't run
// here (FastNoiseLite needs the Godot runtime), so we test the topology directly.
public class RiverTests
{
    // Canonical id for a hex corner: the sorted triple of the three tiles meeting
    // there, so the same vertex compares equal from any tile/corner naming it.
    private static (Vector2I, Vector2I, Vector2I) Corner(Vector2I tile, int c)
    {
        var (a, b, cc) = HexGrid.CornerTiles(tile, c);
        var arr = new[] { a, b, cc };
        System.Array.Sort(arr, (u, v) => u.X != v.X ? u.X - v.X : u.Y - v.Y);
        return (arr[0], arr[1], arr[2]);
    }

    // The two endpoint vertices of edge (tile, dir): corners e and e+1, where
    // e = (6-dir)%6 — the exact flat-top mapping WorldOverlay.DrawRivers uses.
    private static ((Vector2I, Vector2I, Vector2I), (Vector2I, Vector2I, Vector2I))
        EdgeEndpoints(Vector2I tile, int dir)
    {
        int e = (6 - dir) % 6;
        return (Corner(tile, e), Corner(tile, (e + 1) % 6));
    }

    [Fact]
    public void Edge_IsSharedBoundary_WithNeighbourAcrossIt()
    {
        var tile = new Vector2I(3, 4); // arbitrary interior tile
        for (int dir = 0; dir < 6; dir++)
        {
            var neighbour = tile + HexGrid.Directions[dir];
            var (a1, b1)  = EdgeEndpoints(tile, dir);
            // Same edge named from the neighbour's side: opposite direction.
            var (a2, b2)  = EdgeEndpoints(neighbour, (dir + 3) % 6);

            // The endpoint-vertex pair must be identical from both sides (unordered).
            bool same = (a1.Equals(a2) && b1.Equals(b2)) || (a1.Equals(b2) && b1.Equals(a2));
            Assert.True(same, $"edge (tile {tile}, dir {dir}) does not match its neighbour's edge");
        }
    }

    [Fact]
    public void AdjacentEdgesOfATile_ShareExactlyOneCorner()
    {
        var tile = new Vector2I(3, 4);
        for (int dir = 0; dir < 6; dir++)
        {
            var (a1, b1) = EdgeEndpoints(tile, dir);
            var (a2, b2) = EdgeEndpoints(tile, (dir + 1) % 6);

            int shared = 0;
            if (a1.Equals(a2) || a1.Equals(b2)) shared++;
            if (b1.Equals(a2) || b1.Equals(b2)) shared++;

            // Two edges of the same hex that are one step apart meet at one corner —
            // this is what lets a turning river chain stay connected.
            Assert.Equal(1, shared);
        }
    }
}
