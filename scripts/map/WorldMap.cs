using System.Collections.Generic;
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
    private const int   MapWidth            = 60;
    private const int   MapHeight           = 40;
    private const float SecondsPerTile      = 0.12f;
    private const int   MinAISpawnDistance  = 10;

    private GameSession      _session          = null!;
    private GameState        _state            = null!;
    private SelectionState   _selection        = new();
    private EndTurnQueue     _endTurnQueue     = new();
    private MovementAnimator _animator         = null!;
    private CameraController _cameraController = null!;
    private FogOfWar         _viewerFog        = null!;
    private Player           _viewerPlayer     = null!;
    private City?            _pendingCapture;

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

        _viewerPlayer = _state.AddPlayer(new Player { Id = 0, Name = "Player",     IsHuman = true,  Color = Colors.Blue });
        var aiPlayer  = _state.AddPlayer(new Player { Id = 1, Name = "Barbarians", IsHuman = false, Color = Colors.Red  });
        _viewerFog    = _state.Fog(_viewerPlayer);
        _session      = new GameSession(_state, _viewerPlayer);

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

        // Confine the AI to the player's landmass for MVP. Cross-continent /
        // island AI is a Phase 5+ concern (needs naval movement). See
        // ROADMAP.md → Post-MVP.
        var landmass       = _state.GetConnectedLandmass(startPos);
        var aiStart        = PickAISpawn(startPos, landmass);
        var aiSettlerStart = FindNeighborOnLandmass(aiStart, landmass) ?? aiStart;
        _state.Units.Add(new Unit(warriorDef, aiPlayer, aiStart));
        _state.Units.Add(new Unit(settlerDef, aiPlayer, aiSettlerStart));

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

        if (clickedUnit != null)
        {
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
        // Right-click on a workable tile while a city is selected: toggle the
        // worker lock. Civ-5 style — see CLAUDE.md UI/UX section.
        if (_selection.City is { } city && city.Owner == _viewerPlayer)
        {
            ToggleWorkerLock(city, axial);
            return;
        }
        if (_selection.Unit == null) return;

        var attacker    = _selection.Unit;
        var target      = _state.Units.Find(u => u.Position == axial && u.Owner != _viewerPlayer);
        int effectiveMP = attacker.Fortified ? attacker.Data.Movement : attacker.MovementRemaining;

        if (target != null
            && attacker.Data.Attack > 0
            && HexGrid.Distance(attacker.Position, axial) <= attacker.Data.Range
            && effectiveMP > 0)
        {
            var attackerPos = attacker.Position;
            var result      = _session.TryAttack(attacker, target);
            if (result.Outcome != GameState.AttackOutcome.Invalid)
            {
                _renderer.FlashCombat(attackerPos, axial);
                _ui.ShowNotification(FormatCombatResult(attacker, target, result));
                Deselect();
                _renderer.QueueRedraw();
                BuildAndStartEndTurnQueue();
            }
            return;
        }

        if (!_selection.ReachableTiles.Contains(axial)) return;

        var move = _session.TryMove(attacker, axial);
        if (!move.Success) return;

        // Capture is applied once the animation lands on the city tile.
        _pendingCapture = move.CapturedOnArrival;
        _animator.Start(attacker, move.Path);
        Deselect();
    }

    private bool IsBlockedByEnemyUnit(Vector2I tile)
        => _state.Units.Any(u => u.Position == tile && u.Owner != _viewerPlayer);

    private static string FormatCombatResult(Unit attacker, Unit target, GameState.AttackResult r) => r.Outcome switch
    {
        GameState.AttackOutcome.DefenderKilled =>
            $"{attacker.Data.Name} killed {target.Data.Name}! (took {r.AttackerDmg})",
        GameState.AttackOutcome.AttackerKilled =>
            $"{attacker.Data.Name} died attacking {target.Data.Name} (dealt {r.DefenderDmg})",
        GameState.AttackOutcome.BothKilled =>
            $"{attacker.Data.Name} and {target.Data.Name} destroyed each other!",
        _ =>
            $"{attacker.Data.Name} hits {target.Data.Name} for {r.DefenderDmg} (took {r.AttackerDmg})",
    };

    private void UpdatePathPreview(Vector2I axial)
    {
        if (axial == _selection.PendingDestination) return;
        if (_selection.ReachableTiles.Contains(axial))
        {
            var path = HexGrid.FindPath(_selection.Unit!.Position, axial,
                tile => IsBlockedByEnemyUnit(tile) ? int.MaxValue : _state.MovementCost(tile));
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

    private void SelectUnit(Unit unit)
    {
        int Cost(Vector2I tile) => IsBlockedByEnemyUnit(tile) ? int.MaxValue : _state.MovementCost(tile);
        // Fortified units preview their full move range so right-click orders are validated as if awake.
        int effectiveMP = unit.Fortified ? unit.Data.Movement : unit.MovementRemaining;
        var reachable   = HexGrid.GetReachableTiles(unit.Position, effectiveMP, Cost).ToHashSet();
        _selection.SelectUnit(unit, reachable);
        _ui.SetFoundCityVisible(unit.Data.Special == "found_city");
        _ui.HideCityPanel();
        _ui.ShowUnitPanel(unit);
        _renderer.QueueRedraw();
    }

    private void SelectCity(City city)
    {
        _selection.SelectCity(city);
        _ui.SetFoundCityVisible(false);
        _ui.HideUnitPanel();
        RefreshCityPanel(city);
        _renderer.QueueRedraw();
    }

    private void Deselect()
    {
        _selection.Clear();
        _ui.SetFoundCityVisible(false);
        _ui.HideCityPanel();
        _ui.HideUnitPanel();
        _renderer.QueueRedraw();
    }

    // ── Animation hook ───────────────────────────────────────────────────────

    private void OnAnimationCompleted()
    {
        City? capturedCity = null;
        if (_pendingCapture != null && _animator.AnimatingUnit == null)
        {
            var captor = _state.Units.Find(u => u.Owner == _viewerPlayer && u.Position == _pendingCapture.Position);
            if (captor != null)
            {
                capturedCity = _pendingCapture;
                _session.ResolveCapture(captor, capturedCity);
            }
            _pendingCapture = null;
        }

        RecomputeFog();
        _renderer.QueueRedraw();
        _cameraController.StartPostAnimDelay();

        if (capturedCity != null)
        {
            // Open the city panel so the player can choose production immediately.
            // Queue advancement is suppressed so the persistent notification isn't overwritten.
            SelectCity(capturedCity);
            _cameraController.DeferOrCenter(WorldRenderer.AxialToWorld(capturedCity.Position));
            _ui.ShowNotification($"{capturedCity.Name} captured — choose production!", persistent: true);
            return;
        }

        ShowNextOrAdvanceQueue();
    }

    // ── Cities ───────────────────────────────────────────────────────────────

    private void TryFoundCity(Unit settler)
    {
        var result = _session.TryFoundCity(settler, out var city);
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
        _ui.ShowNotification($"{city!.Name} founded!");
        _renderer.QueueRedraw();
    }

    private void SetProduction(City city, string item)
    {
        city.ProductionItem     = item;
        city.ProductionProgress = 0;
        _ui.HidePersistentNotification();
        RefreshCityPanel(city);
    }

    private void SetCityFocus(City city, CityFocus focus)
    {
        if (city.Workforce.Focus == focus) return;
        city.Workforce.Focus = focus;
        CityWorkforceService.Recompute(_state, city);
        RefreshCityPanel(city);
        _renderer.QueueRedraw();
    }

    private void ToggleWorkerLock(City city, Vector2I axial)
    {
        if (!CityWorkforceService.Workable(_state, city).Contains(axial)) return;
        if (!city.Workforce.Locked.Remove(axial))
        {
            // Cap locked tiles at population — locking past the limit would
            // evict another locked tile on next recompute, which is confusing.
            if (city.Workforce.Locked.Count >= city.Population)
            {
                _ui.ShowNotification("No idle citizens — unlock a tile first.");
                return;
            }
            city.Workforce.Locked.Add(axial);
        }
        CityWorkforceService.Recompute(_state, city);
        RefreshCityPanel(city);
        _renderer.QueueRedraw();
    }

    private void RefreshCityPanel(City city) =>
        _ui.ShowCityPanel(city, _state.Catalog,
            item  => SetProduction(city, item),
            focus => SetCityFocus(city, focus));

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
        _session.Fortify(_selection.Unit);
        if (_endTurnQueue.Count > 0) AdvanceEndTurnQueue();
        else                         Deselect();
    }

    private void ProcessTurn()
    {
        var summary = _session.EndTurn();

        Deselect();
        RecomputeFog();
        _ui.SetTurn(_state.TurnManager.TurnNumber);
        if (summary.Notifications.Count > 0)
            _ui.ShowNotification(string.Join("  |  ", summary.Notifications));
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

    // Picks an AI starting tile on `landmass` (the player's continent).
    // Preference order:
    //   1. Tiles at least MinAISpawnDistance away (avoids spawning adjacent).
    //   2. Whatever's farthest if the island is too small for that rule.
    // Falls back to playerStart only if the landmass is empty (player landed
    // on a lone unwalkable tile — shouldn't happen in practice).
    private static Vector2I PickAISpawn(Vector2I playerStart, HashSet<Vector2I> landmass)
    {
        if (landmass.Count == 0) return playerStart;

        Vector2I best        = playerStart;
        int      bestDist    = -1;
        Vector2I farFromMin  = playerStart;
        int      farFromMinD = -1;

        foreach (var tile in landmass)
        {
            int d = HexGrid.Distance(playerStart, tile);
            if (d > farFromMinD) { farFromMinD = d; farFromMin = tile; }
            if (d >= MinAISpawnDistance && d > bestDist) { bestDist = d; best = tile; }
        }
        return bestDist >= 0 ? best : farFromMin;
    }

    // Walkable neighbour of `tile` that's on the same landmass, used to place
    // the AI settler one hex from the warrior without leaving the continent.
    private static Vector2I? FindNeighborOnLandmass(Vector2I tile, HashSet<Vector2I> landmass)
    {
        foreach (var n in HexGrid.GetNeighbors(tile))
            if (landmass.Contains(n)) return n;
        return null;
    }
}
