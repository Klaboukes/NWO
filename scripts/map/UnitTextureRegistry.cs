using System.Collections.Generic;
using Godot;
using NWO.Art;

namespace NWO.Map;

// Resolves the billboard texture for each unit type (Phase 7 V7.3).
//
// Mirrors TerrainTextureRegistry's placeholder-first policy: load a real PNG from
// res://assets/art/units/<unitId>.png when the artist has dropped one in, otherwise
// fall back to the procedural sprite synthesised by UnitArtGenerator — so every unit
// type always has a distinct sprite and real art can replace it with no code change.
public static class UnitTextureRegistry
{
    private static readonly Dictionary<string, Texture2D> Cache = new();

    public static Texture2D For(string unitId)
    {
        if (Cache.TryGetValue(unitId, out var tex)) return tex;

        string path = $"res://assets/art/units/{unitId}.png";
        tex = ResourceLoader.Exists(path) && ResourceLoader.Load<Texture2D>(path) is { } png
            ? png
            : ImageTexture.CreateFromImage(UnitArtGenerator.Generate(unitId));

        Cache[unitId] = tex;
        return tex;
    }
}
