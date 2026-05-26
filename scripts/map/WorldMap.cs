using System.Linq;
using Godot;
using NWO.Core;
using NWO.Entities;
using NWO.UI;

namespace NWO.Map;

// Scene coordinator. Owns the GameState model and stitches together input,
// camera, animation, selection, end-turn queue, renderer, and UI. Each
// concern lives in its own class so this file stays under ~300 lines and the
// gameplay logic is testable without a scene running.
public partial class WorldMap : Node2D
{
    private const int   MapWidth        = 60;
    private const int   MapHeight       = 40;
    private const float SecondsPerTile  = 0.12f;

    private GameState        _state            = null!;
    private SelectionState   _selection        = new();
    private EndTurnQueue     _endTurnQueue     = new();
    private MovementAnimator _animator         = null!;
    private CameraController _cameraController = null!;
    private FogOfWar         _viewerFog        = null!;
    private Player           _viewerPlayer     = null!;

    private WorldRenderer _renderer = null!;
    private UIController  _ui       = null!;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        int seed = (int)GD.Randi();
        GD.Print($"Map seed: {seed}  (pass to MapGenerator.Generate to reproduce)");
        var map     = MapGenerator.Generate(MapWidth, MapHeight, seed);
        var catalog = DataCatalog.Load();
        _state      = new GameState(map, catalog);

        _viewerPlayer = _state.AddPlayer(new Player { Id = 0, Name = "Player", IsHuman = true, Color = Colors.Blue });
        _viewerFog    = _state.Fog(_viewerPlayer);

        _renderer         = GetNode<WorldRenderer>("WorldRenderer");
        _ui               = GetNode<UIController>("UI");
        _cameraController = new CameraController(GetNode<Camera2D>("Camera2D"));
        _animator         = new MovementAnimator(SecondsPerTile, WorldRenderer.AxialToWorld);

        _animator.Completed   += OnAnimationCompleted;
        _animator.TileEntered += () => { RecomputeFog(); _renderer.QueueRedraw(); };
        _ui.EndTurnPressed    += OnEndTurnPressed;
        _ui.FoundCityPressed  += () => { if (_selection.Unit != null) TryFoundCity(_selection.Unit); };

        var warriorDef = catalog.Unit("warrior")!;
        var settlerDef = catalog.Unit("settler")!;
        var startPos   = _state.FindWalkableTileNear(MapCenterAxial());
        _state.Units.Add(new Unit(warriorDef, _viewerPlayer, startPos));
        _state.Units.Add(new Unit(settlerDef, _viewerPlayer,
            _state.FindWalkableTileNear(new Vector2I(startPos.X + 3, startPos.Y))));

