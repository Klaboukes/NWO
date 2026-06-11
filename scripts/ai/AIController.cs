using System.Collections.Generic;
using System.Linq;
using Godot;
using NWO.Core;
using NWO.Entities;
using NWO.Map;

namespace NWO.AI;

// Strategic AI. Each turn it: picks a research target when idle; moves and fights
// its units (attacking only when the deterministic combat forecast favours it,
// retreating the wounded, garrisoning threatened cities, settling new sites with
// its Settlers, improving tiles with its Workers); and finally chooses production
// for every idle city (defenders first, then Walls under threat, expansion,
// Workers, then attackers).
//
// MVP simplifications:
//   - AI sees through fog (iterates GameState.Units/Cities directly).
//     TODO: gate by per-AI FogOfWar when difficulty levels are added.
//   - AI moves resolve instantly — no MovementAnimator interleave.
//   - Deterministic (no RNG): combat decisions use CombatResolver.Expected so
//     tests and replays are reproducible.
public class AIController
{
    private readonly GameState _state;

    // Per-turn cache of connected landmasses (terrain geometry is stable within a
    // turn), so the cross-continent checks below BFS each continent at most once.
    private readonly List<HashSet<Vector2I>> _turnLandmasses = new();

    public AIController(GameState state) => _state = state;

    // How many cities the AI wants before it stops prioritising expansion.
    private const int ExpansionTarget = 3;
    // Enemy military within this many tiles of a city counts as a threat.
    private const int ThreatRadius = 3;
    // Below this HP a unit breaks off to heal instead of pressing an attack.
    private const int RetreatHp = 35;
    // How far a unit will travel to reinforce a threatened friendly city.
    private const int GarrisonRange = 6;
    // Radius the AI scans around a Settler for a better settle site.
    private const int SiteSearchRadius = 6;

    // Production preferences, best-first. Filtered by tech/resource availability.
    private static readonly string[] DefenderPrefs = { "spearman", "warrior" };
    private static readonly string[] AttackerPrefs = { "swordsman", "horseman", "warrior", "archer" };
    private static readonly string[] NavalCombatPrefs = { "frigate", "galley" };
    private static readonly string[] NavalTransportPrefs = { "galleon", "transport" };

    // Research order: cheap economy/unlock techs first, then the prereq chains.
    private static readonly string[] ResearchPrefs =
    {
        "pottery", "mining", "sailing", "bronze_working", "animal_husbandry",
        "writing", "navigation", "iron_working", "horseback_riding", "philosophy",
    };

    public void TakeTurn(Player ai)
    {
        _turnLandmasses.Clear();
        ReviewDiplomacy(ai);
        ChooseResearch(ai);

        // Snapshot the unit list — combat/founding can add or remove units.
        foreach (var unit in _state.Units.Where(u => u.Owner == ai).ToList())
        {
            if (!_state.Units.Contains(unit)) continue; // killed/disbanded earlier this loop
            Act(ai, unit);
        }

        // Production pass: every idle city gets an order, plus a focus by need.
        foreach (var city in _state.Cities.Where(c => c.Owner == ai))
        {
            if (city.ProductionItem == null)
                city.ProductionItem = ChooseProduction(ai, city);
            SetFocus(city);
        }
    }

    // ── Diplomacy ──────────────────────────────────────────────────────────────

    // Opportunistic war needs at least this strength advantage; stalemate peace
    // needs both sides inside the inverse band. The bands don't overlap, so a
    // freshly-made peace isn't re-declared the next turn (no flip-flopping).
    private const double WarStrengthRatio   = 2.0;
    private const double PeaceStrengthRatio = 1.33;

