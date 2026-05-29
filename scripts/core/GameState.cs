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

    public int    CurrentPlayerIndex { get; private set; }
    public Player CurrentPlayer      => Players[CurrentPlayerIndex];

    private readonly Dictionary<Player, FogOfWar>     _fog  = new();
    private readonly Dictionary<Player, Civilization> _civs = new();
    private readonly Random                           _combatRng;
    private int                                       _nextCityName;

    public GameState(MapData map, DataCatalog catalog, int? combatSeed = null)
    {
        Map        = map;
        Catalog    = catalog;
        _combatRng = combatSeed.HasValue ? new Random(combatSeed.Value) : new Random();
    }

    public Player AddPlayer(Player player)
    {
        Players.Add(player);
        _fog[player]  = new FogOfWar();
        _civs[player] = new Civilization(player) { Treasury = CivEconomyService.StartingTreasury };
        return player;
    }

    public FogOfWar     Fog(Player player) => _fog[player];
    public Civilization Civ(Player player) => _civs[player];

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

        // The first city a player founds is its capital (Phase 6 domination
        // victory targets it). Checked before the new city is added.
        bool isCapital = !Cities.Exists(c => c.Owner == settler.Owner);

        Units.Remove(settler);
        city = new City(NextCityName(), settler.Owner, pos) { IsCapital = isCapital };
        Cities.Add(city);
        CityWorkforceService.Recompute(this, city);
        // Founding a new city can shift tile control near neighbours.
        foreach (var other in Cities)
            if (other != city && HexGrid.Distance(other.Position, pos) <= CityWorkforceService.WorkRadius * 2)
                CityWorkforceService.Recompute(this, other);
        return FoundCityResult.Success;
    }

    // ── Combat ───────────────────────────────────────────────────────────────

    public enum AttackOutcome { Invalid, Hit, AttackerKilled, DefenderKilled, BothKilled }

    public record AttackResult(AttackOutcome Outcome, int AttackerDmg, int DefenderDmg);

    public AttackResult TryAttack(Unit attacker, Unit defender)
    {
        if (attacker.Owner == defender.Owner)                     return Invalid();
        if (attacker.MovementRemaining <= 0)                      return Invalid();
        int dist = HexGrid.Distance(attacker.Position, defender.Position);
        if (dist <= 0 || dist > attacker.Data.Range)              return Invalid();
        if (attacker.Data.Attack <= 0)                            return Invalid();

        bool isRanged = attacker.Data.Range >= 2;
        var  combat   = CombatResolver.Resolve(attacker, defender, _combatRng, isRanged);

        attacker.HP -= combat.AttackerDamage;
        defender.HP -= combat.DefenderDamage;

        bool attackerDead = attacker.HP <= 0;
        bool defenderDead = defender.HP <= 0;

        if (attackerDead) Units.Remove(attacker);
        if (defenderDead) Units.Remove(defender);

        attacker.MovementRemaining = 0;

        AttackOutcome outcome =
            attackerDead && defenderDead ? AttackOutcome.BothKilled
            : attackerDead               ? AttackOutcome.AttackerKilled
            : defenderDead               ? AttackOutcome.DefenderKilled
            :                              AttackOutcome.Hit;

        return new AttackResult(outcome, combat.AttackerDamage, combat.DefenderDamage);

        static AttackResult Invalid() => new(AttackOutcome.Invalid, 0, 0);
    }

    // Transfer a city to the captor's player. Captor's own movement bookkeeping
    // (zeroing remaining moves) is done by the caller's normal move flow.
    public void CaptureCity(Unit captor, City city)
    {
        city.Owner              = captor.Owner;
        city.ProductionItem     = null;
        city.ProductionProgress = 0;
        city.Workforce.Locked.Clear();
        CityWorkforceService.Recompute(this, city);
    }

    // ── End-of-turn processing ───────────────────────────────────────────────

    public record ProductionCompletion(City City, string Item);

    // Ends the current player's turn: processes their cities, resets their unit
    // movement, then advances to the next player. When the index wraps back to
    // 0, the global turn number ticks up.
    public List<string> EndPlayerTurn(List<ProductionCompletion> completions)
    {
        var notifications = new List<string>();
        var player        = CurrentPlayer;

        foreach (var city in Cities)
        {
            if (city.Owner != player) continue;

            // Refresh assignments before yields are spent — enemy moves last
            // turn may have blockaded a worked tile.
            CityWorkforceService.Recompute(this, city);

            if (city.ProcessFood())
            {
                notifications.Add($"{city.Name} grew to population {city.Population}!");
                CityWorkforceService.Recompute(this, city);
            }

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

        foreach (var unit in Units)
            if (unit.Owner == player) unit.ResetForNewTurn();

        CivEconomyService.ProcessEndOfTurn(this, player, notifications);

        CurrentPlayerIndex = (CurrentPlayerIndex + 1) % Players.Count;
        if (CurrentPlayerIndex == 0) TurnManager.AdvanceTurn();
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
                CityWorkforceService.Recompute(this, city);
                break;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

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

    // All tiles reachable from `origin` by walking over land — i.e. the
    // connected landmass it sits on. Returns an empty set if origin itself is
    // impassable. Used to confine the AI's starting position to the player's
    // continent during MVP; once naval movement / cross-island AI lands, the
    // caller should switch to picking from any landmass.
    public HashSet<Vector2I> GetConnectedLandmass(Vector2I origin)
    {
        var landmass = new HashSet<Vector2I>();
        if (MovementCost(origin) == int.MaxValue) return landmass;

        landmass.Add(origin);
        var queue = new Queue<Vector2I>();
        queue.Enqueue(origin);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var n in HexGrid.GetNeighbors(current))
            {
                if (!Map.Tiles.ContainsKey(n))         continue;
                if (MovementCost(n) == int.MaxValue)   continue;
                if (!landmass.Add(n))                  continue;
                queue.Enqueue(n);
            }
        }
        return landmass;
    }

    private static readonly string[] CityNames =
    {
        "Rome", "Athens", "Babylon", "Cairo", "Paris", "London",
        "Moscow", "Beijing", "Delhi", "Tokyo", "Istanbul", "Berlin",
        "Madrid", "Lisbon", "Amsterdam", "Vienna", "Warsaw", "Prague",
    };

    public string NextCityName() => CityNames[_nextCityName++ % CityNames.Length];
}
