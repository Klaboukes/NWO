namespace NWO.Map;

// Tile improvements a Worker can build. Yields, build time, required tech and
// valid terrain all live in ImprovementService (not on the enum) so AI, UI,
// tooltips and tests share one rules source. Road affects movement, not yield.
public enum ImprovementType
{
    None,
    Farm,    // +1 food   (Grassland/Plains)
    Mine,    // +1 prod    (Hills, needs Mining)
    Pasture, // +1 prod    (Grassland/Plains, needs Animal Husbandry)
    Road,    // halves movement cost (any passable land)
}
