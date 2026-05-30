using System;
using System.Collections.Generic;
using Godot;

namespace NWO.Map;

// Resolves one Texture2D per TerrainType for the WorldRenderer. Mirrors the
// AudioManager placeholder policy (see docs/ROADMAP.md V7.1): each terrain loads
// res://assets/art/tiles/<terrain>.png when that file exists, otherwise a
// foreshortened "2.5D block" hex is synthesized in code — so the tilted map
// renders with zero committed art and real pixel tiles drop in later with no
// code change.
//
// Anchoring contract for the renderer: every texture is TileW wide and
// (TopFaceH + SkirtH) tall; the hex top-face CENTER sits at (TileW/2, TopFaceH/2).
// Draw at  AxialToWorld(tile) - new Vector2(TileW/2, TopFaceH/2) - (0, lift)  so
// the top face lands on the tile centre and the darker cliff skirt hangs below.
public sealed class TileTextureSet
{
    // Top-face bounding box matches the foreshortened hex: width 2*HexSize,
    // height 2*HexSize*VerticalScale. SkirtH is the visible cliff below it.
    public static readonly int TileW    = Mathf.RoundToInt(WorldRenderer.HexSize * 2f);
    public static readonly int TopFaceH = Mathf.RoundToInt(WorldRenderer.HexSize * 2f * WorldRenderer.VerticalScale);
    public static readonly int SkirtH   = Mathf.RoundToInt(WorldRenderer.HexSize * 0.45f);

    private readonly Dictionary<TerrainType, Texture2D> _textures = new();

    public Texture2D For(TerrainType terrain)
    {
        if (_textures.TryGetValue(terrain, out var tex)) return tex;
        tex = Resolve(terrain);
        _textures[terrain] = tex;
        return tex;
    }

    // Prefer a real tile if the artist has dropped one in; otherwise synthesize.
    private static Texture2D Resolve(TerrainType terrain)
    {
        string path = $"res://assets/art/tiles/{terrain.ToString().ToLowerInvariant()}.png";
        if (ResourceLoader.Exists(path) && ResourceLoader.Load<Texture2D>(path) is { } tile)
            return tile;
        return MakePlaceholder(terrain);
    }

    // A flat-top foreshortened hex (terrain colour) over a darker copy shifted down
    // by SkirtH, so a cliff band peeks out the bottom — a recognizable 2.5D block.
    private static Texture2D MakePlaceholder(TerrainType terrain)
    {
        int   w        = TileW;
        int   h        = TopFaceH + SkirtH;
        float size     = WorldRenderer.HexSize - 1f;            // small inset for a gap
        float cx       = w * 0.5f;
        float cyTop    = TopFaceH * 0.5f;
        Color top      = WorldRenderer.TerrainColor(terrain);
        Color cliff    = new(top.R * 0.5f, top.G * 0.5f, top.B * 0.5f);
        Color rim      = new(top.R * 1.15f, top.G * 1.15f, top.B * 1.15f); // top-edge highlight

        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float dx = x - cx;
            float dyTop = y - cyTop;
            if (InHex(dx, dyTop, size))
                img.SetPixel(x, y, dyTop < -size * WorldRenderer.VerticalScale * 0.55f ? rim : top);
            else if (InHex(dx, dyTop - SkirtH, size))
                img.SetPixel(x, y, cliff);
        }

        return ImageTexture.CreateFromImage(img);
    }

    // Point-in-convex-hexagon for the foreshortened flat-top hex (dx,dy relative
    // to centre). Same vertex layout as WorldRenderer.HexVertices.
    private static bool InHex(float dx, float dy, float size)
    {
        var p    = new Vector2(dx, dy);
        var prev = Vertex(5, size);
        bool? positive = null;
        for (int i = 0; i < 6; i++)
        {
            var cur  = Vertex(i, size);
            var edge = cur - prev;
            var to   = p - prev;
            float cross = edge.X * to.Y - edge.Y * to.X;
            if (cross != 0f)
            {
                bool s = cross > 0f;
                if (positive == null) positive = s;
                else if (positive != s) return false;
            }
            prev = cur;
        }
        return true;
    }

    private static Vector2 Vertex(int i, float size)
    {
        float a = Mathf.DegToRad(60f * i);
        return new Vector2(MathF.Cos(a) * size, MathF.Sin(a) * size * WorldRenderer.VerticalScale);
    }
}
