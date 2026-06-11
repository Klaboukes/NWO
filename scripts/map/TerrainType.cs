namespace NWO.Map;

// The BASE terrain kinds a tile can be (Civ5 model, Phase 14): vegetation and other
// overlays (Forest, Jungle, Marsh, Oasis, Ice, Hills) are Features layered on top —
// see Feature.cs / FeatureRules.cs. Gameplay numbers (yields, movement cost) are
// looked up in TerrainYields, not stored on the enum. Serialized by name in saves.
public enum TerrainType
{
    Grassland,
    Plains,
    Desert,
    Tundra,
    Snow,
    Mountain,
    Ocean,
    Coast,
    Savanna,
    Lake,      // inland water (Phase 14): workable fresh water, impassable to ships
}
