namespace NWO.Map;

// The three Civ5-style resource tiers (Phase 9).
//   Bonus     — always visible, dense, +1 Food or +1 Prod on the worked tile.
//   Strategic — tech-revealed, sparse, +1 Prod and gate unit production.
//   Luxury    — tech-revealed, very sparse, +1 Gold; each unique type controlled
//               feeds a future amenity/happiness system.
public enum ResourceTier
{
    Bonus,
    Strategic,
    Luxury,
}

// Per-resource yield + tier lookup. Lives beside TerrainYields/ImprovementService
// so AI, tooltips, economy, and tests share one source of truth. Yields apply only
// when the tile is worked and the resource is revealed (see CityWorkforceService /
// CivEconomyService).
public static class ResourceYields
{
    public static ResourceTier Tier(ResourceType r) => r switch
    {
        ResourceType.Horses or ResourceType.Iron => ResourceTier.Strategic,
        ResourceType.Gems or ResourceType.GoldOre or ResourceType.Silver
            or ResourceType.Silk or ResourceType.Spices or ResourceType.Dyes
            or ResourceType.Cotton or ResourceType.Incense or ResourceType.Ivory => ResourceTier.Luxury,
        _                                         => ResourceTier.Bonus,
    };

    public static int Food(ResourceType r) => r switch
    {
        ResourceType.Wheat  => 1,
        ResourceType.Fish   => 1,
        ResourceType.Cattle => 1,
        ResourceType.Deer   => 1,
        ResourceType.Banana => 1,
        _                   => 0,
    };

    public static int Production(ResourceType r) => r switch
    {
        ResourceType.Sheep  => 1,
        ResourceType.Stone  => 1,
        ResourceType.Horses => 1,
        ResourceType.Iron   => 1,
        _                   => 0,
    };

    public static int Gold(ResourceType r) => Tier(r) == ResourceTier.Luxury ? 1 : 0;
}
