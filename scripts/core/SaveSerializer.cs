using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using NWO.Entities;
using NWO.Map;

namespace NWO.Core;

// Pure save (de)serialization: maps a GameState to/from a flat DTO graph and JSON.
// Deliberately free of Godot file IO so it runs headless under xUnit — SaveService
// wraps this with user:// file access. Ownership is stored by Player.Id (never
// object refs) and rebound on load; the DataCatalog is re-attached from res://data
// rather than serialized; fog Visible and city yields are recomputed, not stored.
public static class SaveSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters    = { new Vector2IJsonConverter(), new JsonStringEnumConverter() },
    };

    // ── DTOs ───────────────────────────────────────────────────────────────────

    public sealed class SaveGameDto
    {
        public SaveHeaderDto    Header             { get; set; } = new();
        public int              TurnNumber         { get; set; }
        public int              CurrentPlayerIndex { get; set; }
        public int              CombatSeed         { get; set; }
        public int              NextCityName       { get; set; }
        public MapDto           Map                { get; set; } = new();
        public List<PlayerDto>  Players            { get; set; } = new();
        public List<CivDto>     Civs               { get; set; } = new();
        public List<UnitDto>    Units              { get; set; } = new();
        public List<CityDto>    Cities             { get; set; } = new();
        public List<FogDto>     Fog                { get; set; } = new();
        public List<DiploDto>   Diplomacy          { get; set; } = new();
    }

    public sealed class DiploDto
    {
        public int              A      { get; set; }
        public int              B      { get; set; }
        public DiplomaticStance Stance { get; set; }
    }

    public sealed class SaveHeaderDto
    {
        public string Name      { get; set; } = "";
        public string Timestamp { get; set; } = "";
        public int    Turn      { get; set; }
    }

    public sealed class MapDto
    {
        public int                                Width        { get; set; }
        public int                                Height       { get; set; }
        public Dictionary<Vector2I, TerrainType>     Tiles        { get; set; } = new();
        public Dictionary<Vector2I, ResourceType>    Resources    { get; set; } = new();
        public Dictionary<Vector2I, ImprovementType> Improvements { get; set; } = new();
        public Dictionary<Vector2I, Feature>         Features     { get; set; } = new();
        public List<RiverEdgeDto>                    Rivers       { get; set; } = new();
        public List<Vector2I>                        KeySites     { get; set; } = new();
    }

    public sealed class RiverEdgeDto
    {
        public Vector2I Tile { get; set; }
        public int      Dir  { get; set; }
    }

    public sealed class PlayerDto
    {
        public int      Id        { get; set; }
        public string   Name      { get; set; } = "";
        public bool     IsHuman   { get; set; }
        public ColorDto Color     { get; set; } = new();
        public string?  FactionId { get; set; }
    }

    public sealed class ColorDto
    {
        public float R { get; set; }
        public float G { get; set; }
        public float B { get; set; }
        public float A { get; set; } = 1f;
    }

    public sealed class CivDto
    {
        public int          OwnerId            { get; set; }
        public int          Treasury           { get; set; }
        public int          ScienceAccumulated { get; set; }
        public string?      CurrentResearch    { get; set; }
        public List<string> ResearchedTechs    { get; set; } = new();
    }

    public sealed class UnitDto
    {
        public string               DataId            { get; set; } = "";
        public int                  OwnerId           { get; set; }
        public Vector2I             Position          { get; set; }
        public int                  HP                { get; set; }
        public int                  MovementRemaining { get; set; }
        public bool                 Fortified         { get; set; }
        public bool                 ActedThisTurn     { get; set; }
        public bool                 SkippedThisTurn   { get; set; }
        public bool                 SleepUntilHealed  { get; set; }
        public int                  Experience        { get; set; }
        public ImprovementTaskDto?  Task              { get; set; }
        // Cargo units aboard this transport. Null/empty for non-transports.
        public List<UnitDto>?       Cargo             { get; set; }
    }

    public sealed class ImprovementTaskDto
    {
        public Vector2I        Tile           { get; set; }
        public ImprovementType Type           { get; set; }
        public int             TurnsRemaining { get; set; }
    }

    public sealed class CityDto
    {
        public string         Name               { get; set; } = "";
        public int            OwnerId            { get; set; }
        public Vector2I       Position           { get; set; }
        public bool           IsCapital          { get; set; }
        public int            Population         { get; set; }
        public float          FoodAccumulated    { get; set; }
        public int            ProductionProgress { get; set; }
        public string?        ProductionItem     { get; set; }
        public int            HP                 { get; set; }
        public bool           AttackedSinceTurn  { get; set; }
        public List<string>   Buildings          { get; set; } = new();
        public CityFocus       Focus             { get; set; }
        public List<Vector2I> Locked             { get; set; } = new();
    }

    public sealed class FogDto
    {
        public int            OwnerId    { get; set; }
        public List<Vector2I> Discovered { get; set; } = new();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static UnitDto UnitToDto(Unit u) => new()
    {
        DataId            = u.Data.Id,
        OwnerId           = u.Owner.Id,
        Position          = u.Position,
        HP                = u.HP,
        MovementRemaining = u.MovementRemaining,
        Fortified         = u.Fortified,
        ActedThisTurn     = u.ActedThisTurn,
        SkippedThisTurn   = u.SkippedThisTurn,
        SleepUntilHealed  = u.SleepUntilHealed,
        Experience        = u.Experience,
        Task              = u.CurrentTask is { } t
            ? new ImprovementTaskDto { Tile = t.Tile, Type = t.Type, TurnsRemaining = t.TurnsRemaining }
            : null,
        Cargo             = u.Cargo.Count > 0 ? u.Cargo.Select(UnitToDto).ToList() : null,
    };

    private static Unit UnitFromDto(UnitDto ud, DataCatalog catalog, Dictionary<int, Player> byId)
    {
        var def = catalog.Unit(ud.DataId)!;
        if (!byId.TryGetValue(ud.OwnerId, out var owner))
            throw new InvalidOperationException($"Save file references unknown player id {ud.OwnerId}.");
        return new Unit(def, owner, ud.Position)
        {
            HP                = ud.HP,
            MovementRemaining = ud.MovementRemaining,
            Fortified         = ud.Fortified,
            ActedThisTurn     = ud.ActedThisTurn,
            SkippedThisTurn   = ud.SkippedThisTurn,
            SleepUntilHealed  = ud.SleepUntilHealed,
            Experience        = ud.Experience,
            CurrentTask       = ud.Task is { } t
                ? new ImprovementTask(t.Tile, t.Type, t.TurnsRemaining)
                : null,
        };
    }

    // ── Serialize ────────────────────────────────────────────────────────────

    public static string Serialize(GameState state, string saveName)
    {
        var dto = new SaveGameDto
        {
            Header = new SaveHeaderDto
            {
                Name      = saveName,
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                Turn      = state.TurnManager.TurnNumber,
            },
            TurnNumber         = state.TurnManager.TurnNumber,
            CurrentPlayerIndex = state.CurrentPlayerIndex,
            CombatSeed         = state.CombatSeed,
            NextCityName       = state.NextCityNameIndex,
            Map = new MapDto
            {
                Width        = state.Map.Width,
                Height       = state.Map.Height,
                Tiles        = new(state.Map.Tiles),
                Resources    = new(state.Map.Resources),
                Improvements = new(state.Map.Improvements),
                Features     = new(state.Map.Features),
                Rivers       = state.Map.Rivers
                    .Select(e => new RiverEdgeDto { Tile = e.Tile, Dir = e.Dir }).ToList(),
                KeySites     = state.Map.KeySites.ToList(),
            },
            Players = state.Players.Select(p => new PlayerDto
            {
                Id        = p.Id,
                Name      = p.Name,
                IsHuman   = p.IsHuman,
                Color     = new ColorDto { R = p.Color.R, G = p.Color.G, B = p.Color.B, A = p.Color.A },
                FactionId = p.FactionId,
            }).ToList(),
            Civs = state.Players.Select(p =>
            {
                var civ = state.Civ(p);
                return new CivDto
                {
                    OwnerId            = p.Id,
                    Treasury           = civ.Treasury,
                    ScienceAccumulated = civ.ScienceAccumulated,
                    CurrentResearch    = civ.CurrentResearch,
                    ResearchedTechs    = civ.ResearchedTechs.ToList(),
                };
            }).ToList(),
            Units = state.Units.Select(UnitToDto).ToList(),
            Cities = state.Cities.Select(c => new CityDto
            {
                Name               = c.Name,
                OwnerId            = c.Owner.Id,
                Position           = c.Position,
                IsCapital          = c.IsCapital,
                Population         = c.Population,
                FoodAccumulated    = c.FoodAccumulated,
                ProductionProgress = c.ProductionProgress,
                ProductionItem     = c.ProductionItem,
                HP                 = c.HP,
                AttackedSinceTurn  = c.AttackedSinceTurn,
                Buildings          = c.Buildings.ToList(),
                Focus              = c.Workforce.Focus,
                Locked             = c.Workforce.Locked.ToList(),
            }).ToList(),
            Fog = state.Players.Select(p => new FogDto
            {
                OwnerId    = p.Id,
                Discovered = state.Fog(p).Discovered.ToList(),
            }).ToList(),
            Diplomacy = state.Diplomacy.Entries()
                .Select(e => new DiploDto { A = e.A, B = e.B, Stance = e.Stance }).ToList(),
        };

        return JsonSerializer.Serialize(dto, Options);
    }

    public static SaveHeaderDto? ReadHeader(string json)
        => JsonSerializer.Deserialize<SaveGameDto>(json, Options)?.Header;

    // ── Deserialize ──────────────────────────────────────────────────────────

    public static GameState Deserialize(string json, DataCatalog catalog)
    {
        var dto = JsonSerializer.Deserialize<SaveGameDto>(json, Options)
                  ?? throw new InvalidOperationException("Save file is empty or malformed.");

        var map = new MapData(dto.Map.Width, dto.Map.Height);
        foreach (var (pos, t) in dto.Map.Tiles)        map.Tiles[pos]        = t;
        foreach (var (pos, r) in dto.Map.Resources)    map.Resources[pos]    = r;
        foreach (var (pos, i) in dto.Map.Improvements) map.Improvements[pos] = i;
        foreach (var (pos, f) in dto.Map.Features)     map.Features[pos]     = f;
        foreach (var e in dto.Map.Rivers)              map.Rivers.Add((e.Tile, e.Dir));
        foreach (var s in dto.Map.KeySites)            map.KeySites.Add(s);

        var state    = new GameState(map, catalog, dto.CombatSeed);
        var byId      = new Dictionary<int, Player>();
        foreach (var pd in dto.Players)
        {
            var player = state.AddPlayer(new Player
            {
                Id        = pd.Id,
                Name      = pd.Name,
                IsHuman   = pd.IsHuman,
                Color     = new Color(pd.Color.R, pd.Color.G, pd.Color.B, pd.Color.A),
                FactionId = pd.FactionId,
            });
            byId[pd.Id] = player;
        }

        foreach (var cd in dto.Civs)
        {
            var civ = state.Civ(byId[cd.OwnerId]);
            civ.Treasury           = cd.Treasury;
            civ.ScienceAccumulated = cd.ScienceAccumulated;
            civ.CurrentResearch    = cd.CurrentResearch;
            civ.ResearchedTechs.Clear();
            foreach (var tech in cd.ResearchedTechs) civ.ResearchedTechs.Add(tech);
        }

        foreach (var ud in dto.Units)
        {
            if (catalog.Unit(ud.DataId) == null) continue; // unknown unit id — drop it
            var unit = UnitFromDto(ud, catalog, byId);
            state.Units.Add(unit);
            if (ud.Cargo == null) continue;
            foreach (var cargoDto in ud.Cargo)
            {
                if (catalog.Unit(cargoDto.DataId) == null) continue;
                unit.Cargo.Add(UnitFromDto(cargoDto, catalog, byId));
            }
        }

        foreach (var cd in dto.Cities)
        {
            var city = new City(cd.Name, byId[cd.OwnerId], cd.Position)
            {
                IsCapital          = cd.IsCapital,
                Population         = cd.Population,
                FoodAccumulated    = cd.FoodAccumulated,
                ProductionProgress = cd.ProductionProgress,
                ProductionItem     = cd.ProductionItem,
                HP                 = cd.HP,
                AttackedSinceTurn  = cd.AttackedSinceTurn,
            };
            foreach (var b in cd.Buildings)  city.Buildings.Add(b);
            city.Workforce.Focus = cd.Focus;
            foreach (var l in cd.Locked)     city.Workforce.Locked.Add(l);
            state.Cities.Add(city);
        }

        foreach (var fd in dto.Fog)
        {
            var discovered = state.Fog(byId[fd.OwnerId]).Discovered;
            foreach (var tile in fd.Discovered) discovered.Add(tile);
        }

        foreach (var d in dto.Diplomacy)
            state.Diplomacy.Set(d.A, d.B, d.Stance);

        state.RestoreTurnPointer(dto.TurnNumber, dto.CurrentPlayerIndex, dto.NextCityName);

        // Visible fog and city yields aren't stored — rebuild them from the state.
        foreach (var p in state.Players) state.RecomputeFog(p);
        foreach (var c in state.Cities)  CityWorkforceService.Recompute(state, c);

        return state;
    }
}

