using Godot;

namespace NWO.Map;

// Pure axial-hex ↔ 3D-world projection (no Godot nodes, so it is unit-testable).
//
// The world is true 3D now (Phase 7 V7.1): the ground is the X/Z plane and +Y is
// up. The "Civ5 tilt" comes from a fixed-angle Camera3D, NOT from squashing the
// projection — so there is no more VerticalScale. Elevation is real geometry: a
// tile's top face sits at TopHeight(terrain) above the ground plane.
//
// Picking inverts only the ground plane (X/Z): a screen ray is intersected with
// Y = 0 and the hit point is fed to WorldToAxial. A tile's clickable footprint
// therefore stays on the ground regardless of how tall its prism is — the same
// contract the old baked-2.5D renderer had.
public static class HexProjection
{
    public const float HexSize = 32f;

    // Vertical thickness of a flat (sea-level) tile prism, so even flat terrain
    // shows a small cliff edge. Raised terrain adds Elevation on top.
    public const float BaseThickness = HexSize * 0.25f;

    // Flat-top axial hex on the ground plane. Y is always 0 here; callers add
    // TopHeight when they want the tile's top face.
    public static Vector3 AxialToWorld(Vector2I axial)
    {
        float x = HexSize * 1.5f           * axial.X;
        float z = HexSize * Mathf.Sqrt(3f) * (axial.Y + axial.X * 0.5f);
        return new Vector3(x, 0f, z);
    }

    // Invert a ground-plane point (uses X and Z; Y is ignored — picking happens
    // on the Y = 0 plane). Mirror of AxialToWorld with Z standing in for the old
    // 2D Y axis.
    public static Vector2I WorldToAxial(Vector3 world)
    {
        float qf = (2f / 3f * world.X) / HexSize;
        float rf = (-1f / 3f * world.X + Mathf.Sqrt(3f) / 3f * world.Z) / HexSize;
        return CubeRound(qf, rf);
    }

    // Real prism height above the ground plane (the top-face Y). Monotonic:
    // flat < canopy < Hills < Mountain — the same ordering the old draw-only
    // ElevationLift used, now as actual geometry. Features stack: a Hills lift
    // and a Forest/Jungle canopy lift both add on top of the base terrain, so a
    // forested hill is the tallest non-mountain tile (Phase 14).
    public const float HillLift   = HexSize * 0.22f;
    public const float CanopyLift = HexSize * 0.12f; // dense Forest/Jungle canopy

    public static float Elevation(TerrainType terrain) => terrain switch
    {
        TerrainType.Mountain => HexSize * 0.55f,
        _                    => 0f, // flat: vegetation lift comes from the feature mask
    };

    // Elevation including the tile's feature mask (lifts stack on the base terrain).
    public static float Elevation(TerrainType terrain, Feature features)
        => Elevation(terrain)
         + ((features & Feature.Hills) != 0 ? HillLift : 0f)
         + ((features & (Feature.Forest | Feature.Jungle)) != 0 ? CanopyLift : 0f);

    // Y of a tile's top face (where sprites, glyphs, and overlays sit).
    public static float TopHeight(TerrainType terrain) => BaseThickness + Elevation(terrain);

    public static float TopHeight(TerrainType terrain, Feature features)
        => BaseThickness + Elevation(terrain, features);

    // The six flat-top hex corners on the ground plane, relative to a tile centre.
    // i in [0,6); use with AxialToWorld(...) + corner, raising Y to the top face.
    public static Vector3 Corner(int i, float size)
    {
        float a = Mathf.DegToRad(60f * i);
        return new Vector3(Mathf.Cos(a) * size, 0f, Mathf.Sin(a) * size);
    }

    private static Vector2I CubeRound(float q, float r)
    {
        float s  = -q - r;
        int   rq = Mathf.RoundToInt(q);
        int   rr = Mathf.RoundToInt(r);
        int   rs = Mathf.RoundToInt(s);
        float dq = Mathf.Abs(rq - q);
        float dr = Mathf.Abs(rr - r);
        float ds = Mathf.Abs(rs - s);
        if      (dq > dr && dq > ds) rq = -rr - rs;
        else if (dr > ds)            rr = -rq - rs;
        return new Vector2I(rq, rr);
    }

    public static Color TerrainColor(TerrainType terrain) => terrain switch
    {
        TerrainType.Ocean     => new Color(0.18f, 0.35f, 0.65f),
        TerrainType.Coast     => new Color(0.33f, 0.55f, 0.80f),
        TerrainType.Lake      => new Color(0.28f, 0.52f, 0.72f), // calm inland water
        TerrainType.Desert    => new Color(0.87f, 0.80f, 0.55f),
        TerrainType.Plains    => new Color(0.80f, 0.78f, 0.50f),
        TerrainType.Grassland => new Color(0.38f, 0.68f, 0.32f),
        TerrainType.Tundra    => new Color(0.70f, 0.75f, 0.68f),
        TerrainType.Snow      => new Color(0.92f, 0.95f, 0.98f),
        TerrainType.Mountain  => new Color(0.55f, 0.50f, 0.48f),
        TerrainType.Savanna   => new Color(0.72f, 0.70f, 0.34f), // dry golden-green
        _                     => Colors.Magenta,
    };

    // Base colour a vegetation/overlay feature paints over its terrain (drives the
    // composite art ramp + minimap tinting). Forest/Jungle keep the pre-split
    // terrain greens so the look carries over.
    public static Color FeatureColor(Feature veg) => veg switch
    {
        Feature.Forest => new Color(0.18f, 0.45f, 0.20f),
        Feature.Jungle => new Color(0.16f, 0.42f, 0.18f), // rich dark green
        Feature.Marsh  => new Color(0.34f, 0.50f, 0.38f), // muted marsh
        Feature.Oasis  => new Color(0.30f, 0.62f, 0.45f), // lush pocket in the sand
        Feature.Ice    => new Color(0.88f, 0.93f, 0.97f),
        _              => Colors.Magenta,
    };
}
