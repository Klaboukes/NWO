using System.Collections.Generic;
using System.Text.Json;
using Godot;
using NWO.Entities;

namespace NWO.Core;

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
}
