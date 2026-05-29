using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using NWO.Core;
using NWO.Entities;
using NWO.Map;

namespace NWO.UI;

// Owns the HUD widgets and translates button presses into events the
// WorldMap subscribes to. UI layout lives in scenes/ui/UI.tscn — this script
// just grabs node references in _Ready() and exposes a typed surface.
public partial class UIController : CanvasLayer
{
    [Export] private NodePath _turnLabelPath        = "Root/TurnLabel";
    [Export] private NodePath _goldLabelPath        = "Root/GoldLabel";
    [Export] private NodePath _scienceLabelPath     = "Root/ScienceLabel";
    [Export] private NodePath _notifLabelPath       = "Root/NotifLabel";
    [Export] private NodePath _combatForecastPath   = "Root/CombatForecastLabel";
    [Export] private NodePath _endTurnButtonPath    = "Root/EndTurnButton";
    [Export] private NodePath _foundCityButtonPath  = "Root/FoundCityButton";
    [Export] private NodePath _cityPanelPath        = "Root/CityPanel";
    [Export] private NodePath _cityNameLabelPath    = "Root/CityPanel/VBox/CityNameLabel";
    [Export] private NodePath _cityStatsLabelPath   = "Root/CityPanel/VBox/CityStatsLabel";
    [Export] private NodePath _buildListPath        = "Root/CityPanel/VBox/BuildList";
    [Export] private NodePath _unitPanelPath        = "Root/UnitPanel";
    [Export] private NodePath _unitNameLabelPath    = "Root/UnitPanel/VBox/UnitNameLabel";
    [Export] private NodePath _unitHPLabelPath      = "Root/UnitPanel/VBox/UnitHPLabel";
    [Export] private NodePath _unitStatsLabelPath   = "Root/UnitPanel/VBox/UnitStatsLabel";
    [Export] private NodePath _workerActionsPath    = "Root/UnitPanel/VBox/WorkerActions";
    [Export] private NodePath _eventLogPath         = "Root/EventLog";
    [Export] private NodePath _techTreePanelPath    = "Root/TechTreePanel";

    private const double NotifDuration = 3.0;

    private Label         _turnLabel       = null!;
    private Label         _goldLabel       = null!;
    private Label         _scienceLabel    = null!;
    private Label         _notifLabel      = null!;
    private Label         _combatForecast  = null!;
    private Button        _endTurnButton   = null!;
    private Button        _foundCityButton = null!;
    private Panel         _cityPanel       = null!;
    private Label         _cityNameLabel   = null!;
    private Label         _cityStatsLabel  = null!;
    private VBoxContainer _buildList       = null!;
    private Panel                    _unitPanel      = null!;
    private Label                    _unitNameLabel  = null!;
    private Label                    _unitHPLabel    = null!;
    private Label                    _unitStatsLabel = null!;
    private VBoxContainer            _workerActions  = null!;
    private EventLogController       _eventLog       = null!;
    private TechTreePanelController  _techTreePanel  = null!;

    private Unit?  _displayedUnit;
    private double _notifSecondsLeft;
    private bool   _notifPersistent;

    public event Action? EndTurnPressed;
    public event Action? FoundCityPressed;
    public event Action<ImprovementType>? BuildImprovementPressed;
    public event Action<Vector2I>? EventFocusRequested;

    public override void _Ready()
    {
        _turnLabel       = GetNode<Label>(_turnLabelPath);
        _goldLabel       = GetNode<Label>(_goldLabelPath);
        _scienceLabel    = GetNode<Label>(_scienceLabelPath);
        _notifLabel      = GetNode<Label>(_notifLabelPath);
        _combatForecast  = GetNode<Label>(_combatForecastPath);
        _endTurnButton   = GetNode<Button>(_endTurnButtonPath);
        _foundCityButton = GetNode<Button>(_foundCityButtonPath);
        _cityPanel       = GetNode<Panel>(_cityPanelPath);
        _cityNameLabel   = GetNode<Label>(_cityNameLabelPath);
        _cityStatsLabel  = GetNode<Label>(_cityStatsLabelPath);
        _buildList       = GetNode<VBoxContainer>(_buildListPath);
        _unitPanel       = GetNode<Panel>(_unitPanelPath);
        _unitNameLabel   = GetNode<Label>(_unitNameLabelPath);
        _unitHPLabel     = GetNode<Label>(_unitHPLabelPath);
        _unitStatsLabel  = GetNode<Label>(_unitStatsLabelPath);
        _workerActions   = GetNode<VBoxContainer>(_workerActionsPath);
        _eventLog        = GetNode<EventLogController>(_eventLogPath);
        _techTreePanel   = GetNode<TechTreePanelController>(_techTreePanelPath);

        _endTurnButton.Pressed   += () => EndTurnPressed?.Invoke();
        _foundCityButton.Pressed += () => FoundCityPressed?.Invoke();
        _eventLog.FocusRequested += tile => EventFocusRequested?.Invoke(tile);
    }

