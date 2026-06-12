using Godot;
using NWO.Art;

namespace NWO.Map;

// Resolves the billboard texture for city sprites (Phase 7 V7.3 / V7.5).
//
// Two variants: regular city and capital. Same placeholder-first policy as
// UnitTextureRegistry — a real PNG at res://assets/art/cities/{city|capital}.png
// overrides the procedural sprite from CityArtGenerator with no code change.
//
// Since painterly v2 all city art is full-colour; WorldRenderer never tints the
// body and always shows the owner-coloured banner, so there is no placeholder/real
// split (docs/ART_ASSETS.md "team-colour banners").
public static class CityTextureRegistry
{
    private static Texture2D? _city;
    private static Texture2D? _capital;

    public static Texture2D For(bool isCapital)
    {
        ref var slot = ref isCapital ? ref _capital : ref _city;
        if (slot is { } hit) return hit;

        string path = isCapital
            ? "res://assets/art/cities/capital.png"
            : "res://assets/art/cities/city.png";
        slot = ResourceLoader.Exists(path) && ResourceLoader.Load<Texture2D>(path) is { } png
            ? png
            : ImageTexture.CreateFromImage(CityArtGenerator.Generate(isCapital));
        return slot;
    }
}
