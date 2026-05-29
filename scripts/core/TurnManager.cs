namespace NWO.Core;

// Tracks the global turn number. Ticks up once per full round (after the last
// player ends their turn) — see GameState.EndPlayerTurn.
public class TurnManager
{
    public int TurnNumber { get; private set; } = 1;

    public void AdvanceTurn() => TurnNumber++;
}