    // Minimal stance logic until the diplomacy UI lands (Phase 10 follow-up):
    //  - Declare war from Peace when overwhelmingly stronger (never breaks a
    //    signed NonAggression/Alliance pact).
    //  - Between two AIs, a stalemated war (forces roughly even) settles into
    //    Peace. Wars with the human are never ended unilaterally — peace with
    //    the player will be the player's call via the future diplomacy UI.
    private void ReviewDiplomacy(Player ai)
    {
        double ownStrength = MilitaryStrength(ai);
        foreach (var other in _state.Players)
        {
            if (other == ai) continue;
            double theirs = MilitaryStrength(other);
            switch (_state.Diplomacy.Between(ai.Id, other.Id))
            {
                case DiplomaticStance.Peace when ownStrength > 0
                                                 && ownStrength >= WarStrengthRatio * theirs:
                    _state.Diplomacy.Set(ai.Id, other.Id, DiplomaticStance.War);
                    break;
                case DiplomaticStance.War when !other.IsHuman
                                               && ownStrength <= PeaceStrengthRatio * theirs
                                               && theirs <= PeaceStrengthRatio * ownStrength:
                    _state.Diplomacy.Set(ai.Id, other.Id, DiplomaticStance.Peace);
                    break;
            }
        }
    }

    // HP-weighted combat strength of a player's army (effective stats, so
    // faction passives and veterancy count). Deterministic.
    private double MilitaryStrength(Player p)
    {
        double total = 0;
        foreach (var u in _state.Units)
            if (u.Owner == p && u.Data.Attack > 0)
                total += (_state.EffectiveAttack(u) + _state.EffectiveDefense(u)) * (u.HP / 100.0);
        return total;
    }

    // ── Research ───────────────────────────────────────────────────────────────

    private void ChooseResearch(Player ai)
    {
        var civ = _state.Civ(ai);
        if (civ.CurrentResearch != null) return;

        foreach (var id in ResearchPrefs)
        {
            if (civ.ResearchedTechs.Contains(id)) continue;
            // SetResearch validates prereqs/existence and assigns on success.
            if (CivEconomyService.SetResearch(_state, ai, id)
                == CivEconomyService.SetResearchResult.Ok)
                return;
        }

        // Preference list exhausted — research anything still available (e.g.
        // Calendar, or future techs not in the list) so science is never wasted.
        foreach (var tech in _state.Catalog.Techs)
        {
            if (civ.ResearchedTechs.Contains(tech.Id)) continue;
            if (CivEconomyService.SetResearch(_state, ai, tech.Id)
                == CivEconomyService.SetResearchResult.Ok)
                return;
        }
    }

    // ── Production ──────────────────────────────────────────────────────────────

    private string? ChooseProduction(Player ai, City city)
    {
        var civ = _state.Civ(ai);

        // 1. A city with no military garrison builds a defender first.
        if (!IsDefended(ai, city))
        {
            var defender = BestBuildableUnit(ai, civ, DefenderPrefs);
            if (defender != null) return defender;
        }

        bool threatened = EnemyMilitaryNear(ai, city.Position, ThreatRadius);

        // 2. Under threat with no Walls yet: fortify.
        var walls = _state.Catalog.Building("walls");
        if (threatened && walls != null
            && !city.Buildings.Contains("walls")
            && TechAllows(civ, walls.RequiredTech))
            return "building:walls";

        int cities   = _state.Cities.Count(c => c.Owner == ai);
        int settlers = CountOwn(ai, "found_city");

        // 3. Expand while below target and safe (don't pump Settlers into a war).
        if (!threatened && cities + settlers < ExpansionTarget
            && _state.Catalog.Unit("settler") != null)
            return "unit:settler";

        // 4. Keep roughly one Worker per city to develop tiles.
        int workers = CountOwn(ai, "build_improvement");
        if (workers < cities && _state.Catalog.Unit("worker") != null)
            return "unit:worker";

        // 5. Safe and already fielding an army (a garrison plus a field unit per
        //    city): round out the economy with the cheapest unowned yield
        //    building, so the AI doesn't fall behind on food/science/gold.
        int military = _state.Units.Count(u => u.Owner == ai && u.Data.Attack > 0);
        if (!threatened && military >= cities * 2)
        {
            var building = CheapestEconomyBuilding(civ, city);
            if (building != null) return building;
        }

        // 6. Coastal cities build naval units when sailing/navigation is researched.
        //    Always secure at least one transport for cross-continent projection before
        //    massing combat ships.
        if (IsCityCoastal(city))
        {
            bool hasTransport = _state.Units.Any(u => u.Owner == ai && u.Data.CargoCapacity > 0);
            if (!hasTransport)
            {
                var tr = BestBuildableUnit(ai, civ, NavalTransportPrefs);
                if (tr != null) return tr;
            }
            var naval = BestBuildableUnit(ai, civ, NavalCombatPrefs)
                     ?? BestBuildableUnit(ai, civ, NavalTransportPrefs);
            if (naval != null) return naval;
        }

        // 7. Otherwise build offensive strength (fall back to a defender).
        return BestBuildableUnit(ai, civ, AttackerPrefs)
            ?? BestBuildableUnit(ai, civ, DefenderPrefs);
    }

