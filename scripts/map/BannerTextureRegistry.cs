using Godot;
using NWO.Art;

namespace NWO.Map;

// Resolves the single owner-colour banner texture shown beside full-colour unit /
// city art (Phase 7 follow-up; see docs/ART_ASSETS.md "team-colour banners").
//
// Same placeholder-first policy as the other registries: a real PNG at
// res://assets/art/ui/banner.png overrides the procedural pennant from
// BannerArtGenerator with no code change. One shared texture for all owners — the
// per-player colour comes from Sprite3D.Modulate, not the texture.
public static class BannerTextureRegistry
{
    private static Texture2D? _banner;

    public static Texture2D Banner()
    {
        if (_banner != null) return _banner;
        const string path = "res://assets/art/ui/banner.png";
        _banner = ResourceLoader.Exists(path) && ResourceLoader.Load<Texture2D>(path) is { } png
            ? png
            : ImageTexture.CreateFromImage(BannerArtGenerator.Generate());
        return _banner;
    }
}
