using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using NWO.Audio;
using NWO.Core;
using NWO.Entities;
using NWO.Map;

namespace NWO.UI;

// Owns the HUD widgets and translates button presses into events the
// WorldMap subscribes to. UI layout lives in scenes/ui/UI.tscn — this script
// just grabs node references in _Ready() and exposes a typed surface.
public partial class UIController : CanvasLayer
{
    [Export] private NodePath _turnLabelPath        = "Root/TopBar/TurnLabel";
    [Export] private NodePath _goldLabelPath        = "Root/TopBar/GoldLabel";
    [Export] private NodePath _goldIconPath         = "Root/TopBar/GoldIcon";
    [Export] private NodePath _scienceLabelPath     = "Root/TopBar/ScienceLabel";
    [Export] private NodePath _scienceIconPath      = "Root/TopBar/ScienceIcon";
    [Export] private NodePath _notifLabelPath       = "Root/NotifLabel";
    [Export] private NodePath _combatForecastPath   = "Root/CombatForecastLabel";
    [Export] private NodePath _hudClusterPath       = "Root/HudCluster";
    [Export] private NodePath _endTurnButtonPath    = "Root/HudCluster/EndTurnButton";
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
    [Export] private NodePath _minimapPath          = "Root/HudCluster/Minimap";
    [Export] private NodePath _tileTooltipPath      = "Root/TileTooltip";
    [Export] private NodePath _tileTooltipLabelPath = "Root/TileTooltip/Label";
    [Export] private NodePath _techTreePanelPath    = "Root/TechTreePanel";
    [Export] private NodePath _menuButtonPath       = "Root/TopBar/MenuButton";
    [Export] private NodePath _pauseMenuPath        = "Root/PauseMenu";
    [Export] private NodePath _saveBrowserPath      = "Root/SaveBrowser";
    [Export] private NodePath _civilopediaPath      = "Root/Civilopedia";

    private const double NotifDuration = 3.0;

    private Label         _turnLabel       = null!;
    private Label         _goldLabel       = null!;
    private TextureRect   _goldIcon        = null!;
    private Label         _scienceLabel    = null!;
    private TextureRect   _scienceIcon     = null!;
    private Label         _notifLabel      = null!;
    private Label         _combatForecast  = null!;
    private Panel         _hudCluster      = null!;
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
    private MinimapController        _minimap        = null!;
    private Control                  _tileTooltip    = null!;
    private Label                    _tileTooltipLabel = null!;
    private TechTreePanelController  _techTreePanel  = null!;
    private Button                   _menuButton     = null!;
    private Control                  _pauseMenu      = null!;
    private SaveBrowserController    _saveBrowser    = null!;
    private CivilopediaController    _civilopedia    = null!;

    private Unit?  _displayedUnit;
    private double _notifSecondsLeft;
    private bool   _notifPersistent;

    public event Action? EndTurnPressed;
    public event Action? FoundCityPressed;
    public event Action<ImprovementType>? BuildImprovementPressed;
    public event Action<Vector2I>? EventFocusRequested;
    public event Action<string>? SaveRequested;     // slot display name
    public event Action<string>? LoadRequested;     // save file name
    public event Action?         MainMenuRequested;
    public event Action<Unit>?   LoadTransportPressed;   // board a transport ship
    public event Action<Unit>?   UnloadCargoPressed;     // land a cargo unit

    public override void _Ready()
    {
        _turnLabel       = GetNode<Label>(_turnLabelPath);
        _goldLabel       = GetNode<Label>(_goldLabelPath);
        _goldIcon        = GetNode<TextureRect>(_goldIconPath);
        _scienceLabel    = GetNode<Label>(_scienceLabelPath);
        _scienceIcon     = GetNode<TextureRect>(_scienceIconPath);

        _goldIcon.Texture    = HudIconRegistry.For("gold");
        _scienceIcon.Texture = HudIconRegistry.For("science");
        _notifLabel      = GetNode<Label>(_notifLabelPath);
        _combatForecast  = GetNode<Label>(_combatForecastPath);
        _hudCluster      = GetNode<Panel>(_hudClusterPath);
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
        _minimap         = GetNode<MinimapController>(_minimapPath);
        _tileTooltip     = GetNode<Control>(_tileTooltipPath);
        _tileTooltipLabel = GetNode<Label>(_tileTooltipLabelPath);
        _techTreePanel   = GetNode<TechTreePanelController>(_techTreePanelPath);
        _menuButton      = GetNode<Button>(_menuButtonPath);
        _pauseMenu       = GetNode<Control>(_pauseMenuPath);
        _saveBrowser     = GetNode<SaveBrowserController>(_saveBrowserPath);
        _civilopedia     = GetNode<CivilopediaController>(_civilopediaPath);

        _endTurnButton.Pressed   += () => { Click(); EndTurnPressed?.Invoke(); };
        _foundCityButton.Pressed += () => { Click(); FoundCityPressed?.Invoke(); };
        _eventLog.FocusRequested += tile => EventFocusRequested?.Invoke(tile);

        WirePauseMenu();
    }

