using System;
using System.Collections.Generic;
using Godot;
using NWO.Core;
using NWO.Entities;
using NWO.Map;

namespace NWO.UI;

// Scaled overview of the whole map drawn in a fixed HUD rect. Shows terrain for
// discovered tiles (dark for the undiscovered), city/unit dots, and the current
// camera viewport as a white outline. Click anywhere to recenter the main
// camera on that spot.
//
// Reads from GameState each frame (cheap: a few thousand small rects) and never
// mutates it. Initialize() is called once by WorldMap with the world camera and
// a recenter callback.
public partial class MinimapController : Control
{
    private GameState        _state         = null!;
    private Player           _viewer        = null!;
    private Camera2D         _camera        = null!;
    private Action<Vector2>  _onRecenter    = null!;

    // World-space bounding box of all tiles, and the fit transform into this
    // control's local pixel rect (computed once the map is known).
    private Vector2 _worldMin;
    private Vector2 _worldSize;
    private float   _scale;
    private Vector2 _offset;
    private Vector2 _cell;

    // Precomputed tile world positions (constant for the map's lifetime).
    private readonly List<(Vector2I Axial, Vector2 World)> _tiles = new();

    public void Initialize(GameState state, Player viewer, Camera2D camera, Action<Vector2> onRecenter)
    {
        _state      = state;
        _viewer     = viewer;
        _camera     = camera;
        _onRecenter = onRecenter;

        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        foreach (var axial in _state.Map.Tiles.Keys)
        {
            var w = Flatten(WorldRenderer.AxialToWorld(axial));
            _tiles.Add((axial, w));
            min = new Vector2(Mathf.Min(min.X, w.X), Mathf.Min(min.Y, w.Y));
            max = new Vector2(Mathf.Max(max.X, w.X), Mathf.Max(max.Y, w.Y));
        }
        _worldMin  = min;
        _worldSize = max - min;
        Recompute();
        Resized += Recompute;
    }

    // Fit the world bounds into the control rect, preserving aspect ratio.
    private void Recompute()
    {
        if (_worldSize.X <= 0 || _worldSize.Y <= 0) return;
        var size = Size;
        _scale  = Mathf.Min(size.X / _worldSize.X, size.Y / _worldSize.Y);
        _offset = (size - _worldSize * _scale) * 0.5f;
        // Slightly oversized cells so the staggered hex grid tiles without gaps.
        _cell = new Vector2(WorldRenderer.HexSize * 1.5f, WorldRenderer.HexSize * 1.8f) * _scale;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_state != null && Visible) QueueRedraw(); // camera viewport tracks every frame
    }

    public override void _Draw()
    {
        if (_state == null) return;
        var fog = _state.Fog(_viewer);

        // Backdrop so undiscovered area reads as solid black, not transparent.
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.05f, 0.05f, 0.07f));

        // 1. Terrain (discovered) / dim (seen-but-not-visible handled by alpha).
        foreach (var (axial, world) in _tiles)
        {
            if (!fog.IsDiscovered(axial)) continue;
            var col = WorldRenderer.TerrainColor(_state.Map.Tiles[axial]);
            if (!fog.IsVisible(axial)) col = col.Darkened(0.45f);
            DrawRect(CellRect(world), col);
        }

        // 2. Cities — small white squares (own tinted by owner color).
        foreach (var city in _state.Cities)
        {
            if (!fog.IsDiscovered(city.Position)) continue;
            var p = ToLocal(Flatten(WorldRenderer.AxialToWorld(city.Position)));
            DrawRect(new Rect2(p - new Vector2(2.5f, 2.5f), new Vector2(5f, 5f)), city.Owner.Color);
            DrawRect(new Rect2(p - new Vector2(2.5f, 2.5f), new Vector2(5f, 5f)), Colors.White, false, 1f);
        }

        // 3. Units — owner-colored dots (only where currently visible).
        foreach (var unit in _state.Units)
        {
            if (!fog.IsVisible(unit.Position)) continue;
            DrawCircle(ToLocal(Flatten(WorldRenderer.AxialToWorld(unit.Position))), 1.8f, unit.Owner.Color);
        }

        // 4. Camera viewport outline (flattened into the un-squashed minimap space).
        var center  = _camera.GetScreenCenterPosition();
        var halfView = GetViewportRect().Size * 0.5f / _camera.Zoom;
        var tl = ToLocal(Flatten(center - halfView));
        var br = ToLocal(Flatten(center + halfView));
        DrawRect(new Rect2(tl, br - tl), Colors.White, false, 1.5f);

        // Frame.
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(1, 1, 1, 0.6f), false, 1f);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
        {
            // ToWorld yields flattened space; the camera lives in foreshortened space.
            _onRecenter(Unflatten(ToWorld(mb.Position)));
            AcceptEvent();
        }
    }

    private Rect2 CellRect(Vector2 world)
    {
        var p = ToLocal(world);
        return new Rect2(p - _cell * 0.5f, _cell);
    }

    private Vector2 ToLocal(Vector2 world) => (world - _worldMin) * _scale + _offset;
    private Vector2 ToWorld(Vector2 local) => (local - _offset) / _scale + _worldMin;

    // The main view is vertically foreshortened (WorldRenderer.VerticalScale) for the
    // tilted look; the minimap undoes that so it reads as a flat top-down overview.
    // All minimap layout happens in this "flattened" space — only the recenter
    // callback converts back to the camera's (foreshortened) world space.
    private static Vector2 Flatten(Vector2 world)  => new(world.X, world.Y / WorldRenderer.VerticalScale);
    private static Vector2 Unflatten(Vector2 flat) => new(flat.X,  flat.Y * WorldRenderer.VerticalScale);
}
