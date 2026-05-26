using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using NWO.Core;
using NWO.Entities;

namespace NWO.Map;

// Root node for the in-game world view.
// Owns map generation, unit list, city list, selection, movement, fog of war, and all rendering.
public partial class WorldMap : Node2D
{
    private const int   MapWidth        = 60;
    private const int   MapHeight       = 40;
    private const float HexSize         = 32f;
    private const float HexGap          = 1f;
    private const float PanSpeed        = 600f;
    private const float SecondsPerTile  = 0.12f;
    private const double NotifDuration  = 3.0;
    private const int   CitySightRadius = 2;
    private const int   MinCityDistance = 3;

    // ── State ────────────────────────────────────────────────────────────────

    private MapData  _mapData;
    private Camera2D _camera;

    private List<UnitData>     _unitDefs     = new();
    private List<BuildingData> _buildingDefs = new();

    private readonly List<Unit>         _units           = new();
    private Unit?                       _selectedUnit;
    private HashSet<Vector2I>           _reachableTiles  = new();
    private readonly HashSet<Vector2I>  _visibleTiles    = new();
    private readonly HashSet<Vector2I>  _discoveredTiles = new();

    private readonly List<City>         _cities          = new();
    private City?                       _selectedCity;
    private int                         _nextCityName;

    private readonly TurnManager _turnManager = new();

    // ── Animation ────────────────────────────────────────────────────────────

    private Unit?           _animUnit;
    private List<Vector2I>? _animPath;
    private int             _animIndex;
    private float           _animT;
    private Vector2         _animPos;
    private Vector2I        _animCurrentTile;

    // ── Pending move preview ──────────────────────────────────────────────────

    private Vector2I?        _pendingDestination;
    private List<Vector2I>?  _pendingPathPreview;

    // ── Map panning ───────────────────────────────────────────────────────────

    private bool    _isPanning;
    private Vector2 _panStartMousePos;
    private Vector2 _panStartCameraPos;

    // ── UI nodes (created programmatically in SetupUI) ────────────────────

    private Label         _turnLabel       = null!;
    private Label         _notifLabel      = null!;
    private double        _notifSecondsLeft;
    private bool          _notifPersistentActive;
    private readonly List<object> _endTurnQueue = new();
    private Panel         _cityPanel       = null!;
    private Label         _cityNameLabel   = null!;
    private Label         _cityStatsLabel  = null!;
    private VBoxContainer _buildList       = null!;
    private Button        _foundCityButton = null!;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        _unitDefs     = DataLoader.LoadUnits();
        _buildingDefs = DataLoader.LoadBuildings();

        int seed = (int)GD.Randi();
        GD.Print($"Map seed: {seed}  (pass to MapGenerator.Generate to reproduce)");
        _mapData = MapGenerator.Generate(MapWidth, MapHeight, seed);
        _camera  = GetNode<Camera2D>("Camera2D");

        var sum = Vector2.Zero;
        foreach (var axial in _mapData.Tiles.Keys)
            sum += AxialToWorld(axial);
        _camera.Position = sum / _mapData.Tiles.Count;

        var warriorDef = _unitDefs.First(u => u.Id == "warrior");
        var settlerDef = _unitDefs.First(u => u.Id == "settler");
        var startPos   = FindWalkableTileNear(MapCenterAxial());
        _units.Add(new Unit(warriorDef, startPos));
        _units.Add(new Unit(settlerDef, FindWalkableTileNear(new Vector2I(startPos.X + 3, startPos.Y))));

