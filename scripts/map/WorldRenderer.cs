using System.Collections.Generic;
using Godot;
using NWO.Core;
using NWO.Entities;

namespace NWO.Map;

// Pure renderer: reads from GameState, SelectionState, MovementAnimator and
// draws terrain, units, cities, fog, and the pending-move preview. Owns no
// gameplay state and never mutates the world — call QueueRedraw() from outside
// when something visible changes.
public partial class WorldRenderer : Node2D
{
    public const  float HexSize           = 32f;
    private const float HexGap            = 1f;
    private const float CombatFlashSecs   = 0.4f;

    // Baked 2.5D look: squash the vertical axis so the grid reads as a tilted
    // ("Civ5-style") view rather than straight-down. Folded into AxialToWorld so
    // the projection stays invertible — WorldToAxial divides it back out, keeping
    // mouse picking and movement animation correct.
    public const float VerticalScale = 0.62f;

    private GameState        _state            = null!;
    private SelectionState   _selection        = null!;
    private MovementAnimator _animator         = null!;
    private Player           _viewerPlayer     = null!;

    private readonly TileTextureSet           _tiles       = new();
    private List<(Vector2I axial, TerrainType terrain)> _sortedTiles = new();
    private Rect2 _mapRect; // world-space bounds (padded) for the solid fog backdrop

    private Vector2I? _flashAttacker;
    private Vector2I? _flashDefender;
    private float     _flashTimeLeft;

    public void Initialize(
        GameState state,
        SelectionState selection,
        MovementAnimator animator,
        Player viewerPlayer)
    {
        _state        = state;
        _selection    = selection;
        _animator     = animator;
        _viewerPlayer = viewerPlayer;

        // Crisp pixel-art: no bilinear smoothing on the tile/sprite textures.
        TextureFilter = TextureFilterEnum.Nearest;

        // The map is static after generation — sort tiles back-to-front once so the
        // 2.5D tiles/cliffs and lifted terrain overlap in painter's order.
        _sortedTiles = new List<(Vector2I, TerrainType)>(_state.Map.Tiles.Count);
        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        foreach (var kv in _state.Map.Tiles)
        {
            _sortedTiles.Add((kv.Key, kv.Value));
            var w = AxialToWorld(kv.Key);
            min = new Vector2(Mathf.Min(min.X, w.X), Mathf.Min(min.Y, w.Y));
            max = new Vector2(Mathf.Max(max.X, w.X), Mathf.Max(max.Y, w.Y));
        }
        _sortedTiles.Sort((a, b) =>
        {
            var wa = AxialToWorld(a.axial);
            var wb = AxialToWorld(b.axial);
            int cmp = wa.Y.CompareTo(wb.Y);
            return cmp != 0 ? cmp : wa.X.CompareTo(wb.X);
        });

        // Pad past tile half-extents, the tallest elevation lift, and the skirt so
        // the backdrop fully covers every drawn tile (incl. raised/overhanging ones).
        var pad = new Vector2(TileTextureSet.TileW, TileTextureSet.TopFaceH + TileTextureSet.SkirtH)
                  + new Vector2(0f, ElevationLift(TerrainType.Mountain));
        _mapRect = new Rect2(min - pad, (max - min) + pad * 2f);
    }

    // World-space draw centre for a tile's top face: the ground-plane projection
    // raised by the terrain's elevation lift. Overlays, sprites, and glyphs use
    // this so they ride on top of the lifted tile (picking still ignores lift).
    private Vector2 TileCenter(Vector2I axial) =>
        AxialToWorld(axial) - new Vector2(0f, LiftAt(axial));

    private float LiftAt(Vector2I axial) =>
        _state.Map.Tiles.TryGetValue(axial, out var t) ? ElevationLift(t) : 0f;

    // Briefly tints the attacker and defender tiles red. Caller invokes after combat.
    public void FlashCombat(Vector2I attacker, Vector2I defender)
    {
        _flashAttacker = attacker;
        _flashDefender = defender;
        _flashTimeLeft = CombatFlashSecs;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_flashTimeLeft <= 0f) return;
        _flashTimeLeft -= (float)delta;
        if (_flashTimeLeft <= 0f)
        {
            _flashAttacker = null;
            _flashDefender = null;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_state == null) return;

