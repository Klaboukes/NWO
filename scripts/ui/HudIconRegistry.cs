using System.Collections.Generic;
using Godot;
using NWO.Art;

namespace NWO.UI;

// Resolves the texture for each HUD status icon (Phase 7 V7.4).
//
// Mirrors UnitTextureRegistry's placeholder-first policy: load a real PNG from
// res://assets/art/ui/<iconId>.png when an artist has dropped one in, otherwise fall
// back to the procedural icon synthesised by HudIconGenerator — so the HUD always has
// crisp icons and real art can replace them with no code change.
public static class HudIconRegistry
{
    private static readonly Dictionary<string, Texture2D> Cache = new();

    public static Texture2D For(string iconId)
    {
        if (Cache.TryGetValue(iconId, out var tex)) return tex;

        string path = $"res://assets/art/ui/{iconId}.png";
        tex = ResourceLoader.Exists(path) && ResourceLoader.Load<Texture2D>(path) is { } png
            ? png
            : ImageTexture.CreateFromImage(HudIconGenerator.Generate(iconId));

        Cache[iconId] = tex;
        return tex;
    }
}
