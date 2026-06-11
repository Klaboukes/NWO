namespace NWO.Entities;

// Immutable building definition loaded from data/buildings.json. Never modified
// at runtime. Yields feed city/economy recompute; Effect is a numeric string tag
// parsed by GameState.BuildingEffectSum at its mechanic's seam —
// "new_units_bonus_xp_<n>" (Barracks) and "city_defense_plus_<n>" (Walls).
public record BuildingData
{
    public string         Id             { get; init; } = "";
    public string         Name           { get; init; } = "";
    public int            ProductionCost { get; init; }
    public string?        RequiredTech   { get; init; }
    public BuildingYields Yields         { get; init; } = new();
    public string?        Effect         { get; init; }
}

public record BuildingYields
{
    public int Food       { get; init; }
    public int Production { get; init; }
    public int Gold       { get; init; }
    public int Science    { get; init; }
    public int Culture    { get; init; }
}
