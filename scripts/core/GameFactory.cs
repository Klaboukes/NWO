using System.Collections.Generic;
using Godot;
using NWO.Entities;
using NWO.Map;

namespace NWO.Core;

// Builds a fresh GameState for a new match: generates the map, adds the human +
// AI players, and places their starting units (the AI is confined to the
// player's landmass for MVP — see ROADMAP Post-MVP). Extracted from
// WorldMap._Ready so new-game and load-game share one bootstrap path: the load
// path skips this entirely and hands WorldMap a deserialized GameState via
// GameLaunch, while a new game runs NewGame here.
public static class GameFactory
{
    public const int MapWidth  = 60;
    public const int MapHeight = 40;

    private const int MinAISpawnDistance = 10;

    public record NewGameResult(GameState State, Player Viewer);

    public static NewGameResult NewGame(int seed)
    {
        var map     = MapGenerator.Generate(MapWidth, MapHeight, seed);
        var catalog = DataCatalog.Load();
        var state   = new GameState(map, catalog, seed);

        var viewer   = state.AddPlayer(new Player { Id = 0, Name = "Player",     IsHuman = true,  Color = Colors.Blue });
        var aiPlayer = state.AddPlayer(new Player { Id = 1, Name = "Barbarians", IsHuman = false, Color = Colors.Red  });

        var warriorDef = catalog.Unit("warrior")!;
        var settlerDef = catalog.Unit("settler")!;

        var startPos = state.FindWalkableTileNear(MapCenterAxial());
        state.Units.Add(new Unit(warriorDef, viewer, startPos));
        state.Units.Add(new Unit(settlerDef, viewer,
            state.FindWalkableTileNear(new Vector2I(startPos.X + 3, startPos.Y))));

        // Confine the AI to the player's landmass for MVP. Cross-continent /
        // island AI is a Post-MVP concern (needs naval movement).
        var landmass       = state.GetConnectedLandmass(startPos);
        var aiStart        = PickAISpawn(startPos, landmass);
        var aiSettlerStart = FindNeighborOnLandmass(aiStart, landmass) ?? aiStart;
        state.Units.Add(new Unit(warriorDef, aiPlayer, aiStart));
        state.Units.Add(new Unit(settlerDef, aiPlayer, aiSettlerStart));

        return new NewGameResult(state, viewer);
    }

    public static Vector2I MapCenterAxial()
    {
        int col = MapWidth  / 2;
        int row = MapHeight / 2;
        return new Vector2I(col, row - (col - (col & 1)) / 2);
    }

    // Picks an AI starting tile on `landmass` (the player's continent).
    // Preference order:
    //   1. Tiles at least MinAISpawnDistance away (avoids spawning adjacent).
    //   2. Whatever's farthest if the island is too small for that rule.
    // Falls back to playerStart only if the landmass is empty (player landed
    // on a lone unwalkable tile — shouldn't happen in practice).
    private static Vector2I PickAISpawn(Vector2I playerStart, HashSet<Vector2I> landmass)
    {
        if (landmass.Count == 0) return playerStart;

        Vector2I best        = playerStart;
        int      bestDist    = -1;
        Vector2I farFromMin  = playerStart;
        int      farFromMinD = -1;

        foreach (var tile in landmass)
        {
            int d = HexGrid.Distance(playerStart, tile);
            if (d > farFromMinD) { farFromMinD = d; farFromMin = tile; }
            if (d >= MinAISpawnDistance && d > bestDist) { bestDist = d; best = tile; }
        }
        return bestDist >= 0 ? best : farFromMin;
    }

    // Walkable neighbour of `tile` that's on the same landmass, used to place
    // the AI settler one hex from the warrior without leaving the continent.
    private static Vector2I? FindNeighborOnLandmass(Vector2I tile, HashSet<Vector2I> landmass)
    {
        foreach (var n in HexGrid.GetNeighbors(tile))
            if (landmass.Contains(n)) return n;
        return null;
    }
}
