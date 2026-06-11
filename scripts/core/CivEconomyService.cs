using System.Collections.Generic;
using System.Linq;
using Godot;
using NWO.Entities;
using NWO.Map;

namespace NWO.Core;

// Per-turn civ-wide economy + research + emergency disband. Called once per
// player at end of turn after the city/unit pass in GameState.EndPlayerTurn.
//
// Mirrors CityWorkforceService in shape: pure static service over GameState.
public static class CivEconomyService
{
    // Each civ founds with a small treasury so the first few turns don't trip
    // the disband path before any income exists.
    public const int StartingTreasury = 50;

    // Civ-5-style maintenance grace: the first FreeUnitMaintenance gold of
    // unit upkeep is waived, so a 1-warrior + 1-settler opening costs 0/turn.
    public const int FreeUnitMaintenance = 2;

    public static void ProcessEndOfTurn(GameState state, Player player, List<GameEvent> notifications)
    {
        var civ = state.Civ(player);

        int sciencePerTurn = SciencePerTurn(state, player);
        int goldPerTurn    = GoldPerTurn(state, player);

        civ.Treasury           += goldPerTurn;
        civ.ScienceAccumulated += sciencePerTurn;

        AccumulateCulture(state, player, civ, notifications);
        AdvanceResearch(state, player, civ, notifications);
        EnforceTreasury(state, civ, notifications);
    }

    // Per-city culture banks toward border expansion; the same amount also
    // accumulates on the civ (lifetime total, feeds civ score — ScoreService).
    private static void AccumulateCulture(GameState state, Player player, Civilization civ, List<GameEvent> notifications)
    {
        foreach (var city in state.Cities)
        {
            if (city.Owner != player) continue;
            int culture = CityCulturePerTurn(state, city);
            civ.CultureAccumulated += culture;
            if (!city.AddCulture(culture)) continue;

            notifications.Add(new GameEvent($"{city.Name}'s borders expanded!", city.Position));
            CityWorkforceService.Recompute(state, city);
            // A wider ring can wrest tiles from a neighbour's overlap zone.
            foreach (var other in state.Cities)
                if (other != city && HexGrid.Distance(other.Position, city.Position) <= City.MaxBorderRadius * 2)
                    CityWorkforceService.Recompute(state, other);
        }
    }

    public static int SciencePerTurn(GameState state, Player player)
    {
        int science = 0;
        foreach (var city in state.Cities)
        {
            if (city.Owner != player) continue;
            // Base: 1 science from the city center (Civ 5 default-ish).
            science += 1;
            foreach (var buildingId in city.Buildings)
            {
                var bdef = state.Catalog.Building(buildingId);
                if (bdef != null) science += bdef.Yields.Science;
            }
        }
        // The Cognate reaches each tech tier sooner.
        return (int)System.Math.Round(science * state.Catalog.FactionOf(player).ScienceMult);
    }

    // Every city radiates a base 1 culture per turn (Civ 5's palace/city base),
    // plus its buildings' culture yields (the Monument).
    public const int CityBaseCulture = 1;

    public static int CityCulturePerTurn(GameState state, City city)
    {
        int culture = CityBaseCulture;
        foreach (var buildingId in city.Buildings)
        {
            var bdef = state.Catalog.Building(buildingId);
            if (bdef != null) culture += bdef.Yields.Culture;
        }
        return culture;
    }

    // Civ-wide culture per turn: the sum over its cities. Drives border growth
    // per city and the civ's lifetime total (score) — see AccumulateCulture.
    public static int CulturePerTurn(GameState state, Player player)
    {
        int culture = 0;
        foreach (var city in state.Cities)
            if (city.Owner == player)
                culture += CityCulturePerTurn(state, city);
        return culture;
    }

    public static int GoldPerTurn(GameState state, Player player)
    {
        int income = 0;
        foreach (var city in state.Cities)
        {
            if (city.Owner != player) continue;
            // Worked-tile trade income (terrain + revealed luxury resources).
            foreach (var tile in city.Workforce.Assigned)
            {
                if (state.Map.Tiles.TryGetValue(tile, out var terrain))
                    income += TerrainYields.Gold(terrain);

                var res = state.Map.ResourceAt(tile);
                if (res != ResourceType.None && ResourceService.IsRevealed(state, player, res))
                    income += ResourceYields.Gold(res);
            }
            // Building income.
            foreach (var buildingId in city.Buildings)
            {
                var bdef = state.Catalog.Building(buildingId);
                if (bdef != null) income += bdef.Yields.Gold;
            }
        }
        // The Syndicate's high gold income scales gross trade/building income
        // (maintenance is unaffected — upkeep still costs full price).
        income = (int)System.Math.Round(income * state.Catalog.FactionOf(player).GoldMult);

        int paid = System.Math.Max(0, TotalMaintenance(state, player) - FreeUnitMaintenance);
        return income - paid;
    }

    // Gross unit upkeep for a player, before the free-maintenance allowance. The
    // net cost is Max(0, this − FreeUnitMaintenance); see GoldPerTurn / EnforceTreasury.
    public static int TotalMaintenance(GameState state, Player player)
    {
        int maintenance = 0;
        foreach (var unit in state.Units)
            if (unit.Owner == player) maintenance += unit.Data.MaintenanceGold;
        return maintenance;
    }

