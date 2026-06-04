using Godot;
using NWO.Core;
using NWO.Entities;

namespace NWO.Map;

// Screen-space overlay drawn on top of the 3D world (Phase 7 V7.1). Terrain and
// unit/city bodies are real 3D (WorldRenderer); this layer paints the bits that
// are far easier in 2D — movement range, workforce/path overlays, fog dimming,
// selection rings, HP bars, glyphs, and the combat flash — by projecting each
// element's 3D anchor to the screen with Camera3D.UnprojectPosition. Because the
// camera tilt is fixed, perspective just works: nearer tiles project larger.
//
// Reads GameState / SelectionState / MovementAnimator; never mutates them.
public partial class WorldOverlay : Node2D
{
    private const float CombatFlashSecs = 0.4f;
    private const float Gap             = 1f;
    private const float UnitSizeWorld   = HexProjection.HexSize * 0.80f;

    private GameState        _state     = null!;
    private SelectionState   _selection = null!;
    private MovementAnimator _animator  = null!;
    private Player           _viewer    = null!;
    private Camera3D         _camera    = null!;

    private Vector2I? _flashAttacker;
    private Vector2I? _flashDefender;
    private float     _flashTimeLeft;

    public void Initialize(
        GameState state, SelectionState selection,
        MovementAnimator animator, Player viewer, Camera3D camera)
    {
        _state     = state;
        _selection = selection;
        _animator  = animator;
        _viewer    = viewer;
        _camera    = camera;
        TextureFilter = TextureFilterEnum.Nearest; // crisp pixel-art icons (resources)
    }

    public void FlashCombat(Vector2I attacker, Vector2I defender)
    {
        _flashAttacker = attacker;
        _flashDefender = defender;
        _flashTimeLeft = CombatFlashSecs;
    }

    public override void _Process(double delta)
    {
        if (_state == null) return;
        if (_flashTimeLeft > 0f)
        {
            _flashTimeLeft -= (float)delta;
            if (_flashTimeLeft <= 0f) { _flashAttacker = null; _flashDefender = null; }
        }
        // Redraw every frame so overlays track camera pan/zoom and animation.
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_state == null || _camera == null) return;
        var fog = _state.Fog(_viewer);

