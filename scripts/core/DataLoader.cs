using System.Collections.Generic;
using System.Text.Json;
using Godot;
using NWO.Entities;

namespace NWO.Core;

// Loads the JSON data files (units/buildings/techs) from res://data at startup
// and deserializes them into the immutable record types. DataCatalog indexes the
// results for O(1) lookup; nothing here is called after startup.
public static class DataLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static List<UnitData> LoadUnits()
    {
        var json = ReadResFile("res://data/units.json");
        var file = JsonSerializer.Deserialize<UnitsFile>(json, Options);
        return file?.Units ?? new List<UnitData>();
    }

    public static List<BuildingData> LoadBuildings()
    {
        var json = ReadResFile("res://data/buildings.json");
        var file = JsonSerializer.Deserialize<BuildingsFile>(json, Options);
        return file?.Buildings ?? new List<BuildingData>();
    }

    public static List<TechData> LoadTechs()
    {
        var json = ReadResFile("res://data/techs.json");
        var file = JsonSerializer.Deserialize<TechsFile>(json, Options);
        return file?.Techs ?? new List<TechData>();
    }

    private static string ReadResFile(string resPath)
    {
        using var file = FileAccess.Open(resPath, FileAccess.ModeFlags.Read);
        if (file == null)
            throw new System.IO.FileNotFoundException($"Cannot open {resPath}: {FileAccess.GetOpenError()}");
        return file.GetAsText();
    }

    // ── JSON wrapper types ───────────────────────────────────────────────────

    private class UnitsFile
    {
        public List<UnitData> Units { get; set; } = new();
    }

    private class BuildingsFile
    {
        public List<BuildingData> Buildings { get; set; } = new();
    }

    private class TechsFile
    {
        public List<TechData> Techs { get; set; } = new();
    }
}
