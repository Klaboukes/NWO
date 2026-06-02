using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace NWO.Map;

/// <summary>
/// Pure static hex grid math using axial coordinates (q, r).
/// Flat-top orientation. Impassable tiles signal int.MaxValue movement cost.
/// Reference: https://www.redblobgames.com/grids/hexagons/
/// </summary>
public static class HexGrid
{
    // Flat-top axial directions: E, NE, NW, W, SW, SE
    public static readonly Vector2I[] Directions =
    {
        new( 1,  0),  // E
        new( 1, -1),  // NE
        new( 0, -1),  // NW
        new(-1,  0),  // W
        new(-1,  1),  // SW
        new( 0,  1),  // SE
    };

    public static Vector2I[] GetNeighbors(Vector2I axial)
    {
        var result = new Vector2I[6];
        for (int i = 0; i < 6; i++)
            result[i] = axial + Directions[i];
        return result;
    }

    // The three tiles that meet at corner `c` (0..5) of `tile`: the tile itself plus
    // the two neighbours sharing that vertex. Corner c sits between edges (c-1) and c,
    // i.e. between the neighbours in directions (7-c)%6 and (6-c)%6. Used by river
    // tracing (vertex graph) and rendering (consistent corner height) — one source of
    // truth for hex-corner topology.
    public static (Vector2I A, Vector2I B, Vector2I C) CornerTiles(Vector2I tile, int c)
        => (tile,
            tile + Directions[(7 - c) % 6],
            tile + Directions[(6 - c) % 6]);

    // Cube-coordinate distance formula adapted to axial (q + r + s = 0, s = -q - r)
    public static int Distance(Vector2I a, Vector2I b)
    {
        int dq = a.X - b.X;
        int dr = a.Y - b.Y;
        return (Math.Abs(dq) + Math.Abs(dr) + Math.Abs(dq + dr)) / 2;
    }

    // All tiles at exactly radius steps away (hollow ring)
    public static List<Vector2I> GetRing(Vector2I center, int radius)
    {
        if (radius == 0)
            return new List<Vector2I> { center };

        var results = new List<Vector2I>(6 * radius);
        // Start at the SW corner of the ring and walk around all 6 sides
        var current = center + Directions[4] * radius;
        for (int side = 0; side < 6; side++)
        {
            for (int step = 0; step < radius; step++)
            {
                results.Add(current);
                current += Directions[side];
            }
        }
        return results;
    }

    // All tiles within radius steps (filled disc) — used for sight and area effects
    public static List<Vector2I> GetRange(Vector2I center, int radius)
    {
        var results = new List<Vector2I>(1 + 3 * radius * (radius + 1)) { center };
        for (int r = 1; r <= radius; r++)
            results.AddRange(GetRing(center, r));
        return results;
    }

    // Tiles reachable within movementPoints using Dijkstra.
    // movementCost delegate: return cost to enter a tile, or int.MaxValue if impassable.
    public static List<Vector2I> GetReachableTiles(
        Vector2I origin,
        int movementPoints,
        Func<Vector2I, int> movementCost)
    {
        var costSoFar = new Dictionary<Vector2I, int> { [origin] = 0 };
        var frontier = new PriorityQueue<Vector2I, int>();
        frontier.Enqueue(origin, 0);

        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();

            foreach (var neighbor in GetNeighbors(current))
            {
                int cost = movementCost(neighbor);
                if (cost == int.MaxValue) continue;

                int newCost = costSoFar[current] + cost;
                if (newCost > movementPoints) continue;

                if (!costSoFar.TryGetValue(neighbor, out int existing) || newCost < existing)
                {
                    costSoFar[neighbor] = newCost;
                    frontier.Enqueue(neighbor, newCost);
                }
            }
        }

        costSoFar.Remove(origin);
        return costSoFar.Keys.ToList();
    }

    // A* pathfinding. Returns full path including from and to, or empty list if no path exists.
    // movementCost delegate: return cost to enter a tile, or int.MaxValue if impassable.
    //
    // MaxExpansions is a defensive ceiling: a cost function that's passable in every
    // direction (e.g. a missing map-bounds check) would otherwise explore the infinite
    // hex plane forever when no path exists. 50k tiles covers any realistic map.
    private const int MaxExpansions = 50_000;

    public static List<Vector2I> FindPath(
        Vector2I from,
        Vector2I to,
        Func<Vector2I, int> movementCost)
    {
        if (from == to)
            return new List<Vector2I> { from };

        var gScore = new Dictionary<Vector2I, int> { [from] = 0 };
        var cameFrom = new Dictionary<Vector2I, Vector2I>();
        var openSet = new PriorityQueue<Vector2I, int>();
        openSet.Enqueue(from, Distance(from, to));

        int expansions = 0;
        while (openSet.Count > 0 && expansions++ < MaxExpansions)
        {
            var current = openSet.Dequeue();

            if (current == to)
                return ReconstructPath(cameFrom, current);

            foreach (var neighbor in GetNeighbors(current))
            {
                int cost = movementCost(neighbor);
                if (cost == int.MaxValue) continue;

                int tentativeG = gScore[current] + cost;
                if (!gScore.TryGetValue(neighbor, out int existingG) || tentativeG < existingG)
                {
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeG;
                    openSet.Enqueue(neighbor, tentativeG + Distance(neighbor, to));
                }
            }
        }

        return new List<Vector2I>(); // no path found or expansion budget exhausted
    }

    private static List<Vector2I> ReconstructPath(Dictionary<Vector2I, Vector2I> cameFrom, Vector2I current)
    {
        var path = new List<Vector2I>();
        while (cameFrom.TryGetValue(current, out var prev))
        {
            path.Add(current);
            current = prev;
        }
        path.Add(current);
        path.Reverse();
        return path;
    }
}