    // Gold to rush-buy the remaining production of a city's current item.
    public const int GoldPerProduction = 4;

    public static int BuyCost(GameState state, City city)
    {
        if (city.ProductionItem == null) return 0;
        int cost      = state.EffectiveItemCost(city.Owner, city.ProductionItem);
        int remaining = System.Math.Max(0, cost - city.ProductionProgress);
        if (remaining == 0) return 0;
        // The Syndicate rush-buys cheaply (RushBuyDiscount in 0..1).
        double discount = state.Catalog.FactionOf(city.Owner).RushBuyDiscount;
        return System.Math.Max(1, (int)System.Math.Round(remaining * GoldPerProduction * (1.0 - discount)));
    }

    // Gold to hire a Reaver mercenary (Phase 10.6). Only the Syndicate (the
    // "can_hire_reavers" trait) may do this — its signature gold-for-force play.
    public const int HireReaverCost = 60;

    public static bool CanHireReaver(GameState state, Player player)
        => state.Catalog.FactionOf(player).Traits.Contains("can_hire_reavers")
           && state.Civ(player).Treasury >= HireReaverCost;

    // Spends gold to spawn a hired mercenary on `pos` for the buyer. Returns the new
    // unit, or null if the buyer can't hire (wrong faction or insufficient gold). The
    // unit is the buyer's warrior variant (the Syndicate's Mercenary).
    public static Unit? HireReaver(GameState state, Player player, Vector2I pos)
    {
        if (!CanHireReaver(state, player)) return null;
        var def = state.Catalog.Unit(state.Catalog.ResolveUnitForFaction("warrior", player));
        if (def == null) return null;

        state.Civ(player).Treasury -= HireReaverCost;
        var unit = new Unit(def, player, pos);
        state.Units.Add(unit);
        return unit;
    }

    public enum SetResearchResult { Ok, AlreadyResearched, MissingPrereq, UnknownTech }

    // Validates and assigns a new research target. Carries over any banked
    // science (Civ-5-style: switching projects doesn't lose accumulated beakers).
    public static SetResearchResult SetResearch(GameState state, Player player, string techId)
    {
        var tech = state.Catalog.Tech(techId);
        if (tech == null) return SetResearchResult.UnknownTech;

        var civ = state.Civ(player);
        if (civ.ResearchedTechs.Contains(techId)) return SetResearchResult.AlreadyResearched;
        foreach (var prereq in tech.Prerequisites)
            if (!civ.ResearchedTechs.Contains(prereq))
                return SetResearchResult.MissingPrereq;

        civ.CurrentResearch = techId;
        return SetResearchResult.Ok;
    }

    private static void AdvanceResearch(GameState state, Player player, Civilization civ, List<GameEvent> notifications)
    {
        if (civ.CurrentResearch == null) return;
        var tech = state.Catalog.Tech(civ.CurrentResearch);
        if (tech == null) { civ.CurrentResearch = null; return; }
        if (civ.ScienceAccumulated < tech.ScienceCost) return;

        civ.ScienceAccumulated -= tech.ScienceCost;
        civ.ResearchedTechs.Add(tech.Id);
        civ.CurrentResearch = null;
        notifications.Add(new GameEvent($"Researched {tech.Name}!", Owner: player));
    }

    // While treasury is negative, disband the player's cheapest-production-cost
    // unit. Ties broken by lowest current HP. Each disband refunds the upkeep it
    // actually frees this turn (the marginal cost above the free-maintenance
    // allowance). Loop terminates when the treasury is non-negative, the player has
    // no units left, or shedding the cheapest unit would free no upkeep — disbanding
    // further can't help the deficit, so we stop rather than scrap free units.
    private static void EnforceTreasury(GameState state, Civilization civ, List<GameEvent> notifications)
    {
        while (civ.Treasury < 0)
        {
            var victim = state.Units
                .Where(u => u.Owner == civ.Owner)
                .OrderBy(u => u.Data.ProductionCost)
                .ThenBy(u => u.HP)
                .FirstOrDefault();
            if (victim == null) return;

            // Upkeep freed by removing this unit: the drop in billed maintenance
            // (Max(0, total − free)) before vs after. Zero when the unit sat within
            // the free allowance, in which case disbanding it wouldn't help.
            int maintBefore = TotalMaintenance(state, civ.Owner);
            int relief = System.Math.Max(0, maintBefore - FreeUnitMaintenance)
                       - System.Math.Max(0, maintBefore - victim.Data.MaintenanceGold - FreeUnitMaintenance);
            if (relief <= 0) return;

            var victimPos = victim.Position;
            state.Units.Remove(victim);
            victim.Cargo.Clear(); // destroy cargo so it doesn't become a ghost if victim was a transport
            notifications.Add(new GameEvent($"Treasury depleted — disbanded {victim.Data.Name}.", victimPos));

            // Maintenance is billed up-front for the turn; the freed upkeep is
            // credited back so one shortfall doesn't cascade into extra losses.
            civ.Treasury += relief;
        }
    }
}
