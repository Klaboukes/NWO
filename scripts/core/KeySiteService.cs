using NWO.Entities;
using NWO.Map;

namespace NWO.Core;

// Control logic for the Phase 10.5 objective sites. A key site is held by whoever
// owns the nearest city within ControlRadius; if no city is close enough it's
// uncontested (null). Holding every key site wins the match (see VictoryService).
public static class KeySiteService
{
    public const int ControlRadius = 3;

    // The player controlling `site`, or null if no city is within range. Ties on
    // distance resolve to the earlier-founded city (GameState.Cities order) for
    // deterministic saves/replays.
    public static Player? Controller(GameState state, Godot.Vector2I site)
    {
        City? nearest = null;
        int   best    = int.MaxValue;
        foreach (var c in state.Cities)
        {
            int d = HexGrid.Distance(c.Position, site);
            if (d <= ControlRadius && d < best) { best = d; nearest = c; }
        }
        return nearest?.Owner;
    }

    // How many key sites `player` currently controls.
    public static int ControlledCount(GameState state, Player player)
    {
        int n = 0;
        foreach (var site in state.Map.KeySites)
            if (Controller(state, site) == player) n++;
        return n;
    }

    // True if `player` holds every key site (and at least one exists) — the
    // objective-victory condition.
    public static bool ControlsAll(GameState state, Player player)
        => state.Map.KeySites.Count > 0 && ControlledCount(state, player) == state.Map.KeySites.Count;
}
