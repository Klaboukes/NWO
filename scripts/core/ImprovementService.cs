using System.Collections.Generic;
using Godot;
using NWO.Entities;
using NWO.Map;

namespace NWO.Core;

// Rules for Worker tile improvements: build time, tech gate, valid terrain, and
// yield effects. Pure-logic over GameState so the Worker UI, AI, yield recompute
// (CityWorkforceService) and tests all agree. Road is movement-only (no yield);
// its cost effect lives in GameState.MovementCost.
public static class ImprovementService
{
    // All buildable improvement types, in display order.
    public static readonly ImprovementType[] All =
        { ImprovementType.Farm, ImprovementType.Mine, ImprovementType.Pasture, ImprovementType.Road };

    public static int BuildTurns(ImprovementType t) => t switch
    {
        ImprovementType.Farm    => 3,
        ImprovementType.Mine    => 3,
        ImprovementType.Pasture => 3,
        ImprovementType.Road    => 2,
        _                       => 0,
    };

    public static string? RequiredTech(ImprovementType t) => t switch
    {
        ImprovementType.Mine    => "mining",
        ImprovementType.Pasture => "animal_husbandry",
        _                       => null,
    };

    public static int Food(ImprovementType t) => t == ImprovementType.Farm ? 1 : 0;

    public static int Production(ImprovementType t)
        => t is ImprovementType.Mine or ImprovementType.Pasture ? 1 : 0;

    // Whether an improvement may sit on a tile, given its feature mask. Farm/Pasture
    // want clear flatland (no Hills, no vegetation — NWO has no tree-chopping); Mine
    // wants a bare Hills feature on any base terrain; Road follows any passable tile.
    public static bool ValidOn(ImprovementType t, TerrainType terrain, Feature features) => t switch
    {
        ImprovementType.Farm    => features == Feature.None && terrain is TerrainType.Grassland or TerrainType.Plains,
        ImprovementType.Mine    => features == Feature.Hills,
        ImprovementType.Pasture => features == Feature.None && terrain is TerrainType.Grassland or TerrainType.Plains,
        ImprovementType.Road    => TerrainYields.MovementCost(terrain) != int.MaxValue, // any passable land
        _                       => false,
    };

    // Can `player` build `type` on `tile` right now: passable land of a valid
    // terrain, required tech researched, and not already that improvement.
    public static bool CanBuild(GameState state, Player player, Vector2I tile, ImprovementType type)
    {
        if (type == ImprovementType.None)                       return false;
        if (!state.Map.Tiles.TryGetValue(tile, out var terrain)) return false;
        if (!ValidOn(type, terrain, state.Map.FeatureAt(tile))) return false;
        var req = RequiredTech(type);
        if (req != null && !state.Civ(player).ResearchedTechs.Contains(req)) return false;
        if (state.Map.ImprovementAt(tile) == type)              return false;
        return true;
    }

    // The improvements `player` may start on `tile`, paired with their build time.
    public static IEnumerable<(ImprovementType Type, int Turns)> BuildableOptions(
        GameState state, Player player, Vector2I tile)
    {
        foreach (var t in All)
            if (CanBuild(state, player, tile, t))
                yield return (t, BuildTurns(t));
    }
}