    // Cheapest tech-available building the city lacks that carries any yield
    // (food/production/gold/science/culture). Pure-effect buildings (Walls,
    // Barracks) are handled by their own steps, not this economic fallback.
    private string? CheapestEconomyBuilding(Civilization civ, City city)
    {
        BuildingData? best = null;
        foreach (var b in _state.Catalog.Buildings)
        {
            if (city.Buildings.Contains(b.Id))   continue;
            if (!TechAllows(civ, b.RequiredTech)) continue;
            var y = b.Yields;
            if (y.Food + y.Production + y.Gold + y.Science + y.Culture <= 0) continue;
            if (best == null || b.ProductionCost < best.ProductionCost) best = b;
        }
        return best == null ? null : $"building:{best.Id}";
    }

    // Set the city's work focus by need: grow small cities, otherwise pump the
    // production of whatever it's building.
    private static void SetFocus(City city)
    {
        var want = city.Population < 3
            ? CityFocus.Food
            : city.ProductionItem != null ? CityFocus.Production : CityFocus.Balanced;
        city.Workforce.Focus = want;
    }

    // First preferred unit id that exists and the civ may build (tech + resource).
    private string? BestBuildableUnit(Player ai, Civilization civ, string[] prefs)
    {
        foreach (var id in prefs)
        {
            var u = _state.Catalog.Unit(id);
            if (u == null) continue;
            if (!TechAllows(civ, u.RequiredTech)) continue;
            if (!ResourceService.Allows(_state, ai, u.RequiredResource)) continue;
            return $"unit:{id}";
        }
        return null;
    }

    private static bool TechAllows(Civilization civ, string? requiredTech)
        => requiredTech == null || civ.ResearchedTechs.Contains(requiredTech);

    private int CountOwn(Player ai, string special)
        => _state.Units.Count(u => u.Owner == ai && u.Data.Special == special);

    // A city is "defended" if a friendly combat unit stands on its tile.
    private bool IsDefended(Player ai, City city)
        => _state.Units.Any(u =>
            u.Owner == ai && u.Data.Attack > 0 && u.Position == city.Position);

    // A city is coastal if its tile or any immediate neighbour is SEA water — a
    // Lake doesn't count (ships built beside a lake could never leave it).
    private bool IsCityCoastal(City city)
        => HexGrid.GetNeighbors(city.Position)
            .Prepend(city.Position)
            .Any(t => _state.Map.Tiles.TryGetValue(t, out var tt) && TerrainYields.IsSeaWater(tt));

    private bool EnemyMilitaryNear(Player ai, Vector2I pos, int radius)
        => _state.Units.Any(u =>
            IsHostile(ai, u.Owner) && u.Data.Attack > 0 && HexGrid.Distance(pos, u.Position) <= radius);

    // ── Per-unit behaviour ───────────────────────────────────────────────────────

    private void Act(Player ai, Unit unit)
    {
        if (unit.Data.Special == "found_city")        { HandleSettler(ai, unit); return; }
        if (unit.Data.Special == "build_improvement") { HandleWorker(ai, unit); return; }
        if (unit.Data.IsNaval)                        { HandleNaval(ai, unit);  return; }
        HandleMilitary(ai, unit);
    }

