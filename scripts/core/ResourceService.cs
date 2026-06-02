using NWO.Entities;
using NWO.Map;

namespace NWO.Core;

// Strategic-resource queries: id mapping, per-civ reveal (do you have the tech?),
// and access (do you control a tile that has it?). Pure-logic over GameState so
// the build-list filter, AI, and tests share one source of truth. Mirrors the
// shape of CityWorkforceService / CivEconomyService.
public static class ResourceService
{
    public static string ToId(ResourceType r) => r switch
    {
        ResourceType.Horses => "horses",
        ResourceType.Iron   => "iron",
        ResourceType.Wheat  => "wheat",
        ResourceType.Fish   => "fish",
        ResourceType.Cattle => "cattle",
        ResourceType.Sheep  => "sheep",
        ResourceType.Deer   => "deer",
        ResourceType.Stone  => "stone",
        ResourceType.Banana => "banana",
        _                   => "",
    };

    public static ResourceType FromId(string? id) => id switch
    {
        "horses" => ResourceType.Horses,
        "iron"   => ResourceType.Iron,
        "wheat"  => ResourceType.Wheat,
        "fish"   => ResourceType.Fish,
        "cattle" => ResourceType.Cattle,
        "sheep"  => ResourceType.Sheep,
        "deer"   => ResourceType.Deer,
        "stone"  => ResourceType.Stone,
        "banana" => ResourceType.Banana,
        _        => ResourceType.None,
    };

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
}