        var fog = _state.Fog(_viewerPlayer);

        // Solid backdrop so undiscovered area (and the inter-tile gaps) read as
        // clean black — never leaking the hidden terrain underneath.
        DrawRect(_mapRect, Colors.Black);

        // 1. Terrain + fog — 2.5D textured tiles, drawn back-to-front so cliffs and
        // lifted terrain overlap correctly (painter's order). Fog is drawn IN this
        // pass, per tile, using the same sprite footprint: an undiscovered tile is a
        // pure-black silhouette (no terrain to leak), and a forward tile correctly
        // paints over the skirt of a hidden tile behind it.
        var dim = new Color(0f, 0f, 0f, 0.55f);
        var anchor = new Vector2(TileTextureSet.TileW * 0.5f, TileTextureSet.TopFaceH * 0.5f);
        foreach (var (axial, terrain) in _sortedTiles)
        {
            var tex     = _tiles.For(terrain);
            var drawPos = AxialToWorld(axial) - anchor - new Vector2(0f, ElevationLift(terrain));
            if (!fog.IsDiscovered(axial))
            {
                DrawTexture(tex, drawPos, Colors.Black);   // hidden: black silhouette
                continue;
            }
            DrawTexture(tex, drawPos);
            if (!fog.IsVisible(axial))
                DrawTexture(tex, drawPos, dim);            // explored but not in sight
        }

        // 1b. Strategic resources — drawn once revealed to the viewer (has the
        // tech) and the tile has been discovered.
        foreach (var (axial, res) in _state.Map.Resources)
        {
            if (!fog.IsDiscovered(axial)) continue;
            if (!ResourceService.IsRevealed(_state, _viewerPlayer, res)) continue;
            var w   = TileCenter(axial) + new Vector2(-HexSize * 0.30f, HexSize * 0.32f);
            var col = res == ResourceType.Horses ? new Color(0.85f, 0.75f, 0.55f)
                                                 : new Color(0.55f, 0.55f, 0.62f);
            DrawCircle(w, HexSize * 0.12f, col);
            DrawCircle(w, HexSize * 0.12f, Colors.Black, false, 1f);
        }

        // 1c. Tile improvements — a small glyph at bottom-right of discovered tiles.
        foreach (var (axial, imp) in _state.Map.Improvements)
        {
            if (!fog.IsDiscovered(axial)) continue;
            var w = TileCenter(axial) + new Vector2(HexSize * 0.18f, HexSize * 0.18f);
            (string glyph, Color col) = imp switch
            {
                ImprovementType.Farm    => ("F", new Color(0.95f, 0.85f, 0.30f)),
                ImprovementType.Mine    => ("M", new Color(0.75f, 0.70f, 0.65f)),
                ImprovementType.Pasture => ("P", new Color(0.80f, 0.65f, 0.45f)),
                ImprovementType.Road    => ("=", new Color(0.55f, 0.45f, 0.35f)),
                _                       => ("", Colors.White),
            };
            if (glyph.Length > 0)
                DrawString(ThemeDB.FallbackFont, w, glyph, HorizontalAlignment.Left, -1, 12, col);
        }

        // 2. Movement range overlay
        foreach (var axial in _selection.ReachableTiles)
            DrawPolygon(HexVertices(TileCenter(axial), HexSize - HexGap),
                new[] { new Color(1f, 1f, 0.2f, 0.35f) });

        // 2a. Selected-city workable / assigned / locked tile overlays.
        if (_selection.City is { } selectedCity && selectedCity.Owner == _viewerPlayer)
        {
            foreach (var tile in CityWorkforceService.Workable(_state, selectedCity))
            {
                var w = TileCenter(tile);
                bool assigned = selectedCity.Workforce.Assigned.Contains(tile);
                bool locked   = selectedCity.Workforce.Locked.Contains(tile);
                var tint = assigned
                    ? new Color(0.35f, 0.85f, 0.35f, 0.30f)
                    : new Color(0.45f, 0.75f, 1f,   0.18f);
                DrawPolygon(HexVertices(w, HexSize - HexGap), new[] { tint });
                if (assigned)
                    DrawCircle(w + new Vector2(HexSize * 0.30f, -HexSize * 0.30f),
                        HexSize * 0.10f, Colors.White);
                if (locked)
                    DrawCircle(w + new Vector2(-HexSize * 0.30f, -HexSize * 0.30f),
                        HexSize * 0.10f, Colors.Gold);
            }
        }

