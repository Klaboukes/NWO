using System.Collections.Generic;
using Godot;
using NWO.Core;
using NWO.Entities;

namespace NWO.Map;

// 3D world view (Phase 7 V7.1–V7.3): builds the terrain as hex-prism MeshInstance3Ds
// and draws units/cities as billboard Sprite3Ds, all under a fixed-tilt Camera3D.
// Owns no gameplay state — it reads GameState / MovementAnimator and rebuilds the
// view on Refresh(). The fiddly 2D bits (selection rings, HP bars, glyphs, range
// overlays) live in WorldOverlay, drawn on top via the camera's UnprojectPosition.
//
// Fog: undiscovered tiles hide their prism (the black environment shows through);
// the "explored but not currently visible" dimming is painted by WorldOverlay.
// V7.3: each unit type and city variant uses a distinct sprite from
// UnitTextureRegistry / CityTextureRegistry (placeholder-first, real PNGs override).
public partial class WorldRenderer : Node3D
{
    private GameState        _state    = null!;
    private MovementAnimator _animator = null!;
    private Player           _viewer   = null!;

    private readonly TerrainMeshFactory _terrain = new();
    private readonly Dictionary<Vector2I, MeshInstance3D> _tiles   = new();
    private readonly Dictionary<Unit, Sprite3D>           _units   = new();
    private readonly Dictionary<City, Sprite3D>           _cities  = new();
    // Owner-colour banners, shown only beside full-colour (real-art) sprites.
    private readonly Dictionary<Unit, Sprite3D>           _uBanner = new();
    private readonly Dictionary<City, Sprite3D>           _cBanner = new();

    // World width of a unit/city sprite (texture is 128px square).
    private const float UnitSizeWorld   = HexSizeRef * 0.80f;
    private const float CitySizeWorld   = HexSizeRef * 1.05f;
    private const float BannerSizeWorld = HexSizeRef * 0.50f;
    private const float HexSizeRef      = HexProjection.HexSize;

    public void Initialize(GameState state, MovementAnimator animator, Player viewer)
    {
        _state    = state;
        _animator = animator;
        _viewer   = viewer;

        BuildTerrain();
        Refresh();
    }

    private void BuildTerrain()
    {
        foreach (var (axial, terrain) in _state.Map.Tiles)
        {
            var mi = new MeshInstance3D
            {
                Mesh     = _terrain.For(terrain, _state.Map.IsHill(axial)),
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
        Prune(_uBanner, _state.Units);

        foreach (var unit in _state.Units)
        {
            bool visible = fog.IsVisible(unit.Position);
            bool realArt = UnitTextureRegistry.IsRealArt(unit.Data.Id);
            var sprite = Ensure(_units, unit, UnitTextureRegistry.For(unit.Data.Id), UnitSizeWorld);
            sprite.Visible  = visible;

            // Banner only accompanies full-colour art; placeholders are tinted whole.
            var banner = realArt ? EnsureBanner(_uBanner, unit) : null;
            if (banner != null) banner.Visible = visible;
            if (!visible) continue;

            var top = TileTop(unit == _animator.AnimatingUnit
                ? _animator.CurrentTile
                : unit.Position);
            // While animating, ride the interpolated X/Z but keep the tile-top Y.
            Vector3 baseXZ;
            if (unit == _animator.AnimatingUnit)
            {
                var p = _animator.CurrentWorldPos;
                baseXZ = new Vector3(p.X, top.Y, p.Z);
            }
            else
            {
                baseXZ = top;
            }
            sprite.Position = baseXZ + new Vector3(0f, UnitSizeWorld * 0.5f, 0f);
            // Real art keeps its own colours; the banner carries the owner tint.
            sprite.Modulate = realArt ? Colors.White : unit.Owner.Color;
            if (banner != null)
            {
                banner.Position = baseXZ + new Vector3(0f, BannerSizeWorld * 0.5f, 0f);
                banner.Modulate = unit.Owner.Color;
            }
        }
    }

    private void RefreshCities(FogOfWar fog)
    {
        Prune(_cities, _state.Cities);
        Prune(_cBanner, _state.Cities);

        foreach (var city in _state.Cities)
        {
            bool discovered = fog.IsDiscovered(city.Position);
            bool realArt = CityTextureRegistry.HasRealArt(city.IsCapital);
            var sprite = Ensure(_cities, city, CityTextureRegistry.For(city.IsCapital), CitySizeWorld);
            sprite.Visible = discovered;

            var banner = realArt ? EnsureBanner(_cBanner, city) : null;
            if (banner != null) banner.Visible = discovered;
            if (!discovered) continue;

            bool seen = fog.IsVisible(city.Position);
            var top = TileTop(city.Position);
            sprite.Position = top + new Vector3(0f, CitySizeWorld * 0.5f, 0f);
            var col = city.Owner.Color;
            // Real art keeps its colours (dimmed when out of sight); banner carries owner tint.
            sprite.Modulate = realArt
                ? (seen ? Colors.White : Colors.White.Darkened(0.45f))
                : (seen ? col : col.Darkened(0.45f));
            if (banner != null)
            {
                banner.Position = top + new Vector3(0f, BannerSizeWorld * 0.5f, 0f);
                banner.Modulate = seen ? col : col.Darkened(0.45f);
            }
        }
    }

    // World-space tile-top centre (X/Z from the axial projection, Y from the
    // terrain's prism height).
    private Vector3 TileTop(Vector2I axial)
    {
        var w = HexProjection.AxialToWorld(axial);
        if (_state.Map.Tiles.TryGetValue(axial, out var terrain))
            w.Y = HexProjection.TopHeight(terrain, _state.Map.IsHill(axial));
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
            PixelSize     = worldSize / 128f, // sprites are 128px square
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            RenderPriority = 1,
        };
        AddChild(s);
        map[key] = s;
        return s;
    }

    // Lazily create the shared owner-colour banner billboard for an entity. Drawn
    // above the body sprite (higher RenderPriority) at the unit/city base.
    private Sprite3D EnsureBanner<T>(Dictionary<T, Sprite3D> map, T key) where T : notnull
    {
        if (map.TryGetValue(key, out var s)) return s;
        s = new Sprite3D
        {
            Texture        = BannerTextureRegistry.Banner(),
            Billboard      = BaseMaterial3D.BillboardModeEnum.Enabled,
            Shaded         = false,
            PixelSize      = BannerSizeWorld / 128f,
            TextureFilter  = BaseMaterial3D.TextureFilterEnum.Nearest,
            RenderPriority = 2,
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
}
