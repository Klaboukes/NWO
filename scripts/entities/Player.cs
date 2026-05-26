using Godot;

namespace NWO.Entities;

public record Player
{
    public int    Id      { get; init; }
    public string Name    { get; init; } = "";
    public bool   IsHuman { get; init; }
    public Color  Color   { get; init; } = Colors.White;
}
