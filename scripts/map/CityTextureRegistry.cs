using Godot;
using NWO.Art;

namespace NWO.Map;

// Resolves the billboard texture for city sprites (Phase 7 V7.3).
//
// Two variants: regular city and capital. Same placeholder-first policy as
// UnitTextureRegistry — a real PNG at res://assets/art/cities/{city|capital}.png
// overrides the procedural sprite from CityArtGenerator with no code change.
public static class CityTextureRegistry
{
    private static Texture2D? _city;
    private static Texture2D? _capital;

    public static Texture2D For(bool isCapital) => isCapital ? Capital() : City();

    private static Texture2D City()
    {
        if (_city != null) return _city;
        const string path = "res://assets/art/cities/city.png";
        _city = ResourceLoader.Exists(path) && ResourceLoader.Load<Texture2D>(path) is { } png
            ? png
            : ImageTexture.CreateFromImage(CityArtGenerator.Generate(isCapital: false));
        return _city;
    }

    private static Texture2D Capital()
    {
        if (_capital != null) return _capital;
        const string path = "res://assets/art/cities/capital.png";
        _capital = ResourceLoader.Exists(path) && ResourceLoader.Load<Texture2D>(path) is { } png
            ? png
            : ImageTexture.CreateFromImage(CityArtGenerator.Generate(isCapital: true));
        return _capital;
    }
}
