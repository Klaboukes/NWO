namespace NWO.Entities;

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
