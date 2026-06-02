using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using NWO.Entities;
using NWO.Map;

namespace NWO.Core;

// Owns the auto-assign + yield-recompute logic for cities. Pure-logic; takes
// the GameState it reads from. Replaces GameState.ComputeCityYields, which
// only summed terrain in a static radius-1 ring.
public static class CityWorkforceService
{
    public const int WorkRadius = 2;

    // Recompute Assigned tiles + FoodYield/ProductionYield for one city.
    public static void Recompute(GameState state, City city)
    {
        var workable = Workable(state, city).ToList();
        var workableSet = new HashSet<Vector2I>(workable);

        // Drop locked tiles that are no longer workable.
        city.Workforce.Locked.RemoveWhere(t => !workableSet.Contains(t));

        var assigned = city.Workforce.Assigned;
        assigned.Clear();

        // Seed from locked tiles, capped at population.
        foreach (var locked in RankByFocus(state, city, city.Workforce.Locked))
        {
            if (assigned.Count >= city.Population) break;
            assigned.Add(locked);
        }

        // Fill remaining slots from best unassigned workable tiles.
        var remaining = workable.Where(t => !assigned.Contains(t));
        foreach (var tile in RankByFocus(state, city, remaining))
        {
            if (assigned.Count >= city.Population) break;
            assigned.Add(tile);
        }

        // Totals: city-center floor + worked tiles + building bonuses.
        var centerTerrain = state.Map.Tiles.GetValueOrDefault(city.Position, TerrainType.Grassland);
        var (food, prod) = TerrainYields.CityCenter(centerTerrain); // Civ 5 floor: ≥2F / ≥1P
        var centerFeat = state.Map.FeatureAt(city.Position);
        food += FeatureYields.Food(centerFeat);       // Hills: -1F / +1P on the centre
        prod += FeatureYields.Production(centerFeat);
        if (state.Map.IsRiverAdjacent(city.Position)) food += 1; // floodplain centre
        food = Math.Max(2, food); // re-apply the centre floor after deltas
        prod = Math.Max(1, prod);

        foreach (var tile in assigned)
        {
            if (!state.Map.Tiles.TryGetValue(tile, out var t)) continue;
            int tf = TerrainYields.Food(t);
            int tp = TerrainYields.Production(t);

            // Worker-built improvements on the tile.
            var imp = state.Map.ImprovementAt(tile);
            tf += ImprovementService.Food(imp);
            tp += ImprovementService.Production(imp);

            // A revealed resource on a worked tile adds its tier yield (bonus/
            // strategic add Food/Prod; luxuries add Gold via CivEconomyService).
            var res = state.Map.ResourceAt(tile);
            if (res != ResourceType.None && ResourceService.IsRevealed(state, city.Owner, res))
            {
                tf += ResourceYields.Food(res);
                tp += ResourceYields.Production(res);
            }

            // Hills feature trades food for production; floodplain adds food.
            var feat = state.Map.FeatureAt(tile);
            tf += FeatureYields.Food(feat);
            tp += FeatureYields.Production(feat);
            if (state.Map.IsRiverAdjacent(tile)) tf += 1;

            food += Math.Max(0, tf); // a tile's food can't go negative (Civ 5 clamp)
            prod += tp;
        }

        foreach (var buildingId in city.Buildings)
        {
            var bdef = state.Catalog.Building(buildingId);
            if (bdef == null) continue;
            food += bdef.Yields.Food;
            prod += bdef.Yields.Production;
        }

        city.FoodYield       = food;
        city.ProductionYield = prod;
    }

    // Tiles in the city's work radius (excluding center) that this city may
    // currently work: passable land, controlled by this city, not enemy-occupied.
    public static IEnumerable<Vector2I> Workable(GameState state, City city)
    {
        foreach (var tile in HexGrid.GetRange(city.Position, WorkRadius))
        {
            if (tile == city.Position)                                  continue;
            if (!state.Map.Tiles.TryGetValue(tile, out var terrain))    continue;
            if (terrain == TerrainType.Mountain)                        continue;
            if (terrain == TerrainType.Ocean)                           continue;
            if (ControllingCity(state, tile) != city)                   continue;
            if (state.Units.Any(u => u.Position == tile && u.Owner != city.Owner)) continue;
            yield return tile;
        }
    }

    // Nearest city center within WorkRadius owns the tile. Ties resolved by
    // city order in GameState.Cities (earlier-founded wins) for deterministic
    // saves/replays.
    public static City? ControllingCity(GameState state, Vector2I tile)
    {
        City? best     = null;
        int   bestDist = int.MaxValue;
        foreach (var c in state.Cities)
        {
            int d = HexGrid.Distance(c.Position, tile);
            if (d > WorkRadius) continue;
            if (d < bestDist)
            {
                best     = c;
                bestDist = d;
            }
        }
        return best;
    }

    public static int Score(CityFocus focus, int food, int prod) => focus switch
    {
        CityFocus.Food       => 5 * food + prod,
        CityFocus.Production => food + 5 * prod,
        _                    => 2 * food + prod, // Balanced
    };

    private static IEnumerable<Vector2I> RankByFocus(
        GameState state, City city, IEnumerable<Vector2I> tiles)
    {
        return tiles
            .Select(t =>
            {
                var terrain = state.Map.Tiles.GetValueOrDefault(t, TerrainType.Grassland);
                var feat    = state.Map.FeatureAt(t);
                int food    = TerrainYields.Food(terrain) + FeatureYields.Food(feat);
                int prod    = TerrainYields.Production(terrain) + FeatureYields.Production(feat);
                if (state.Map.IsRiverAdjacent(t)) food += 1; // floodplain bonus
                food        = Math.Max(0, food);
                return (tile: t,
                        score: Score(city.Workforce.Focus, food, prod),
                        dist:  HexGrid.Distance(city.Position, t));
            })
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.dist)
            .ThenBy(x => x.tile.X)
            .ThenBy(x => x.tile.Y)
            .Select(x => x.tile);
    }
}
