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
}
