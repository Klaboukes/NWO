using System;

namespace NWO.Map;

// Terrain features overlay a base TerrainType (Civ5 model, Phase 14). A tile carries
// a flag mask so combinations exist (Grassland + Forest + Hills = a forested hill),
// and can still hold a resource (Grassland + Hills + Sheep). Which combinations are
// legal on which base terrain is defined in FeatureRules. Stored sparsely in
// MapData.Features; serialized by name ("Forest, Hills") in saves.
[Flags]
public enum Feature
{
    None   = 0,
    Hills  = 1,
    Forest = 1 << 1,
    Jungle = 1 << 2,
    Marsh  = 1 << 3,
    Oasis  = 1 << 4,
    Ice    = 1 << 5,
}

// Yield deltas a feature mask applies on top of its base terrain (and improvements/
// resources). Flag-additive: each set flag contributes its delta. Lives beside
// TerrainYields / ImprovementService so every yield site agrees. Callers clamp the
// tile's food at 0 so features can't push a worked tile negative.
//
// Calibration (see docs/MECHANICS.md): Grassland+Forest = 1F/2P (the pre-split
// Forest terrain exactly), Grassland+Jungle = 1F (pre-split Jungle), Desert+Oasis
// = 3F/1P/1G (Civ5's oasis), Forest+Hills on Grassland = 0F/3P lumber-hill.
public static class FeatureYields
{
    public static int Food(Feature f)
        => Sum(f, Feature.Hills, -1) + Sum(f, Feature.Forest, -1) + Sum(f, Feature.Jungle, -1)
         + Sum(f, Feature.Marsh, -1) + Sum(f, Feature.Oasis, +3);

    public static int Production(Feature f)
        => Sum(f, Feature.Hills, +1) + Sum(f, Feature.Forest, +2);

    public static int Gold(Feature f) => Sum(f, Feature.Oasis, +1);

    // Extra movement cost to enter a tile carrying the feature mask. Rough features
    // (Hills, Forest, Jungle, Marsh) each add 1 and stack (Forest+Hills = 3 total).
    public static int MovementCost(Feature f)
        => Sum(f, Feature.Hills, 1) + Sum(f, Feature.Forest, 1)
         + Sum(f, Feature.Jungle, 1) + Sum(f, Feature.Marsh, 1);

    private static int Sum(Feature mask, Feature flag, int delta)
        => (mask & flag) != 0 ? delta : 0;
}
