using System;
using Godot;

namespace NWO.Map;

// Tile tooltip dwell timer. The cursor must rest on a discovered tile for Delay
// before its tooltip appears; crossing into a new tile restarts the countdown.
// The countdown is driven from _Process (no motion events fire while the cursor
// is perfectly still, which is exactly when we want it to show). Extracted from
// WorldMap, which wires the four delegates to the map state and the HUD.
public sealed class TileTooltipController
{
    private const float Delay = 0.4f;

    private readonly Func<Vector2I, bool>     _canShow;   // tile is on-map AND discovered to the viewer
    private readonly Func<Vector2I, string>   _buildText; // tooltip body for a tile
    private readonly Action<string, Vector2>  _show;
    private readonly Action                   _hide;

    private Vector2I? _tile;
    private Vector2   _screenPos;
    private float     _dwell;
    private bool      _shown;

    public TileTooltipController(
        Func<Vector2I, bool> canShow,
        Func<Vector2I, string> buildText,
        Action<string, Vector2> show,
        Action hide)
    {
        _canShow   = canShow;
        _buildText = buildText;
        _show      = show;
        _hide      = hide;
    }

    // Per-frame: once the cursor has rested on a tile long enough, show its tooltip.
    public void Tick(float delta)
    {
        if (_tile is not { } tile || _shown) return;
        _dwell += delta;
        if (_dwell >= Delay)
        {
            _show(_buildText(tile), _screenPos);
            _shown = true;
        }
    }

    // Cursor moved over `tile` (at screenPos). Crossing into a new tile (or onto an
    // undiscovered/off-map tile) restarts the countdown and hides any shown tooltip;
    // resting on the same tile lets the Tick countdown elapse and — once shown —
    // keeps the tooltip following the cursor within that tile.
    public void RegisterHover(Vector2I tile, Vector2 screenPos)
    {
        _screenPos = screenPos;
        if (!_canShow(tile)) { Clear(); return; }

        if (_tile != tile)
        {
            _tile  = tile;
            _dwell = 0f;
            if (_shown) { _hide(); _shown = false; }
        }
        else if (_shown)
        {
            _show(_buildText(tile), screenPos); // follow cursor within the tile
        }
    }

    // Forget the hovered tile and hide any visible tooltip (cursor left the map, or
    // the map is panning under the cursor).
    public void Clear()
    {
        _tile  = null;
        _dwell = 0f;
        if (_shown) { _hide(); _shown = false; }
    }
}
