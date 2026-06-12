using System.Collections.Generic;
using Godot;
using NWO.Art;

namespace NWO.Map;

// Resolves the billboard texture for each unit type (Phase 7 V7.3 / V7.5).
//
// Mirrors TerrainTextureRegistry's placeholder-first policy: load a real PNG from
// res://assets/art/units/<unitId>.png when the artist has dropped one in, otherwise
// fall back to the procedural sprite synthesised by UnitArtGenerator — so every unit
// type always has a distinct sprite and real art can replace it with no code change.
//
// Since painterly v2 all unit art — synthesised or dropped-in — is full-colour;
// WorldRenderer never tints the body and always shows the owner-coloured banner
// (docs/ART_ASSETS.md "team-colour banners"), so there is no placeholder/real split.
public static class UnitTextureRegistry
{
    private static readonly Dictionary<string, Texture2D> Cache = new();

    public static Texture2D For(string unitId)
    {
        if (Cache.TryGetValue(unitId, out var hit)) return hit;

        string path = $"res://assets/art/units/{unitId}.png";
        Texture2D tex =
            ResourceLoader.Exists(path) && ResourceLoader.Load<Texture2D>(path) is { } png
                ? png
                : ImageTexture.CreateFromImage(UnitArtGenerator.Generate(unitId));

        Cache[unitId] = tex;
        return tex;
    }
}
