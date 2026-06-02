using System.Collections.Generic;
using Godot;
using NWO.Art;

namespace NWO.Map;

// Resolves the map icon texture for each resource (Phase 9).
//
// Mirrors UnitTextureRegistry's placeholder-first policy: load a real PNG from
// res://assets/art/resources/<resource>.png when the artist has dropped one in,
// otherwise fall back to the procedural icon synthesised by ResourceIconGenerator —
// so every resource always has a distinct icon and real art can replace it with no
// code change. File stem is the lowercase ResourceType name (e.g. wheat.png,
// goldore.png); see the add-art-asset skill.
public static class ResourceIconRegistry
{
    private static readonly Dictionary<ResourceType, Texture2D> Cache = new();

    public static Texture2D For(ResourceType resource)
    {
        if (Cache.TryGetValue(resource, out var tex)) return tex;

        string path = $"res://assets/art/resources/{resource.ToString().ToLowerInvariant()}.png";
        tex = ResourceLoader.Exists(path) && ResourceLoader.Load<Texture2D>(path) is { } png
            ? png
            : ImageTexture.CreateFromImage(ResourceIconGenerator.Generate(resource));

        Cache[resource] = tex;
        return tex;
    }
}