        _renderer.Initialize(_state, _selection, _animator, _viewerPlayer);
        _cameraController.Position = WorldRenderer.AxialToWorld(MapCenterAxial());
        RecomputeFog();
        _ui.SetTurn(_state.TurnManager.TurnNumber);
        _renderer.QueueRedraw();
        BuildAndStartEndTurnQueue();
    }

    // ── Per-frame ────────────────────────────────────────────────────────────

    public override void _Process(double delta)
    {
        var dir = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up))    dir.Y -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down))  dir.Y += 1;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left))  dir.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) dir.X += 1;
        _cameraController.ApplyKeyboardPan(dir, (float)delta);

        if (_animator.Tick((float)delta))
        {
            _cameraController.CenterOn(_animator.CurrentWorldPos);
            _renderer.QueueRedraw();
        }

        _cameraController.Tick((float)delta);
    }

    // ── Input ────────────────────────────────────────────────────────────────

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true } key)
        {
            HandleKeyPress(key.Keycode);
            return;
        }

        if (@event is InputEventMouseMotion motion)
        {
            if (_cameraController.IsPanning)
                _cameraController.ApplyMousePan(motion.Relative);
            else if (_selection.Unit != null && !_animator.IsAnimating)
                UpdatePathPreview(WorldRenderer.WorldToAxial(GetGlobalMousePosition()));
            return;
        }

        if (@event is not InputEventMouseButton mb) return;
        if (mb.Pressed)
        {
            switch (mb.ButtonIndex)
            {
                case MouseButton.WheelUp:   _cameraController.Zoom(1.15f); break;
                case MouseButton.WheelDown: _cameraController.Zoom(0.87f); break;
                case MouseButton.Left when !_animator.IsAnimating:
                    HandleLeftPress(WorldRenderer.WorldToAxial(GetGlobalMousePosition()));
                    break;
                case MouseButton.Right when !_animator.IsAnimating:
                    HandleRightPress(WorldRenderer.WorldToAxial(GetGlobalMousePosition()));
                    break;
                case MouseButton.Middle:
                    _cameraController.IsPanning = true;
                    break;
            }
        }
        else if (mb.ButtonIndex == MouseButton.Middle)
        {
            _cameraController.IsPanning = false;
        }
    }

    private void HandleKeyPress(Key keycode)
    {
        switch (keycode)
        {
            case Key.Tab:
                CycleToNextUnitNeedingAttention();
                break;
            case Key.Enter:
            case Key.KpEnter:
                if (_animator.IsAnimating) break;
                if (_endTurnQueue.Count > 0) AdvanceEndTurnQueue();
                else                         OnEndTurnPressed();
                break;
            case Key.Space when _endTurnQueue.Count > 0:
                AdvanceEndTurnQueue();
                break;
            case Key.B when _selection.Unit?.Data.Special == "found_city":
                TryFoundCity(_selection.Unit);
                break;
            case Key.F when _selection.Unit?.Data.Special == "found_city":
                TryFoundCity(_selection.Unit);
                break;
            case Key.F when _selection.Unit != null:
                FortifySelectedUnit();
                break;
            case Key.Escape:
                _cameraController.IsPanning = false;
                _endTurnQueue.Clear();
                _ui.HidePersistentNotification();
                Deselect();
                break;
        }
    }

    // ── Selection / movement ─────────────────────────────────────────────────

    private void HandleLeftPress(Vector2I axial)
    {
        if (!_state.Map.Tiles.ContainsKey(axial)) return;

        var clickedUnit = _state.Units.Find(u => u.Position == axial && u.Owner == _viewerPlayer);
        var clickedCity = _state.Cities.Find(c => c.Position == axial);

        if (clickedUnit != null && (clickedUnit.MovementRemaining > 0 || clickedUnit.Fortified))
        {
            if (clickedUnit.Fortified)
            {
                clickedUnit.Fortified         = false;
                clickedUnit.MovementRemaining = clickedUnit.Data.Movement;
            }
            SelectUnit(clickedUnit);
            _cameraController.CenterOn(WorldRenderer.AxialToWorld(clickedUnit.Position));
        }
        else if (clickedCity != null)
        {
            SelectCity(clickedCity);
            _cameraController.CenterOn(WorldRenderer.AxialToWorld(clickedCity.Position));
        }
        else
        {
            Deselect();
        }
    }

    private void HandleRightPress(Vector2I axial)
    {
        if (_selection.Unit == null) return;
        if (!_selection.ReachableTiles.Contains(axial)) return;

        var path = HexGrid.FindPath(_selection.Unit.Position, axial, _state.MovementCost);
        if (path.Count < 2) return;

        StartMove(_selection.Unit, path);
        Deselect();
    }

    private void UpdatePathPreview(Vector2I axial)
    {
        if (axial == _selection.PendingDestination) return;
        if (_selection.ReachableTiles.Contains(axial))
        {
            var path = HexGrid.FindPath(_selection.Unit!.Position, axial, _state.MovementCost);
            if (path.Count >= 2)
            {
                _selection.PendingDestination = axial;
                _selection.PendingPathPreview = path;
                _renderer.QueueRedraw();
            }
        }
        else if (_selection.PendingDestination != null)
        {
            _selection.PendingDestination = null;
            _selection.PendingPathPreview = null;
            _renderer.QueueRedraw();
        }
    }

    private void CycleToNextUnitNeedingAttention()
    {
        var candidates = _state.Units
            .Where(u => u.Owner == _viewerPlayer && u.NeedsAttention)
            .ToList();
        if (candidates.Count == 0) return;

        int currentIdx = _selection.Unit != null ? candidates.IndexOf(_selection.Unit) : -1;
        var next = candidates[(currentIdx + 1) % candidates.Count];

        SelectUnit(next);
        _cameraController.CenterOn(WorldRenderer.AxialToWorld(next.Position));
    }

    private void StartMove(Unit unit, System.Collections.Generic.List<Vector2I> path)
    {
        if (path.Count < 2) return;
        int cost = 0;
        for (int i = 1; i < path.Count; i++)
            cost += _state.MovementCost(path[i]);
        unit.MovementRemaining = Mathf.Max(0, unit.MovementRemaining - cost);
        unit.Position          = path[^1];
        _animator.Start(unit, path);
    }

    private void SelectUnit(Unit unit)
    {
        var reachable = HexGrid.GetReachableTiles(unit.Position, unit.MovementRemaining, _state.MovementCost).ToHashSet();
        _selection.SelectUnit(unit, reachable);
        _ui.SetFoundCityVisible(unit.Data.Special == "found_city");
        _ui.HideCityPanel();
        _renderer.QueueRedraw();
    }

    private void SelectCity(City city)
    {
        _selection.SelectCity(city);
        _ui.SetFoundCityVisible(false);
        _ui.ShowCityPanel(city, _state.Catalog, item => SetProduction(city, item));
        _renderer.QueueRedraw();
    }

    private void Deselect()
    {
        _selection.Clear();
        _ui.SetFoundCityVisible(false);
        _ui.HideCityPanel();
        _renderer.QueueRedraw();
    }

    // ── Animation hook ───────────────────────────────────────────────────────

    private void OnAnimationCompleted()
    {
        RecomputeFog();
        _renderer.QueueRedraw();
        _cameraController.StartPostAnimDelay();
        ShowNextOrAdvanceQueue();
    }

    // ── Cities ───────────────────────────────────────────────────────────────

    private void TryFoundCity(Unit settler)
    {
        var result = _state.TryFoundCity(settler, out var city);
        switch (result)
        {
            case GameState.FoundCityResult.BadTerrain:
                _ui.ShowNotification("Cannot found a city here.");
                return;
            case GameState.FoundCityResult.TooClose:
                _ui.ShowNotification("Too close to another city.");
                return;
        }

        Deselect();
        RecomputeFog();
        _ui.ShowNotification($"{city!.Name} founded!");
        _renderer.QueueRedraw();
    }

    private void SetProduction(City city, string item)
    {
        city.ProductionItem     = item;
        city.ProductionProgress = 0;
        _ui.ShowCityPanel(city, _state.Catalog, i => SetProduction(city, i));
    }

    // ── End-of-turn queue ────────────────────────────────────────────────────

    private void OnEndTurnPressed()
    {
        if (_animator.IsAnimating) return;
        _cameraController.CancelPostAnimDelay();
        _endTurnQueue.Clear();
        ProcessTurn();
    }

    private void BuildAndStartEndTurnQueue()
    {
        _endTurnQueue.Clear();
        foreach (var u in _state.Units.Where(u => u.Owner == _viewerPlayer && u.NeedsAttention))
            _endTurnQueue.Add(u);
        foreach (var c in _state.Cities.Where(c => c.Owner == _viewerPlayer && c.NeedsAttention))
            _endTurnQueue.Add(c);
        ShowNextOrAdvanceQueue();
    }

    private void AdvanceEndTurnQueue()
    {
        _cameraController.CancelPostAnimDelay();
        _endTurnQueue.Pop();
        ShowNextOrAdvanceQueue();
    }

    private void ShowNextOrAdvanceQueue()
    {
        var item = _endTurnQueue.PeekValid();
        if (item == null)
        {
            _ui.HidePersistentNotification();
            return;
        }

        Deselect();
        switch (item)
        {
            case Unit unit:
                SelectUnit(unit);
                _cameraController.DeferOrCenter(WorldRenderer.AxialToWorld(unit.Position));
                break;
            case City city:
                SelectCity(city);
                _cameraController.DeferOrCenter(WorldRenderer.AxialToWorld(city.Position));
                break;
        }
        _ui.ShowNotification(item.PromptText, persistent: true);
        _renderer.QueueRedraw();
    }

    private void FortifySelectedUnit()
    {
        if (_selection.Unit == null) return;
        _selection.Unit.Fortified         = true;
        _selection.Unit.MovementRemaining = 0;
        if (_endTurnQueue.Count > 0) AdvanceEndTurnQueue();
        else                         Deselect();
    }

    private void ProcessTurn()
    {
        var completions   = new System.Collections.Generic.List<GameState.ProductionCompletion>();
        var notifications = _state.ProcessEndOfTurn(completions);

        Deselect();
        RecomputeFog();
        _ui.SetTurn(_state.TurnManager.TurnNumber);
        if (notifications.Count > 0)
            _ui.ShowNotification(string.Join("  |  ", notifications));
        _renderer.QueueRedraw();
        BuildAndStartEndTurnQueue();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void RecomputeFog()
    {
        var overrides = _animator.IsAnimating
            ? new System.Collections.Generic.Dictionary<Unit, Vector2I> { [_animator.AnimatingUnit!] = _animator.CurrentTile }
            : null;
        _state.RecomputeFog(_viewerPlayer, overrides);
    }

    private static Vector2I MapCenterAxial()
    {
        int col = MapWidth  / 2;
        int row = MapHeight / 2;
        return new Vector2I(col, row - (col - (col & 1)) / 2);
    }
}