    private void HandleNaval(Player ai, Unit unit)
    {
        // Transports ferry troops rather than fight: pick up stranded units and
        // land them on enemy shores (see HandleTransport).
        if (unit.Data.CargoCapacity > 0) { HandleTransport(ai, unit); return; }

        // Attack an in-range enemy naval unit when the forecast favours it.
        var target = NearestEnemyInRange(ai, unit);
        if (target != null && WorthAttacking(unit, target))
        {
            _state.TryAttack(unit, target);
            return;
        }

        // Patrol toward the nearest enemy naval unit, or toward a water tile
        // adjacent to an enemy city when no naval opponents exist.
        var goal = NearestNavalTarget(ai, unit.Position);
        if (goal.HasValue) StepToward(unit, goal.Value);
    }

    // A transport carrying troops makes for the nearest enemy shore and disembarks;
    // an empty one sails to collect the nearest friendly unit stranded on a continent
    // with no overland enemy to fight.
    private void HandleTransport(Player ai, Unit transport)
    {
        if (transport.Cargo.Count > 0)
        {
            if (TryAmphibiousLanding(ai, transport)) return;
            var shore = NearestEnemyShore(ai, transport.Position);
            if (shore is { } s) StepToward(transport, s);
            return;
        }

        var pickup = NearestStrandedPickup(ai, transport.Position);
        if (pickup is { } p) StepToward(transport, p);
    }

    // Disembark cargo onto adjacent enemy-continent land, nearest-to-the-enemy first.
    // Only lands on a continent that actually holds an enemy, so troops aren't dumped
    // on an empty or friendly shore. Returns true if at least one unit was put ashore.
    private bool TryAmphibiousLanding(Player ai, Unit transport)
    {
        bool landed = false;
        foreach (var cargo in new List<Unit>(transport.Cargo))
        {
            Vector2I? best = null;
            int       bestEnemyDist = int.MaxValue;
            foreach (var n in HexGrid.GetNeighbors(transport.Position))
            {
                if (!_state.Map.Tiles.TryGetValue(n, out var nt))             continue;
                if (TerrainYields.IsWater(nt) || nt == TerrainType.Mountain)  continue;
                if (_state.Units.Any(u => u.Position == n))                   continue;
                if (_state.Cities.Any(c => c.Position == n && c.Owner != ai)) continue; // UnloadUnit blocks enemy city tiles
                if (!LandmassHasEnemy(ai, LandmassAt(n)))                     continue; // only enemy soil
                int d = NearestEnemyDistance(ai, n);
                if (d < bestEnemyDist) { bestEnemyDist = d; best = n; }
            }
            if (best is { } tile && _state.UnloadUnit(transport, cargo, tile)) landed = true;
            else break; // no open enemy-shore tile left this turn
        }
        return landed;
    }

    private Vector2I? NearestNavalTarget(Player ai, Vector2I from)
    {
        Vector2I? best    = null;
        int       bestDist = int.MaxValue;

        foreach (var other in _state.Units)
        {
            if (!IsHostile(ai, other.Owner) || !other.Data.IsNaval) continue;
            int d = HexGrid.Distance(from, other.Position);
            if (d < bestDist) { best = other.Position; bestDist = d; }
        }

        if (best != null) return best;

        // No enemy ships — advance to a coastal tile adjacent to the nearest enemy city.
        foreach (var city in _state.Cities)
        {
            if (!IsHostile(ai, city.Owner)) continue;
            foreach (var n in HexGrid.GetNeighbors(city.Position))
            {
                if (!_state.Map.Tiles.TryGetValue(n, out var tt) || !TerrainYields.IsSeaWater(tt)) continue;
                int d = HexGrid.Distance(from, n);
                if (d < bestDist) { best = n; bestDist = d; }
            }
        }
        return best;
    }