    public override void _Process(double delta)
    {
        if (_notifSecondsLeft > 0 && !_notifPersistent)
        {
            _notifSecondsLeft -= delta;
            _notifLabel.Modulate = new Color(1, 1, 1, (float)(_notifSecondsLeft / NotifDuration));
            if (_notifSecondsLeft <= 0) _notifLabel.Visible = false;
        }

        if (_displayedUnit != null) RefreshUnitPanel();
    }

    // ── Public surface ───────────────────────────────────────────────────────

    public void SetTurn(int turn) => _turnLabel.Text = $"Turn {turn}";

    public void SetCivStatus(GameState state, Player player)
    {
        var civ           = state.Civ(player);
        int goldPerTurn   = CivEconomyService.GoldPerTurn(state, player);
        int sciencePerTurn = CivEconomyService.SciencePerTurn(state, player);
        _goldLabel.Text   = $"Treasury: {civ.Treasury}  ({(goldPerTurn >= 0 ? "+" : "")}{goldPerTurn}/turn)";

        if (civ.CurrentResearch == null)
        {
            _scienceLabel.Text = $"Science: No research  (+{sciencePerTurn}/turn)";
            return;
        }
        var tech = state.Catalog.Tech(civ.CurrentResearch);
        int cost = tech?.ScienceCost ?? 0;
        string name = tech?.Name ?? civ.CurrentResearch;
        _scienceLabel.Text = $"Science: {name} {civ.ScienceAccumulated}/{cost}  (+{sciencePerTurn}/turn)";
    }

    public void SetFoundCityVisible(bool visible) => _foundCityButton.Visible = visible;

    public void SetEndTurnState(string label, bool blocked)
    {
        _endTurnButton.Text     = label;
        _endTurnButton.Modulate = blocked ? new Color(1f, 0.85f, 0.4f) : Colors.White;
    }

    public void ShowNotification(string text, bool persistent = false)
    {
        _notifLabel.Text       = text;
        _notifLabel.Modulate   = Colors.White;
        _notifLabel.Visible    = true;
        _notifPersistent       = persistent;
        _notifSecondsLeft      = persistent ? 0 : NotifDuration;
    }

    // Append a turn's end-of-turn events to the scrolling log. Events with a
    // Focus tile become clickable rows (see EventLogController).
    public void LogEvents(IEnumerable<GameEvent> events) => _eventLog.Add(events);

    // Combat-odds preview shown while hovering an attackable target. Pass null to hide.
    public void ShowCombatForecast(string? text)
    {
        if (string.IsNullOrEmpty(text)) { _combatForecast.Visible = false; return; }
        _combatForecast.Text     = text;
        _combatForecast.Modulate = new Color(1f, 0.9f, 0.6f);
        _combatForecast.Visible  = true;
    }

    public void HidePersistentNotification()
    {
        if (!_notifPersistent) return;
        _notifPersistent    = false;
        _notifLabel.Visible = false;
    }

    public void HideCityPanel() => _cityPanel.Visible = false;

    public void ToggleTechTree(GameState state, Player player, Action<string> onSetResearch)
        => _techTreePanel.Toggle(state, player, onSetResearch);

    public void HideTechTree() => _techTreePanel.Hide();

    public bool IsTechTreeVisible => _techTreePanel.Visible;

    public void ShowUnitPanel(Unit unit)
    {
        _displayedUnit     = unit;
        _unitPanel.Visible = true;
        ClearWorkerActions();
        RefreshUnitPanel();
    }

    public void HideUnitPanel()
    {
        _displayedUnit     = null;
        _unitPanel.Visible = false;
        ClearWorkerActions();
    }

    // Populate the Worker build menu with one button per available improvement.
    // Pass an empty list (or a non-Worker unit) to clear it.
    public void ShowWorkerActions(IEnumerable<(ImprovementType Type, int Turns)> options)
    {
        ClearWorkerActions();
        foreach (var (type, turns) in options)
        {
            var btn = new Button { Text = $"Build {type} ({turns}t)", FocusMode = Control.FocusModeEnum.None };
            var captured = type;
            btn.Pressed += () => BuildImprovementPressed?.Invoke(captured);
            _workerActions.AddChild(btn);
        }
    }

    private void ClearWorkerActions()
    {
        foreach (var child in _workerActions.GetChildren()) child.QueueFree();
    }

    private const int UnitMaxHP = 100;

