using System;

namespace NWO.Map;

// Lookup tables for terrain-driven gameplay numbers. Lives here (not in WorldMap)
// so it can be reused by AI, tooltips, and tests without dragging in the scene.
// Vegetation (Forest/Jungle/Marsh/...) is a Feature, not a terrain — its deltas
// live in FeatureYields and stack on top of these base numbers.
public static class TerrainYields
{
    // Movement cost to enter a tile. int.MaxValue = impassable. Rough-terrain
    // surcharges come from FeatureYields.MovementCost (Hills/Forest/Jungle/Marsh).
    public static int MovementCost(TerrainType t) => t switch
    {
        TerrainType.Mountain => int.MaxValue,
        TerrainType.Ocean    => int.MaxValue,
        TerrainType.Coast    => int.MaxValue,
        TerrainType.Lake     => int.MaxValue, // and sea ships can't enter either (Civ5)
        _                    => 1,
    };

    public static int Food(TerrainType t) => t switch
    {
        TerrainType.Grassland => 2,
        TerrainType.Plains    => 1,
        TerrainType.Tundra    => 1,
        TerrainType.Coast     => 2,
        TerrainType.Ocean     => 1,
        TerrainType.Lake      => 2, // Civ5 lake tile: 2F / 1G
        TerrainType.Savanna   => 1,
        _                     => 0,
    };

    public static int Production(TerrainType t) => t switch
    {
        TerrainType.Plains  => 1,
        TerrainType.Desert  => 1,
        _                   => 0,
    };

    // Trade income from a worked tile. Water carries gold (Civ-5 trade),
    // land tiles none until improvements/buildings provide it.
    public static int Gold(TerrainType t) => t switch
    {
        TerrainType.Coast => 1,
        TerrainType.Ocean => 1,
        TerrainType.Lake  => 1,
        _                 => 0,
    };

    // All water, including inland lakes — founding/working/fertility rules.
    public static bool IsWater(TerrainType t)
        => t is TerrainType.Ocean or TerrainType.Coast or TerrainType.Lake;

    // Sea water only: what naval units sail on and what "coastal" means to the
    // AI's ship logic. A Lake is water but NOT sea — a lake-side city must never
    // count as coastal or its ships could never leave (Civ5 rule).
    public static bool IsSeaWater(TerrainType t)
        => t is TerrainType.Ocean or TerrainType.Coast;

    // Water is excluded explicitly (not just Ocean): settlers can't reach Coast
    // today, but key-site placement and future mechanics call this too.
    public static bool CanFoundCityOn(TerrainType t)
        => !IsWater(t) && t != TerrainType.Mountain;

    // City-center floor: Civ 5 rule — always at least 2 food / 1 production.
    public static (int Food, int Prod) CityCenter(TerrainType t)
        => (Math.Max(2, Food(t)), Math.Max(1, Production(t)));
}