        // 2b. Pending move path preview
        if (_selection.PendingPathPreview is { } preview)
        {
            for (int i = 1; i < preview.Count; i++)
                DrawPolygon(HexVertices(TileCenter(preview[i]), HexSize - HexGap),
                    new[] { new Color(1f, 0.55f, 0f, 0.45f) });
            for (int i = 0; i < preview.Count - 1; i++)
                DrawLine(TileCenter(preview[i]), TileCenter(preview[i + 1]),
                    Colors.Orange, 2.5f);
            DrawCircle(TileCenter(preview[^1]), HexSize * 0.22f, Colors.Orange);
        }

        // 3. Cities
        foreach (var city in _state.Cities)
        {
            if (!fog.IsVisible(city.Position) && !fog.IsDiscovered(city.Position)) continue;
            var pos  = TileCenter(city.Position);
            bool seen = fog.IsVisible(city.Position);
            var col   = seen ? new Color(0.95f, 0.90f, 0.70f) : new Color(0.50f, 0.47f, 0.37f);
            DrawRect(new Rect2(pos - new Vector2(HexSize * 0.35f, HexSize * 0.35f),
                               new Vector2(HexSize * 0.70f, HexSize * 0.70f)), col);
            if (city == _selection.City)
                DrawRect(new Rect2(pos - new Vector2(HexSize * 0.38f, HexSize * 0.38f),
                                   new Vector2(HexSize * 0.76f, HexSize * 0.76f)),
                         Colors.White, false, 2f);
            if (seen)
                DrawString(ThemeDB.FallbackFont, pos + new Vector2(-HexSize * 0.35f, -HexSize * 0.45f),
                    $"{city.Name} ({city.Population})", HorizontalAlignment.Left, -1, 11, Colors.White);
            if (seen && city.HP < City.MaxHP)
                DrawHpBar(pos - new Vector2(0f, HexSize * 0.52f), city.HP / (float)City.MaxHP, HexSize * 0.72f);
        }

        // 4. Units
        foreach (var unit in _state.Units)
        {
            if (!fog.IsVisible(unit.Position)) continue;
            var pos = unit == _animator.AnimatingUnit
                ? _animator.CurrentWorldPos - new Vector2(0f, LiftAt(_animator.CurrentTile))
                : TileCenter(unit.Position);
            DrawCircle(pos, HexSize * 0.32f, unit.Owner.Color);
            DrawString(ThemeDB.FallbackFont, pos + new Vector2(-5f, 5f),
                unit.Data.Name[..1], HorizontalAlignment.Left, -1, 14, Colors.White);
            if (unit == _selection.Unit)
                DrawArc(pos, HexSize * 0.40f, 0f, Mathf.Tau, 24, Colors.Yellow, 2.5f);
            if (unit.MovementRemaining == 0)
                DrawCircle(pos, HexSize * 0.32f, new Color(0f, 0f, 0f, 0.45f));
            if (unit.Fortified)
                DrawArc(pos, HexSize * 0.38f, 0f, Mathf.Tau, 24, new Color(0.4f, 0.8f, 1f), 1.5f);
            if (unit.HP < Unit.MaxHP)
                DrawHpBar(pos - new Vector2(0f, HexSize * 0.46f), unit.HP / (float)Unit.MaxHP, HexSize * 0.6f);
        }

