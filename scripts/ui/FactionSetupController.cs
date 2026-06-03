using System.Collections.Generic;
using Godot;
using NWO.Audio;
using NWO.Core;
using NWO.Entities;

namespace NWO.UI;

// New-game setup screen (Phase 10.2). The player chooses how many factions play
// (2–8) and which faction fills each slot; slot 0 is the human, the rest are AI.
// "Start" builds the roster, hands it to WorldMap via GameLaunch, and launches.
// Reavers are an NPC raider faction (not a player slot), so they're excluded here.
public partial class FactionSetupController : Control
{
    private const int MinPlayers = 2;
    private const int MaxPlayers = 8;

    private OptionButton  _playerCount = null!;
    private VBoxContainer _slots       = null!;
    private readonly List<OptionButton> _slotPicks = new();
    private List<FactionData> _selectable = new();

    public override void _Ready()
    {
        var catalog = DataCatalog.Load();
        foreach (var f in catalog.Factions)
            if (f.Id != "reavers") _selectable.Add(f);

        _playerCount = GetNode<OptionButton>("CenterPanel/VBox/CountRow/PlayerCount");
        _slots       = GetNode<VBoxContainer>("CenterPanel/VBox/Slots");

        for (int n = MinPlayers; n <= MaxPlayers; n++)
            _playerCount.AddItem(n.ToString());
        _playerCount.Selected = 2; // default 4 players (index 2 → value 4)
        _playerCount.ItemSelected += _ => { Click(); RebuildSlots(); };

        GetNode<Button>("CenterPanel/VBox/Buttons/StartButton").Pressed += () => { Click(); OnStart(); };
        GetNode<Button>("CenterPanel/VBox/Buttons/BackButton").Pressed  += () => { Click(); OnBack(); };

        RebuildSlots();
    }

    private int PlayerCountValue => _playerCount.Selected + MinPlayers;

    private static void Click() => AudioManager.Instance?.Play(Sfx.Click);

    // Rebuilds one faction-picker row per player slot. Each row keeps its prior
    // selection where possible; new rows default to a distinct faction.
    private void RebuildSlots()
    {
        var previous = new List<int>();
        foreach (var pick in _slotPicks) previous.Add(pick.Selected);

        foreach (Node child in _slots.GetChildren()) child.QueueFree();
        _slotPicks.Clear();

        int count = PlayerCountValue;
        for (int i = 0; i < count; i++)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 12);

            var label = new Label { Text = i == 0 ? "You" : $"AI {i}", CustomMinimumSize = new Vector2(80, 0) };
            row.AddChild(label);

            var pick = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            foreach (var f in _selectable) pick.AddItem(f.Name);
            pick.Selected = i < previous.Count ? previous[i] : i % _selectable.Count;
            row.AddChild(pick);

            _slots.AddChild(row);
            _slotPicks.Add(pick);
        }
    }

    private void OnStart()
    {
        var roster = new List<FactionChoice>();
        for (int i = 0; i < _slotPicks.Count; i++)
        {
            var faction = _selectable[_slotPicks[i].Selected];
            roster.Add(new FactionChoice(faction.Id, IsHuman: i == 0));
        }

        GameLaunch.LoadedGame    = null;
        GameLaunch.NewGameSeed   = (int)GD.Randi();
        GameLaunch.NewGameRoster = roster;
        GetTree().ChangeSceneToFile(Scenes.World);
    }

    private void OnBack() => GetTree().ChangeSceneToFile(Scenes.MainMenu);
}
