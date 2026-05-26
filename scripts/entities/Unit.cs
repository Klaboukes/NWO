using Godot;

namespace NWO.Entities;

// Runtime state for one unit instance. Holds mutable gameplay values only.
// Visual/animation position is owned by the renderer (WorldMap), not here.
public class Unit
{
    public UnitData Data              { get; }
    public Vector2I Position          { get; set; }
    public int      HP                { get; set; } = 100;
    public int      MovementRemaining { get; set; }
    public bool     Fortified         { get; set; }

    public Unit(UnitData data, Vector2I position)
    {
        Data              = data;
        Position          = position;
        MovementRemaining = data.Movement;
    }

    public void ResetForNewTurn()
    {
        if (!Fortified)
            MovementRemaining = Data.Movement;
    }
}
