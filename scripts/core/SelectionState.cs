using System.Collections.Generic;
using Godot;
using NWO.Entities;

namespace NWO.Core;

// Holds what's currently selected and any pending move preview.
// Pure data — no Godot scene dependency.
public class SelectionState
{
    public Unit?            Unit              { get; set; }
    public City?            City              { get; set; }
    public HashSet<Vector2I> ReachableTiles   { get; private set; } = new();
    public Vector2I?        PendingDestination { get; set; }
    public List<Vector2I>?  PendingPathPreview { get; set; }

    public void Clear()
    {
        Unit               = null;
        City               = null;
        ReachableTiles     = new HashSet<Vector2I>();
        PendingDestination = null;
        PendingPathPreview = null;
    }

    public void SelectUnit(Unit unit, HashSet<Vector2I> reachable)
    {
        Unit               = unit;
        City               = null;
        ReachableTiles     = reachable;
        PendingDestination = null;
        PendingPathPreview = null;
    }

    public void SelectCity(City city)
    {
        Unit               = null;
        City               = city;
        ReachableTiles     = new HashSet<Vector2I>();
        PendingDestination = null;
        PendingPathPreview = null;
    }
}
