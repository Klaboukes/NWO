using System.Collections.Generic;
using Godot;
using NWO.Core;
using NWO.Entities;

namespace NWO.Map;

// 3D world view (Phase 7 V7.1): builds the terrain as hex-prism MeshInstance3Ds
// and draws units/cities as billboard Sprite3Ds, all under a fixed-tilt Camera3D.
// Owns no gameplay state — it reads GameState / MovementAnimator and rebuilds the
// view on Refresh(). The fiddly 2D bits (selection rings, HP bars, glyphs, range
// overlays) live in WorldOverlay, drawn on top via the camera's UnprojectPosition.
//
// Fog: undiscovered tiles hide their prism (the black environment shows through);
// the "explored but not currently visible" dimming is painted by WorldOverlay.
public partial class WorldRenderer : Node3D
{
    private GameState        _state    = null!;
    private MovementAnimator _animator = null!;
    private Player           _viewer   = null!;

    private readonly TerrainMeshFactory _terrain = new();
    private readonly Dictionary<Vector2I, MeshInstance3D> _tiles  = new();
    private readonly Dictionary<Unit, Sprite3D>           _units  = new();
    private readonly Dictionary<City, Sprite3D>           _cities = new();

    private ImageTexture _unitToken = null!;
    private ImageTexture _cityToken = null!;

    // World width of a unit/city token (texture is 64px square).
    private const float UnitSizeWorld = HexSizeRef * 0.80f;
    private const float CitySizeWorld = HexSizeRef * 1.05f;
    private const float HexSizeRef    = HexProjection.HexSize;

    public void Initialize(GameState state, MovementAnimator animator, Player viewer)
    {
        _state    = state;
        _animator = animator;
        _viewer   = viewer;

        _unitToken = MakeDiscToken();
        _cityToken = MakeSquareToken();

        BuildTerrain();
        Refresh();
    }

    private void BuildTerrain()
    {
        foreach (var (axial, terrain) in _state.Map.Tiles)
        {
            var mi = new MeshInstance3D
            {
                Mesh     = _terrain.For(terrain),
                Position = HexProjection.AxialToWorld(axial),
            };
            AddChild(mi);
            _tiles[axial] = mi;
        }
    }

    // Update fog visibility on terrain prisms and reconcile unit/city billboards
    // (position, owner tint, visibility). Called whenever the view changes.
    public void Refresh()
    {
        if (_state == null) return;
        var fog = _state.Fog(_viewer);

        foreach (var (axial, mi) in _tiles)
            mi.Visible = fog.IsDiscovered(axial);

        RefreshUnits(fog);
        RefreshCities(fog);
    }

    private void RefreshUnits(FogOfWar fog)
    {
        // Drop billboards for units that no longer exist.
        Prune(_units, _state.Units);

        foreach (var unit in _state.Units)
        {
            bool visible = fog.IsVisible(unit.Position);
            var sprite = Ensure(_units, unit, _unitToken, UnitSizeWorld);
            sprite.Visible  = visible;
            if (!visible) continue;

            var top = TileTop(unit == _animator.AnimatingUnit
                ? _animator.CurrentTile
                : unit.Position);
            // While animating, ride the interpolated X/Z but keep the tile-top Y.
            if (unit == _animator.AnimatingUnit)
            {
                var p = _animator.CurrentWorldPos;
                sprite.Position = new Vector3(p.X, top.Y + UnitSizeWorld * 0.5f, p.Z);
            }
            else
            {
                sprite.Position = top + new Vector3(0f, UnitSizeWorld * 0.5f, 0f);
            }
            sprite.Modulate = unit.Owner.Color;
        }
    }

    private void RefreshCities(FogOfWar fog)
    {
        Prune(_cities, _state.Cities);

        foreach (var city in _state.Cities)
        {
            bool discovered = fog.IsDiscovered(city.Position);
            var sprite = Ensure(_cities, city, _cityToken, CitySizeWorld);
            sprite.Visible = discovered;
            if (!discovered) continue;

            bool seen = fog.IsVisible(city.Position);
            sprite.Position = TileTop(city.Position) + new Vector3(0f, CitySizeWorld * 0.5f, 0f);
            var col = city.Owner.Color;
            sprite.Modulate = seen ? col : col.Darkened(0.45f);
        }
    }

    // World-space tile-top centre (X/Z from the axial projection, Y from the
    // terrain's prism height).
    private Vector3 TileTop(Vector2I axial)
    {
        var w = HexProjection.AxialToWorld(axial);
        if (_state.Map.Tiles.TryGetValue(axial, out var terrain))
            w.Y = HexProjection.TopHeight(terrain);
        return w;
    }

    private Sprite3D Ensure<T>(Dictionary<T, Sprite3D> map, T key, Texture2D tex, float worldSize)
        where T : notnull
    {
        if (map.TryGetValue(key, out var s)) return s;
        s = new Sprite3D
        {
            Texture       = tex,
            Billboard     = BaseMaterial3D.BillboardModeEnum.Enabled,
            Shaded        = false,
            PixelSize     = worldSize / 64f, // tokens are 64px square
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            RenderPriority = 1,
        };
        AddChild(s);
        map[key] = s;
        return s;
    }

    // Remove billboards whose backing entity is gone (killed unit / razed city).
    private void Prune<T>(Dictionary<T, Sprite3D> map, IEnumerable<T> live) where T : notnull
    {
        var alive = new HashSet<T>(live);
        var stale = new List<T>();
        foreach (var key in map.Keys)
            if (!alive.Contains(key)) stale.Add(key);
        foreach (var key in stale)
        {
            map[key].QueueFree();
            map.Remove(key);
        }
    }

    // ── Placeholder billboard tokens (owner-tinted via Sprite3D.Modulate) ──
    // Real art drops in at V7.3 by overriding the Texture per unit/city type.

    private static ImageTexture MakeDiscToken()
    {
        const int s = 64; const float c = 31.5f; const float r = 28f;
        var img = Image.CreateEmpty(s, s, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float d = new Vector2(x - c, y - c).Length();
            if (d <= r - 3f)      img.SetPixel(x, y, Colors.White);
            else if (d <= r)      img.SetPixel(x, y, new Color(0.1f, 0.1f, 0.1f)); // dark ring
        }
        return ImageTexture.CreateFromImage(img);
    }

    private static ImageTexture MakeSquareToken()
    {
        const int s = 64;
        var img = Image.CreateEmpty(s, s, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            bool border = x < 6 || x >= s - 6 || y < 6 || y >= s - 6;
            bool inner  = x >= 4 && x < s - 4 && y >= 4 && y < s - 4;
            if (!inner) continue;
            img.SetPixel(x, y, border ? new Color(0.1f, 0.1f, 0.1f) : Colors.White);
        }
        return ImageTexture.CreateFromImage(img);
    }
}
