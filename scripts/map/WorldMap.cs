using System.Collections.Generic;
using System.Linq;
using Godot;
using NWO.Audio;
using NWO.Core;
using NWO.Entities;
using NWO.UI;

namespace NWO.Map;

// Scene coordinator. Owns the GameState model and stitches together input,
// camera, animation, selection, end-turn queue, renderer, and UI. Each concern
// lives in its own class — input decoding (WorldInputRouter), the tile-tooltip
// dwell timer (TileTooltipController), combat result text (CombatMessages), and
// event-log visibility (EventVisibilityFilter) — so this file stays a thin
// coordinator and the gameplay logic is testable without a scene running.
public partial class WorldMap : Node3D
{
    private const float SecondsPerTile = 0.12f;

    private GameSession      _session          = null!;
    private GameState        _state            = null!;
    private SelectionState   _selection        = new();
    private EndTurnQueue     _endTurnQueue     = new();
    private MovementAnimator _animator         = null!;
    private CameraController _cameraController = null!;
    private WorldInputRouter _input            = null!;
    private TileTooltipController _tooltip      = null!;
    private FogOfWar         _viewerFog        = null!;
    private Player           _viewerPlayer     = null!;
    private City?            _pendingCapture;

    private WorldRenderer _renderer = null!;
    private WorldOverlay  _overlay  = null!;
    private Camera3D      _camera3D = null!;
    private UIController  _ui       = null!;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        ResolveLaunch();
        _viewerFog = _state.Fog(_viewerPlayer);
        _session   = new GameSession(_state, _viewerPlayer);

        _renderer         = GetNode<WorldRenderer>("WorldRenderer");
        _overlay          = GetNode<WorldOverlay>("OverlayLayer/WorldOverlay");
        _ui               = GetNode<UIController>("UI");
        var pivot         = GetNode<Node3D>("Pivot");
        _camera3D         = GetNode<Camera3D>("Pivot/Camera3D");
        GetNode<DirectionalLight3D>("Sun").RotationDegrees = new Vector3(-55f, -35f, 0f);
        _cameraController = new CameraController(pivot, _camera3D);
        _animator         = new MovementAnimator(SecondsPerTile, HexProjection.AxialToWorld);

        _input = new WorldInputRouter(
            _cameraController,
            () => _animator.IsAnimating,
            () => ScreenToAxial(GetViewport().GetMousePosition()));
        _input.KeyPressed   += HandleKeyPress;
        _input.LeftClicked  += HandleLeftPress;
        _input.RightPressed += HandleRightPress;
        _input.Hovered      += OnHover;
        _input.Panned       += () => _tooltip.Clear();

        _tooltip = new TileTooltipController(
            canShow:   axial => _state.Map.Tiles.ContainsKey(axial) && _viewerFog.IsDiscovered(axial),
            buildText: BuildTileInfo,
            show:      _ui.ShowTileTooltip,
            hide:      _ui.HideTileTooltip);

        _animator.Completed   += OnAnimationCompleted;
        _animator.TileEntered += () => { RecomputeFog(); Redraw(); };
        _ui.EndTurnPressed    += OnEndTurnPressed;
        _ui.FoundCityPressed  += () => { if (_selection.Unit != null) TryFoundCity(_selection.Unit); };
        _ui.BuildImprovementPressed += OnBuildImprovement;
        _ui.EventFocusRequested     += FocusCameraOn;
        _ui.SaveRequested           += OnSaveRequested;
        _ui.LoadRequested           += OnLoadRequested;
        _ui.MainMenuRequested       += OnMainMenuRequested;