    private void HandleMilitary(Player ai, Unit unit)
    {
        // 1. Attack an in-range enemy unit when the forecast favours it.
        var target = NearestEnemyInRange(ai, unit);
        if (target != null && WorthAttacking(unit, target))
        {
            _state.TryAttack(unit, target);
            return;
        }

        // 2. Bombard / assault an in-range enemy city if we'll survive the reprisal.
        var cityTarget = EnemyCityInRange(ai, unit);
        if (cityTarget != null && WorthAttackingCity(unit, cityTarget))
        {
            _state.TryAttackCity(unit, cityTarget);
            return;
        }

        // 3. Wounded: break off toward the nearest friendly city to heal.
        if (unit.HP < RetreatHp)
        {
            var refuge = NearestOwnCity(ai, unit.Position);
            if (refuge is { } r && r != unit.Position) StepToward(unit, r);
            return;
        }

        // 4. Already garrisoning a city — hold the tile (units can't stack, so this
        //    one is its sole defender).
        if (_state.Cities.Any(c => c.Owner == ai && c.Position == unit.Position))
            return;

        // 5. Reinforce the nearest threatened, undefended friendly city in range.
        var needy = NearestThreatenedCity(ai, unit.Position);
        if (needy is { } n)
        {
            StepToward(unit, n);
            return;
        }

        // 6. No enemy is reachable overland (they're across water) — head for the
        //    coast and board a transport for an overseas assault.
        var landmass = LandmassAt(unit.Position);
        if (!LandmassHasEnemy(ai, landmass))
        {
            HandleStrandedLandUnit(ai, unit, landmass);
            return;
        }

        // 7. Otherwise advance on the nearest enemy unit or city.
        var goal = NearestEnemyOrCityPosition(ai, unit.Position);
        if (goal.HasValue) StepToward(unit, goal.Value);
    }

    // A land unit with no overland enemy boards an adjacent friendly transport if it
    // can, otherwise stages on the nearest shore so a transport can collect it.
    private void HandleStrandedLandUnit(Player ai, Unit unit, HashSet<Vector2I> landmass)
    {
        var transport = _state.Units.FirstOrDefault(t =>
            t.Owner == ai && t.Data.CargoCapacity > 0 &&
            t.Cargo.Count < t.Data.CargoCapacity &&
            HexGrid.Distance(unit.Position, t.Position) <= 1);
        if (transport != null && _state.LoadUnit(unit, transport)) return;

        var shore = NearestShoreTile(landmass, unit.Position);
        if (shore is { } s && s != unit.Position) StepToward(unit, s);
    }

    // Melee: don't attack if the reprisal would kill us, unless we kill them first;
    // otherwise attack when we'd kill them or win the damage trade. Ranged units
    // take no reprisal, so they always fire. Forecasts use the same effective
    // (faction/veterancy-modified) strengths actual combat resolves with.
    private bool WorthAttacking(Unit unit, Unit target)
    {
        if (unit.Data.Range >= 2) return true; // ranged: no reprisal
        var e = CombatResolver.Expected(
            _state.EffectiveAttack(unit), unit.HP,
            _state.EffectiveDefense(target), target.HP, isRanged: false);
        bool kill = e.DefenderDamage >= target.HP;
        if (e.AttackerDamage >= unit.HP) return false; // suicidal
        return kill || e.DefenderDamage >= e.AttackerDamage;
    }

    private bool WorthAttackingCity(Unit unit, City city)
    {
        bool ranged = unit.Data.Range >= 2;
        if (ranged) return true;
        int defStrength = _state.CityDefenseTotal(city);
        var e = CombatResolver.Expected(
            _state.EffectiveAttack(unit), unit.HP, defStrength, city.HP, isRanged: false);
        return e.AttackerDamage < unit.HP; // survive the reprisal
    }

    // ── Settlers ──────────────────────────────────────────────────────────────

    private void HandleSettler(Player ai, Unit settler)
    {
        // Found right here if it's a legal site (enforces MinCityDistance/terrain).
        if (_state.TryFoundCity(settler, out _) == GameState.FoundCityResult.Success)
            return;

        // Otherwise march toward the best nearby site and settle next turn.
        var site = BestExpansionSite(ai, settler);
        if (site is { } s && s != settler.Position) StepToward(settler, s);
    }

    // Best foundable tile within SiteSearchRadius: legal terrain, far enough from
    // every existing city, scored by surrounding yields and biased toward closer
    // sites so the Settler doesn't trek across the map.
    private Vector2I? BestExpansionSite(Player ai, Unit settler)
    {
        Vector2I? best     = null;
        int       bestScore = int.MinValue;
        foreach (var tile in HexGrid.GetRange(settler.Position, SiteSearchRadius))
        {
            if (!_state.Map.Tiles.TryGetValue(tile, out var terrain)) continue;
            if (!TerrainYields.CanFoundCityOn(terrain))                continue;
            if (_state.MovementCost(tile) == int.MaxValue)             continue;
            if (_state.Cities.Any(c => HexGrid.Distance(c.Position, tile) < _state.EffectiveMinCityDistance(ai)))
                continue;

            int score = SiteScore(tile) - HexGrid.Distance(settler.Position, tile);
            if (score > bestScore) { bestScore = score; best = tile; }
        }
        return best;
    }

