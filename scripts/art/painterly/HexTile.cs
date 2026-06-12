using Godot;

namespace NWO.Art.Painterly;

// Flat-top hex footprint helpers for terrain tiles (moved from
// TerrainArtGenerator v1). The hexagon is inscribed in the square tile:
// circumradius = size/2, apothem = 0.866 * circumradius, centred.
public static class HexTile
{
    // Signed distance from the hex edge: >0 inside, 0 on an edge, <0 outside.
    public static float EdgeDistance(float x, float y, int size)
    {
        float c = size / 2f;
        return -Sdf.Hexagon(new Vector2(x, y), new Vector2(c, c), c);
    }

    public static bool InFootprint(float x, float y, int size, float margin)
        => EdgeDistance(x, y, size) >= margin;

    // Ambient-occlusion rim: darken a band just inside the hex edge so the tile
    // reads as a raised, sculpted piece. Pixels outside the hex are never shown
    // (the UV maps the square texture onto the hexagon), so they're skipped.
    public static void EdgeAo(Canvas c, float band, float darken)
    {
        int size = c.Width;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = EdgeDistance(x + 0.5f, y + 0.5f, size);
            if (d < 0f || d >= band) continue;
            float t = 1f - d / band;
            c.ScaleRgb(x, y, 1f - t * t * darken); // quadratic: gentle onset, firm edge
        }
    }
}
