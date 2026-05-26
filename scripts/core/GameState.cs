using System;
using System.Collections.Generic;
using Godot;
using NWO.Entities;
using NWO.Map;

namespace NWO.Core;

// Authoritative gameplay state. Holds the world model (map, players, units,
// cities, fog, turn) and exposes pure-logic operations on it. Owns no Godot
// scene state — the renderer and UI read from this; input handlers mutate it.
//
// Designed for testability: every method here can be exercised without a scene
// running, and the per-player fog dictionary makes it easy to add an AI player
// in Phase 3 without further refactoring.
public class GameState
{
    private const int CitySightRadius = 2;
    private const int MinCityDistance = 3;

    public MapData     Map         { get; }
    public DataCatalog Catalog     { get; }
    public TurnManager TurnManager { get; } = new();

    public List<Player> Players { get; } = new();
    public List<Unit>   Units   { get; } = new();
    public List<City>   Cities  { get; } = new();

    private readonly Dictionary<Player, FogOfWar> _fog = new();
    private int _nextCityName;

    public GameState(MapData map, DataCatalog catalog)
    {
        Map     = map;
        Catalog = catalog;
    }

    public Player AddPlayer(Player player)
    {
        Players.Add(player);
        _fog[player] = new FogOfWar();
        return player;
    }

    public FogOfWar Fog(Player player) => _fog[player];

    public void RecomputeFog(Player player, IReadOnlyDictionary<Unit, Vector2I>? animOverrides = null)
        => _fog[player].Recompute(player, Units, Cities, Map, CitySightRadius, animOverrides);

    // ── City founding ────────────────────────────────────────────────────────

    public enum FoundCityResult { Success, BadTerrain, TooClose }

    public FoundCityResult TryFoundCity(Unit settler, out City? city)
    {
        city = null;
        var pos = settler.Position;
        var terrain = Map.Tiles.GetValueOrDefault(pos, TerrainType.Ocean);
        if (!TerrainYields.CanFoundCityOn(terrain))
            return FoundCityResult.BadTerrain;
        foreach (var existing in Cities)
            if (HexGrid.Distance(existing.Position, pos) < MinCityDistance)
                return FoundCityResult.TooClose;

        Units.Remove(settler);
        city = new City(NextCityName(), settler.Owner, pos);
        ComputeCityYields(city);
        Cities.Add(city);
        return FoundCityResult.Success;
    }

    // ── End-of-turn processing ───────────────────────────────────────────────

    public record ProductionCompletion(City City, string Item);

    public List<string> ProcessEndOfTurn(List<ProductionCompletion> completions)
    {
        var notifications = new List<string>();

        foreach (var city in Cities)
        {
            if (city.ProcessFood())
                notifications.Add($"{city.Name} grew to population {city.Population}!");

            if (city.ProductionItem != null)
            {
                int cost = Catalog.ItemCost(city.ProductionItem);
                string? done = city.AdvanceProduction(cost);
                if (done != null)
                {
                    completions.Add(new ProductionCompletion(city, done));
                    CompleteProduction(city, done);
                    notifications.Add($"{city.Name} completed {Catalog.ItemName(done)}!");
                }
            }
        }

        foreach (var unit in Units) unit.ResetForNewTurn();
        TurnManager.AdvanceTurn();
        return notifications;
    }

    private void CompleteProduction(City city, string item)
    {
        var (kind, id) = DataCatalog.SplitItem(item);
        switch (kind)
        {
            case "unit":
                var udef = Catalog.Unit(id);
                if (udef != null) Units.Add(new Unit(udef, city.Owner, city.Position));
                break;
            case "building":
                var bdef = Catalog.Building(id);
                if (bdef == null) return;
                city.Buildings.Add(id);
                city.FoodYield       += bdef.Yields.Food;
                city.ProductionYield += bdef.Yields.Production;
                break;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    public void ComputeCityYields(City city)
    {
        int food = 0, prod = 0;
        foreach (var tile in HexGrid.GetRange(city.Position, 1))
        {
            if (!Map.Tiles.TryGetValue(tile, out var t)) continue;
            food += TerrainYields.Food(t);
            prod += TerrainYields.Production(t);
        }
        city.FoodYield       = Math.Max(1, food);
        city.ProductionYield = Math.Max(1, prod);
    }

    public int MovementCost(Vector2I axial)
        => Map.Tiles.TryGetValue(axial, out var t) ? TerrainYields.MovementCost(t) : int.MaxValue;

    public Vector2I FindWalkableTileNear(Vector2I origin)
    {
        if (MovementCost(origin) != int.MaxValue) return origin;
        var visited = new HashSet<Vector2I> { origin };
        var queue   = new Queue<Vector2I>();
        queue.Enqueue(origin);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var n in HexGrid.GetNeighbors(current))
            {
                if (!visited.Add(n)) continue;
                if (MovementCost(n) != int.MaxValue) return n;
                queue.Enqueue(n);
            }
        }
        return origin;
    }

    private static readonly string[] CityNames =
    {
        "Rome", "Athens", "Babylon", "Cairo", "Paris", "London",
        "Moscow", "Beijing", "Delhi", "Tokyo", "Istanbul", "Berlin",
        "Madrid", "Lisbon", "Amsterdam", "Vienna", "Warsaw", "Prague",
    };

    public string NextCityName() => CityNames[_nextCityName++ % CityNames.Length];
}
