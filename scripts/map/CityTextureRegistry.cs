using Godot;
using NWO.Art;

namespace NWO.Map;

// Resolves the billboard texture for city sprites (Phase 7 V7.3).
//
// Two variants: regular city and capital. Same placeholder-first policy as
// UnitTextureRegistry — a real PNG at res://assets/art/cities/{city|capital}.png
// overrides the procedural sprite from CityArtGenerator with no code change.
//
// HasRealArt reports which branch resolved: a synthesised placeholder is white-fill
// (WorldRenderer tints the whole sprite by owner colour), while real PNG art is
// full-colour (WorldRenderer leaves it untinted and shows an owner-tinted banner
// instead — see docs/ART_ASSETS.md "team-colour banners").
public static class CityTextureRegistry
{
    private static (Texture2D Tex, bool Real)? _city;
    private static (Texture2D Tex, bool Real)? _capital;

    public static Texture2D For(bool isCapital)        => Resolve(isCapital).Tex;
    public static bool HasRealArt(bool isCapital)       => Resolve(isCapital).Real;

    private static (Texture2D Tex, bool Real) Resolve(bool isCapital)
    {
        ref var slot = ref isCapital ? ref _capital : ref _city;
        if (slot is { } hit) return hit;

        string path = isCapital
            ? "res://assets/art/cities/capital.png"
            : "res://assets/art/cities/city.png";
        (Texture2D Tex, bool Real) r =
            ResourceLoader.Exists(path) && ResourceLoader.Load<Texture2D>(path) is { } png
                ? (png, true)
                : (ImageTexture.CreateFromImage(CityArtGenerator.Generate(isCapital)), false);

        slot = r;
        return r;
    }
}
