namespace NWO.Map;

// Terrain features overlay a base TerrainType (Phase 9.x). Hills is the first:
// a tile can be Grassland + Hills, Savanna + Hills, etc., and still carry a resource
// (Grassland + Hills + Sheep). Stored sparsely in MapData.Features.
public enum Feature
{
    None,
    Hills,
}

// Yield deltas a feature applies on top of its base terrain (and improvements/
// resources). Lives beside TerrainYields / ImprovementService so every yield site
// agrees. Hills trade food for production (Civ 5 convention); callers clamp the
// tile's food at 0 so a feature can't push a worked tile negative.
public static class FeatureYields
{
    public static int Food(Feature f)       => f == Feature.Hills ? -1 : 0;
    public static int Production(Feature f)  => f == Feature.Hills ? +1 : 0;

    // Extra movement cost to enter a tile carrying the feature.
    public static int MovementCost(Feature f) => f == Feature.Hills ? 1 : 0;
}
