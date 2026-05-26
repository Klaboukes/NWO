using System.Collections.Generic;
using NWO.Entities;

namespace NWO.Core;

// Loaded-once, indexed-by-id catalog of game data. Replaces repeated LINQ scans
// of List<UnitData> / List<BuildingData> with O(1) dictionary lookups.
//
// Item ids follow the convention "kind:id" (e.g. "unit:warrior", "building:granary").
// SplitItem() parses these and the typed lookups (UnitFromItem, BuildingFromItem)
// handle the kind dispatch in one place.
public class DataCatalog
{
    public IReadOnlyList<UnitData>     Units     { get; }
    public IReadOnlyList<BuildingData> Buildings { get; }

    private readonly Dictionary<string, UnitData>     _unitsById;
    private readonly Dictionary<string, BuildingData> _buildingsById;

    public DataCatalog(IReadOnlyList<UnitData> units, IReadOnlyList<BuildingData> buildings)
    {
        Units          = units;
        Buildings      = buildings;
        _unitsById     = new Dictionary<string, UnitData>(units.Count);
        _buildingsById = new Dictionary<string, BuildingData>(buildings.Count);
        foreach (var u in units)     _unitsById[u.Id]     = u;
        foreach (var b in buildings) _buildingsById[b.Id] = b;
    }

    public static DataCatalog Load()
        => new(DataLoader.LoadUnits(), DataLoader.LoadBuildings());

    public UnitData?     Unit(string id)     => _unitsById.GetValueOrDefault(id);
    public BuildingData? Building(string id) => _buildingsById.GetValueOrDefault(id);

    public UnitData?     UnitFromItem(string item)
        => SplitItem(item) is { Kind: "unit", Id: var id } ? Unit(id) : null;

    public BuildingData? BuildingFromItem(string item)
        => SplitItem(item) is { Kind: "building", Id: var id } ? Building(id) : null;

    public int ItemCost(string item)
    {
        var (kind, id) = SplitItem(item);
        return kind switch
        {
            "unit"     => _unitsById.TryGetValue(id, out var u)     ? u.ProductionCost : int.MaxValue,
            "building" => _buildingsById.TryGetValue(id, out var b) ? b.ProductionCost : int.MaxValue,
            _          => int.MaxValue,
        };
    }

    public string ItemName(string item)
    {
        var (kind, id) = SplitItem(item);
        return kind switch
        {
            "unit"     => _unitsById.TryGetValue(id, out var u)     ? u.Name : id,
            "building" => _buildingsById.TryGetValue(id, out var b) ? b.Name : id,
            _          => item,
        };
    }

    public static (string Kind, string Id) SplitItem(string item)
    {
        int sep = item.IndexOf(':');
        return sep < 0 ? ("", item) : (item[..sep], item[(sep + 1)..]);
    }
}
