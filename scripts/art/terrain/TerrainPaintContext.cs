using Godot;
using NWO.Art.Painterly;

namespace NWO.Art.Terrain;

// Everything a terrain painter needs for one tile: the canvas + painter, the
// deterministic RNG/seed pair (both derived from the (terrain, feature) identity
// only), and the terrain's painterly ramp. Painters mutate the canvas; the
// facade turns it into an Image once at the end.
public sealed class TerrainPaintContext
{
    public TerrainPaintContext(int size, ulong rngSeed, int noiseSeed, Color baseColor)
    {
        Size      = size;
        Canvas    = new Canvas(size);
        Painter   = new Painter(Canvas);
        Rng       = new Rng(rngSeed);
        Seed      = noiseSeed;
        BaseColor = baseColor;
        Ramp      = ColorRamp.Painterly(baseColor);
    }

    public int Size { get; }
    public int Seed { get; }
    public Canvas Canvas { get; }
    public Painter Painter { get; }
    public Rng Rng { get; }
    public Color BaseColor { get; }
    public ColorRamp Ramp { get; }

    // Roll a position spread across the WHOLE hex footprint. `clearance` is how
    // far (px) the point must sit inside the hex edge — large props stay off the
    // rim, small ones can hug it. Gives up after a few tries so a crowded tile
    // can't loop forever (same contract as v1's TryProp).
    public bool TryPlace(float clearance, out Vector2 pos)
    {
        for (int attempt = 0; attempt < 12; attempt++)
        {
            pos = new Vector2(Rng.Range(8f, Size - 8f), Rng.Range(8f, Size - 8f));
            if (HexTile.InFootprint(pos.X, pos.Y, Size, clearance)) return true;
        }
        pos = new Vector2(Size / 2f, Size / 2f);
        return false;
    }
}
