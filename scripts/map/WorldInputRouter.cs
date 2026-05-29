using System;
using Godot;
using NWO.Core;

namespace NWO.Map;

// Decodes raw mouse/keyboard/touchpad input into high-level world intents, so
// WorldMap can subscribe to semantic events (a tile was clicked, a key was
// pressed, the cursor hovered) instead of carrying the click-vs-drag state
// machine and wheel/gesture plumbing itself.
//
// Camera pan/zoom are applied directly (they're pure pass-throughs); everything
// that touches gameplay or the HUD is raised as an event for WorldMap to handle.
public sealed class WorldInputRouter
{
    private const float DragThreshold   = 8f;   // px the cursor must travel before LMB becomes a pan, not a click
    private const float WheelPanStep    = 80f;  // view shift per horizontal-wheel notch (touchpad two-finger horizontal scroll)
    private const float PanGestureScale = 2.5f; // touchpad two-finger pan-gesture sensitivity

    private readonly CameraController  _camera;
    private readonly Func<bool>        _isAnimating;
    private readonly Func<Vector2I>    _mouseTile; // axial tile currently under the cursor

    // Left-mouse click-vs-drag state. A press starts a candidate click; once the
    // cursor travels past DragThreshold it becomes a camera pan and the release no
    // longer selects (Civ 5 grab-pan).
    private bool    _lmbDown;
    private bool    _lmbPanning;
    private Vector2 _lmbDragAccum;

    public event Action<Key>?      KeyPressed;
    public event Action<Vector2I>? LeftClicked;  // a click that didn't become a drag-pan
    public event Action<Vector2I>? RightPressed;
    public event Action<Vector2>?  Hovered;      // viewport position; fires when not panning/animating
    public event Action?           Panned;       // any camera pan this frame (so the tooltip can clear)

    public WorldInputRouter(CameraController camera, Func<bool> isAnimating, Func<Vector2I> mouseTile)
    {
        _camera      = camera;
        _isAnimating = isAnimating;
        _mouseTile   = mouseTile;
    }

    public void Handle(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventKey { Pressed: true } key:
                KeyPressed?.Invoke(key.Keycode);
                return;

            case InputEventMouseMotion motion:
                HandleMotion(motion);
                return;

            // Touchpad gestures. Reliable on macOS; on Windows precision touchpads
            // two-finger scroll usually arrives as wheel events (handled below) and
            // these may never fire — harmless to handle anyway.
            case InputEventPanGesture pan:
                _camera.ApplyMousePan(pan.Delta * PanGestureScale);
                return;
            case InputEventMagnifyGesture magnify:
                _camera.Zoom(magnify.Factor);
                return;

            case InputEventMouseButton mb:
                HandleButton(mb);
                return;
        }
    }

    // Reset the click-vs-drag and middle-pan state (Esc / focus loss).
    public void CancelDrag()
    {
        _camera.IsPanning = false;
        _lmbDown    = false;
        _lmbPanning = false;
    }

    private void HandleMotion(InputEventMouseMotion motion)
    {
        if (_camera.IsPanning) // middle-mouse pan
        {
            _camera.ApplyMousePan(motion.Relative);
            Panned?.Invoke();
        }
        else if (_lmbDown && !_isAnimating()
                 && (_lmbPanning || (_lmbDragAccum += motion.Relative).Length() > DragThreshold))
        {
            // LMB held and dragged past the threshold → camera pan (Civ 5 left-drag
            // grab-pan), no tile tooltip while panning.
            _lmbPanning = true;
            _camera.ApplyMousePan(motion.Relative);
            Panned?.Invoke();
        }
        else
        {
            Hovered?.Invoke(motion.Position);
        }
    }

    private void HandleButton(InputEventMouseButton mb)
    {
        if (mb.Pressed)
        {
            switch (mb.ButtonIndex)
            {
                case MouseButton.WheelUp:    _camera.Zoom(1.15f); break;
                case MouseButton.WheelDown:  _camera.Zoom(0.87f); break;
                // Horizontal two-finger scroll on a touchpad → pan sideways.
                case MouseButton.WheelRight: _camera.ApplyMousePan(new Vector2(-WheelPanStep, 0)); break;
                case MouseButton.WheelLeft:  _camera.ApplyMousePan(new Vector2( WheelPanStep, 0)); break;
                case MouseButton.Left when !_isAnimating():
                    // Defer select/move to release so we can tell a click from a drag-pan.
                    _lmbDown      = true;
                    _lmbPanning   = false;
                    _lmbDragAccum = Vector2.Zero;
                    break;
                case MouseButton.Right when !_isAnimating():
                    RightPressed?.Invoke(_mouseTile());
                    break;
                case MouseButton.Middle:
                    _camera.IsPanning = true;
                    break;
            }
        }
        else if (mb.ButtonIndex == MouseButton.Middle)
        {
            _camera.IsPanning = false;
        }
        else if (mb.ButtonIndex == MouseButton.Left)
        {
            // Released without dragging past the threshold → treat as a click.
            if (_lmbDown && !_lmbPanning && !_isAnimating())
                LeftClicked?.Invoke(_mouseTile());
            _lmbDown    = false;
            _lmbPanning = false;
        }
    }
}