    private void RefreshUnitPanel()
    {
        var unit = _displayedUnit!;
        _unitNameLabel.Text = unit.Data.Name;

        _unitHPLabel.Text     = $"HP: {unit.HP} / {UnitMaxHP}";
        _unitHPLabel.Modulate = unit.HP >= 70 ? Colors.LightGreen
                              : unit.HP >= 30 ? Colors.Yellow
                                              : Colors.IndianRed;

        string status = unit.CurrentTask is { } task ? $"  (Building {task.Type}: {task.TurnsRemaining}t left)"
                      : unit.SleepUntilHealed        ? "  (Healing…)"
                      : unit.Fortified               ? "  (Fortified)"
                                                     : "";
        string hints = unit.Data.Special == "found_city"   ? "[B] Found City   [Space] Skip"
                     : unit.Data.Special == "build_improvement" ? "Build below   [Space] Skip   [F] Fortify"
                                                              : "[Space] Skip   [F] Fortify   [H] Heal";
        _unitStatsLabel.Text =
            $"Moves: {unit.MovementRemaining} / {unit.Data.Movement}{status}\n{hints}";
    }

    public void ShowCityPanel(
        GameState state,
        City city,
        Civilization civ,
        Action<string> onSetProduction,
        Action<CityFocus> onSetFocus,
        Action onBuyProduction)
    {
        var catalog = state.Catalog;
        _cityPanel.Visible = true;
        _cityNameLabel.Text = city.Name;

        int netFood = city.FoodYield - city.Population;
        string prod = city.ProductionItem != null
            ? $"{catalog.ItemName(city.ProductionItem)} ({ProductionTurnsLeft(city, catalog)}) "
            : "Idle";
        int locked   = city.Workforce.Locked.Count;
        int assigned = city.Workforce.Assigned.Count;
        _cityStatsLabel.Text =
            $"Pop: {city.Population}  (workers {assigned}{(locked > 0 ? $", {locked} locked" : "")})\n" +
            $"Focus: {city.Workforce.Focus}\n" +
            $"Food: {city.FoodAccumulated:F0}/{city.GrowthThreshold}  ({(netFood >= 0 ? "+" : "")}{netFood}/turn)\n" +
            $"Prod: {city.ProductionYield}/turn\n" +
            $"Producing: {prod}\n" +
            $"\nRight-click a highlighted tile on the map to lock/unlock workers." +
            $"\nBuildings: {(city.Buildings.Count > 0 ? string.Join(", ", city.Buildings) : "none")}";

        foreach (var child in _buildList.GetChildren()) child.QueueFree();

        var focusRow = new HBoxContainer();
        foreach (var f in new[] { CityFocus.Balanced, CityFocus.Food, CityFocus.Production })
        {
            var fbtn = new Button
            {
                Text       = city.Workforce.Focus == f ? $"[{f}]" : f.ToString(),
                FocusMode  = Control.FocusModeEnum.None,
            };
            var captured = f;
            fbtn.Pressed += () => onSetFocus(captured);
            focusRow.AddChild(fbtn);
        }
        _buildList.AddChild(focusRow);

        // Rush-buy the current item with gold.
        if (city.ProductionItem != null)
        {
            int price       = CivEconomyService.BuyCost(state, city);
            bool affordable = price > 0 && civ.Treasury >= price;
            var buyBtn = new Button
            {
                Text      = $"Buy now: {price} gold",
                Disabled  = !affordable,
                FocusMode = Control.FocusModeEnum.None,
            };
            buyBtn.Pressed += () => onBuyProduction();
            _buildList.AddChild(buyBtn);
        }

        foreach (var u in catalog.Units.Where(u =>
                     TechAllows(civ, u.RequiredTech)
                     && ResourceService.Allows(state, civ.Owner, u.RequiredResource)))
        {
            var btn = new Button { Text = $"{u.Name} ({u.ProductionCost} prod)", FocusMode = Control.FocusModeEnum.None };
            if (city.ProductionItem == $"unit:{u.Id}") btn.Text += "  ◀";
            btn.Pressed += () => onSetProduction($"unit:{u.Id}");
            _buildList.AddChild(btn);
        }
        foreach (var b in catalog.Buildings.Where(b => TechAllows(civ, b.RequiredTech) && !city.Buildings.Contains(b.Id)))
        {
            var btn = new Button { Text = $"{b.Name} ({b.ProductionCost} prod)", FocusMode = Control.FocusModeEnum.None };
            if (city.ProductionItem == $"building:{b.Id}") btn.Text += "  ◀";
            btn.Pressed += () => onSetProduction($"building:{b.Id}");
            _buildList.AddChild(btn);
        }
    }

    private static bool TechAllows(Civilization civ, string? requiredTech)
        => requiredTech == null || civ.ResearchedTechs.Contains(requiredTech);

    private static string ProductionTurnsLeft(City city, DataCatalog catalog)
    {
        if (city.ProductionItem == null || city.ProductionYield <= 0) return "∞";
        int cost = catalog.ItemCost(city.ProductionItem);
        int left = Math.Max(0, cost - city.ProductionProgress);
        return $"{(int)Math.Ceiling(left / (float)city.ProductionYield)} turns";
    }
}
