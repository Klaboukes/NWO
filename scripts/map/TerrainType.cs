namespace NWO.Map;

// The terrain kinds a tile can be. Gameplay numbers (yields, movement cost) are
// looked up in TerrainYields, not stored on the enum.
public enum TerrainType
{
    Grassland,
    Plains,
    Desert,
    Tundra,
    Snow,
    Hills,
    Forest,
    Mountain,
    Ocean,
    Coast,
    // Phase 9 biomes (appended — never reorder; TerrainType is serialized in saves).
    Savanna,
    Jungle,
    Wetlands,
}
