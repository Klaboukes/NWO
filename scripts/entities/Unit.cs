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
    public bool     ActedThisTurn     { get; set; } // set on move/attack; gates end-of-turn healing
    public bool     SkippedThisTurn   { get; set; } // [Space] skip — done this turn, but still heals
    public bool     SleepUntilHealed  { get; set; } // [H] fortify until HP is full, then auto-wake

    public const int MaxHP = 100;

    public Unit(UnitData data, Player owner, Vector2I position)
    {
        Data              = data;
        Owner             = owner;
        Position          = position;
        MovementRemaining = data.Movement;
    }

    public void ResetForNewTurn()
    {
        ActedThisTurn   = false;
        SkippedThisTurn = false; // a one-turn pass; standing orders (Fortify/Sleep) persist
        if (!Fortified)
            MovementRemaining = Data.Movement;
    }

    // ── IEndTurnItem ─────────────────────────────────────────────────────────

    // A unit needs orders if it still has moves and hasn't been parked this turn
    // (fortified, sleeping-until-healed, or skipped).
    public bool     NeedsAttention => MovementRemaining > 0 && !Fortified && !SkippedThisTurn;
    public string   PromptText     => $"{Data.Name} has moves — [Space] Skip  [F] Fortify  [H] Heal";
    public Vector2I FocusPosition  => Position;
}
