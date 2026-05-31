using System;
using System.Collections.Generic;
using Godot;
using NWO.Entities;

namespace NWO.Core;

// Drives a unit visually along a precomputed path. Game-state movement (cost
// deduction, position update) happens up front in Start(); this class only
// owns the visual interpolation.
public class MovementAnimator
{
    private readonly float _secondsPerTile;
    private readonly Func<Vector2I, Vector3> _axialToWorld;

    private Unit?           _unit;
    private List<Vector2I>? _path;
    private int             _index;
    private float           _t;

    public Vector3  CurrentWorldPos { get; private set; }
    public Vector2I CurrentTile     { get; private set; }
    public Unit?    AnimatingUnit   => _unit;
    public bool     IsAnimating     => _unit != null;

    // Fired when the visual reaches the end of the path. Recipient should update
    // fog of war (which depends on visual position) and run any post-move logic.
    public event Action? Completed;

    // Fired when the visual crosses into a new tile. Recipient may want to
    // refresh fog of war so revealed tiles appear as the unit moves.
    public event Action? TileEntered;

    public MovementAnimator(float secondsPerTile, Func<Vector2I, Vector3> axialToWorld)
    {
        _secondsPerTile = secondsPerTile;
        _axialToWorld   = axialToWorld;
    }

    public void Start(Unit unit, List<Vector2I> path)
    {
        _unit           = unit;
        _path           = path;
        _index          = 0;
        _t              = 0f;
        CurrentTile     = path[0];
        CurrentWorldPos = _axialToWorld(path[0]);
    }

    // Advance the animation; returns true if the visual moved (caller should redraw).
    public bool Tick(float delta)
    {
        if (_unit == null || _path == null) return false;

        _t += delta / _secondsPerTile;

        if (_t >= 1f)
        {
            _t -= 1f;
            _index++;
            if (_index >= _path.Count - 1)
            {
                CurrentTile     = _path[^1];
                CurrentWorldPos = _axialToWorld(_path[^1]);
                _unit           = null;
                _path           = null;
                Completed?.Invoke();
                return true;
            }
            CurrentTile = _path[_index];
            TileEntered?.Invoke();
        }

        CurrentWorldPos = _axialToWorld(_path[_index])
            .Lerp(_axialToWorld(_path[_index + 1]), _t);
        return true;
    }
}
