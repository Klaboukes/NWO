using System.Collections.Generic;
using System.Linq;
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

        AdvanceResearch(state, civ, notifications);
        EnforceTreasury(state, civ, notifications);
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
        return science;
    }

    public static int GoldPerTurn(GameState state, Player player)
    {
        int income = 0;
        foreach (var city in state.Cities)
        {
            if (city.Owner != player) continue;
            // Worked-tile trade income.
            foreach (var tile in city.Workforce.Assigned)
                if (state.Map.Tiles.TryGetValue(tile, out var terrain))
                    income += TerrainYields.Gold(terrain);
            // Building income.
            foreach (var buildingId in city.Buildings)
            {
                var bdef = state.Catalog.Building(buildingId);
                if (bdef != null) income += bdef.Yields.Gold;
            }
        }
        int maintenance = 0;
        foreach (var unit in state.Units)
        {
            if (unit.Owner != player) continue;
            maintenance += unit.Data.MaintenanceGold;
        }
        int paid = System.Math.Max(0, maintenance - FreeUnitMaintenance);
        return income - paid;
    }

    // Gold to rush-buy the remaining production of a city's current item.
    public const int GoldPerProduction = 4;

    public static int BuyCost(GameState state, City city)
    {
        if (city.ProductionItem == null) return 0;
        int cost      = state.Catalog.ItemCost(city.ProductionItem);
        int remaining = System.Math.Max(0, cost - city.ProductionProgress);
        return remaining * GoldPerProduction;
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

    private static void AdvanceResearch(GameState state, Civilization civ, List<GameEvent> notifications)
    {
        if (civ.CurrentResearch == null) return;
        var tech = state.Catalog.Tech(civ.CurrentResearch);
        if (tech == null) { civ.CurrentResearch = null; return; }
        if (civ.ScienceAccumulated < tech.ScienceCost) return;

        civ.ScienceAccumulated -= tech.ScienceCost;
        civ.ResearchedTechs.Add(tech.Id);
        civ.CurrentResearch = null;
        notifications.Add($"Researched {tech.Name}!");
    }

    // While treasury is negative, disband the player's cheapest-production-cost
    // unit. Ties broken by lowest current HP. Loop terminates when treasury
    // is non-negative or the player has no units left.
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

            var victimPos = victim.Position;
            state.Units.Remove(victim);
            // Disbanding refunds nothing — gold deficit persists until income
            // catches up. Notification surfaces the loss to the player.
            notifications.Add(new GameEvent($"Treasury depleted — disbanded {victim.Data.Name}.", victimPos));

            // Maintenance is paid up-front for the turn; removing the unit gives
            // its maintenance back as a small relief so the player doesn't lose
            // multiple units to one shortfall.
            civ.Treasury += victim.Data.MaintenanceGold;
        }
    }
}