        SetupUI();
        UpdateFogOfWar();
        UpdateTurnLabel();
        QueueRedraw();
    }

    private void SetupUI()
    {
        var canvas = new CanvasLayer();
        AddChild(canvas);

        var root = new Control();
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.MouseFilter = Control.MouseFilterEnum.Ignore;
        canvas.AddChild(root);

        // Turn label — top-left
        _turnLabel = new Label { Text = "Turn 1" };
        _turnLabel.AnchorLeft = _turnLabel.AnchorRight = _turnLabel.AnchorTop = _turnLabel.AnchorBottom = 0f;
        _turnLabel.OffsetLeft = 10; _turnLabel.OffsetRight = 200;
        _turnLabel.OffsetTop  = 10; _turnLabel.OffsetBottom = 36;
        root.AddChild(_turnLabel);

        // Notification label — top-center
        _notifLabel = new Label { Text = "", HorizontalAlignment = HorizontalAlignment.Center };
        _notifLabel.AnchorLeft = 0.5f; _notifLabel.AnchorRight = 0.5f;
        _notifLabel.AnchorTop  = 0f;   _notifLabel.AnchorBottom = 0f;
        _notifLabel.OffsetLeft = -200; _notifLabel.OffsetRight  = 200;
        _notifLabel.OffsetTop  = 10;   _notifLabel.OffsetBottom = 36;
        _notifLabel.Visible = false;
        root.AddChild(_notifLabel);

        // End Turn button — bottom-right
        var endTurn = new Button { Text = "End Turn" };
        endTurn.AnchorLeft = endTurn.AnchorRight = 1f;
        endTurn.AnchorTop  = endTurn.AnchorBottom = 1f;
        endTurn.OffsetLeft = -150; endTurn.OffsetRight  = -10;
        endTurn.OffsetTop  = -50;  endTurn.OffsetBottom = -10;
        endTurn.Pressed += OnEndTurnPressed;
        root.AddChild(endTurn);

        // Found City button — bottom-center, hidden unless Settler selected
        _foundCityButton = new Button { Text = "Found City  [F]", Visible = false };
        _foundCityButton.AnchorLeft = _foundCityButton.AnchorRight = 0.5f;
        _foundCityButton.AnchorTop  = _foundCityButton.AnchorBottom = 1f;
        _foundCityButton.OffsetLeft = -80; _foundCityButton.OffsetRight  = 80;
        _foundCityButton.OffsetTop  = -50; _foundCityButton.OffsetBottom = -10;
        _foundCityButton.Pressed += () => { if (_selectedUnit != null) TryFoundCity(_selectedUnit); };
        root.AddChild(_foundCityButton);

        // City panel — right side, full height
        _cityPanel = new Panel { Visible = false };
        _cityPanel.AnchorLeft = _cityPanel.AnchorRight = 1f;
        _cityPanel.AnchorTop  = 0f; _cityPanel.AnchorBottom = 1f;
        _cityPanel.OffsetLeft = -250; _cityPanel.OffsetRight = 0;
        _cityPanel.OffsetTop  = 0;   _cityPanel.OffsetBottom = 0;
        root.AddChild(_cityPanel);

        var vbox = new VBoxContainer();
        vbox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        vbox.AddThemeConstantOverride("separation", 4);
        _cityPanel.AddChild(vbox);

        _cityNameLabel = new Label { Text = "" };
        _cityNameLabel.AddThemeFontSizeOverride("font_size", 18);
        vbox.AddChild(_cityNameLabel);

        _cityStatsLabel = new Label { Text = "", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        vbox.AddChild(_cityStatsLabel);

        vbox.AddChild(new HSeparator());

        var buildTitle = new Label { Text = "Production" };
        buildTitle.AddThemeFontSizeOverride("font_size", 14);
        vbox.AddChild(buildTitle);

        _buildList = new VBoxContainer();
        _buildList.AddThemeConstantOverride("separation", 2);
        vbox.AddChild(_buildList);
    }

    // ── Rendering ────────────────────────────────────────────────────────────

    public override void _Draw()
    {
        // 1. Terrain base
        foreach (var (axial, terrain) in _mapData.Tiles)
        {
            var poly = HexVertices(AxialToWorld(axial), HexSize - HexGap);
            DrawPolygon(poly, new[] { TerrainColor(terrain) });
        }

        // 2. Movement range overlay
        foreach (var axial in _reachableTiles)
        {
            var poly = HexVertices(AxialToWorld(axial), HexSize - HexGap);
            DrawPolygon(poly, new[] { new Color(1f, 1f, 0.2f, 0.35f) });
        }

        // 2b. Pending move path preview
        if (_pendingPathPreview != null)
        {
            foreach (var axial in _pendingPathPreview.Skip(1))
                DrawPolygon(HexVertices(AxialToWorld(axial), HexSize - HexGap),
                    new[] { new Color(1f, 0.55f, 0f, 0.45f) });
            for (int i = 0; i < _pendingPathPreview.Count - 1; i++)
                DrawLine(AxialToWorld(_pendingPathPreview[i]),
                         AxialToWorld(_pendingPathPreview[i + 1]),
                         Colors.Orange, 2.5f);
            DrawCircle(AxialToWorld(_pendingPathPreview[^1]), HexSize * 0.22f, Colors.Orange);
        }

        // 3. Cities
        foreach (var city in _cities)
        {
            if (!_visibleTiles.Contains(city.Position) && !_discoveredTiles.Contains(city.Position)) continue;
            var pos   = AxialToWorld(city.Position);
            bool seen = _visibleTiles.Contains(city.Position);
            var col   = seen ? new Color(0.95f, 0.90f, 0.70f) : new Color(0.50f, 0.47f, 0.37f);
            DrawRect(new Rect2(pos - new Vector2(HexSize * 0.35f, HexSize * 0.35f),
                               new Vector2(HexSize * 0.70f, HexSize * 0.70f)), col);
            if (city == _selectedCity)
                DrawRect(new Rect2(pos - new Vector2(HexSize * 0.38f, HexSize * 0.38f),
                                   new Vector2(HexSize * 0.76f, HexSize * 0.76f)),
                         Colors.White, false, 2f);
            if (seen)
            {
                DrawString(ThemeDB.FallbackFont, pos + new Vector2(-HexSize * 0.35f, -HexSize * 0.45f),
                    $"{city.Name} ({city.Population})", HorizontalAlignment.Left, -1, 11, Colors.White);
            }
        }

        // 4. Units (only visible tiles)
        foreach (var unit in _units)
        {
            if (!_visibleTiles.Contains(unit.Position)) continue;
            var pos = unit == _animUnit ? _animPos : AxialToWorld(unit.Position);
            DrawCircle(pos, HexSize * 0.32f, new Color(0.15f, 0.40f, 0.90f));
            DrawString(ThemeDB.FallbackFont, pos + new Vector2(-5f, 5f),
                unit.Data.Name[..1], HorizontalAlignment.Left, -1, 14, Colors.White);
            if (unit == _selectedUnit)
                DrawArc(pos, HexSize * 0.40f, 0f, Mathf.Tau, 24, Colors.Yellow, 2.5f);
            if (unit.MovementRemaining == 0)
                DrawCircle(pos, HexSize * 0.32f, new Color(0f, 0f, 0f, 0.45f));
            if (unit.Fortified)
                DrawArc(pos, HexSize * 0.38f, 0f, Mathf.Tau, 24, new Color(0.4f, 0.8f, 1f), 1.5f);
        }

        // 5. Fog of war
        foreach (var axial in _mapData.Tiles.Keys)
        {
            if (!_discoveredTiles.Contains(axial))
                DrawPolygon(HexVertices(AxialToWorld(axial), HexSize - HexGap), new[] { Colors.Black });
            else if (!_visibleTiles.Contains(axial))
                DrawPolygon(HexVertices(AxialToWorld(axial), HexSize - HexGap),
                    new[] { new Color(0f, 0f, 0f, 0.55f) });
        }
    }

    // ── Input ────────────────────────────────────────────────────────────────

    public override void _Process(double delta)
    {
        var dir = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up))    dir.Y -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down))  dir.Y += 1;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left))  dir.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) dir.X += 1;
        if (dir != Vector2.Zero)
            _camera.Position += dir.Normalized() * PanSpeed * (float)delta / _camera.Zoom.X;

        if (_animUnit != null)
            TickAnimation((float)delta);

        if (_notifSecondsLeft > 0 && !_notifPersistentActive)
        {
            _notifSecondsLeft -= delta;
            _notifLabel.Modulate = new Color(1, 1, 1, (float)(_notifSecondsLeft / NotifDuration));
            if (_notifSecondsLeft <= 0) _notifLabel.Visible = false;
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true } key)
        {
            switch (key.Keycode)
            {
                case Key.Space when _endTurnQueue.Count > 0:
                    AdvanceEndTurnQueue();
                    break;
                case Key.F when _selectedUnit?.Data.Special == "found_city":
                    TryFoundCity(_selectedUnit);
                    break;
                case Key.F when _selectedUnit != null:
                    FortifySelectedUnit();
                    break;
                case Key.Escape:
                    _isPanning = false;
                    _endTurnQueue.Clear();
                    HidePersistentNotif();
                    Deselect();
                    break;
            }
            return;
        }

        if (@event is InputEventMouseMotion panMotion && _isPanning)
        {
            _camera.Position -= panMotion.Relative / _camera.Zoom.X;
            return;
        }

        if (@event is InputEventMouseMotion && _selectedUnit != null
            && Input.IsMouseButtonPressed(MouseButton.Left) && _animUnit == null)
        {
            var axial = WorldToAxial(GetGlobalMousePosition());
            if (axial == _pendingDestination) return;
            if (_reachableTiles.Contains(axial))
            {
                var path = HexGrid.FindPath(_selectedUnit.Position, axial, MovementCost);
                if (path.Count >= 2)
                {
                    _pendingDestination = axial;
                    _pendingPathPreview = path;
                    QueueRedraw();
                }
            }
            else
            {
                _pendingDestination = null;
                _pendingPathPreview = null;
                QueueRedraw();
            }
            return;
        }

        if (@event is not InputEventMouseButton mb) return;

        var zoomMin = Vector2.One * 0.2f;
        var zoomMax = Vector2.One * 5.0f;

        if (mb.Pressed)
        {
            switch (mb.ButtonIndex)
            {
                case MouseButton.WheelUp:
                    _camera.Zoom = (_camera.Zoom * 1.15f).Clamp(zoomMin, zoomMax);
                    break;
                case MouseButton.WheelDown:
                    _camera.Zoom = (_camera.Zoom * 0.87f).Clamp(zoomMin, zoomMax);
                    break;
                case MouseButton.Left when _animUnit == null:
                    HandleLeftPress(WorldToAxial(GetGlobalMousePosition()));
                    break;
                case MouseButton.Right:
                    CancelPendingMove();
                    break;
            }
        }
        else if (mb.ButtonIndex == MouseButton.Left && _animUnit == null)
        {
            HandleLeftRelease();
        }
    }

    // ── Click logic ──────────────────────────────────────────────────────────

    private void HandleLeftPress(Vector2I axial)
    {
        if (!_mapData.Tiles.ContainsKey(axial)) return;

        var clickedUnit = _units.Find(u => u.Position == axial);
        var clickedCity = _cities.Find(c => c.Position == axial);

        if (_selectedUnit != null && _reachableTiles.Contains(axial))
        {
            // Show path preview — actual move happens on mouse-up
            var path = HexGrid.FindPath(_selectedUnit.Position, axial, MovementCost);
            if (path.Count >= 2)
            {
                _pendingDestination = axial;
                _pendingPathPreview = path;
                QueueRedraw();
            }
        }
        else if (clickedUnit != null && (clickedUnit.MovementRemaining > 0 || clickedUnit.Fortified))
        {
            if (clickedUnit.Fortified)
            {
                clickedUnit.Fortified         = false;
                clickedUnit.MovementRemaining = clickedUnit.Data.Movement;
            }
            Deselect();
            _selectedUnit   = clickedUnit;
            _reachableTiles = HexGrid.GetReachableTiles(
                clickedUnit.Position, clickedUnit.MovementRemaining, MovementCost).ToHashSet();
            _foundCityButton.Visible = clickedUnit.Data.Special == "found_city";
            QueueRedraw();
        }
        else if (clickedCity != null)
        {
            Deselect();
            SelectCity(clickedCity);
        }
        else
        {
            Deselect();
            _isPanning         = true;
            _panStartMousePos  = GetViewport().GetMousePosition();
            _panStartCameraPos = _camera.Position;
        }
    }

    private void HandleLeftRelease()
    {
        _isPanning = false;
        if (_pendingDestination == null || _selectedUnit == null) return;
        var dest = _pendingDestination.Value;
        _pendingDestination = null;
        _pendingPathPreview = null;
        StartMove(_selectedUnit, dest);
        Deselect();
    }

    private void CancelPendingMove()
    {
        _pendingDestination = null;
        _pendingPathPreview = null;
        Deselect();
    }

    private void Deselect()
    {
        _selectedUnit       = null;
        _reachableTiles     = new HashSet<Vector2I>();
        _selectedCity       = null;
        _pendingDestination = null;
        _pendingPathPreview = null;
        _foundCityButton.Visible = false;
        _cityPanel.Visible = false;
        QueueRedraw();
    }

    // ── Movement & animation ─────────────────────────────────────────────────

    private void StartMove(Unit unit, Vector2I destination)
    {
        var path = HexGrid.FindPath(unit.Position, destination, MovementCost);
        if (path.Count < 2) return;

        int cost = 0;
        for (int i = 1; i < path.Count; i++)
            cost += MovementCost(path[i]);
        unit.MovementRemaining = Mathf.Max(0, unit.MovementRemaining - cost);
        unit.Position          = destination;

        _animUnit        = unit;
        _animPath        = path;
        _animIndex       = 0;
        _animT           = 0f;
        _animPos         = AxialToWorld(path[0]);
        _animCurrentTile = path[0];
    }

    private void TickAnimation(float delta)
    {
        _animT += delta / SecondsPerTile;

        if (_animT >= 1f)
        {
            _animT -= 1f;
            _animIndex++;
            if (_animIndex >= _animPath!.Count - 1)
            {
                _animPos         = AxialToWorld(_animPath[^1]);
                _animCurrentTile = _animPath[^1];
                _animUnit        = null;
                _animPath        = null;
                UpdateFogOfWar();
                QueueRedraw();
                if (_endTurnQueue.Count > 0) PruneAndShowEndTurnQueue();
                return;
            }
            _animCurrentTile = _animPath[_animIndex];
            UpdateFogOfWar();
        }

        _animPos = AxialToWorld(_animPath![_animIndex])
            .Lerp(AxialToWorld(_animPath[_animIndex + 1]), _animT);
        QueueRedraw();
    }

    // ── City logic ───────────────────────────────────────────────────────────

    private void TryFoundCity(Unit settler)
    {
        var pos     = settler.Position;
        var terrain = _mapData.Tiles.GetValueOrDefault(pos, TerrainType.Ocean);
        if (terrain == TerrainType.Ocean || terrain == TerrainType.Mountain)
        {
            ShowNotification("Cannot found a city here.");
            return;
        }
        if (_cities.Any(c => HexGrid.Distance(c.Position, pos) < MinCityDistance))
        {
            ShowNotification("Too close to another city.");
            return;
        }

        _units.Remove(settler);
        var city = new City(NextCityName(), pos);
        ComputeCityYields(city);
        _cities.Add(city);

        Deselect();
        UpdateFogOfWar();
        ShowNotification($"{city.Name} founded!");
        QueueRedraw();
    }

    private void SelectCity(City city)
    {
        _selectedCity = city;
        _cityPanel.Visible = true;
        RefreshCityPanel(city);
    }

    private void RefreshCityPanel(City city)
    {
        _cityNameLabel.Text = city.Name;

        int netFood  = city.FoodYield - city.Population;
        string prod  = city.ProductionItem != null
            ? $"{GetItemName(city.ProductionItem)} ({ProductionTurnsLeft(city)}) "
            : "Idle";
        _cityStatsLabel.Text =
            $"Pop: {city.Population}\n" +
            $"Food: {city.FoodAccumulated:F0}/{city.GrowthThreshold}  ({(netFood >= 0 ? "+" : "")}{netFood}/turn)\n" +
            $"Prod: {city.ProductionYield}/turn\n" +
            $"Producing: {prod}\n" +
            $"\nBuildings:\n{(city.Buildings.Count > 0 ? string.Join(", ", city.Buildings) : "none")}";

        foreach (var child in _buildList.GetChildren()) child.QueueFree();

        foreach (var u in _unitDefs.Where(u => u.RequiredTech == null))
        {
            var u2 = u; // capture for lambda
            var btn = new Button { Text = $"{u2.Name} ({u2.ProductionCost} prod)" };
            if (city.ProductionItem == $"unit:{u2.Id}")
                btn.Text += "  ◀";
            btn.Pressed += () => { SetProduction(city, $"unit:{u2.Id}"); };
            _buildList.AddChild(btn);
        }

        foreach (var b in _buildingDefs.Where(b => b.RequiredTech == null && !city.Buildings.Contains(b.Id)))
        {
            var b2 = b;
            var btn = new Button { Text = $"{b2.Name} ({b2.ProductionCost} prod)" };
            if (city.ProductionItem == $"building:{b2.Id}")
                btn.Text += "  ◀";
            btn.Pressed += () => { SetProduction(city, $"building:{b2.Id}"); };
            _buildList.AddChild(btn);
        }
    }

    private void SetProduction(City city, string item)
    {
        city.ProductionItem     = item;
        city.ProductionProgress = 0;
        RefreshCityPanel(city);
    }

    private string ProductionTurnsLeft(City city)
    {
        if (city.ProductionItem == null || city.ProductionYield <= 0) return "∞";
        int cost = GetItemCost(city.ProductionItem);
        int left = Math.Max(0, cost - city.ProductionProgress);
        return $"{(int)Math.Ceiling(left / (float)city.ProductionYield)} turns";
    }

    // ── End of turn ──────────────────────────────────────────────────────────

    private void OnEndTurnPressed()
    {
        if (_animUnit != null) return;
        BuildAndStartEndTurnQueue();
    }

    private void BuildAndStartEndTurnQueue()
    {
        _endTurnQueue.Clear();
        foreach (var unit in _units)
            if (unit.MovementRemaining > 0 && !unit.Fortified)
                _endTurnQueue.Add(unit);
        foreach (var city in _cities)
            if (city.ProductionItem == null)
                _endTurnQueue.Add(city);
        // Research prompt would go here once implemented

        if (_endTurnQueue.Count == 0)
            ProcessTurn();
        else
            ShowNextEndTurnItem();
    }

    private void AdvanceEndTurnQueue()
    {
        if (_endTurnQueue.Count > 0) _endTurnQueue.RemoveAt(0);
        PruneAndShowEndTurnQueue();
    }

    private void PruneAndShowEndTurnQueue()
    {
        while (_endTurnQueue.Count > 0)
        {
            bool valid = _endTurnQueue[0] switch
            {
                Unit u => u.MovementRemaining > 0 && !u.Fortified,
                City c => c.ProductionItem == null,
                _      => false,
            };
            if (valid) break;
            _endTurnQueue.RemoveAt(0);
        }

        if (_endTurnQueue.Count == 0)
        {
            HidePersistentNotif();
            ProcessTurn();
        }
        else
            ShowNextEndTurnItem();
    }

    private void ShowNextEndTurnItem()
    {
        if (_endTurnQueue.Count == 0) return;
        Deselect();
        var item = _endTurnQueue[0];
        if (item is Unit unit)
        {
            _selectedUnit            = unit;
            _reachableTiles          = HexGrid.GetReachableTiles(unit.Position, unit.MovementRemaining, MovementCost).ToHashSet();
            _foundCityButton.Visible = unit.Data.Special == "found_city";
            ShowNotification($"{unit.Data.Name} has moves — [Space] Skip  [F] Fortify", persistent: true);
            _camera.Position         = AxialToWorld(unit.Position);
        }
        else if (item is City city)
        {
            SelectCity(city);
            ShowNotification($"{city.Name} needs production — [Space] Skip", persistent: true);
        }
        QueueRedraw();
    }

    private void FortifySelectedUnit()
    {
        if (_selectedUnit == null) return;
        _selectedUnit.Fortified         = true;
        _selectedUnit.MovementRemaining = 0;
        if (_endTurnQueue.Count > 0)
            AdvanceEndTurnQueue();
        else
            Deselect();
    }

    private void ProcessTurn()
    {
        var notifications = new List<string>();

        foreach (var city in _cities)
        {
            if (city.ProcessFood())
                notifications.Add($"{city.Name} grew to population {city.Population}!");

            if (city.ProductionItem != null)
            {
                int cost     = GetItemCost(city.ProductionItem);
                string? done = city.AdvanceProduction(cost);
                if (done != null)
                {
                    CompleteProduction(city, done);
                    notifications.Add($"{city.Name} completed {GetItemName(done)}!");
                }
            }
        }

        foreach (var unit in _units) unit.ResetForNewTurn();

        _turnManager.AdvanceTurn();
        Deselect();
        UpdateFogOfWar();
        UpdateTurnLabel();

        if (notifications.Count > 0)
            ShowNotification(string.Join("  |  ", notifications));

        QueueRedraw();
    }

    private void CompleteProduction(City city, string item)
    {
        if (item.StartsWith("unit:"))
        {
            var id  = item["unit:".Length..];
            var def = _unitDefs.FirstOrDefault(u => u.Id == id);
            if (def != null) _units.Add(new Unit(def, city.Position));
        }
        else if (item.StartsWith("building:"))
        {
            var id  = item["building:".Length..];
            var def = _buildingDefs.FirstOrDefault(b => b.Id == id);
            if (def == null) return;
            city.Buildings.Add(id);
            city.FoodYield       += def.Yields.Food;
            city.ProductionYield += def.Yields.Production;
        }
    }

    // ── Fog of war ───────────────────────────────────────────────────────────

    private void UpdateFogOfWar()
    {
        _visibleTiles.Clear();
        foreach (var unit in _units)
        {
            var origin = (unit == _animUnit) ? _animCurrentTile : unit.Position;
            foreach (var tile in HexGrid.GetRange(origin, unit.Data.Sight))
            {
                if (!_mapData.Tiles.ContainsKey(tile)) continue;
                _visibleTiles.Add(tile);
                _discoveredTiles.Add(tile);
            }
        }
        foreach (var city in _cities)
            foreach (var tile in HexGrid.GetRange(city.Position, CitySightRadius))
            {
                if (!_mapData.Tiles.ContainsKey(tile)) continue;
                _visibleTiles.Add(tile);
                _discoveredTiles.Add(tile);
            }
    }

    // ── Gameplay helpers ─────────────────────────────────────────────────────

    private int MovementCost(Vector2I axial)
    {
        if (!_mapData.Tiles.TryGetValue(axial, out var terrain)) return int.MaxValue;
        return terrain switch
        {
            TerrainType.Mountain => int.MaxValue,
            TerrainType.Ocean    => int.MaxValue,
            TerrainType.Coast    => int.MaxValue,
            TerrainType.Hills    => 2,
            TerrainType.Forest   => 2,
            _                    => 1,
        };
    }

    private void ComputeCityYields(City city)
    {
        int food = 0, prod = 0;
        foreach (var tile in HexGrid.GetRange(city.Position, 1))
        {
            if (!_mapData.Tiles.TryGetValue(tile, out var t)) continue;
            food += TerrainFoodYield(t);
            prod += TerrainProductionYield(t);
        }
        city.FoodYield       = Math.Max(1, food);
        city.ProductionYield = Math.Max(1, prod);
    }

    private void UpdateTurnLabel() => _turnLabel.Text = $"Turn {_turnManager.TurnNumber}";

    private void ShowNotification(string text, bool persistent = false)
    {
        _notifLabel.Text           = text;
        _notifLabel.Modulate       = Colors.White;
        _notifLabel.Visible        = true;
        _notifPersistentActive     = persistent;
        _notifSecondsLeft          = persistent ? 0 : NotifDuration;
    }

    private void HidePersistentNotif()
    {
        if (!_notifPersistentActive) return;
        _notifPersistentActive = false;
        _notifLabel.Visible    = false;
    }

    private int GetItemCost(string item)
    {
        if (item.StartsWith("unit:"))
        {
            var id = item["unit:".Length..];
            return _unitDefs.FirstOrDefault(u => u.Id == id)?.ProductionCost ?? 9999;
        }
        if (item.StartsWith("building:"))
        {
            var id = item["building:".Length..];
            return _buildingDefs.FirstOrDefault(b => b.Id == id)?.ProductionCost ?? 9999;
        }
        return 9999;
    }

    private string GetItemName(string item)
    {
        if (item.StartsWith("unit:"))
        {
            var id = item["unit:".Length..];
            return _unitDefs.FirstOrDefault(u => u.Id == id)?.Name ?? id;
        }
        if (item.StartsWith("building:"))
        {
            var id = item["building:".Length..];
            return _buildingDefs.FirstOrDefault(b => b.Id == id)?.Name ?? id;
        }
        return item;
    }

    private Vector2I FindWalkableTileNear(Vector2I origin)
    {
        if (MovementCost(origin) != int.MaxValue) return origin;
        var visited = new HashSet<Vector2I> { origin };
        var queue   = new Queue<Vector2I>(new[] { origin });
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var n in HexGrid.GetNeighbors(current))
            {
                if (!visited.Add(n)) continue;
                if (MovementCost(n) != int.MaxValue) return n;
                queue.Enqueue(n);
            }
        }
        return origin;
    }

    private static Vector2I MapCenterAxial()
    {
        int col = MapWidth  / 2;
        int row = MapHeight / 2;
        return new Vector2I(col, row - (col - (col & 1)) / 2);
    }

    private string NextCityName()
    {
        var names = new[]
        {
            "Rome", "Athens", "Babylon", "Cairo", "Paris", "London",
            "Moscow", "Beijing", "Delhi", "Tokyo", "Istanbul", "Berlin",
            "Madrid", "Lisbon", "Amsterdam", "Vienna", "Warsaw", "Prague",
        };
        return names[_nextCityName++ % names.Length];
    }

    // ── Terrain yields ───────────────────────────────────────────────────────

    private static int TerrainFoodYield(TerrainType t) => t switch
    {
        TerrainType.Grassland => 2,
        TerrainType.Plains    => 1,
        TerrainType.Forest    => 1,
        TerrainType.Hills     => 1,
        TerrainType.Tundra    => 1,
        TerrainType.Coast     => 2,
        TerrainType.Ocean     => 1,
        _                     => 0,
    };

    private static int TerrainProductionYield(TerrainType t) => t switch
    {
        TerrainType.Hills   => 2,
        TerrainType.Forest  => 2,
        TerrainType.Plains  => 1,
        TerrainType.Desert  => 1,
        _                   => 0,
    };

    // ── Coordinate helpers ───────────────────────────────────────────────────

    public static Vector2 AxialToWorld(Vector2I axial)
    {
        float x = HexSize * 1.5f            * axial.X;
        float y = HexSize * Mathf.Sqrt(3f)  * (axial.Y + axial.X * 0.5f);
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

    // ── Terrain colours ──────────────────────────────────────────────────────

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
