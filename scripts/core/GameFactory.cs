using System.Collections.Generic;
using System.Linq;
using Godot;
using NWO.Entities;
using NWO.Map;

namespace NWO.Core;

// One player's slot in a new-match roster: which faction, and whether a human
// controls it. The setup screen builds a list of these; GameFactory spawns one
// player per entry. A null FactionId is a neutral/untyped slot.
public record FactionChoice(string? FactionId, bool IsHuman);

// Builds a fresh GameState for a new match: generates the map, adds the human +
// AI players, and places their starting units (the AI is confined to the
// player's landmass for MVP — see ROADMAP Post-MVP). Extracted from
// WorldMap._Ready so new-game and load-game share one bootstrap path: the load
// path skips this entirely and hands WorldMap a deserialized GameState via
// GameLaunch, while a new game runs NewGame here.
public static class GameFactory
{
    public const int MapWidth  = 60;  // Standard size (default)
    public const int MapHeight = 40;

    public static (int W, int H) MapDimensions(MapSize size) => size switch
    {
        MapSize.Small => (44, 28),
        MapSize.Large => (80, 52),
        _             => (MapWidth, MapHeight),
    };

    // Owner tints, assigned by player index. Up to 8 players (FACTIONS.md roster cap).
    private static readonly Color[] PlayerColors =
    {
        Colors.Blue, Colors.Red, Colors.Green, Colors.Gold,
        Colors.MediumPurple, Colors.Orange, Colors.Cyan, Colors.HotPink,
    };

    public record NewGameResult(GameState State, Player Viewer);

    // The fallback when no setup roster is supplied (quick-start / legacy launch):
    // today's human-vs-Reavers match.
    public static IReadOnlyList<FactionChoice> DefaultRoster() => new[]
    {
        new FactionChoice(null, true),
        new FactionChoice("reavers", false),
    };

    public static NewGameResult NewGame(int seed,
        IReadOnlyList<FactionChoice>? roster = null,
        MapScript script = MapScript.Continents,
        MapSize size = MapSize.Standard)
    {
        var (w, h) = MapDimensions(size);
        var map     = MapGenerator.Generate(w, h, seed, script);
        var catalog = DataCatalog.Load();
        var state   = new GameState(map, catalog, seed);
        var viewer  = Populate(state, roster);
        return new NewGameResult(state, viewer);
    }

    // Spawns one player per roster entry into an already-built state and returns the
    // viewer (the human). Separated from map generation so it's unit-testable headless
    // (MapGenerator needs the Godot noise engine; this doesn't). Human(s) are placed
    // first so the viewer lands at the map centre; the rest are spread by
    // farthest-point sampling, confined to the largest landmass for MVP.
    public static Player Populate(GameState state, IReadOnlyList<FactionChoice>? roster)
    {
        var catalog    = state.Catalog;
        var choices    = (roster == null || roster.Count == 0) ? DefaultRoster() : roster;
        var scoutDef   = catalog.Unit("scout")!;
        var settlerDef = catalog.Unit("settler")!;

        var ordered  = choices.OrderByDescending(c => c.IsHuman).ToList();
        // Use the largest connected landmass so all factions share the main continent.
        // The map-centre anchor is then the tile in that landmass closest to grid centre
        // (avoids crashing onto a tiny island when noise/percentile calibration fragments
        // the map and the geometric centre happens to fall on a small pocket).
        var mapCenter = MapCenterAxial(state.Map.Width, state.Map.Height);
        var landmass  = FindLargestLandmass(state);
        var center    = landmass.Count > 0
            ? ClosestInSet(landmass, mapCenter)
            : state.FindWalkableTileNear(mapCenter);
        // Normalize the viewer's start: keep it central, but slide to the most fertile
        // tile within a short radius so the human never opens on a barren pocket.
        center       = MostFertileNear(state, center, NormalizeRadius, landmass);
        var placed   = new List<Vector2I>();
        Player? viewer = null;

        for (int i = 0; i < ordered.Count; i++)
        {
            var choice = ordered[i];
            var start  = placed.Count == 0 ? center : PickSpawn(state, placed, landmass);
            placed.Add(start);

            var player = state.AddPlayer(MakePlayer(i, choice, catalog));
            if (choice.IsHuman) viewer ??= player;

            state.Units.Add(new Unit(scoutDef, player, start));
            state.Units.Add(new Unit(settlerDef, player, FindNeighborOnLandmass(start, landmass) ?? start));
        }

        PlaceKeySites(state, placed, landmass);
        return viewer ?? state.Players[0];
    }

    private const int KeySiteCount = 3;

    // Drops the objective sites onto contested ground: foundable tiles spread by
    // farthest-point sampling from the player spawns (and each other), so they sit
    // between players rather than in anyone's lap.
    private static void PlaceKeySites(GameState state, List<Vector2I> spawns, HashSet<Vector2I> landmass)
    {
        state.Map.KeySites.Clear();
        var anchors = new List<Vector2I>(spawns);
        for (int i = 0; i < KeySiteCount; i++)
        {
            Vector2I best    = anchors.Count > 0 ? anchors[0] : Vector2I.Zero;
            int      bestMin = -1;
            foreach (var tile in landmass)
            {
                if (!TerrainYields.CanFoundCityOn(state.Map.Tiles.GetValueOrDefault(tile, TerrainType.Ocean)))
                    continue;
                int minD = MinDistance(anchors, tile);
                if (minD > bestMin) { bestMin = minD; best = tile; }
            }
            if (bestMin < 0) break; // no foundable land left
            state.Map.KeySites.Add(best);
            anchors.Add(best);
        }
    }

