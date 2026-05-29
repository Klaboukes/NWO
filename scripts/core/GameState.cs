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

    // Seed the combat RNG was created from. Recorded so save/load can reproduce
    // it (a fresh game with a null seed still gets a concrete, savable value).
    public int CombatSeed { get; }

    private readonly Dictionary<Player, FogOfWar>     _fog  = new();
    private readonly Dictionary<Player, Civilization> _civs = new();
    private readonly Random                           _combatRng;
    private int                                       _nextCityName;

    public GameState(MapData map, DataCatalog catalog, int? combatSeed = null)
    {
        Map        = map;
        Catalog    = catalog;
        CombatSeed = combatSeed ?? new Random().Next();
        _combatRng = new Random(CombatSeed);
    }

    // The rotating city-name counter, exposed for save/load round-tripping so a
    // reloaded game keeps naming cities where it left off.
    public int NextCityNameIndex => _nextCityName;

    // Restores the turn cursor after a load. The combat seed and per-player state
    // are set through the constructor / AddPlayer; this carries the remaining
    // scalar bookkeeping that has no public setter. Save/load support only.
    public void RestoreTurnPointer(int turnNumber, int currentPlayerIndex, int nextCityName)
    {
        TurnManager.SetTurn(turnNumber);
        CurrentPlayerIndex = currentPlayerIndex;
        _nextCityName      = nextCityName;
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
        attacker.ActedThisTurn     = true;

        AttackOutcome outcome =
            attackerDead && defenderDead ? AttackOutcome.BothKilled
            : attackerDead               ? AttackOutcome.AttackerKilled
            : defenderDead               ? AttackOutcome.DefenderKilled
            :                              AttackOutcome.Hit;

        return new AttackResult(outcome, combat.AttackerDamage, combat.DefenderDamage);

        static AttackResult Invalid() => new(AttackOutcome.Invalid, 0, 0);
    }

    public record CityAttackResult(
        bool Success, int CityDamage, int AttackerDamage, bool CityConquerable, bool AttackerKilled);

    // Bombard / assault a city: reduces its HP (never below 0) rather than killing
    // it. A melee attacker takes retaliation scaled by the city's defense; ranged
    // takes none. The city is captured separately, by moving a melee unit onto it
    // once it's conquerable (HP == 0). Garrisoned units defend the tile normally
    // (attack them with TryAttack) and also raise the city's defense strength.
    public CityAttackResult TryAttackCity(Unit attacker, City city)
    {
        if (attacker.Owner == city.Owner)            return InvalidCity();
        if (attacker.MovementRemaining <= 0)         return InvalidCity();
        if (attacker.Data.Attack <= 0)               return InvalidCity();
        if (city.HP <= 0)                            return InvalidCity(); // already conquerable
        int dist = HexGrid.Distance(attacker.Position, city.Position);
        if (dist <= 0 || dist > attacker.Data.Range) return InvalidCity();

        bool isRanged    = attacker.Data.Range >= 2;
        int  defStrength = city.CityDefenseStrength + GarrisonDefense(city);
        var  combat      = CombatResolver.Resolve(
            attacker.Data.Attack, attacker.HP, defStrength, city.HP, _combatRng, isRanged);

        city.HP                = Math.Max(0, city.HP - combat.DefenderDamage);
        city.AttackedSinceTurn = true;
        attacker.HP           -= combat.AttackerDamage;

        bool attackerDead = attacker.HP <= 0;
        if (attackerDead) Units.Remove(attacker);

        attacker.MovementRemaining = 0;
        attacker.ActedThisTurn     = true;

        return new CityAttackResult(true, combat.DefenderDamage, combat.AttackerDamage,
            city.IsConquerable, attackerDead);

        static CityAttackResult InvalidCity() => new(false, 0, 0, false, false);
    }

    // Highest Defense among friendly units standing on the city tile (the garrison).
    public int GarrisonDefense(City city)
    {
        int best = 0;
        foreach (var u in Units)
            if (u.Owner == city.Owner && u.Position == city.Position)
                best = Math.Max(best, u.Data.Defense);
        return best;
    }

    // Transfer a city to the captor's player. Captor's own movement bookkeeping
    // (zeroing remaining moves) is done by the caller's normal move flow.
    public void CaptureCity(Unit captor, City city)
    {
        city.Owner              = captor.Owner;
        city.ProductionItem     = null;
        city.ProductionProgress = 0;
        city.HP                 = City.MaxHP / 2; // captured cities start weakened
        city.AttackedSinceTurn  = false;
        city.Workforce.Locked.Clear();
        CityWorkforceService.Recompute(this, city);
    }

    // ── End-of-turn processing ───────────────────────────────────────────────

    public record ProductionCompletion(City City, string Item);

    // Ends the current player's turn: processes their cities, resets their unit
    // movement, then advances to the next player. When the index wraps back to
    // 0, the global turn number ticks up.
    public List<GameEvent> EndPlayerTurn(List<ProductionCompletion> completions)
    {
        var notifications = new List<GameEvent>();
        var player        = CurrentPlayer;

        foreach (var city in Cities)
        {
            if (city.Owner != player) continue;

            // Refresh assignments before yields are spent — enemy moves last
            // turn may have blockaded a worked tile.
            CityWorkforceService.Recompute(this, city);

            if (city.ProcessFood())
            {
                notifications.Add(new GameEvent($"{city.Name} grew to population {city.Population}!", city.Position, GameEventKind.CityGrew));
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
                    notifications.Add(new GameEvent($"{city.Name} completed {Catalog.ItemName(done)}!", city.Position, GameEventKind.CityProduced));
                }
            }

            city.RegenIfUnharassed();
        }

        foreach (var unit in Units)
        {
            if (unit.Owner != player) continue;
            AdvanceImprovementTask(unit, notifications);
            HealUnit(unit, player);
            unit.ResetForNewTurn();
        }

        CivEconomyService.ProcessEndOfTurn(this, player, notifications);

        CurrentPlayerIndex = (CurrentPlayerIndex + 1) % Players.Count;
        if (CurrentPlayerIndex == 0) TurnManager.AdvanceTurn();
        return notifications;
    }

    private const int UnitHealPerTurn      = 10;
    private const int UnitHealNearCityBonus = 5;

    // Units that didn't move or attack this turn recover HP, more when resting on
    // or next to a friendly city. Called before ResetForNewTurn clears ActedThisTurn.
    private void HealUnit(Unit unit, Player player)
    {
        if (unit.ActedThisTurn || unit.HP >= Unit.MaxHP) return;
        int heal = UnitHealPerTurn;
        if (Cities.Exists(c => c.Owner == player && HexGrid.Distance(c.Position, unit.Position) <= 1))
            heal += UnitHealNearCityBonus;
        unit.HP = Math.Min(Unit.MaxHP, unit.HP + heal);

        // "Fortify until healed" wakes the unit once it's back to full strength.
        if (unit.SleepUntilHealed && unit.HP >= Unit.MaxHP)
        {
            unit.SleepUntilHealed = false;
            unit.Fortified        = false;
        }
    }

    // Ticks down a Worker's build task. A worker that moved off its task tile
    // forfeits it. On completion the improvement is written to the map and any
    // city that works the tile re-tallies its yields.
    private void AdvanceImprovementTask(Unit unit, List<GameEvent> notifications)
    {
        if (unit.CurrentTask is not { } task) return;
        if (unit.Position != task.Tile) { unit.CurrentTask = null; return; }

        int remaining = task.TurnsRemaining - 1;
        if (remaining > 0)
        {
            unit.CurrentTask = task with { TurnsRemaining = remaining };
            return;
        }

        Map.Improvements[task.Tile] = task.Type;
        unit.CurrentTask = null;
        notifications.Add(new GameEvent($"Worker built {task.Type}.", task.Tile));
        foreach (var c in Cities)
            if (HexGrid.Distance(c.Position, task.Tile) <= CityWorkforceService.WorkRadius)
                CityWorkforceService.Recompute(this, c);
    }

    // Immediately finishes a city's current production (used by gold rush-buy).
    // Mirrors the end-of-turn completion: spawns the unit / adds the building and
    // clears the queue. Caller is responsible for charging gold. Returns the
    // completion (for notifications) or null if nothing was producing.
    public ProductionCompletion? RushProduction(City city)
    {
        if (city.ProductionItem == null) return null;
        var item = city.ProductionItem;
        CompleteProduction(city, item);
        city.ProductionItem     = null;
        city.ProductionProgress = 0;
        return new ProductionCompletion(city, item);
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
    {
        if (!Map.Tiles.TryGetValue(axial, out var t)) return int.MaxValue;
        int cost = TerrainYields.MovementCost(t);
        if (cost == int.MaxValue) return cost;
        // A road halves the entry cost (min 1) — rough/forest tiles become as
        // cheap as open ground to traverse.
        if (Map.ImprovementAt(axial) == ImprovementType.Road)
            cost = Math.Max(1, cost / 2);
        return cost;
    }

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
