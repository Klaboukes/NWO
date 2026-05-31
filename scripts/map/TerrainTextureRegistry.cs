using System.Collections.Generic;
using Godot;
using NWO.Art;

namespace NWO.Map;

// Resolves the top-face texture + material for each TerrainType (Phase 7 V7.2).
//
// Placeholder policy (mirrors AudioManager, scripts/audio/AudioManager.cs): each
// terrain resolves once to a real res://assets/art/tiles/<terrain>.png when that
// file exists, otherwise to a procedural pixel-art tile synthesized by
// TerrainArtGenerator — so the map looks fully textured on zero committed art and a
// real (hand-edited or AI-generated) PNG drops in later with no code change. The
// committed tiles in assets/art/tiles/ are baked from that same generator (see the
// generate-terrain-art skill), so the placeholder and the baked file are identical.
//
// Lowercase TerrainType name is the file stem, e.g. grassland.png (see the
// add-art-asset skill). All textures use Nearest filtering so pixel art stays crisp
// under the Civ5-style oblique telephoto camera.
public sealed class TerrainTextureRegistry
{
    private readonly Dictionary<TerrainType, Texture2D>        _textures  = new();
    private readonly Dictionary<TerrainType, StandardMaterial3D> _materials = new();

    // Per-terrain top-face material (textured albedo). Cached so all tiles of a
    // terrain share one material. VertexColorUseAsAlbedo lets the mesh's faint rim
    // shading multiply over the texture without re-tinting it (vertex colours are
    // white-ish on the top face).
    public StandardMaterial3D Material(TerrainType terrain)
    {
        if (_materials.TryGetValue(terrain, out var mat)) return mat;
        mat = new StandardMaterial3D
        {
            AlbedoTexture          = For(terrain),
            VertexColorUseAsAlbedo = true,
            Roughness              = 0.95f,
            Metallic               = 0f,
            TextureFilter          = BaseMaterial3D.TextureFilterEnum.Nearest, // crisp pixel-art
        };
        _materials[terrain] = mat;
        return mat;
    }

    // Real PNG if the artist has dropped one in; otherwise the synthesized tile.
    public Texture2D For(TerrainType terrain)
    {
        if (_textures.TryGetValue(terrain, out var tex)) return tex;

        string path = $"res://assets/art/tiles/{terrain.ToString().ToLowerInvariant()}.png";
        tex = ResourceLoader.Exists(path) && ResourceLoader.Load<Texture2D>(path) is { } png
            ? png
            : MakePlaceholder(terrain);

        _textures[terrain] = tex;
        return tex;
    }

    // Synthesize the runtime placeholder via the shared procedural generator, so the
    // fallback look matches the committed PNGs exactly. See TerrainArtGenerator.
    private static ImageTexture MakePlaceholder(TerrainType terrain) =>
        ImageTexture.CreateFromImage(TerrainArtGenerator.Generate(terrain));
}
