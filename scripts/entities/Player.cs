using Godot;

namespace NWO.Entities;

// Player identity, set once at game start. Mutable civ-wide state (treasury,
// research, etc.) lives on the matching Civilization, looked up via GameState.Civ.
public record Player
{
    public int    Id      { get; init; }
    public string Name    { get; init; } = "";
    public bool   IsHuman { get; init; }
    public Color  Color   { get; init; } = Colors.White;

    // Faction identity (id into data/factions.json). Null = no faction / neutral —
    // resolves to FactionData.Neutral via DataCatalog.FactionOf (legacy saves, untyped AI).
    public string? FactionId { get; init; }
}
