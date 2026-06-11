using System.Collections.Generic;
using Godot;
using NWO.Art;

namespace NWO.Map;

// Resolves the top-face texture + material for each (TerrainType, vegetation
// feature) combination (Phase 7 V7.2; Phase 14 composites).
//
// Placeholder policy (mirrors AudioManager, scripts/audio/AudioManager.cs): each
// combo resolves once to a real res://assets/art/tiles/<stem>.png when that file
// exists, otherwise to a procedural pixel-art tile synthesized by
// TerrainArtGenerator — so the map looks fully textured on zero committed art and a
// real (hand-edited or AI-generated) PNG drops in later with no code change. The
// committed tiles in assets/art/tiles/ are baked from that same generator (see the
// generate-terrain-art skill), so the placeholder and the baked file are identical.
//
// File stem: lowercase terrain name, plus "_<feature>" for a vegetation overlay —
// grassland.png, grassland_forest.png, desert_oasis.png (see the add-art-asset
// skill). Hills is geometry (a taller prism), not a texture, so it never appears in
// a stem. All textures use Nearest filtering so pixel art stays crisp under the
// Civ5-style oblique telephoto camera.
public sealed class TerrainTextureRegistry
{
    private readonly Dictionary<(TerrainType, Feature), Texture2D>          _textures  = new();
    private readonly Dictionary<(TerrainType, Feature), StandardMaterial3D> _materials = new();

    // Per-combo top-face material (textured albedo). Cached so all tiles of a combo
    // share one material. VertexColorUseAsAlbedo lets the mesh's faint rim shading
    // multiply over the texture without re-tinting it (vertex colours are white-ish
    // on the top face).
    public StandardMaterial3D Material(TerrainType terrain, Feature veg = Feature.None)
    {
        veg &= FeatureRules.VegMask; // Hills is geometry, not texture
        if (_materials.TryGetValue((terrain, veg), out var mat)) return mat;
        mat = new StandardMaterial3D
        {
            AlbedoTexture          = For(terrain, veg),
            VertexColorUseAsAlbedo = true,
            Roughness              = 0.95f,
            Metallic               = 0f,
            TextureFilter          = BaseMaterial3D.TextureFilterEnum.Nearest, // crisp pixel-art
        };
        _materials[(terrain, veg)] = mat;
        return mat;
    }

    // Real PNG if the artist has dropped one in; otherwise the synthesized tile.
    public Texture2D For(TerrainType terrain, Feature veg = Feature.None)
    {
        veg &= FeatureRules.VegMask;
        if (_textures.TryGetValue((terrain, veg), out var tex)) return tex;

        string path = $"res://assets/art/tiles/{Stem(terrain, veg)}.png";
        tex = ResourceLoader.Exists(path) && ResourceLoader.Load<Texture2D>(path) is { } png
            ? png
            : MakePlaceholder(terrain, veg);

        _textures[(terrain, veg)] = tex;
        return tex;
    }

    public static string Stem(TerrainType terrain, Feature veg)
        => veg == Feature.None
            ? terrain.ToString().ToLowerInvariant()
            : $"{terrain.ToString().ToLowerInvariant()}_{veg.ToString().ToLowerInvariant()}";

    // Synthesize the runtime placeholder via the shared procedural generator, so the
    // fallback look matches the committed PNGs exactly. See TerrainArtGenerator.
    private static ImageTexture MakePlaceholder(TerrainType terrain, Feature veg) =>
        ImageTexture.CreateFromImage(TerrainArtGenerator.Generate(terrain, veg));
}
