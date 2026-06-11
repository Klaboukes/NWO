using System.Collections.Generic;
using Godot;
using NWO.Core;

namespace NWO.Entities;

// Runtime state for one city instance. Food/Production yields are recomputed by
// CityWorkforceService from worked tiles + buildings; civ-wide economy (gold,
// science, research) lives on Civilization, not here.
// What ProcessFood did to the city this turn.
public enum CityFoodResult { None, Grew, Starved }

public class City : IEndTurnItem
{
    public const int MaxHP             = 100;
    private const int CityBaseDefense  = 6;
    private const int RegenPerTurn     = 10;

    // Culture borders: every city starts controlling the initial ring and banks
    // culture toward expanding it (CityWorkforceService.ControllingCity reads
    // BorderRadius). Culture per turn is computed by CivEconomyService.
    public const int InitialBorderRadius = 2;
    public const int MaxBorderRadius     = 3;

    public string         Name               { get; set; }
    public Player         Owner              { get; set; }
    public Vector2I       Position           { get; set; }
    public bool           IsCapital          { get; set; } // first city a player founds; kept on capture
    public int            Population         { get; set; } = 1;
    public float          FoodAccumulated    { get; set; }
    public int            ProductionProgress { get; set; }
    public string?        ProductionItem     { get; set; } // "unit:warrior" or "building:monument"
    public int            FoodYield          { get; set; }
    public int            ProductionYield    { get; set; }
    public HashSet<string> Buildings         { get; }     = new();
    public CityWorkforce  Workforce          { get; }     = new();

    // Culture banked toward the next border expansion (spent on each ring).
    public int CultureAccumulated { get; set; }
    public int BorderRadius       { get; set; } = InitialBorderRadius;

    // Combat state. HP is depleted by attacks and regenerates when not harassed;
    // a city is captured (not destroyed) once HP hits 0 and a melee unit enters.
    public int  HP                { get; set; } = MaxHP;
    public bool AttackedSinceTurn { get; set; }            // gates regen

    public int GrowthThreshold => 15 + 6 * Population;

    // Intrinsic defensive strength. Garrison and defensive-building bonuses
    // (effect tag "city_defense_plus_<n>", e.g. Walls) are added by the combat
    // caller — GameState.CityDefenseTotal — which holds the unit list and catalog.
    public int CityDefenseStrength => CityBaseDefense + Population;

    // True once HP is exhausted — a melee enemy may now move in to capture it.
    public bool IsConquerable => HP <= 0;

    public City(string name, Player owner, Vector2I position)
    {
        Name     = name;
        Owner    = owner;
        Position = position;
    }

    // End-of-owner-turn regen: heal unless the city was attacked since the owner's
    // previous turn. Clears the flag so attacks during the coming enemy turns are
    // what gate the next regen.
    // regenMult scales the heal for factions whose capital regenerates faster
    // (Dominion); the caller passes 1.0 for ordinary cities.
    public void RegenIfUnharassed(double regenMult = 1.0)
    {
        if (!AttackedSinceTurn && HP < MaxHP)
            HP = System.Math.Min(MaxHP, HP + (int)System.Math.Round(RegenPerTurn * regenMult));
        AttackedSinceTurn = false;
    }

    public CityFoodResult ProcessFood()
    {
        FoodAccumulated += FoodYield - Population;
        if (FoodAccumulated < 0)
        {
            // Starvation (Civ 5-style): a deficit with an empty basket costs a
            // citizen, so blockades and pillaged farms have real consequences.
            FoodAccumulated = 0;
            if (Population <= 1) return CityFoodResult.None; // cities never starve away entirely
            Population--;
            return CityFoodResult.Starved;
        }
        if (FoodAccumulated < GrowthThreshold) return CityFoodResult.None;
        FoodAccumulated -= GrowthThreshold;
        Population++;
        return CityFoodResult.Grew;
    }

    // Banks culture toward border growth. Returns true when the border ring
    // expanded this call (the cost is spent, leftover culture keeps banking).
    public bool AddCulture(int amount)
    {
        CultureAccumulated += amount;
        bool expanded = false;
        while (BorderRadius < MaxBorderRadius && CultureAccumulated >= NextBorderCost)
        {
            CultureAccumulated -= NextBorderCost;
            BorderRadius++;
            expanded = true;
        }
        return expanded;
    }

    // Culture needed for the next ring; grows with each expansion.
    public int NextBorderCost => 30 * (BorderRadius - 1);

    // Adds one turn of production. Returns completed item id when threshold is reached.
    public string? AdvanceProduction(int itemCost)
    {
        if (ProductionItem == null) return null;
        ProductionProgress += ProductionYield;
        if (ProductionProgress < itemCost) return null;
        ProductionProgress -= itemCost;
        var done      = ProductionItem;
        ProductionItem = null;
        return done;
    }

    // ── IEndTurnItem ─────────────────────────────────────────────────────────

    public bool     NeedsAttention => ProductionItem == null;
    public Vector2I FocusPosition  => Position;
}