    private int SiteScore(Vector2I center)
    {
        int score = 0;
        foreach (var tile in HexGrid.GetRange(center, CityWorkforceService.WorkRadius))
        {
            if (!_state.Map.Tiles.TryGetValue(tile, out var t)) continue;
            score += TerrainYields.Food(t) + TerrainYields.Production(t);
        }
        return score;
    }

    // ── Workers ───────────────────────────────────────────────────────────────

    private void HandleWorker(Player ai, Unit worker)
    {
        if (worker.CurrentTask != null) return; // already building

        // On a tile we control that can take an improvement: start building.
        var here = BestImprovementOn(ai, worker.Position);
        if (here != ImprovementType.None
            && CityWorkforceService.ControllingCity(_state, worker.Position)?.Owner == ai)
        {
            worker.Fortified         = false;
            worker.SleepUntilHealed  = false;
            worker.CurrentTask       = new ImprovementTask(
                worker.Position, here, ImprovementService.BuildTurns(here));
            worker.MovementRemaining = 0;
            return;
        }

        // Otherwise walk to the nearest controlled tile that needs improving.
        var goal = NearestImprovableTile(ai, worker.Position);
        if (goal is { } g && g != worker.Position) StepToward(worker, g);
    }

    // Best non-Road improvement the AI can build on a tile (Road is movement-only,
    // so it's skipped here in favour of yield improvements).
    private ImprovementType BestImprovementOn(Player ai, Vector2I tile)
    {
        foreach (var (type, _) in ImprovementService.BuildableOptions(_state, ai, tile))
            if (type != ImprovementType.Road)
                return type;
        return ImprovementType.None;
    }

    private Vector2I? NearestImprovableTile(Player ai, Vector2I from)
    {
        Vector2I? best     = null;
        int       bestDist = int.MaxValue;
        foreach (var city in _state.Cities.Where(c => c.Owner == ai))
        foreach (var tile in HexGrid.GetRange(city.Position, city.BorderRadius))
        {
            if (tile == city.Position)                                  continue;
            if (CityWorkforceService.ControllingCity(_state, tile)?.Owner != ai) continue;
            if (BestImprovementOn(ai, tile) == ImprovementType.None)    continue;
            if (_state.Units.Any(u => u.Position == tile))              continue; // keep clear
            int d = HexGrid.Distance(from, tile);
            if (d < bestDist) { bestDist = d; best = tile; }
        }
        return best;
    }

    // ── Targeting helpers ────────────────────────────────────────────────────────

    private Unit? NearestEnemyInRange(Player ai, Unit unit)
    {
        if (unit.Data.Attack <= 0) return null;

        Unit? best     = null;
        int   bestDist = int.MaxValue;
        foreach (var other in _state.Units)
        {
            if (other.Owner == ai || !_state.Diplomacy.CanAttack(ai.Id, other.Owner.Id)) continue;
            int d = HexGrid.Distance(unit.Position, other.Position);
            if (d <= 0 || d > unit.Data.Range) continue;
            if (d < bestDist) { best = other; bestDist = d; }
        }
        return best;
    }

    private City? EnemyCityInRange(Player ai, Unit unit)
    {
        if (unit.Data.Attack <= 0) return null;

        City? best     = null;
        int   bestDist = int.MaxValue;
        foreach (var city in _state.Cities)
        {
            if (city.Owner == ai || city.HP <= 0 || !_state.Diplomacy.CanAttack(ai.Id, city.Owner.Id)) continue;
            int d = HexGrid.Distance(unit.Position, city.Position);
            if (d <= 0 || d > unit.Data.Range) continue;
            if (d < bestDist) { best = city; bestDist = d; }
        }
        return best;
    }

