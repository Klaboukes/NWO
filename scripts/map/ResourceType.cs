namespace NWO.Map;

// Strategic resources scattered on the map by MapGenerator. A resource is only
// usable by a civ once it has researched the revealing tech (see techs.json
// "revealedResources") and controls the tile via one of its cities — see
// ResourceService. Gates the units in units.json with a matching RequiredResource.
public enum ResourceType
{
    None,
    Horses, // Plains / Grassland — gates Horseman (reveal: Animal Husbandry)
    Iron,   // Hills              — gates Swordsman (reveal: Bronze Working)

    // Bonus resources (Phase 9.2) — always visible, +1 Food or +1 Prod when worked.
    // Appended (never reorder); ResourceType is serialized in saves.
    Wheat,  // Plains / Grassland
    Fish,   // Coast / Ocean
    Cattle, // Grassland
    Sheep,  // Hills / Grassland
    Deer,   // Forest / Tundra
    Stone,  // Hills / Plains
    Banana, // Jungle

    // Luxury resources (Phase 9.3) — tech-revealed, very sparse, +1 Gold when worked.
    // Appended (never reorder); ResourceType is serialized in saves.
    Gems,    // Hills / Mountains (reveal: Mining)
    GoldOre, // Hills / Mountains (reveal: Mining)
    Silver,  // Hills            (reveal: Mining)
    Silk,    // Forest           (reveal: Calendar)
    Spices,  // Jungle / Forest  (reveal: Calendar)
    Dyes,    // Forest / Jungle  (reveal: Calendar)
    Cotton,  // Plains / Grassland (reveal: Calendar)
    Incense, // Desert / Plains  (reveal: Calendar)
    Ivory,   // Plains / Grassland (reveal: Animal Husbandry)
}