        DrawFogDimming(fog);
        DrawRivers(fog);
        DrawResources(fog);
        DrawImprovements(fog);
        DrawMovementRange();
        DrawWorkforce();
        DrawPathPreview();
        DrawCities(fog);
        DrawUnits(fog);
        DrawCombatFlash();
    }

    // ── Tile-anchored overlay passes ───────────────────────────────────────────

    // "Explored but not currently visible" tiles get a translucent dark hex.
    private void DrawFogDimming(FogOfWar fog)
    {
        var dim = new Color(0f, 0f, 0f, 0.45f);
        foreach (var (axial, _) in _state.Map.Tiles)
            if (fog.IsDiscovered(axial) && !fog.IsVisible(axial))
                FillHex(axial, HexProjection.HexSize - Gap, dim);
    }

    // River edges as blue channels along the shared tile boundary (Phase 9.4). Drawn
    // as a dark outline under a bright core so they read against both land and coast,
    // and scaled with zoom so they stay visible.
    private void DrawRivers(FogOfWar fog)
    {
        var core    = new Color(0.35f, 0.70f, 1.00f, 0.95f);
        var outline = new Color(0.04f, 0.18f, 0.42f, 0.90f);
        float radius = HexProjection.HexSize;
        foreach (var (tile, dir) in _state.Map.Rivers)
        {
            // The edge is stored on one side; show it if either bordering tile is seen.
            if (!fog.IsDiscovered(tile) && !fog.IsDiscovered(tile + HexGrid.Directions[dir])) continue;
            // Flat-top: edge facing direction dir is edge (6-dir)%6, joining corners e and e+1.
            int e  = (6 - dir) % 6;
            var wA = RiverCorner(tile, e,           radius);
            var wB = RiverCorner(tile, (e + 1) % 6, radius);
            if (_camera.IsPositionBehind(wA) || _camera.IsPositionBehind(wB)) continue;
            var a = _camera.UnprojectPosition(wA);
            var b = _camera.UnprojectPosition(wB);
            float w = Mathf.Max(3f, 5f * ScaleAt(RiverCorner(tile, e, radius)));
            DrawLine(a, b, outline, w + 2f);
            DrawLine(a, b, core,    w);
        }
    }

    // World position of a hex corner, lifted to the tallest tile sharing that vertex.
    // Computing height per-vertex (not per-owning-tile) makes adjacent river segments
    // meet at exactly the same point, so the channel reads as one continuous line and
    // rides on top of terrain instead of sinking into a neighbouring cliff.
    private Vector3 RiverCorner(Vector2I tile, int corner, float radius)
    {
        var (a, b, c) = HexGrid.CornerTiles(tile, corner);
        float y = Mathf.Max(TileTop(a).Y, Mathf.Max(TileTop(b).Y, TileTop(c).Y));
        var w = HexProjection.AxialToWorld(tile) + HexProjection.Corner(corner, radius);
        w.Y = y;
        return w;
    }

    private void DrawResources(FogOfWar fog)
    {
        foreach (var (axial, res) in _state.Map.Resources)
        {
            if (!fog.IsDiscovered(axial)) continue;
            if (!ResourceService.IsRevealed(_state, _viewer, res)) continue;
            var (p, scale) = Anchor(axial, new Vector3(-HexProjection.HexSize * 0.30f, 0f, HexProjection.HexSize * 0.32f));
            if (p == null) continue;
            var tex  = ResourceIconRegistry.For(res);
            float sz = HexProjection.HexSize * 0.46f * scale;
            DrawTextureRect(tex, new Rect2(p.Value - new Vector2(sz * 0.5f, sz * 0.5f), new Vector2(sz, sz)), false);
        }
    }

    private void DrawImprovements(FogOfWar fog)
    {
        foreach (var (axial, imp) in _state.Map.Improvements)
        {
            if (!fog.IsDiscovered(axial)) continue;
            var (p, scale) = Anchor(axial, new Vector3(HexProjection.HexSize * 0.18f, 0f, HexProjection.HexSize * 0.18f));
            if (p == null) continue;
            (string glyph, Color col) = imp switch
            {
                ImprovementType.Farm    => ("F", new Color(0.95f, 0.85f, 0.30f)),
                ImprovementType.Mine    => ("M", new Color(0.75f, 0.70f, 0.65f)),
                ImprovementType.Pasture => ("P", new Color(0.80f, 0.65f, 0.45f)),
                ImprovementType.Road    => ("=", new Color(0.55f, 0.45f, 0.35f)),
                _                       => ("", Colors.White),
            };
            if (glyph.Length > 0)
                DrawString(ThemeDB.FallbackFont, p.Value, glyph, HorizontalAlignment.Left, -1, FontPx(12, scale), col);
        }
    }

    private void DrawMovementRange()
    {
        if (_selection.ReachableTiles.Count == 0) return;
        var reachable = _selection.ReachableTiles;
        var color = new Color(1f, 0.95f, 0.15f, 0.90f);
        float radius = HexProjection.HexSize - Gap;
        bool hasUnit = _selection.Unit != null;
        var  unitPos = hasUnit ? _selection.Unit!.Position : default;

        foreach (var axial in reachable)
        {
            var top = TileTop(axial);
            if (_camera.IsPositionBehind(top)) continue;

            for (int d = 0; d < 6; d++)
            {
                var neighbor = axial + HexGrid.Directions[d];
                if (reachable.Contains(neighbor)) continue;
                if (hasUnit && neighbor == unitPos) continue; // unit's tile not in set but is "occupied origin"

                // Flat-top: edge facing direction d is edge (6-d)%6, connecting corners e and e+1.
                int e = (6 - d) % 6;
                var wA = top + HexProjection.Corner(e,           radius);
                var wB = top + HexProjection.Corner((e + 1) % 6, radius);
                if (_camera.IsPositionBehind(wA) || _camera.IsPositionBehind(wB)) continue;
                DrawLine(_camera.UnprojectPosition(wA), _camera.UnprojectPosition(wB), color, 2.5f);
            }
        }
    }

    private void DrawWorkforce()
    {
        if (_selection.City is not { } city || city.Owner != _viewer) return;
        foreach (var tile in CityWorkforceService.Workable(_state, city))
        {
            bool assigned = city.Workforce.Assigned.Contains(tile);
            bool locked   = city.Workforce.Locked.Contains(tile);
            var tint = assigned ? new Color(0.35f, 0.85f, 0.35f, 0.30f)
                                : new Color(0.45f, 0.75f, 1f,   0.18f);
            FillHex(tile, HexProjection.HexSize - Gap, tint);
            if (assigned)
            {
                var (p, scale) = Anchor(tile, new Vector3(HexProjection.HexSize * 0.30f, 0f, -HexProjection.HexSize * 0.30f));
                if (p != null) DrawCircle(p.Value, HexProjection.HexSize * 0.10f * scale, Colors.White);
            }
            if (locked)
            {
                var (p, scale) = Anchor(tile, new Vector3(-HexProjection.HexSize * 0.30f, 0f, -HexProjection.HexSize * 0.30f));
                if (p != null) DrawCircle(p.Value, HexProjection.HexSize * 0.10f * scale, Colors.Gold);
            }
        }
    }

    private void DrawPathPreview()
    {
        if (_selection.PendingPathPreview is not { } preview) return;
        for (int i = 1; i < preview.Count; i++)
            FillHex(preview[i], HexProjection.HexSize - Gap, new Color(1f, 0.55f, 0f, 0.45f));
        for (int i = 0; i < preview.Count - 1; i++)
        {
            var a = Project(TileTop(preview[i]));
            var b = Project(TileTop(preview[i + 1]));
            if (a != null && b != null) DrawLine(a.Value, b.Value, Colors.Orange, 2.5f);
        }
        var (end, scale) = Anchor(preview[^1], Vector3.Zero);
        if (end != null) DrawCircle(end.Value, HexProjection.HexSize * 0.22f * scale, Colors.Orange);
    }

    private void DrawCities(FogOfWar fog)
    {
        foreach (var city in _state.Cities)
        {
            if (!fog.IsDiscovered(city.Position)) continue;
            bool seen   = fog.IsVisible(city.Position);
            var anchor  = TileTop(city.Position) + new Vector3(0f, HexProjection.HexSize * 1.05f * 0.5f, 0f);
            var p       = Project(anchor);
            if (p == null) continue;
            float scale = ScaleAt(TileTop(city.Position));

            if (city == _selection.City)
            {
                float half = HexProjection.HexSize * 0.42f * scale;
                DrawRect(new Rect2(p.Value - new Vector2(half, half), new Vector2(half * 2f, half * 2f)),
                    Colors.White, false, 2f);
            }
            if (seen)
                DrawString(ThemeDB.FallbackFont, p.Value + new Vector2(-HexProjection.HexSize * 0.35f * scale, -HexProjection.HexSize * 0.55f * scale),
                    $"{city.Name} ({city.Population})", HorizontalAlignment.Left, -1, FontPx(11, scale), Colors.White);
            if (seen && city.HP < City.MaxHP)
                DrawHpBar(p.Value - new Vector2(0f, HexProjection.HexSize * 0.62f * scale), city.HP / (float)City.MaxHP, HexProjection.HexSize * 0.72f * scale);
        }
    }

    private void DrawUnits(FogOfWar fog)
    {
        foreach (var unit in _state.Units)
        {
            if (!fog.IsVisible(unit.Position)) continue;
            var anchorWorld = UnitAnchorWorld(unit);
            var p = Project(anchorWorld);
            if (p == null) continue;
            float scale = ScaleAt(TileTop(unit == _animator.AnimatingUnit ? _animator.CurrentTile : unit.Position));

            DrawString(ThemeDB.FallbackFont, p.Value + new Vector2(-4f * scale, 5f * scale),
                unit.Data.Name[..1], HorizontalAlignment.Left, -1, FontPx(14, scale), new Color(0.1f, 0.1f, 0.1f));
            if (unit == _selection.Unit)
                DrawArc(p.Value, HexProjection.HexSize * 0.46f * scale, 0f, Mathf.Tau, 24, Colors.Yellow, 2.5f);
            if (unit.MovementRemaining == 0)
                DrawCircle(p.Value, HexProjection.HexSize * 0.40f * scale, new Color(0f, 0f, 0f, 0.40f));
            if (unit.Fortified)
                DrawArc(p.Value, HexProjection.HexSize * 0.44f * scale, 0f, Mathf.Tau, 24, new Color(0.4f, 0.8f, 1f), 1.5f);
            if (unit.HP < Unit.MaxHP)
                DrawHpBar(p.Value - new Vector2(0f, HexProjection.HexSize * 0.55f * scale), unit.HP / (float)Unit.MaxHP, HexProjection.HexSize * 0.6f * scale);
            // Cargo badge: show "N/M" below transport ships so the player knows how many
            // units are aboard and how many slots remain.
            if (unit.Data.CargoCapacity > 0)
            {
                var badge = $"{unit.Cargo.Count}/{unit.Data.CargoCapacity}";
                DrawString(ThemeDB.FallbackFont,
                    p.Value + new Vector2(-5f * scale, HexProjection.HexSize * 0.35f * scale),
                    badge, HorizontalAlignment.Left, -1, FontPx(9, scale), new Color(0.9f, 0.9f, 0.1f));
            }
        }
    }

    private void DrawCombatFlash()
    {
        if (_flashTimeLeft <= 0f) return;
        float alpha = Mathf.Clamp(_flashTimeLeft / CombatFlashSecs, 0f, 1f) * 0.6f;
        var col = new Color(1f, 0.2f, 0.2f, alpha);
        if (_flashAttacker is { } a) FillHex(a, HexProjection.HexSize - Gap, col);
        if (_flashDefender is { } d) FillHex(d, HexProjection.HexSize - Gap, col);
    }

    // ── Projection helpers ──────────────────────────────────────────────────────

    // Tile-top world centre (X/Z from axial, Y from the prism height).
    private Vector3 TileTop(Vector2I axial)
    {
        var w = HexProjection.AxialToWorld(axial);
        if (_state.Map.Tiles.TryGetValue(axial, out var terrain))
            w.Y = HexProjection.TopHeight(terrain, _state.Map.IsHill(axial));
        return w;
    }

    private Vector3 UnitAnchorWorld(Unit unit)
    {
        if (unit == _animator.AnimatingUnit)
        {
            var pos = _animator.CurrentWorldPos;
            float y = TileTop(_animator.CurrentTile).Y + UnitSizeWorld * 0.5f;
            return new Vector3(pos.X, y, pos.Z);
        }
        return TileTop(unit.Position) + new Vector3(0f, UnitSizeWorld * 0.5f, 0f);
    }

    // Screen position + local px-per-world scale for a tile, offset within its top
    // plane. Returns null when behind the camera (don't draw).
    private (Vector2? pos, float scale) Anchor(Vector2I axial, Vector3 planeOffset)
    {
        var top   = TileTop(axial);
        var world = top + planeOffset;
        return (Project(world), ScaleAt(top));
    }

    private Vector2? Project(Vector3 world)
        => _camera.IsPositionBehind(world) ? null : _camera.UnprojectPosition(world);

    // Pixels per world-unit at a given tile top (drives radii/line widths/fonts so
    // overlays scale with zoom and perspective like the old Camera2D zoom did).
    private float ScaleAt(Vector3 worldTop)
    {
        if (_camera.IsPositionBehind(worldTop)) return 1f;
        var a = _camera.UnprojectPosition(worldTop);
        var b = _camera.UnprojectPosition(worldTop + new Vector3(HexProjection.HexSize, 0f, 0f));
        float px = a.DistanceTo(b);
        return px <= 0.001f ? 1f : px / HexProjection.HexSize;
    }

    private static int FontPx(int basePx, float scale) => Mathf.Clamp(Mathf.RoundToInt(basePx * scale), 6, 48);

    // Fill a tile's top-face hexagon (projected), used for range/fog/path/flash.
    private void FillHex(Vector2I axial, float size, Color col)
    {
        var top = TileTop(axial);
        if (_camera.IsPositionBehind(top)) return;
        var pts = new Vector2[6];
        for (int i = 0; i < 6; i++)
        {
            var w = top + HexProjection.Corner(i, size);
            if (_camera.IsPositionBehind(w)) return;
            pts[i] = _camera.UnprojectPosition(w);
        }
        DrawColoredPolygon(pts, col);
    }

    private void DrawHpBar(Vector2 topCenter, float frac, float width)
    {
        frac = Mathf.Clamp(frac, 0f, 1f);
        float h = Mathf.Max(2f, 3f * width / (HexProjection.HexSize * 0.6f));
        var bg = new Rect2(topCenter - new Vector2(width * 0.5f, 0f), new Vector2(width, h));
        DrawRect(bg, new Color(0f, 0f, 0f, 0.7f));
        var col = frac > 0.5f ? Colors.LimeGreen : frac > 0.25f ? Colors.Yellow : Colors.Red;
        DrawRect(new Rect2(bg.Position, new Vector2(width * frac, h)), col);
    }
}