        // 4b. Combat flash
        if (_flashTimeLeft > 0f)
        {
            float alpha = Mathf.Clamp(_flashTimeLeft / CombatFlashSecs, 0f, 1f) * 0.6f;
            var col = new Color(1f, 0.2f, 0.2f, alpha);
            if (_flashAttacker is { } a)
                DrawPolygon(HexVertices(TileCenter(a), HexSize - HexGap), new[] { col });
            if (_flashDefender is { } d)
                DrawPolygon(HexVertices(TileCenter(d), HexSize - HexGap), new[] { col });
        }

    }

    // ── Coordinate helpers (public — used by input handlers + camera init) ──

    public static Vector2 AxialToWorld(Vector2I axial)
    {
        float x = HexSize * 1.5f           * axial.X;
        float y = HexSize * Mathf.Sqrt(3f) * (axial.Y + axial.X * 0.5f) * VerticalScale;
        return new Vector2(x, y);
    }

    public static Vector2I WorldToAxial(Vector2 world)
    {
        // Undo the vertical foreshortening before inverting the hex projection so
        // picking lands on the same tile AxialToWorld placed (on the flat ground
        // plane — elevation lift is a draw-only offset and intentionally ignored).
        float worldY = world.Y / VerticalScale;
        float qf = (2f / 3f * world.X) / HexSize;
        float rf = (-1f / 3f * world.X + Mathf.Sqrt(3f) / 3f * worldY) / HexSize;
        return CubeRound(qf, rf);
    }

    // Draw-only upward offset so raised terrain reads as elevated in the 2.5D
    // view. NOT used by WorldToAxial — a tile's clickable footprint stays on the
    // ground plane.
    public static float ElevationLift(TerrainType terrain) => terrain switch
    {
        TerrainType.Mountain => HexSize * 0.55f,
        TerrainType.Hills    => HexSize * 0.28f,
        TerrainType.Forest   => HexSize * 0.12f,
        _                    => 0f,
    };

    private static Vector2I CubeRound(float q, float r)
    {
        float s  = -q - r;
        int   rq = Mathf.RoundToInt(q);
        int   rr = Mathf.RoundToInt(r);
        int   rs = Mathf.RoundToInt(s);
        float dq = Mathf.Abs(rq - q);
        float dr = Mathf.Abs(rr - r);
        float ds = Mathf.Abs(rs - s);
        if      (dq > dr && dq > ds) rq = -rr - rs;
        else if (dr > ds)            rr = -rq - rs;
        return new Vector2I(rq, rr);
    }

    // Small HP bar centered horizontally at `topCenter`. frac in [0,1].
    private void DrawHpBar(Vector2 topCenter, float frac, float width)
    {
        frac = Mathf.Clamp(frac, 0f, 1f);
        const float h = 3f;
        var bg = new Rect2(topCenter - new Vector2(width * 0.5f, 0f), new Vector2(width, h));
        DrawRect(bg, new Color(0f, 0f, 0f, 0.7f));
        var col = frac > 0.5f ? Colors.LimeGreen : frac > 0.25f ? Colors.Yellow : Colors.Red;
        DrawRect(new Rect2(bg.Position, new Vector2(width * frac, h)), col);
    }

    private static Vector2[] HexVertices(Vector2 center, float size)
    {
        var v = new Vector2[6];
        for (int i = 0; i < 6; i++)
        {
            float a = Mathf.DegToRad(60f * i);
            // Match the foreshortened projection so overlays align with the tiles.
            v[i] = center + new Vector2(Mathf.Cos(a) * size, Mathf.Sin(a) * size * VerticalScale);
        }
        return v;
    }

    public static Color TerrainColor(TerrainType terrain) => terrain switch
    {
        TerrainType.Ocean     => new Color(0.18f, 0.35f, 0.65f),
        TerrainType.Coast     => new Color(0.33f, 0.55f, 0.80f),
        TerrainType.Desert    => new Color(0.87f, 0.80f, 0.55f),
        TerrainType.Plains    => new Color(0.80f, 0.78f, 0.50f),
        TerrainType.Grassland => new Color(0.38f, 0.68f, 0.32f),
        TerrainType.Forest    => new Color(0.18f, 0.45f, 0.20f),
        TerrainType.Hills     => new Color(0.60f, 0.55f, 0.35f),
        TerrainType.Tundra    => new Color(0.70f, 0.75f, 0.68f),
        TerrainType.Snow      => new Color(0.92f, 0.95f, 0.98f),
        TerrainType.Mountain  => new Color(0.55f, 0.50f, 0.48f),
        _                     => Colors.Magenta,
    };
}
