using Godot;

namespace NWO.Core;

// A turn-summary event surfaced in the HUD event log. Text is the message
// shown to the player; Focus, when set, is the map tile the player can click
// to recenter the camera on (e.g. the city that grew, the worker that finished
// an improvement). Research/economy events with no place on the map leave it
// null.
public readonly record struct GameEvent(string Text, Vector2I? Focus = null)
{
    // Implicit lift from a bare message keeps producers that have no tile to
    // point at terse: `notifications.Add("Researched Pottery!")`.
    public static implicit operator GameEvent(string text) => new(text, null);
}
