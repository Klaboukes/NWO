using Godot;
using NWO.Core;

namespace NWO.Entities;

// Runtime state for one unit instance. Holds mutable gameplay values only.
// Visual/animation position is owned by the renderer (WorldMap), not here.
public class Unit : IEndTurnItem
{
    public UnitData Data              { get; }
    public Player   Owner             { get; }
    public Vector2I Position          { get; set; }
    public int      HP                { get; set; } = 100;
    public int      MovementRemaining { get; set; }
    public bool     Fortified         { get; set; }

    public Unit(UnitData data, Player owner, Vector2I position)
    {
        Data              = data;
        Owner             = owner;
        Position          = position;
        MovementRemaining = data.Movement;
    }

    public void ResetForNewTurn()
    {
        if (!Fortified)
            MovementRemaining = Data.Movement;
    }

    // ── IEndTurnItem ─────────────────────────────────────────────────────────

    public bool     NeedsAttention => MovementRemaining > 0 && !Fortified;
    public string   PromptText     => $"{Data.Name} has moves — [Space] Skip  [F] Fortify";
    public Vector2I FocusPosition  => Position;
}
