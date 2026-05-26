namespace NWO.Core;

// Something the human player must address before ending their turn:
// a unit with moves remaining, a city without production, a research prompt, etc.
public interface IEndTurnItem
{
    // True if this item still needs attention. Skipped/satisfied items return false.
    bool NeedsAttention { get; }

    // Short message shown to the player when this item is the queue head.
    string PromptText { get; }

    // Where to center the camera when surfacing this item (axial coords).
    Godot.Vector2I FocusPosition { get; }
}
