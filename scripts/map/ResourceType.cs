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
}
