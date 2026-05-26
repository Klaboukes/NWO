namespace NWO.Core;

public class TurnManager
{
    public int TurnNumber { get; private set; } = 1;

    public void AdvanceTurn() => TurnNumber++;
}
