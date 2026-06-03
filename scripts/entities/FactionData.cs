using System.Collections.Generic;

namespace NWO.Entities;

// Immutable faction definition loaded from data/factions.json. One per faction in
// the fixed v1 roster (6 ideological factions + the Reavers). Faction asymmetry is
// expressed as a flat "modifier bag": scalar multipliers/bonuses read inline by the
// static services, named trait flags for the few structural passives, and a
// base-unit -> variant-unit map for unique units.
//
// Every scalar defaults to its identity value (1.0 / 0 / false), so a faction's JSON
// only declares its deltas, and a player with no faction resolves to FactionData.Neutral
// (see DataCatalog.FactionOf) — every hook stays an unconditional `* mult` / `+ bonus`.
public record FactionData
{
    public string Id   { get; init; } = "";
    public string Name { get; init; } = "";
    public string Tree { get; init; } = ""; // social-policy lineage, display only

    // ── Scalar modifiers (identity defaults) ─────────────────────────────────
    public double CombatStrengthMult   { get; init; } = 1.0; // CombatResolver (via GameState)
    public double GoldMult             { get; init; } = 1.0; // CivEconomyService.GoldPerTurn
    public double ScienceMult          { get; init; } = 1.0; // CivEconomyService.SciencePerTurn
    public double RushBuyDiscount      { get; init; }        // 0..1, CivEconomyService.BuyCost
    public int    SightBonus           { get; init; }        // FogOfWar (via GameState.RecomputeFog)
    public double TerrainCostMult      { get; init; } = 1.0; // GameState.MovementCost(axial, unit)
    public double SettleCostMult       { get; init; } = 1.0; // settler production cost
    public int    MinCityDistanceDelta { get; init; }        // effective settle spacing delta
    public int    CityDefenseBonus     { get; init; }        // capital only, City defense
    public double CityRegenMult        { get; init; } = 1.0; // capital only, City.RegenIfUnharassed
    public int    CapitalProductionBonus { get; init; }      // CityWorkforceService (capital)
    public double XpGainMult           { get; init; } = 1.0; // veterancy speed (Iron Pact)
    public bool   HealInEnemyLand      { get; init; }        // GameState.HealUnit (Iron Pact)

    // ── Structural traits (named flags, resolved at their one seam) ───────────
    // Known traits: "new_city_defender" (Free Settlements), "can_hire_reavers" (Syndicate).
    public HashSet<string> Traits { get; init; } = new();

    // ── Unique units: base unit id -> variant unit id (both live in units.json) ─
    public Dictionary<string, string> UnitVariants { get; init; } = new();

    // Shared all-identity faction for players with no faction id (and legacy saves).
    public static readonly FactionData Neutral = new() { Id = "", Name = "" };
}
