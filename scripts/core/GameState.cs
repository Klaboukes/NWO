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

    // Minimum tile distance between any two cities. Public so the AI can score
    // candidate settle sites without founding-and-failing (see AIController).
    public const int MinCityDistance = 3;

    public MapData     Map         { get; }
    public DataCatalog Catalog     { get; }
    public TurnManager TurnManager { get; } = new();
    public Diplomacy   Diplomacy   { get; } = new();

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
        => _fog[player].Recompute(player, Units, Cities, Map, CitySightRadius,
                                  Catalog.FactionOf(player).SightBonus, animOverrides);

    // ── City founding ────────────────────────────────────────────────────────

    public enum FoundCityResult { Success, BadTerrain, TooClose }

    // Per-player settle spacing: the base minimum plus the founder faction's delta
    // (Free Settlements packs tighter). Floored at 2 so cities never overlap work tiles.
    public int EffectiveMinCityDistance(Player player)
        => Math.Max(2, MinCityDistance + Catalog.FactionOf(player).MinCityDistanceDelta);

    public FoundCityResult TryFoundCity(Unit settler, out City? city)
    {
        city = null;
        var pos = settler.Position;
        var terrain = Map.Tiles.GetValueOrDefault(pos, TerrainType.Ocean);
        if (!TerrainYields.CanFoundCityOn(terrain))
            return FoundCityResult.BadTerrain;
        int minDist = EffectiveMinCityDistance(settler.Owner);
        foreach (var existing in Cities)
            if (HexGrid.Distance(existing.Position, pos) < minDist)
                return FoundCityResult.TooClose;

        // The first city a player founds is its capital (Phase 6 domination
        // victory targets it). Checked before the new city is added.
        bool isCapital = !Cities.Exists(c => c.Owner == settler.Owner);

        Units.Remove(settler);
        city = new City(NextCityName(), settler.Owner, pos) { IsCapital = isCapital };
        Cities.Add(city);
        SpawnNewCityDefender(city);
        CityWorkforceService.Recompute(this, city);
        // Founding a new city can shift tile control near neighbours.
        foreach (var other in Cities)
            if (other != city && HexGrid.Distance(other.Position, pos) <= CityWorkforceService.WorkRadius * 2)
                CityWorkforceService.Recompute(this, other);
        return FoundCityResult.Success;
    }

    // The Free Settlements faction's new cities are born with a defender. Spawns the
    // cheapest tech/resource-free combat unit (faction-resolved, so the Pioneer's
    // militia identity carries through), placed on the new city tile.
    private void SpawnNewCityDefender(City city)
    {
        if (!Catalog.FactionOf(city.Owner).Traits.Contains("new_city_defender")) return;

        UnitData? best = null;
        foreach (var u in Catalog.Units)
        {
            if (u.Defense <= 0 || u.RequiredTech != null || u.RequiredResource != null) continue;
            if (best == null || u.ProductionCost < best.ProductionCost) best = u;
        }
        if (best == null) return;

        var def = Catalog.Unit(Catalog.ResolveUnitForFaction(best.Id, city.Owner)) ?? best;
        Units.Add(new Unit(def, city.Owner, city.Position));
    }

    // ── Combat ───────────────────────────────────────────────────────────────

    public enum AttackOutcome { Invalid, Hit, AttackerKilled, DefenderKilled, BothKilled }

    public record AttackResult(AttackOutcome Outcome, int AttackerDmg, int DefenderDmg);

    public AttackResult TryAttack(Unit attacker, Unit defender)
    {
        if (attacker.Owner == defender.Owner)                     return Invalid();
        if (!Diplomacy.CanAttack(attacker.Owner.Id, defender.Owner.Id)) return Invalid();
        if (attacker.MovementRemaining <= 0)                      return Invalid();
        int dist = HexGrid.Distance(attacker.Position, defender.Position);
        if (dist <= 0 || dist > attacker.Data.Range)              return Invalid();
        if (attacker.Data.Attack <= 0)                            return Invalid();

        bool isRanged = attacker.Data.Range >= 2;
        var  combat   = CombatResolver.Resolve(
            EffectiveAttack(attacker), attacker.HP, EffectiveDefense(defender), defender.HP, _combatRng, isRanged);

        attacker.HP -= combat.AttackerDamage;
        defender.HP -= combat.DefenderDamage;

        bool attackerDead = attacker.HP <= 0;
        bool defenderDead = defender.HP <= 0;

        if (attackerDead) { Units.Remove(attacker); attacker.Cargo.Clear(); } else GrantCombatXp(attacker);
        if (defenderDead) { Units.Remove(defender); defender.Cargo.Clear(); } else GrantCombatXp(defender);

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
        if (!Diplomacy.CanAttack(attacker.Owner.Id, city.Owner.Id)) return InvalidCity();
        if (attacker.MovementRemaining <= 0)         return InvalidCity();
        if (attacker.Data.Attack <= 0)               return InvalidCity();
        if (city.HP <= 0)                            return InvalidCity(); // already conquerable
        int dist = HexGrid.Distance(attacker.Position, city.Position);
        if (dist <= 0 || dist > attacker.Data.Range) return InvalidCity();

        bool isRanged    = attacker.Data.Range >= 2;
        int  defStrength = CityDefenseTotal(city);
        var  combat      = CombatResolver.Resolve(
            EffectiveAttack(attacker), attacker.HP, defStrength, city.HP, _combatRng, isRanged);

        city.HP                = Math.Max(0, city.HP - combat.DefenderDamage);
        city.AttackedSinceTurn = true;
        attacker.HP           -= combat.AttackerDamage;

        bool attackerDead = attacker.HP <= 0;
        if (attackerDead) { Units.Remove(attacker); attacker.Cargo.Clear(); } else GrantCombatXp(attacker);

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

    // ── Faction-modified combat (Phase 10) ─────────────────────────────────────
    // The faction modifier bag is resolved here, at the one layer that holds both
    // the Catalog and the owning Player, so CombatResolver/City stay pure.

    private const int CombatXpPerFight = 6;

    private double CombatStrengthFactor(Unit u)
        => Catalog.FactionOf(u.Owner).CombatStrengthMult * u.VeterancyMult;

    private int EffectiveAttack(Unit u)  => Math.Max(1, (int)Math.Round(u.Data.Attack  * CombatStrengthFactor(u)));
    private int EffectiveDefense(Unit u) => Math.Max(1, (int)Math.Round(u.Data.Defense * CombatStrengthFactor(u)));

    private void GrantCombatXp(Unit u)
        => u.Experience += Math.Max(1, (int)Math.Round(CombatXpPerFight * Catalog.FactionOf(u.Owner).XpGainMult));

    // Effective defensive strength of a city under assault: intrinsic + garrison +
    // the owner faction's fortress-capital bonus (capital only). Single source of
    // truth for both TryAttackCity and the AI's city-attack scoring.
    public int CityDefenseTotal(City city)
    {
        int bonus = city.IsCapital ? Catalog.FactionOf(city.Owner).CityDefenseBonus : 0;
        return city.CityDefenseStrength + GarrisonDefense(city) + bonus;
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

    // ── Naval transport (cargo) ────────────────────────────────────────────────
    // Core load/unload mechanics, shared by the player path (GameSession wraps these
    // with viewer/fog handling) and the AI (which calls them directly). Validation
    // lives here so both agree on what a legal embark/disembark is.

    // Boards a land unit onto an adjacent friendly transport (same owner) sitting on
    // water with free capacity. The unit leaves the map into transport.Cargo, spending
    // its move. Returns false if any precondition fails.
    public bool LoadUnit(Unit landUnit, Unit transport)
    {
        if (landUnit.Owner != transport.Owner)                     return false;
        if (landUnit.Data.IsNaval)                                 return false;
        if (transport.Data.CargoCapacity <= 0)                     return false;
        if (transport.Cargo.Count >= transport.Data.CargoCapacity) return false;
        if (landUnit.MovementRemaining <= 0)                       return false; // spent units can't board
        if (!Units.Contains(landUnit))                             return false; // prevent double-load
        if (HexGrid.Distance(landUnit.Position, transport.Position) > 1) return false;
        if (!Map.Tiles.TryGetValue(transport.Position, out var tt)) return false;
        if (!TerrainYields.IsWater(tt))                            return false;

        Units.Remove(landUnit);
        landUnit.MovementRemaining = 0;
        landUnit.ActedThisTurn     = true;
        transport.Cargo.Add(landUnit);
        return true;
    }

    // Disembarks one cargo unit onto an adjacent passable, unoccupied land tile. The
    // transport spends all its remaining movement. Returns false if any precondition fails.
    public bool UnloadUnit(Unit transport, Unit cargoUnit, Vector2I destTile)
    {
        if (transport.Data.CargoCapacity <= 0)        return false;
        if (!transport.Cargo.Contains(cargoUnit))     return false;
        if (HexGrid.Distance(transport.Position, destTile) != 1) return false;
        if (!Map.Tiles.TryGetValue(destTile, out var dt)) return false;
        if (TerrainYields.IsWater(dt) || dt == TerrainType.Mountain) return false;
        if (Units.Exists(u => u.Position == destTile)) return false; // block friendly stacking too

        transport.Cargo.Remove(cargoUnit);
        cargoUnit.Position          = destTile;
        cargoUnit.MovementRemaining = 0;
        cargoUnit.ActedThisTurn     = true;
        Units.Add(cargoUnit);
        transport.MovementRemaining = 0;
        transport.ActedThisTurn     = true;
        return true;
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
                int cost = EffectiveItemCost(city.Owner, city.ProductionItem);
                string? done = city.AdvanceProduction(cost);
                if (done != null)
                {
                    completions.Add(new ProductionCompletion(city, done));
                    CompleteProduction(city, done);
                    notifications.Add(new GameEvent($"{city.Name} completed {Catalog.ItemName(done)}!", city.Position, GameEventKind.CityProduced));
                }
            }

            double regenMult = city.IsCapital ? Catalog.FactionOf(city.Owner).CityRegenMult : 1.0;
            city.RegenIfUnharassed(regenMult);
        }

        foreach (var unit in Units)
        {
            if (unit.Owner != player) continue;
            AdvanceImprovementTask(unit, notifications);
            HealUnit(unit, player);
            unit.ResetForNewTurn();
        }

        // Cargo units are not in state.Units while in transit; heal and reset them separately.
        foreach (var unit in Units)
        {
            if (unit.Owner != player) continue;
            foreach (var cargo in unit.Cargo)
            {
                HealUnit(cargo, player);
                cargo.ResetForNewTurn();
            }
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
        // Iron Pact's "heal in enemy land": its units recover even after fighting
        // this turn, so an offensive keeps its strength up mid-campaign. Other
        // factions only heal when they didn't act.
        bool healsAfterActing = Catalog.FactionOf(player).HealInEnemyLand;
        if ((unit.ActedThisTurn && !healsAfterActing) || unit.HP >= Unit.MaxHP) return;
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
                // Unique-unit swap: the owner faction may field a variant of the
                // queued base unit (UI/AI keep speaking in base ids).
                var udef = Catalog.Unit(Catalog.ResolveUnitForFaction(id, city.Owner));
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

    // Production cost of a build item for a specific owner, applying the faction's
    // settle discount to settler-class units (Free Settlements builds settlers fast).
    public int EffectiveItemCost(Player owner, string item)
    {
        int cost = Catalog.ItemCost(item);
        if (cost == int.MaxValue) return cost;
        var (kind, id) = DataCatalog.SplitItem(item);
        if (kind == "unit" && Catalog.Unit(Catalog.ResolveUnitForFaction(id, owner))?.Special == "found_city")
            cost = Math.Max(1, (int)Math.Round(cost * Catalog.FactionOf(owner).SettleCostMult));
        return cost;
    }

    public int MovementCost(Vector2I axial)
    {
        if (!Map.Tiles.TryGetValue(axial, out var t)) return int.MaxValue;
        int cost = TerrainYields.MovementCost(t);
        if (cost == int.MaxValue) return cost;
        // A Hills feature adds rough-terrain movement cost on top of the base terrain.
        cost += FeatureYields.MovementCost(Map.FeatureAt(axial));
        // A road halves the entry cost (min 1) — rough/forest tiles become as
        // cheap as open ground to traverse.
        if (Map.ImprovementAt(axial) == ImprovementType.Road)
            cost = Math.Max(1, cost / 2);
        return cost;
    }

    // Movement cost to enter `axial` for a specific unit. Naval units move freely
    // on Ocean/Coast and are blocked on land; land units remain blocked on Ocean/Coast.
    // A unit that ignores terrain cost (Scout) pays a flat 1 for any passable tile.
    public int MovementCost(Vector2I axial, Unit unit)
    {
        if (!Map.Tiles.TryGetValue(axial, out var t)) return int.MaxValue;
        bool isWater = TerrainYields.IsWater(t);

        if (unit.Data.IsNaval)
            return isWater ? 1 : int.MaxValue;

        // Land units: Ocean/Coast remain impassable via the base terrain cost.
        int cost = MovementCost(axial);
        if (cost == int.MaxValue)         return cost;
        if (unit.Data.IgnoresTerrainCost) return 1;
        // Voyagers' reduced terrain costs scale every tile's entry cost (min 1).
        double mult = Catalog.FactionOf(unit.Owner).TerrainCostMult;
        return Math.Max(1, (int)Math.Round(cost * mult));
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