    private Vector2I? NearestOwnCity(Player ai, Vector2I from)
    {
        Vector2I? best     = null;
        int       bestDist = int.MaxValue;
        foreach (var city in _state.Cities)
        {
            if (city.Owner != ai) continue;
            int d = HexGrid.Distance(from, city.Position);
            if (d < bestDist) { bestDist = d; best = city.Position; }
        }
        return best;
    }

    private Vector2I? NearestThreatenedCity(Player ai, Vector2I from)
    {
        Vector2I? best     = null;
        int       bestDist = int.MaxValue;
        foreach (var city in _state.Cities)
        {
            if (city.Owner != ai)              continue;
            if (IsDefended(ai, city))          continue;
            if (!EnemyMilitaryNear(ai, city.Position, ThreatRadius)) continue;
            int d = HexGrid.Distance(from, city.Position);
            if (d == 0 || d > GarrisonRange)   continue;
            if (d < bestDist) { bestDist = d; best = city.Position; }
        }
        return best;
    }

    // "Enemy" everywhere below means a player this AI may actually attack —
    // peaceful/allied players are never marched on, threatened, or invaded.
    private bool IsHostile(Player ai, Player other) => _state.Diplomacy.CanAttack(ai.Id, other.Id);

    private Vector2I? NearestEnemyOrCityPosition(Player ai, Vector2I from)
    {
        Vector2I? best     = null;
        int       bestDist = int.MaxValue;

        foreach (var other in _state.Units)
        {
            if (!IsHostile(ai, other.Owner)) continue;
            int d = HexGrid.Distance(from, other.Position);
            if (d < bestDist) { best = other.Position; bestDist = d; }
        }
        foreach (var city in _state.Cities)
        {
            if (!IsHostile(ai, city.Owner)) continue;
            int d = HexGrid.Distance(from, city.Position);
            if (d < bestDist) { best = city.Position; bestDist = d; }
        }
        return best;
    }

    // ── Cross-continent helpers (Phase 13) ───────────────────────────────────────

    // The connected landmass containing `tile`, memoized for the turn so each
    // continent is flood-filled at most once (terrain doesn't change mid-turn).
    private HashSet<Vector2I> LandmassAt(Vector2I tile)
    {
        foreach (var lm in _turnLandmasses)
            if (lm.Contains(tile)) return lm;
        var fresh = _state.GetConnectedLandmass(tile);
        _turnLandmasses.Add(fresh);
        return fresh;
    }

    private bool LandmassHasEnemy(Player ai, HashSet<Vector2I> landmass)
    {
        foreach (var u in _state.Units)
            if (IsHostile(ai, u.Owner) && landmass.Contains(u.Position)) return true;
        foreach (var c in _state.Cities)
            if (IsHostile(ai, c.Owner) && landmass.Contains(c.Position)) return true;
        return false;
    }

    private int NearestEnemyDistance(Player ai, Vector2I tile)
    {
        int best = int.MaxValue;
        foreach (var u in _state.Units)
            if (IsHostile(ai, u.Owner)) best = System.Math.Min(best, HexGrid.Distance(tile, u.Position));
        foreach (var c in _state.Cities)
            if (IsHostile(ai, c.Owner)) best = System.Math.Min(best, HexGrid.Distance(tile, c.Position));
        return best;
    }

    // Nearest land tile on `landmass` that borders water — an embarkation staging point.
    private Vector2I? NearestShoreTile(HashSet<Vector2I> landmass, Vector2I from)
    {
        Vector2I? best     = null;
        int       bestDist = int.MaxValue;
        foreach (var tile in landmass)
        {
            if (!BordersWater(tile)) continue;
            int d = HexGrid.Distance(from, tile);
            if (d < bestDist) { bestDist = d; best = tile; }
        }
        return best;
    }