        _renderer.Initialize(_state, _animator, _viewerPlayer);
        _overlay.Initialize(_state, _selection, _animator, _viewerPlayer, _camera3D);
        _ui.InitializeMinimap(_state, _viewerPlayer, _camera3D, RecenterCameraWorld);
        _cameraController.Position = HexProjection.AxialToWorld(GameFactory.MapCenterAxial());
        RecomputeFog();
        _ui.SetTurn(_state.TurnManager.TurnNumber);
        _ui.SetCivStatus(_state, _viewerPlayer);
        Redraw();
        BuildAndStartEndTurnQueue();
    }

    // Resolves the pending GameLaunch request into _state + _viewerPlayer: resume
    // a loaded game if one was handed across the scene change, else start a fresh
    // match. Clears the request so a later scene reload doesn't reuse it.
    private void ResolveLaunch()
    {
        if (GameLaunch.LoadedGame is { } loaded)
        {
            _state        = loaded;
            _viewerPlayer = _state.Players.First(p => p.IsHuman);
        }
        else
        {
            int seed = GameLaunch.NewGameSeed ?? (int)GD.Randi();
            GD.Print($"Map seed: {seed}  (pass to MapGenerator.Generate to reproduce)");
            var (state, viewer) = GameFactory.NewGame(seed);
            _state        = state;
            _viewerPlayer = viewer;
        }
        GameLaunch.LoadedGame  = null;
        GameLaunch.NewGameSeed = null;
    }

    // ── Save / load / menu (HUD pause overlay) ─────────────────────────────────

    private void OnSaveRequested(string slotName) => SaveService.Save(_state, slotName);

    private void OnLoadRequested(string file)
    {
        GameLaunch.LoadedGame = SaveService.Load(file, _state.Catalog);
        GetTree().ChangeSceneToFile(Scenes.World);
    }

    private void OnMainMenuRequested() => GetTree().ChangeSceneToFile(Scenes.MainMenu);

    // ── Per-frame ────────────────────────────────────────────────────────────

    public override void _Process(double delta)
    {
        var dir = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up))    dir.Y -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down))  dir.Y += 1;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left))  dir.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) dir.X += 1;
        _cameraController.ApplyKeyboardPan(dir, (float)delta);
        if (dir != Vector2.Zero) _tooltip.Clear(); // keyboard pan moves the map under a still cursor

        if (_animator.Tick((float)delta))
        {
            _cameraController.CenterOn(_animator.CurrentWorldPos);
            Redraw();
        }

        _tooltip.Tick((float)delta);
        _cameraController.Tick((float)delta);
    }

    // ── Input ────────────────────────────────────────────────────────────────

    // Raw input decoding lives in WorldInputRouter; this just forwards the event
    // and reacts to the semantic intents wired up in _Ready.
    public override void _UnhandledInput(InputEvent @event) => _input.Handle(@event);

    // Cursor moved over the map (not panning, not animating). Refresh the move
    // path / combat-odds preview for the selected unit, then feed the dwell timer.
    private void OnHover(Vector2 screenPos)
    {
        var axial = ScreenToAxial(GetViewport().GetMousePosition());
        if (_selection.Unit != null && !_animator.IsAnimating)
        {
            UpdatePathPreview(axial);
            UpdateCombatForecast(axial);
        }
        _tooltip.RegisterHover(axial, screenPos);
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
            case Key.H when _selection.Unit != null:
                SleepSelectedUnitUntilHealed();
                break;
            case Key.C:
                CycleToNextCity();
                break;
            case Key.T:
                if (!_animator.IsAnimating)
                    _ui.ToggleTechTree(_state, _viewerPlayer, OnSetResearch);
                break;
            case Key.Escape:
                _input.CancelDrag();
                _ui.HidePersistentNotification();
                _ui.HideTechTree();
                Deselect();
                break;
        }
    }

    private void OnSetResearch(string techId)
    {
        var result = CivEconomyService.SetResearch(_state, _viewerPlayer, techId);
        if (result == CivEconomyService.SetResearchResult.Ok)
        {
            _ui.SetCivStatus(_state, _viewerPlayer);
            _ui.ToggleTechTree(_state, _viewerPlayer, OnSetResearch); // close on pick
            RefreshEndTurnButton();
        }
    }

    // Civ-5-style: relabel the End Turn button to reflect what's blocking the
    // turn, instead of just silently refusing the click. Order matches the
    // gates in OnEndTurnPressed: attention items → research → all-clear.
    private void RefreshEndTurnButton()
    {
        var head = _endTurnQueue.PeekValid();
        if (head is Unit unit)
        {
            _ui.SetEndTurnState($"{unit.Data.Name} Needs Orders ▶", blocked: true);
            return;
        }
        if (head is City)
        {
            _ui.SetEndTurnState("Choose Production ▶", blocked: true);
            return;
        }
        var civ = _state.Civ(_viewerPlayer);
        if (civ.CurrentResearch == null
            && CivEconomyService.SciencePerTurn(_state, _viewerPlayer) > 0
            && _state.Catalog.Techs.Any(t => !civ.ResearchedTechs.Contains(t.Id)))
        {
            _ui.SetEndTurnState("Choose Research ▶", blocked: true);
            return;
        }
        _ui.SetEndTurnState("End Turn (Enter)", blocked: false);
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
            _cameraController.CenterOn(HexProjection.AxialToWorld(clickedUnit.Position));
        }
        else if (clickedCity != null)
        {
            SelectCity(clickedCity);
            _cameraController.CenterOn(HexProjection.AxialToWorld(clickedCity.Position));
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

        bool inRange = attacker.Data.Attack > 0
            && HexGrid.Distance(attacker.Position, axial) <= attacker.Data.Range
            && effectiveMP > 0;

        if (target != null && inRange)
        {
            var attackerPos = attacker.Position;
            var result      = _session.TryAttack(attacker, target);
            if (result.Outcome != GameState.AttackOutcome.Invalid)
            {
                AudioManager.Instance?.Play(Sfx.Attack);
                _overlay.FlashCombat(attackerPos, axial);
                _ui.ShowNotification(CombatMessages.ForUnitAttack(attacker, target, result));
                Deselect();
                Redraw();
                BuildAndStartEndTurnQueue();
            }
            return;
        }

        // Assault an enemy city in range (bombard its HP; capture is a separate move).
        var cityTarget = _state.Cities.Find(c => c.Position == axial && c.Owner != _viewerPlayer && c.HP > 0);
        if (cityTarget != null && inRange)
        {
            var attackerPos = attacker.Position;
            var result      = _session.TryAttackCity(attacker, cityTarget);
            if (result.Success)
            {
                AudioManager.Instance?.Play(Sfx.Attack);
                _overlay.FlashCombat(attackerPos, axial);
                _ui.ShowNotification(CombatMessages.ForCityAttack(attacker, cityTarget, result));
                Deselect();
                Redraw();
                BuildAndStartEndTurnQueue();
            }
            return;
        }

        if (!_selection.ReachableTiles.Contains(axial)) return;

        var move = _session.TryMove(attacker, axial);
        if (!move.Success) return;

        // Capture is applied once the animation lands on the city tile.
        _pendingCapture = move.CapturedOnArrival;
        AudioManager.Instance?.Play(Sfx.Move);
        _animator.Start(attacker, move.Path);
        Deselect();
    }

    private void UpdatePathPreview(Vector2I axial)
    {
        if (axial == _selection.PendingDestination) return;
        if (_selection.ReachableTiles.Contains(axial))
        {
            var path = HexGrid.FindPath(_selection.Unit!.Position, axial,
                tile => _session.MoveCostFor(_selection.Unit!, tile));
            if (path.Count >= 2)
            {
                _selection.PendingDestination = axial;
                _selection.PendingPathPreview = path;
                Redraw();
            }
        }
        else if (_selection.PendingDestination != null)
        {
            _selection.PendingDestination = null;
            _selection.PendingPathPreview = null;
            Redraw();
        }
    }

    // Civ-5-style odds preview: while hovering an in-range enemy unit or city with
    // a unit selected, show the expected damage to each side.
    private void UpdateCombatForecast(Vector2I axial)
    {
        var attacker = _selection.Unit;
        if (attacker == null || attacker.Data.Attack <= 0
            || HexGrid.Distance(attacker.Position, axial) is var d && (d <= 0 || d > attacker.Data.Range))
        {
            _ui.ShowCombatForecast(null);
            return;
        }

        bool isRanged = attacker.Data.Range >= 2;

        var enemyUnit = _state.Units.Find(u => u.Position == axial && u.Owner != _viewerPlayer);
        if (enemyUnit != null)
        {
            var e = CombatResolver.Expected(attacker, enemyUnit, isRanged);
            _ui.ShowCombatForecast($"Attack {enemyUnit.Data.Name}: deal ~{e.DefenderDamage}, take ~{e.AttackerDamage}");
            return;
        }

        var enemyCity = _state.Cities.Find(c => c.Position == axial && c.Owner != _viewerPlayer && c.HP > 0);
        if (enemyCity != null)
        {
            int def = enemyCity.CityDefenseStrength + _state.GarrisonDefense(enemyCity);
            var e   = CombatResolver.Expected(attacker.Data.Attack, attacker.HP, def, enemyCity.HP, isRanged);
            _ui.ShowCombatForecast(
                $"Assault {enemyCity.Name} (def {def}, {enemyCity.HP} HP): deal ~{e.DefenderDamage}, take ~{e.AttackerDamage}");
            return;
        }

        _ui.ShowCombatForecast(null);
    }

    // [C] Jump between this player's cities (Civ-5 city cycle), centering each.
    private void CycleToNextCity()
    {
        var cities = _state.Cities.Where(c => c.Owner == _viewerPlayer).ToList();
        if (cities.Count == 0) return;

        int currentIdx = _selection.City != null ? cities.IndexOf(_selection.City) : -1;
        var next = cities[(currentIdx + 1) % cities.Count];

        SelectCity(next);
        _cameraController.CenterOn(HexProjection.AxialToWorld(next.Position));
    }

    // Tooltip body for a tile (terrain, yields, revealed resource, improvement).
    // Passed to TileTooltipController as its text builder.
    private string BuildTileInfo(Vector2I axial)
    {
        var terrain = _state.Map.Tiles[axial];
        int food = TerrainYields.Food(terrain);
        int prod = TerrainYields.Production(terrain);
        int gold = TerrainYields.Gold(terrain);

        // Fold in a revealed resource's yields (bonus/strategic Food/Prod, luxury Gold).
        bool hasRes = _state.Map.Resources.TryGetValue(axial, out var res) && res != ResourceType.None
                   && ResourceService.IsRevealed(_state, _viewerPlayer, res);
        if (hasRes)
        {
            food += ResourceYields.Food(res);
            prod += ResourceYields.Production(res);
            gold += ResourceYields.Gold(res);
        }

        // Hills feature trades food for production.
        var feat = _state.Map.FeatureAt(axial);
        food += FeatureYields.Food(feat);
        prod += FeatureYields.Production(feat);

        // Floodplain: river-adjacent tiles gain +1 Food.
        bool riverside = _state.Map.IsRiverAdjacent(axial);
        if (riverside) food += 1;

        food = Mathf.Max(0, food); // a tile's food can't go negative

        var yields = new List<string>();
        if (food > 0) yields.Add($"{food}F");
        if (prod > 0) yields.Add($"{prod}P");
        if (gold > 0) yields.Add($"{gold}G");

        string name = feat == Feature.Hills ? $"{terrain} Hills" : terrain.ToString();
        string text = name;
        if (yields.Count > 0) text += "  " + string.Join(" ", yields);

        if (hasRes)
            text += $"\nResource: {res}";
        if (riverside)
            text += "\nRiver (+1 Food)";
        if (_state.Map.Improvements.TryGetValue(axial, out var imp) && imp != ImprovementType.None)
            text += $"\nImprovement: {imp}";
        return text;
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
        _cameraController.CenterOn(HexProjection.AxialToWorld(next.Position));
    }

    private void SelectUnit(Unit unit)
    {
        int Cost(Vector2I tile) => _session.MoveCostFor(unit, tile);
        // Fortified units preview their full move range so right-click orders are validated as if awake.
        int effectiveMP = unit.Fortified ? unit.Data.Movement : unit.MovementRemaining;
        var reachable   = HexGrid.GetReachableTiles(unit.Position, effectiveMP, Cost).ToHashSet();
        _selection.SelectUnit(unit, reachable);
        _ui.SetFoundCityVisible(unit.Data.Special == "found_city");
        _ui.HideCityPanel();
        _ui.ShowUnitPanel(unit);
        RefreshWorkerActions(unit);
        Redraw();
    }

    // Show the build menu for an idle Worker (a busy one shows its task status
    // on the panel instead). Non-Workers get no build buttons.
    private void RefreshWorkerActions(Unit unit)
    {
        if (unit.Data.Special == "build_improvement" && unit.CurrentTask == null)
            _ui.ShowWorkerActions(ImprovementService.BuildableOptions(_state, _viewerPlayer, unit.Position));
        else
            _ui.ShowWorkerActions(System.Array.Empty<(ImprovementType, int)>());
    }

    private void OnBuildImprovement(ImprovementType type)
    {
        var worker = _selection.Unit;
        if (worker == null) return;
        if (!_session.TryStartImprovement(worker, type))
        {
            _ui.ShowNotification($"Can't build {type} here.");
            return;
        }
        _ui.ShowNotification($"Worker started building {type}.");
        // Worker is now busy (no longer needs attention) — advance the queue.
        if (_endTurnQueue.Count > 0) AdvanceEndTurnQueue();
        else                         Deselect();
        Redraw();
    }

    private void SelectCity(City city)
    {
        _selection.SelectCity(city);
        _ui.SetFoundCityVisible(false);
        _ui.HideUnitPanel();
        RefreshCityPanel(city);
        Redraw();
    }

    private void Deselect()
    {
        _selection.Clear();
        _ui.SetFoundCityVisible(false);
        _ui.HideCityPanel();
        _ui.HideUnitPanel();
        _ui.ShowCombatForecast(null);
        Redraw();
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
        Redraw();
        _cameraController.StartPostAnimDelay();

        if (capturedCity != null)
        {
            // Open the city panel so the player can choose production immediately.
            // Queue advancement is suppressed so the capture event message isn't overwritten.
            SelectCity(capturedCity);
            _cameraController.DeferOrCenter(HexProjection.AxialToWorld(capturedCity.Position));
            _ui.ShowNotification($"{capturedCity.Name} captured!"); // event; the button prompts for production
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
        AudioManager.Instance?.Play(Sfx.CityFound);
        _ui.ShowNotification($"{city!.Name} founded!");
        Redraw();
        // Settler is gone; new city needs production. Rebuild the queue from
        // live state so the head and the button label both reflect that.
        BuildAndStartEndTurnQueue();
    }

    private void SetProduction(City city, string item)
    {
        city.ProductionItem     = item;
        city.ProductionProgress = 0;
        _ui.HidePersistentNotification();
        RefreshCityPanel(city);
        // City no longer needs attention; let PeekValid drop it and advance
        // the queue (which also updates the End Turn button).
        ShowNextOrAdvanceQueue();
    }

    private void SetCityFocus(City city, CityFocus focus)
    {
        if (city.Workforce.Focus == focus) return;
        city.Workforce.Focus = focus;
        CityWorkforceService.Recompute(_state, city);
        RefreshCityPanel(city);
        Redraw();
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
        Redraw();
    }

    private void RefreshCityPanel(City city) =>
        _ui.ShowCityPanel(_state, city, _state.Civ(_viewerPlayer),
            item  => SetProduction(city, item),
            focus => SetCityFocus(city, focus),
            ()    => BuyProduction(city));

    private void BuyProduction(City city)
    {
        if (!_session.TryBuyProduction(city, out var completion))
        {
            _ui.ShowNotification("Not enough gold to buy that.");
            return;
        }
        if (completion != null)
            _ui.ShowNotification($"{city.Name} bought {_state.Catalog.ItemName(completion.Item)}!");
        _ui.SetCivStatus(_state, _viewerPlayer);
        RecomputeFog(); // a bought unit may extend sight
        RefreshCityPanel(city);
        Redraw();
    }

    // ── End-of-turn queue ────────────────────────────────────────────────────

    private void OnEndTurnPressed()
    {
        if (_animator.IsAnimating) return;
        if (PromptForAttentionItemsIfNeeded()) return;
        if (PromptForResearchIfNeeded()) return;
        _cameraController.CancelPostAnimDelay();
        _endTurnQueue.Clear();
        ProcessTurn();
    }

    // If the player still has idle units or cities without production, rebuild
    // the end-turn queue, focus the first item, and hold the turn. The player
    // must Space-skip / move / build / fortify to clear each before End Turn
    // will advance.
    private bool PromptForAttentionItemsIfNeeded()
    {
        BuildAndStartEndTurnQueue();
        return _endTurnQueue.PeekValid() != null;
    }

    // Civ-5 nag: blocks End Turn until the player picks a research target.
    // Opens the tech panel (if not already visible) and shows a one-shot
    // notification. Returns true when the turn should be held.
    private bool PromptForResearchIfNeeded()
    {
        var civ = _state.Civ(_viewerPlayer);
        if (civ.CurrentResearch != null) return false;
        // No city yet → no beakers → nothing to research with. Don't nag.
        if (CivEconomyService.SciencePerTurn(_state, _viewerPlayer) <= 0) return false;

        bool hasUnresearched = false;
        foreach (var tech in _state.Catalog.Techs)
            if (!civ.ResearchedTechs.Contains(tech.Id)) { hasUnresearched = true; break; }
        if (!hasUnresearched) return false;

        // The End Turn button shows "Choose Research ▶"; opening the panel is the
        // guidance, so no banner nag is needed.
        if (!_ui.IsTechTreeVisible)
            _ui.ToggleTechTree(_state, _viewerPlayer, OnSetResearch);
        return true;
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
        // Skipping = "done for this turn" without spending the unit's action, so it
        // leaves the queue (and won't be re-added on End Turn) but still heals.
        if (_endTurnQueue.PeekValid() is Unit skipped)
            skipped.SkippedThisTurn = true;
        _endTurnQueue.Pop();
        ShowNextOrAdvanceQueue();
    }

    private void ShowNextOrAdvanceQueue()
    {
        var item = _endTurnQueue.PeekValid();
        if (item == null)
        {
            _ui.HidePersistentNotification();
            RefreshEndTurnButton();
            return;
        }

        Deselect();
        switch (item)
        {
            case Unit unit:
                SelectUnit(unit);
                _cameraController.DeferOrCenter(HexProjection.AxialToWorld(unit.Position));
                break;
            case City city:
                SelectCity(city);
                _cameraController.DeferOrCenter(HexProjection.AxialToWorld(city.Position));
                break;
        }
        // The blocking state is shown on the End Turn button (RefreshEndTurnButton)
        // and the selected unit/city panel — no banner prompt needed. The banner is
        // reserved for combat results and one-shot game events. Clear any lingering
        // persistent banner as we surface the next item.
        _ui.HidePersistentNotification();
        Redraw();
        RefreshEndTurnButton();
    }

    private void FortifySelectedUnit()
    {
        if (_selection.Unit == null) return;
        _session.Fortify(_selection.Unit);
        if (_endTurnQueue.Count > 0) AdvanceEndTurnQueue();
        else                         Deselect();
    }

    // [H] Fortify the selected unit until it has healed to full, then auto-wake.
    private void SleepSelectedUnitUntilHealed()
    {
        if (_selection.Unit == null) return;
        _session.FortifyUntilHealed(_selection.Unit);
        if (_endTurnQueue.Count > 0) AdvanceEndTurnQueue();
        else                         Deselect();
    }

    private void ProcessTurn()
    {
        var summary = _session.EndTurn();

        Deselect();
        RecomputeFog();
        _ui.SetTurn(_state.TurnManager.TurnNumber);
        // The event log shows only this turn's events.
        _ui.ClearEventLog();
        var events = EventVisibilityFilter.ForViewer(summary.Notifications, _state, _viewerPlayer, _viewerFog);
        if (events.Count > 0)
            _ui.LogEvents(events);
        Redraw();
        _ui.SetCivStatus(_state, _viewerPlayer);

        // A win or loss ends the match: hand the result to the victory screen.
        if (summary.Result != null)
        {
            GameLaunch.LastResult = summary.Result;
            GetTree().ChangeSceneToFile(Scenes.VictoryScreen);
            return;
        }

        BuildAndStartEndTurnQueue();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    // Recenter on a tile picked from the event log. An event for one of the
    // player's own cities also opens that city's panel; anything else just moves
    // the camera.
    private void FocusCameraOn(Vector2I tile)
    {
        var ownCity = _state.Cities.Find(c => c.Position == tile && c.Owner == _viewerPlayer);
        if (ownCity != null) SelectCity(ownCity);
        RecenterCameraWorld(HexProjection.AxialToWorld(tile));
    }

    // Recenter on a world position (minimap click). Cancels any pending
    // post-animation centering so the camera goes where the player clicked.
    private void RecenterCameraWorld(Vector3 world)
    {
        _cameraController.CancelPostAnimDelay();
        _cameraController.CenterOn(world);
    }

    // Refresh the whole view: rebuild the 3D billboards/fog and repaint the 2D
    // overlay. Replaces the old single-node QueueRedraw now that the view is split
    // across a 3D world (WorldRenderer) and a 2D overlay (WorldOverlay).
    private void Redraw()
    {
        _renderer.Refresh();
        _overlay.QueueRedraw();
    }

    // Screen pixel → axial tile. Casts a ray from the fixed-tilt camera onto the
    // ground plane (Y = 0) and inverts the hex projection — so picking lands on a
    // tile's ground footprint regardless of its prism height (matches the old
    // baked-2.5D "picking ignores elevation" contract). Returns an off-map sentinel
    // when the ray misses the ground (callers gate on Map.Tiles.ContainsKey).
    private Vector2I ScreenToAxial(Vector2 screen)
    {
        var from = _camera3D.ProjectRayOrigin(screen);
        var dir  = _camera3D.ProjectRayNormal(screen);
        var hit  = new Plane(Vector3.Up, 0f).IntersectsRay(from, dir);
        return hit.HasValue ? HexProjection.WorldToAxial(hit.Value)
                            : new Vector2I(int.MinValue, int.MinValue);
    }

    private void RecomputeFog()
    {
        var overrides = _animator.IsAnimating
            ? new System.Collections.Generic.Dictionary<Unit, Vector2I> { [_animator.AnimatingUnit!] = _animator.CurrentTile }
            : null;
        _state.RecomputeFog(_viewerPlayer, overrides);
    }

}