    private static Player MakePlayer(int index, FactionChoice choice, DataCatalog catalog)
    {
        var faction = choice.FactionId != null ? catalog.Faction(choice.FactionId) : null;
        string name = faction?.Name ?? (choice.IsHuman ? "Player" : "Independents");
        return new Player
        {
            Id        = index,
            Name      = name,
            IsHuman   = choice.IsHuman,
            Color     = PlayerColors[index % PlayerColors.Length],
            FactionId = choice.FactionId,
        };
    }

    public static Vector2I MapCenterAxial(int width = MapWidth, int height = MapHeight)
    {
        int col = width  / 2;
        int row = height / 2;
        return new Vector2I(col, row - (col - (col & 1)) / 2);
    }

    // Start-normalization tuning (Civ-5 "fertility floor" + impact-and-ripple).
    private const int NormalizeRadius = 3;  // how far the viewer start may slide toward fertility
    private const int FertilityFloor  = 14; // min work-radius yield sum for a viable start

    // Picks the next spawn on `landmass` by impact-and-ripple: farthest-point sampling
    // (the tile whose nearest already-placed start is as far as possible) restricted to
    // tiles that clear the fertility floor. If nothing clears it (barren/small island),
    // falls back to pure spacing so a spawn is always returned.
    private static Vector2I PickSpawn(GameState state, List<Vector2I> placed, HashSet<Vector2I> landmass)
    {
        if (landmass.Count == 0) return placed.Count > 0 ? placed[0] : Vector2I.Zero;

        Vector2I best     = placed[0];
        int      bestMin  = -1;
        double   bestFert = -1;
        foreach (var tile in landmass)
        {
            if (Fertility(state, tile) < FertilityFloor) continue;
            int minD = MinDistance(placed, tile);
            double fert = Fertility(state, tile);
            // Maximize spacing; break ties toward the more fertile site.
            if (minD > bestMin || (minD == bestMin && fert > bestFert))
            {
                bestMin = minD; bestFert = fert; best = tile;
            }
        }
        if (bestMin >= 0) return best;

        // Nothing met the floor — spread purely by distance.
        foreach (var tile in landmass)
        {
            int minD = MinDistance(placed, tile);
            if (minD > bestMin) { bestMin = minD; best = tile; }
        }
        return best;
    }

    private static int MinDistance(List<Vector2I> placed, Vector2I tile)
    {
        int min = int.MaxValue;
        foreach (var p in placed) min = System.Math.Min(min, HexGrid.Distance(p, tile));
        return min;
    }

    // Sum of food+production over a candidate's work radius — the Civ-5 fertility
    // proxy (mirrors AIController.SiteScore). Ignores ownership/units (start of game).
    private static double Fertility(GameState state, Vector2I center)
    {
        double sum = 0;
        foreach (var tile in HexGrid.GetRange(center, CityWorkforceService.WorkRadius))
        {
            if (!state.Map.Tiles.TryGetValue(tile, out var terrain)) continue;
            sum += TerrainYields.Food(terrain) + TerrainYields.Production(terrain);
            var feat = state.Map.FeatureAt(tile);
            sum += FeatureYields.Food(feat) + FeatureYields.Production(feat);
        }
        return sum;
    }

    // The most fertile walkable tile within `radius` of `origin` (same landmass).
    // Used to nudge the viewer's central start onto viable ground.
    private static Vector2I MostFertileNear(GameState state, Vector2I origin, int radius, HashSet<Vector2I> landmass)
    {
        Vector2I best = origin;
        double   bestFert = Fertility(state, origin);
        foreach (var tile in HexGrid.GetRange(origin, radius))
        {
            if (!landmass.Contains(tile)) continue;
            double fert = Fertility(state, tile);
            if (fert > bestFert) { bestFert = fert; best = tile; }
        }
        return best;
    }

    // Walkable neighbour of `tile` that's on the same landmass, used to place
    // the AI settler one hex from the warrior without leaving the continent.
    private static Vector2I? FindNeighborOnLandmass(Vector2I tile, HashSet<Vector2I> landmass)
    {
        foreach (var n in HexGrid.GetNeighbors(tile))
            if (landmass.Contains(n)) return n;
        return null;
    }

    // Returns the largest connected passable region on the map. Visits every tile
    // once via flood-fill so the cost is O(tiles). On a fragmented Archipelago map
    // this is the biggest island; on a Continents map it's the main continent.
    private static HashSet<Vector2I> FindLargestLandmass(GameState state)
    {
        var visited = new HashSet<Vector2I>();
        var largest = new HashSet<Vector2I>();
        foreach (var tile in state.Map.Tiles.Keys)
        {
            if (visited.Contains(tile)) continue;
            if (state.MovementCost(tile) == int.MaxValue) { visited.Add(tile); continue; }
            var region = state.GetConnectedLandmass(tile);
            foreach (var t in region) visited.Add(t);
            if (region.Count > largest.Count) largest = region;
        }
        return largest;
    }

    // The tile in `set` with the smallest hex distance to `target`.
    private static Vector2I ClosestInSet(HashSet<Vector2I> set, Vector2I target)
    {
        Vector2I best    = default;
        int      bestDist = int.MaxValue;
        foreach (var t in set)
        {
            int d = HexGrid.Distance(t, target);
            if (d < bestDist) { bestDist = d; best = t; }
        }
        return best;
    }
}
