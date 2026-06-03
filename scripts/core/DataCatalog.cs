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
    public IReadOnlyList<TechData>     Techs     { get; }
    public IReadOnlyList<FactionData>  Factions  { get; }

    private readonly Dictionary<string, UnitData>     _unitsById;
    private readonly Dictionary<string, BuildingData> _buildingsById;
    private readonly Dictionary<string, TechData>     _techsById;
    private readonly Dictionary<string, FactionData>  _factionsById;
    // Reverse index: item id ("unit:horseman" / "building:granary") → tech that unlocks it.
    private readonly Dictionary<string, TechData>     _unlockedBy;

    public DataCatalog(
        IReadOnlyList<UnitData>     units,
        IReadOnlyList<BuildingData> buildings,
        IReadOnlyList<TechData>?    techs    = null,
        IReadOnlyList<FactionData>? factions = null)
    {
        Units          = units;
        Buildings      = buildings;
        Techs          = techs    ?? new List<TechData>();
        Factions       = factions ?? new List<FactionData>();
        _unitsById     = new Dictionary<string, UnitData>(units.Count);
        _buildingsById = new Dictionary<string, BuildingData>(buildings.Count);
        _techsById     = new Dictionary<string, TechData>(Techs.Count);
        _factionsById  = new Dictionary<string, FactionData>(Factions.Count);
        _unlockedBy    = new Dictionary<string, TechData>();
        foreach (var u in units)     _unitsById[u.Id]     = u;
        foreach (var b in buildings) _buildingsById[b.Id] = b;
        foreach (var t in Techs)     _techsById[t.Id]     = t;
        foreach (var f in Factions)  _factionsById[f.Id]  = f;
        foreach (var t in Techs)
        {
            foreach (var unitId     in t.Unlocks.Units)     _unlockedBy[$"unit:{unitId}"]         = t;
            foreach (var buildingId in t.Unlocks.Buildings) _unlockedBy[$"building:{buildingId}"] = t;
        }
    }

    public static DataCatalog Load()
        => new(DataLoader.LoadUnits(), DataLoader.LoadBuildings(), DataLoader.LoadTechs(),
               DataLoader.LoadFactions());

    public UnitData?     Unit(string id)     => _unitsById.GetValueOrDefault(id);
    public BuildingData? Building(string id) => _buildingsById.GetValueOrDefault(id);
    public TechData?     Tech(string id)     => _techsById.GetValueOrDefault(id);
    public FactionData?  Faction(string id)  => _factionsById.GetValueOrDefault(id);

    // Resolves a player to its faction's modifier bag, falling back to the shared
    // all-identity Neutral faction for players with no/unknown faction id (and legacy
    // saves). Lets every service hook read modifiers unconditionally.
    public FactionData FactionOf(Player player)
        => player.FactionId != null ? _factionsById.GetValueOrDefault(player.FactionId, FactionData.Neutral)
                                     : FactionData.Neutral;

    // Maps a base unit id to the owner faction's unique variant if one exists, else
    // returns the base id unchanged. Applied at the single production seam so UI/AI
    // keep speaking in base ids (see GameState.CompleteProduction).
    public string ResolveUnitForFaction(string baseId, Player owner)
        => FactionOf(owner).UnitVariants.GetValueOrDefault(baseId, baseId);

    public UnitData?     UnitFromItem(string item)
        => SplitItem(item) is { Kind: "unit", Id: var id } ? Unit(id) : null;

    public BuildingData? BuildingFromItem(string item)
        => SplitItem(item) is { Kind: "building", Id: var id } ? Building(id) : null;

    // Returns the tech that unlocks the given build item, or null if no tech is required.
    public TechData? UnlockingTech(string item) => _unlockedBy.GetValueOrDefault(item);

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
