namespace NWO.Entities;

// Immutable type definition loaded from data/units.json. Never modified at runtime.
public record UnitData
{
    public string  Id               { get; init; } = "";
    public string  Name             { get; init; } = "";
    public int     ProductionCost   { get; init; }
    public int     Attack           { get; init; }
    public int     Defense          { get; init; }
    public int     Movement         { get; init; }
    public int     Range            { get; init; }
    public int     Sight            { get; init; }
    public int     MaintenanceGold  { get; init; }
    public string? RequiredTech     { get; init; }
    public string? RequiredResource { get; init; }
    public string? Special          { get; init; }

    // Scout trait: every passable tile costs 1 movement regardless of terrain /
    // rough features (still can't enter genuinely impassable tiles). See
    // GameState.MovementCost(axial, unit).
    public bool    IgnoresTerrainCost { get; init; }

    // Whether this unit may capture an enemy city (melee units only — see
    // GameSession.CanCapture). Defaults true; the Scout opts out. Absent from
    // JSON keeps the default, so existing units need no change.
    public bool    CanCaptureCities   { get; init; } = true;
}