    // Nearest water tile bordering an enemy-held continent — a transport's approach
    // to drop its troops (the landing itself happens in TryAmphibiousLanding once the
    // transport is adjacent to enemy soil). Targets the coastline, not the cities, so
    // it still works when the enemy's cities sit inland.
    private Vector2I? NearestEnemyShore(Player ai, Vector2I from)
    {
        Vector2I? best     = null;
        int       bestDist = int.MaxValue;
        foreach (var landmass in EnemyLandmasses(ai))
        foreach (var tile in landmass)
        {
            foreach (var n in HexGrid.GetNeighbors(tile))
            {
                if (!_state.Map.Tiles.TryGetValue(n, out var nt) || !TerrainYields.IsSeaWater(nt)) continue;
                int d = HexGrid.Distance(from, n);
                if (d < bestDist) { bestDist = d; best = n; }
            }
        }
        return best;
    }

    // The distinct continents that hold an enemy city or land unit (deduped by the
    // memoized landmass instance), so a transport can steer toward enemy soil.
    private IEnumerable<HashSet<Vector2I>> EnemyLandmasses(Player ai)
    {
        var seen = new List<HashSet<Vector2I>>();
        void Consider(Vector2I pos)
        {
            var lm = LandmassAt(pos);
            if (lm.Count > 0 && !seen.Contains(lm)) seen.Add(lm);
        }
        foreach (var c in _state.Cities)
            if (IsHostile(ai, c.Owner)) Consider(c.Position);
        foreach (var u in _state.Units)
            if (IsHostile(ai, u.Owner) && !u.Data.IsNaval) Consider(u.Position);
        return seen;
    }

    // Nearest water tile beside a friendly military unit stranded with no overland
    // enemy — where an empty transport should sail to pick it up.
    private Vector2I? NearestStrandedPickup(Player ai, Vector2I from)
    {
        Vector2I? best     = null;
        int       bestDist = int.MaxValue;
        foreach (var u in _state.Units)
        {
            if (u.Owner != ai || u.Data.IsNaval || u.Data.Attack <= 0) continue;
            if (LandmassHasEnemy(ai, LandmassAt(u.Position)))          continue; // can fight overland
            foreach (var n in HexGrid.GetNeighbors(u.Position))
            {
                if (!_state.Map.Tiles.TryGetValue(n, out var nt) || !TerrainYields.IsSeaWater(nt)) continue;
                int d = HexGrid.Distance(from, n);
                if (d < bestDist) { bestDist = d; best = n; }
            }
        }
        return best;
    }

    // Sea water only: this feeds embarkation staging, where a transport must be
    // able to reach the shore — lakeside tiles don't qualify.
    private bool BordersWater(Vector2I tile)
    {
        foreach (var n in HexGrid.GetNeighbors(tile))
            if (_state.Map.Tiles.TryGetValue(n, out var nt) && TerrainYields.IsSeaWater(nt)) return true;
        return false;
    }

    // ── Movement ──────────────────────────────────────────────────────────────

    private void StepToward(Unit unit, Vector2I goal)
    {
        if (unit.MovementRemaining <= 0) return;
        if (unit.Position == goal)       return;

        var path = HexGrid.FindPath(unit.Position, goal, t => _state.MovementCost(t, unit));
        if (path.Count < 2) return;

        int      budget = unit.MovementRemaining;
        Vector2I last   = unit.Position;
        int      spent  = 0;

        for (int i = 1; i < path.Count; i++)
        {
            int cost = _state.MovementCost(path[i], unit);
            if (cost == int.MaxValue) break;

            // Don't walk onto another unit's tile (enemies blocked, friendly stacking forbidden).
            if (_state.Units.Any(u => u != unit && u.Position == path[i])) break;

            // Don't enter an enemy city unless it's conquerable and we can capture it.
            var cityHere = _state.Cities.Find(c => c.Position == path[i] && c.Owner != unit.Owner);
            if (cityHere != null && !(cityHere.IsConquerable && GameSession.CanCapture(unit))) break;

            if (spent + cost > budget) break;
            spent += cost;
            last   = path[i];
        }

        if (last == unit.Position) return;

        unit.Position          = last;
        unit.MovementRemaining = Mathf.Max(0, unit.MovementRemaining - spent);
        unit.ActedThisTurn     = true;

        var captured = _state.Cities.Find(
            c => c.Position == last && c.Owner != unit.Owner && c.IsConquerable);
        if (captured != null) _state.CaptureCity(unit, captured);
    }
}
