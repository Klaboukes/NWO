using System.Collections.Generic;
using Godot;
using NWO.Core;

namespace NWO.Entities;

// Runtime state for one city instance. Food/Production yields are recomputed by
// CityWorkforceService from worked tiles + buildings; civ-wide economy (gold,
// science, research) lives on Civilization, not here.
public class City : IEndTurnItem
{
    public const int MaxHP             = 100;
    private const int CityBaseDefense  = 6;
    private const int WallsDefenseBonus = 5;
    private const int RegenPerTurn     = 10;

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

    // Combat state. HP is depleted by attacks and regenerates when not harassed;
    // a city is captured (not destroyed) once HP hits 0 and a melee unit enters.
    public int  HP                { get; set; } = MaxHP;
    public bool AttackedSinceTurn { get; set; }            // gates regen

    public int GrowthThreshold => 15 + 6 * Population;

    // Intrinsic defensive strength. The best friendly garrison unit's defense is
    // added by the combat caller (GameState), which has access to the unit list.
    public int CityDefenseStrength =>
        CityBaseDefense + Population + (Buildings.Contains("walls") ? WallsDefenseBonus : 0);

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
    public void RegenIfUnharassed()
    {
        if (!AttackedSinceTurn && HP < MaxHP)
            HP = System.Math.Min(MaxHP, HP + RegenPerTurn);
        AttackedSinceTurn = false;
    }

    // Returns true if city grew this turn.
    public bool ProcessFood()
    {
        FoodAccumulated += FoodYield - Population;
        if (FoodAccumulated < 0) FoodAccumulated = 0;
        if (FoodAccumulated < GrowthThreshold) return false;
        FoodAccumulated -= GrowthThreshold;
        Population++;
        return true;
    }

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
    public string   PromptText     => $"{Name} needs production — [Space] Skip";
    public Vector2I FocusPosition  => Position;
}
