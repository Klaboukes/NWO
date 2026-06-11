namespace NWO.Map;

// The legality matrix for feature masks on base terrain (Civ5 model, Phase 14).
// One source of truth shared by the generator (FeaturePlacer), the histogram
// diagnostic's violation check, the art bake matrix, and tests. Pure static —
// no Godot dependency.
//
//   Feature | Legal base terrain          | Combines with | Extra placement rule
//   ------- | --------------------------- | ------------- | ---------------------
//   Hills   | any land except Mountain    | Forest,Jungle | (generator-driven)
//   Forest  | Grassland, Plains, Tundra   | Hills         | —
//   Jungle  | Grassland, Plains           | Hills         | equatorial band only
//   Marsh   | Grassland                   | nothing       | flat only
//   Oasis   | Desert                      | nothing       | flat, away from water
//   Ice     | Ocean, Coast                | nothing       | polar water, never Lake
//
// Placement-context rules (equatorial band, water adjacency) live in the placer;
// this class validates only the (terrain, mask) shape.
public static class FeatureRules
{
    // All single-feature flags, in a stable display/bake order.
    public static readonly Feature[] Flags =
        { Feature.Hills, Feature.Forest, Feature.Jungle, Feature.Marsh, Feature.Oasis, Feature.Ice };

    // Vegetation/overlay flags that change the tile's top-face art (Hills is
    // geometry — a taller prism — not a texture; see TerrainMeshFactory).
    public const Feature VegMask = Feature.Forest | Feature.Jungle | Feature.Marsh
                                 | Feature.Oasis | Feature.Ice;

    public static bool IsLegal(TerrainType terrain, Feature mask)
    {
        if (mask == Feature.None) return true;

        bool hills  = (mask & Feature.Hills)  != 0;
        bool forest = (mask & Feature.Forest) != 0;
        bool jungle = (mask & Feature.Jungle) != 0;
        bool marsh  = (mask & Feature.Marsh)  != 0;
        bool oasis  = (mask & Feature.Oasis)  != 0;
        bool ice    = (mask & Feature.Ice)    != 0;

        // At most one vegetation/overlay flag (Hills is the only stacking feature).
        int vegCount = (forest ? 1 : 0) + (jungle ? 1 : 0) + (marsh ? 1 : 0)
                     + (oasis ? 1 : 0) + (ice ? 1 : 0);
        if (vegCount > 1) return false;

        if (ice)   return !hills && terrain is TerrainType.Ocean or TerrainType.Coast;
        if (IsWaterBase(terrain) || terrain == TerrainType.Mountain) return false;

        if (marsh) return !hills && terrain == TerrainType.Grassland;
        if (oasis) return !hills && terrain == TerrainType.Desert;
        if (forest) return terrain is TerrainType.Grassland or TerrainType.Plains or TerrainType.Tundra;
        if (jungle) return terrain is TerrainType.Grassland or TerrainType.Plains;

        return true; // bare Hills on any land
    }

    // Every legal (terrain, veg) texture combination — drives the art bake matrix
    // and the texture registry's expectations. Hills is excluded (geometry only).
    public static System.Collections.Generic.IEnumerable<(TerrainType Terrain, Feature Veg)> TextureCombos()
    {
        foreach (TerrainType t in System.Enum.GetValues<TerrainType>())
        {
            yield return (t, Feature.None);
            foreach (var f in Flags)
                if (f != Feature.Hills && IsLegal(t, f))
                    yield return (t, f);
        }
    }

    private static bool IsWaterBase(TerrainType t)
        => t is TerrainType.Ocean or TerrainType.Coast or TerrainType.Lake;
}
