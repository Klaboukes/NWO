using System;
using System.Collections.Generic;
using Godot;
using NWO.Core;

namespace NWO.UI;

// Scrolling event feed. Shows the most recent turn-summary events as rows;
// events that carry a Focus tile render as a clickable button that asks the map
// to recenter there. Replaces the old single " | "-joined notification label
// for end-of-turn summaries. Combat/one-shot messages still use the banner.
public partial class EventLogController : VBoxContainer
{
    private const int MaxRows = 6;

    private readonly LinkedList<GameEvent> _events = new();

    public event Action<Vector2I>? FocusRequested;

    // Append a turn's events (oldest first), trim to the last N rows, redraw.
    public void Add(IEnumerable<GameEvent> events)
    {
        foreach (var e in events)
        {
            _events.AddLast(e);
            while (_events.Count > MaxRows) _events.RemoveFirst();
        }
        Rebuild();
    }

    private void Rebuild()
    {
        foreach (var child in GetChildren()) child.QueueFree();
        foreach (var e in _events)
        {
            if (e.Focus is { } tile)
            {
                var row = new Button
                {
                    Text      = $"▸ {e.Text}",
                    FocusMode = Control.FocusModeEnum.None,
                    Flat      = true,
                    Alignment = HorizontalAlignment.Left,
                };
                var captured = tile;
                row.Pressed += () => FocusRequested?.Invoke(captured);
                AddChild(row);
            }
            else
            {
                AddChild(new Label
                {
                    Text     = $"  {e.Text}",
                    Modulate = new Color(0.85f, 0.85f, 0.85f),
                });
            }
        }
    }
}
