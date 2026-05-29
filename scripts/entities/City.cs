using System.Collections.Generic;
using Godot;
using NWO.Core;

namespace NWO.Entities;

// Runtime state for one city instance. Food/Production yields are recomputed by
// CityWorkforceService from worked tiles + buildings; civ-wide economy (gold,
// science, research) lives on Civilization, not here.
public class City : IEndTurnItem
{
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

    public int GrowthThreshold => 15 + 6 * Population;

    public City(string name, Player owner, Vector2I position)
    {
        Name     = name;
        Owner    = owner;
        Position = position;
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
