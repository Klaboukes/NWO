using System.Collections.Generic;
using Godot;
using NWO.Entities;
using NWO.Map;

namespace NWO.Core;

// Per-player fog state. Tiles a player can currently see (Visible) and tiles
// the player has ever seen (Discovered). Recompute by calling Recompute() with
// the current world state for that player.
public class FogOfWar
{
    public HashSet<Vector2I> Visible    { get; } = new();
    public HashSet<Vector2I> Discovered { get; } = new();

    public bool IsVisible(Vector2I tile)    => Visible.Contains(tile);
    public bool IsDiscovered(Vector2I tile) => Discovered.Contains(tile);

    // Rebuild Visible from scratch; Discovered is monotonic (only ever grows).
    // animOverrides lets the renderer pass per-unit position overrides while
    // a unit is mid-animation so sight follows the visual position.
    public void Recompute(
        Player owner,
        IEnumerable<Unit> units,
        IEnumerable<City> cities,
        MapData map,
        int citySightRadius,
        IReadOnlyDictionary<Unit, Vector2I>? animOverrides = null)
    {
        Visible.Clear();

        foreach (var unit in units)
        {
            if (unit.Owner != owner) continue;
            var origin = animOverrides != null && animOverrides.TryGetValue(unit, out var p)
                ? p : unit.Position;
            Reveal(origin, unit.Data.Sight, map);
        }

        foreach (var city in cities)
        {
            if (city.Owner != owner) continue;
            Reveal(city.Position, citySightRadius, map);
        }
    }

    private void Reveal(Vector2I origin, int radius, MapData map)
    {
        foreach (var tile in HexGrid.GetRange(origin, radius))
        {
            if (!map.Tiles.ContainsKey(tile)) continue;
            Visible.Add(tile);
            Discovered.Add(tile);
        }
    }
}