    // Plays the shared UI click. A no-op under xUnit (no AudioManager autoload).
    private static void Click() => AudioManager.Instance?.Play(Sfx.Click);

    // The HUD "Menu" button opens a pause overlay; Save/Load route through the
    // SaveBrowser. Saving/loading needs the live GameState (held by WorldMap), so
    // this raises events rather than touching SaveService for those two.
    private void WirePauseMenu()
    {
        _menuButton.Pressed += () => { Click(); _pauseMenu.Visible = !_pauseMenu.Visible; };
        Btn("Root/PauseMenu/CenterPanel/VBox/ResumeButton").Pressed   += () => { Click(); _pauseMenu.Visible = false; };
        Btn("Root/PauseMenu/CenterPanel/VBox/SaveButton").Pressed     += () => { Click(); _pauseMenu.Visible = false; _saveBrowser.Open(saveMode: true); };
        Btn("Root/PauseMenu/CenterPanel/VBox/LoadButton").Pressed     += () => { Click(); _pauseMenu.Visible = false; _saveBrowser.Open(saveMode: false); };
        Btn("Root/PauseMenu/CenterPanel/VBox/CivilopediaButton").Pressed += () => { Click(); _pauseMenu.Visible = false; _civilopedia.Visible = true; };
        Btn("Root/PauseMenu/CenterPanel/VBox/MainMenuButton").Pressed += () => { Click(); MainMenuRequested?.Invoke(); };
        Btn("Root/PauseMenu/CenterPanel/VBox/QuitButton").Pressed     += () => { Click(); GetTree().Quit(); };

        _saveBrowser.SaveChosen     += name => { _saveBrowser.Hide(); SaveRequested?.Invoke(name); ShowNotification($"Saved \"{name}\"."); };
        _saveBrowser.LoadChosen     += file => LoadRequested?.Invoke(file);
        _saveBrowser.CloseRequested += () => _saveBrowser.Hide();

        // The in-game Civilopedia is an overlay, not a scene change — Back/Escape just
        // hide it, leaving the match untouched. F1 toggles it (see _UnhandledKeyInput).
        _civilopedia.CloseRequested += () => _civilopedia.Visible = false;
    }

