using System.Collections.Generic;
using NWO.Entities;

namespace NWO.Core;

// Per-viewer rules for which turn-summary events reach a player's event log, and
// whether they stay clickable. Extracted from WorldMap so the (subtle) fog rules
// can be unit-tested. Pure: given the raw events plus the viewer's state and fog,
// it returns the filtered list.
//
//  - an enemy city growing is hidden entirely;
//  - any other enemy-city event is shown, but is only clickable (keeps its Focus
//    tile) once the player has discovered that city — otherwise the tile
//    reference is stripped so we don't reveal an unseen city's location;
//  - player-owned cities and tile-less events pass through unchanged.
public static class EventVisibilityFilter
{
    public static List<GameEvent> ForViewer(
        IEnumerable<GameEvent> events, GameState state, Player viewer, FogOfWar fog)
    {
        var result = new List<GameEvent>();
        foreach (var e in events)
        {
            if (e.Owner != null && e.Owner != viewer) continue;
            if (e.Focus is not { } tile) { result.Add(e); continue; }
            var city = state.Cities.Find(c => c.Position == tile);
            if (city == null || city.Owner == viewer) { result.Add(e); continue; }

            if (e.Kind == GameEventKind.CityGrew) continue; // hide enemy growth
            result.Add(fog.IsDiscovered(tile) ? e : e with { Focus = null });
        }
        return result;
    }
}
