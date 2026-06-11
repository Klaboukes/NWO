using System.Collections.Generic;
using NWO.Entities;
using NWO.Map;

namespace NWO.Core;

// Strategic-resource queries: id mapping, per-civ reveal (do you have the tech?),
// and access (do you control a tile that has it?). Pure-logic over GameState so
// the build-list filter, AI, and tests share one source of truth. Mirrors the
// shape of CityWorkforceService / CivEconomyService.
public static class ResourceService
{
    // Single forward table; the reverse map is derived from it so the two
    // directions can never drift apart (new resources are added in one place).
    private static readonly Dictionary<ResourceType, string> Ids = new()
    {
        [ResourceType.Horses]  = "horses",
        [ResourceType.Iron]    = "iron",
        [ResourceType.Wheat]   = "wheat",
        [ResourceType.Fish]    = "fish",
        [ResourceType.Cattle]  = "cattle",
        [ResourceType.Sheep]   = "sheep",
        [ResourceType.Deer]    = "deer",
        [ResourceType.Stone]   = "stone",
        [ResourceType.Banana]  = "banana",
        [ResourceType.Gems]    = "gems",
        [ResourceType.GoldOre] = "gold_ore",
        [ResourceType.Silver]  = "silver",
        [ResourceType.Silk]    = "silk",
        [ResourceType.Spices]  = "spices",
        [ResourceType.Dyes]    = "dyes",
        [ResourceType.Cotton]  = "cotton",
        [ResourceType.Incense] = "incense",
        [ResourceType.Ivory]   = "ivory",
    };

    private static readonly Dictionary<string, ResourceType> ByIdLookup = BuildReverse();

    private static Dictionary<string, ResourceType> BuildReverse()
    {
        var reverse = new Dictionary<string, ResourceType>(Ids.Count);
        foreach (var (type, id) in Ids) reverse[id] = type;
        return reverse;
    }

    public static string ToId(ResourceType r) => Ids.GetValueOrDefault(r, "");

    public static ResourceType FromId(string? id)
        => id != null && ByIdLookup.TryGetValue(id, out var r) ? r : ResourceType.None;

    // True once the civ has researched the tech that reveals this resource. A
    // resource no tech gates is always revealed; None is trivially revealed.
    public static bool IsRevealed(GameState state, Player player, ResourceType r)
    {
        if (r == ResourceType.None) return true;
        string id = ToId(r);
        foreach (var tech in state.Catalog.Techs)
            if (tech.Unlocks.RevealedResources.Contains(id))
                return state.Civ(player).ResearchedTechs.Contains(tech.Id);
        return true; // not gated by any tech
    }

    // True if the civ has revealed the resource AND controls a tile bearing it
    // (the resource sits within one of the civ's cities' work radius). This is
    // what gates building a unit with a matching RequiredResource.
    public static bool HasAccess(GameState state, Player player, ResourceType r)
    {
        if (r == ResourceType.None)           return false;
        if (!IsRevealed(state, player, r))     return false;
        foreach (var (tile, res) in state.Map.Resources)
        {
            if (res != r) continue;
            if (CityWorkforceService.ControllingCity(state, tile)?.Owner == player)
                return true;
        }
        return false;
    }

    // Build-list gate: a unit with no resource requirement is always allowed;
    // otherwise the civ must have access to the named resource.
    public static bool Allows(GameState state, Player player, string? requiredResource)
        => string.IsNullOrEmpty(requiredResource)
        || HasAccess(state, player, FromId(requiredResource));

    // The distinct luxury resource types a civ currently controls (revealed AND a
    // city works/controls the tile). Scaffold for a future amenity/happiness system
    // (Phase 10): each unique luxury contributes once regardless of how many copies
    // are held. Count via .Count.
    public static HashSet<ResourceType> ControlledUniqueLuxuries(GameState state, Player player)
    {
        var held = new HashSet<ResourceType>();
        foreach (var (tile, res) in state.Map.Resources)
        {
            if (ResourceYields.Tier(res) != ResourceTier.Luxury) continue;
            if (held.Contains(res)) continue;
            if (!IsRevealed(state, player, res)) continue;
            if (CityWorkforceService.ControllingCity(state, tile)?.Owner == player)
                held.Add(res);
        }
        return held;
    }
}
