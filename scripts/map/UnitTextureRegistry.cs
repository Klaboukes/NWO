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
//
// IsRealArt reports which branch resolved: a synthesised placeholder is white-fill +
// dark-outline (WorldRenderer tints the whole sprite by owner colour), while real
// PNG art is full-colour (WorldRenderer leaves it untinted and shows an owner-tinted
// banner instead — see docs/ART_ASSETS.md "team-colour banners").
public static class UnitTextureRegistry
{
    private static readonly Dictionary<string, (Texture2D Tex, bool Real)> Cache = new();

    public static Texture2D For(string unitId)  => Resolve(unitId).Tex;
    public static bool IsRealArt(string unitId) => Resolve(unitId).Real;

    private static (Texture2D Tex, bool Real) Resolve(string unitId)
    {
        if (Cache.TryGetValue(unitId, out var hit)) return hit;

        string path = $"res://assets/art/units/{unitId}.png";
        (Texture2D Tex, bool Real) r =
            ResourceLoader.Exists(path) && ResourceLoader.Load<Texture2D>(path) is { } png
                ? (png, true)
                : (ImageTexture.CreateFromImage(UnitArtGenerator.Generate(unitId)), false);

        Cache[unitId] = r;
        return r;
    }
}