// Vector2I <-> {"x":int,"y":int}. Registered for both standalone values and as a
// dictionary key (the map DTO dictionaries are keyed by Vector2I).
public sealed class Vector2IJsonConverter : JsonConverter<Vector2I>
{
    public override Vector2I Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // As a dictionary key the value arrives as the property-name string "x,y";
        // as a value it arrives as an object {"x":..,"y":..}.
        if (reader.TokenType == JsonTokenType.String)
        {
            var parts = reader.GetString()!.Split(',');
            return new Vector2I(int.Parse(parts[0]), int.Parse(parts[1]));
        }

        int x = 0, y = 0;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;
            if (reader.TokenType != JsonTokenType.PropertyName) continue;
            string prop = reader.GetString()!;
            reader.Read();
            if (prop == "x") x = reader.GetInt32();
            else if (prop == "y") y = reader.GetInt32();
        }
        return new Vector2I(x, y);
    }

    public override void Write(Utf8JsonWriter writer, Vector2I value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("x", value.X);
        writer.WriteNumber("y", value.Y);
        writer.WriteEndObject();
    }

    public override void WriteAsPropertyName(Utf8JsonWriter writer, Vector2I value, JsonSerializerOptions options)
        => writer.WritePropertyName($"{value.X},{value.Y}");

    public override Vector2I ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var parts = reader.GetString()!.Split(',');
        return new Vector2I(int.Parse(parts[0]), int.Parse(parts[1]));
    }
}