    // F1 toggles the Civilopedia overlay from anywhere in-game (Civ 5 convention).
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.F1 })
        {
            Click();
            if (!_civilopedia.Visible) _pauseMenu.Visible = false;
            _civilopedia.Visible = !_civilopedia.Visible;
            GetViewport().SetInputAsHandled();
        }
    }

    private Button Btn(string path) => GetNode<Button>(path);

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
        _goldLabel.Text   = $"{civ.Treasury}  ({(goldPerTurn >= 0 ? "+" : "")}{goldPerTurn}/turn)";

        if (civ.CurrentResearch == null)
        {
            _scienceLabel.Text = $"No research  (+{sciencePerTurn}/turn)";
            return;
        }
        var tech = state.Catalog.Tech(civ.CurrentResearch);
        int cost = tech?.ScienceCost ?? 0;
        string name = tech?.Name ?? civ.CurrentResearch;
        _scienceLabel.Text = $"{name} {civ.ScienceAccumulated}/{cost}  (+{sciencePerTurn}/turn)";
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

    // Wipe the event log (called at the start of each turn).
    public void ClearEventLog() => _eventLog.Clear();

    // One-time minimap setup (needs the world camera + a recenter callback).
    public void InitializeMinimap(GameState state, Player viewer, Camera3D camera, Action<Vector3> onRecenter)
        => _minimap.Initialize(state, viewer, camera, onRecenter);

    // Hovered-tile tooltip (terrain/yields/resource/improvement). Positioned just
    // off the cursor; pass screenPos in viewport coordinates. HideTileTooltip clears it.
    public void ShowTileTooltip(string text, Vector2 screenPos)
    {
        _tileTooltipLabel.Text = text;
        _tileTooltip.Position  = screenPos + new Vector2(16, 16);
        _tileTooltip.Visible   = true;
    }

    public void HideTileTooltip() => _tileTooltip.Visible = false;

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

    public void HideCityPanel()
    {
        _cityPanel.Visible = false;
        _hudCluster.Visible = true; // restore the End Turn + minimap cluster the city panel covered
    }

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
            btn.Pressed += () => { Click(); BuildImprovementPressed?.Invoke(captured); };
            _workerActions.AddChild(btn);
        }
    }

    public void ClearWorkerActions()
    {
        foreach (var child in _workerActions.GetChildren()) child.QueueFree();
    }

    // Append board/unload buttons below any existing worker-action buttons.
    // Call ClearWorkerActions() first if you want a clean slate.
    public void ShowNavalActions(IEnumerable<Unit> boardableTransports, IEnumerable<Unit> landableCargo)
    {
        foreach (var transport in boardableTransports)
        {
            var btn = new Button { Text = $"Board {transport.Data.Name}", FocusMode = Control.FocusModeEnum.None };
            var cap = transport;
            btn.Pressed += () => { Click(); LoadTransportPressed?.Invoke(cap); };
            _workerActions.AddChild(btn);
        }
        foreach (var cargo in landableCargo)
        {
            var btn = new Button { Text = $"Land {cargo.Data.Name}", FocusMode = Control.FocusModeEnum.None };
            var cap = cargo;
            btn.Pressed += () => { Click(); UnloadCargoPressed?.Invoke(cap); };
            _workerActions.AddChild(btn);
        }
    }

    private void RefreshUnitPanel()
    {
        var unit = _displayedUnit!;
        _unitNameLabel.Text = unit.Data.Name;

        _unitHPLabel.Text     = $"HP: {unit.HP} / {Unit.MaxHP}";
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

        // Combat line: strength + attack type. Attack 0 ⇒ civilian (Settler/Worker);
        // Range ≥ 2 ⇒ ranged (matches GameState's isRanged rule). See docs/MECHANICS.md.
        string combatLine = unit.Data.Attack > 0
            ? $"Atk {unit.Data.Attack}   Def {unit.Data.Defense}   " +
              (unit.Data.Range >= 2 ? $"Ranged (range {unit.Data.Range})" : "Melee")
            : "Civilian — non-combat";

        _unitStatsLabel.Text =
            $"{combatLine}\n" +
            $"Sight: {unit.Data.Sight}   Upkeep: {unit.Data.MaintenanceGold}g\n" +
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
        _hudCluster.Visible = false; // hide the End Turn + minimap cluster the city panel overlaps
        _cityNameLabel.Text = city.Name;

        int netFood = city.FoodYield - city.Population;
        string prod = city.ProductionItem != null
            ? $"{ProductionDisplayName(catalog, city)} ({ProductionTurnsLeft(city, catalog)}) "
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

        var focusRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        foreach (var f in new[] { CityFocus.Balanced, CityFocus.Food, CityFocus.Production })
        {
            var fbtn = new Button
            {
                Text       = city.Workforce.Focus == f ? $"[{f}]" : f.ToString(),
                FocusMode  = Control.FocusModeEnum.None,
            };
            var captured = f;
            fbtn.Pressed += () => { Click(); onSetFocus(captured); };
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
            buyBtn.Pressed += () => { Click(); onBuyProduction(); };
            _buildList.AddChild(buyBtn);
        }

        // Unique variants are never queued directly — they swap in for their base id
        // at production. Skip them here, and for the owning faction relabel the base
        // unit to the variant it actually yields (cost stays the base/charged cost).
        foreach (var u in catalog.Units.Where(u =>
                     !catalog.IsFactionVariant(u.Id)
                     && TechAllows(civ, u.RequiredTech)
                     && ResourceService.Allows(state, civ.Owner, u.RequiredResource)))
        {
            var shown = catalog.Unit(catalog.ResolveUnitForFaction(u.Id, civ.Owner)) ?? u;
            int cost  = state.EffectiveItemCost(civ.Owner, $"unit:{u.Id}");
            var btn = new Button { Text = $"{shown.Name} ({cost} prod)", FocusMode = Control.FocusModeEnum.None };
            if (city.ProductionItem == $"unit:{u.Id}") btn.Text += "  ◀";
            btn.Pressed += () => { Click(); onSetProduction($"unit:{u.Id}"); };
            _buildList.AddChild(btn);
        }
        foreach (var b in catalog.Buildings.Where(b => TechAllows(civ, b.RequiredTech) && !city.Buildings.Contains(b.Id)))
        {
            var btn = new Button { Text = $"{b.Name} ({b.ProductionCost} prod)", FocusMode = Control.FocusModeEnum.None };
            if (city.ProductionItem == $"building:{b.Id}") btn.Text += "  ◀";
            btn.Pressed += () => { Click(); onSetProduction($"building:{b.Id}"); };
            _buildList.AddChild(btn);
        }
    }

    private static bool TechAllows(Civilization civ, string? requiredTech)
        => requiredTech == null || civ.ResearchedTechs.Contains(requiredTech);

    // Name of the city's current build item, resolved to the owner's unique variant
    // for units (a Voyager building "scout" shows "Ranger", matching what it yields).
    private static string ProductionDisplayName(DataCatalog catalog, City city)
    {
        var (kind, id) = DataCatalog.SplitItem(city.ProductionItem!);
        if (kind == "unit")
            return catalog.Unit(catalog.ResolveUnitForFaction(id, city.Owner))?.Name
                   ?? catalog.ItemName(city.ProductionItem!);
        return catalog.ItemName(city.ProductionItem!);
    }

    private static string ProductionTurnsLeft(City city, DataCatalog catalog)
    {
        if (city.ProductionItem == null || city.ProductionYield <= 0) return "∞";
        int cost = catalog.ItemCost(city.ProductionItem);
        int left = Math.Max(0, cost - city.ProductionProgress);
        return $"{(int)Math.Ceiling(left / (float)city.ProductionYield)} turns";
    }
}
