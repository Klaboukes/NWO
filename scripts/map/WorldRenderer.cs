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

    private GameState        _state            = null!;
    private SelectionState   _selection        = null!;
    private MovementAnimator _animator         = null!;
    private Player           _viewerPlayer     = null!;

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
    }

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

        // 1. Terrain
        foreach (var (axial, terrain) in _state.Map.Tiles)
            DrawPolygon(HexVertices(AxialToWorld(axial), HexSize - HexGap),
                new[] { TerrainColor(terrain) });

        // 1b. Strategic resources — drawn once revealed to the viewer (has the
        // tech) and the tile has been discovered.
        foreach (var (axial, res) in _state.Map.Resources)
        {
            if (!fog.IsDiscovered(axial)) continue;
            if (!ResourceService.IsRevealed(_state, _viewerPlayer, res)) continue;
            var w   = AxialToWorld(axial) + new Vector2(-HexSize * 0.30f, HexSize * 0.32f);
            var col = res == ResourceType.Horses ? new Color(0.85f, 0.75f, 0.55f)
                                                 : new Color(0.55f, 0.55f, 0.62f);
            DrawCircle(w, HexSize * 0.12f, col);
            DrawCircle(w, HexSize * 0.12f, Colors.Black, false, 1f);
        }

        // 1c. Tile improvements — a small glyph at bottom-right of discovered tiles.
        foreach (var (axial, imp) in _state.Map.Improvements)
        {
            if (!fog.IsDiscovered(axial)) continue;
            var w = AxialToWorld(axial) + new Vector2(HexSize * 0.18f, HexSize * 0.18f);
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
            DrawPolygon(HexVertices(AxialToWorld(axial), HexSize - HexGap),
                new[] { new Color(1f, 1f, 0.2f, 0.35f) });

        // 2a. Selected-city workable / assigned / locked tile overlays.
        if (_selection.City is { } selectedCity && selectedCity.Owner == _viewerPlayer)
        {
            foreach (var tile in CityWorkforceService.Workable(_state, selectedCity))
            {
                var w = AxialToWorld(tile);
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
                DrawPolygon(HexVertices(AxialToWorld(preview[i]), HexSize - HexGap),
                    new[] { new Color(1f, 0.55f, 0f, 0.45f) });
            for (int i = 0; i < preview.Count - 1; i++)
                DrawLine(AxialToWorld(preview[i]), AxialToWorld(preview[i + 1]),
                    Colors.Orange, 2.5f);
            DrawCircle(AxialToWorld(preview[^1]), HexSize * 0.22f, Colors.Orange);
        }

        // 3. Cities
        foreach (var city in _state.Cities)
        {
            if (!fog.IsVisible(city.Position) && !fog.IsDiscovered(city.Position)) continue;
            var pos  = AxialToWorld(city.Position);
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
            var pos = unit == _animator.AnimatingUnit ? _animator.CurrentWorldPos : AxialToWorld(unit.Position);
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
                DrawPolygon(HexVertices(AxialToWorld(a), HexSize - HexGap), new[] { col });
            if (_flashDefender is { } d)
                DrawPolygon(HexVertices(AxialToWorld(d), HexSize - HexGap), new[] { col });
        }

        // 5. Fog of war
        foreach (var axial in _state.Map.Tiles.Keys)
        {
            if (!fog.IsDiscovered(axial))
                DrawPolygon(HexVertices(AxialToWorld(axial), HexSize - HexGap), new[] { Colors.Black });
            else if (!fog.IsVisible(axial))
                DrawPolygon(HexVertices(AxialToWorld(axial), HexSize - HexGap),
                    new[] { new Color(0f, 0f, 0f, 0.55f) });
        }
    }

    // ── Coordinate helpers (public — used by input handlers + camera init) ──

    public static Vector2 AxialToWorld(Vector2I axial)
    {
        float x = HexSize * 1.5f           * axial.X;
        float y = HexSize * Mathf.Sqrt(3f) * (axial.Y + axial.X * 0.5f);
        return new Vector2(x, y);
    }

    public static Vector2I WorldToAxial(Vector2 world)
    {
        float qf = (2f / 3f * world.X) / HexSize;
        float rf = (-1f / 3f * world.X + Mathf.Sqrt(3f) / 3f * world.Y) / HexSize;
        return CubeRound(qf, rf);
    }

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
            v[i] = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * size;
        }
        return v;
    }

    private static Color TerrainColor(TerrainType terrain) => terrain switch
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
