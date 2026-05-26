namespace NWO.Map;

// Lookup tables for terrain-driven gameplay numbers. Lives here (not in WorldMap)
// so it can be reused by AI, tooltips, and tests without dragging in the scene.
public static class TerrainYields
{
    // Movement cost to enter a tile. int.MaxValue = impassable.
    public static int MovementCost(TerrainType t) => t switch
    {
        TerrainType.Mountain => int.MaxValue,
        TerrainType.Ocean    => int.MaxValue,
        TerrainType.Coast    => int.MaxValue,
        TerrainType.Hills    => 2,
        TerrainType.Forest   => 2,
        _                    => 1,
    };

    public static int Food(TerrainType t) => t switch
    {
        TerrainType.Grassland => 2,
        TerrainType.Plains    => 1,
        TerrainType.Forest    => 1,
        TerrainType.Hills     => 1,
        TerrainType.Tundra    => 1,
        TerrainType.Coast     => 2,
        TerrainType.Ocean     => 1,
        _                     => 0,
    };

    public static int Production(TerrainType t) => t switch
    {
        TerrainType.Hills   => 2,
        TerrainType.Forest  => 2,
        TerrainType.Plains  => 1,
        TerrainType.Desert  => 1,
        _                   => 0,
    };

    public static bool CanFoundCityOn(TerrainType t)
        => t != TerrainType.Ocean && t != TerrainType.Mountain;
}
