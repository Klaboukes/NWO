using Godot;

namespace NWO.Core;

// Classifies a turn-summary event so the HUD can apply per-viewer display rules
// (e.g. don't show enemy cities growing). Most events are Generic.
public enum GameEventKind { Generic, CityGrew, CityProduced }

// A turn-summary event surfaced in the HUD event log. Text is the message
// shown to the player; Focus, when set, is the map tile the player can click
// to recenter the camera on (e.g. the city that grew, the worker that finished
// an improvement). Research/economy events with no place on the map leave it
// null. Kind lets the consumer filter (e.g. hide enemy city growth).
public readonly record struct GameEvent(
    string Text,
    Vector2I? Focus = null,
    GameEventKind Kind = GameEventKind.Generic)
{
    // Implicit lift from a bare message keeps producers that have no tile to
    // point at terse: `notifications.Add("Researched Pottery!")`.
    public static implicit operator GameEvent(string text) => new(text, null);
}
