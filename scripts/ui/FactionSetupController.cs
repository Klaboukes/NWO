using System.Collections.Generic;
using Godot;
using NWO.Audio;
using NWO.Core;
using NWO.Entities;

namespace NWO.UI;

// New-game setup screen (Phase 10.2). The player chooses how many factions play
// (2–8) and which faction fills each slot; slot 0 is the human, the rest are AI.
// AI slots default to "Random", resolved to distinct factions when the game starts.
// "Start" builds the roster, hands it to WorldMap via GameLaunch, and launches.
// Reavers are an NPC raider faction (not a player slot), so they're excluded here.
public partial class FactionSetupController : Control
{
    private const int MinPlayers = 2;
    private const int MaxPlayers = 8;
    private const int RandomIndex = 0; // option 0 in every picker is "Random"

    private OptionButton  _playerCount = null!;
    private VBoxContainer _slots       = null!;
    private readonly List<OptionButton> _slotPicks = new();
    private readonly List<int>          _selection = new(); // picker index per slot (0 = Random)
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

    // Rebuilds one faction-picker row per player slot. Existing slots keep their
    // selection; a new player slot defaults to the first faction, new AI slots to
    // "Random" (resolved to a concrete faction at launch).
    private void RebuildSlots()
    {
        int count = PlayerCountValue;
        while (_selection.Count > count) _selection.RemoveAt(_selection.Count - 1);
        while (_selection.Count < count) _selection.Add(_selection.Count == 0 ? 1 : RandomIndex);

        foreach (Node child in _slots.GetChildren()) child.QueueFree();
        _slotPicks.Clear();

        for (int i = 0; i < count; i++)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 12);

            var label = new Label { Text = i == 0 ? "You" : $"AI {i}", CustomMinimumSize = new Vector2(80, 0) };
            row.AddChild(label);

            var pick = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            pick.AddItem("Random");
            foreach (var f in _selectable) pick.AddItem(f.Name);
            pick.Selected = _selection[i];
            int slot = i;
            pick.ItemSelected += idx => { Click(); OnSlotChanged(slot, (int)idx); };
            row.AddChild(pick);

            _slots.AddChild(row);
            _slotPicks.Add(pick);
        }
    }

    // Keeps concrete picks distinct: choosing a faction another slot already holds
    // bumps that other slot back to "Random". Multiple "Random" slots are allowed —
    // they resolve to distinct factions at launch.
    private void OnSlotChanged(int slot, int idx)
    {
        _selection[slot] = idx;
        if (idx == RandomIndex) return;
        for (int i = 0; i < _selection.Count; i++)
        {
            if (i == slot || _selection[i] != idx) continue;
            _selection[i] = RandomIndex;
            _slotPicks[i].Selected = RandomIndex;
        }
    }

    private void OnStart()
    {
        // Concrete picks are taken first; "Random" slots fill from the leftover
        // factions (random order) so the resolved roster stays distinct where it can.
        var used = new HashSet<int>();
        foreach (int sel in _selection)
            if (sel != RandomIndex) used.Add(sel - 1);

        var roster = new List<FactionChoice>();
        for (int i = 0; i < _selection.Count; i++)
        {
            int factionIdx = _selection[i] == RandomIndex ? PickRandomFaction(used) : _selection[i] - 1;
            used.Add(factionIdx);
            roster.Add(new FactionChoice(_selectable[factionIdx].Id, IsHuman: i == 0));
        }

        GameLaunch.LoadedGame    = null;
        GameLaunch.NewGameSeed   = (int)GD.Randi();
        GameLaunch.NewGameRoster = roster;
        GetTree().ChangeSceneToFile(Scenes.World);
    }

    // A random faction index not in `used`; falls back to any faction once every one
    // is taken (more players than factions makes repeats unavoidable).
    private int PickRandomFaction(HashSet<int> used)
    {
        var free = new List<int>();
        for (int i = 0; i < _selectable.Count; i++)
            if (!used.Contains(i)) free.Add(i);
        if (free.Count == 0) return (int)(GD.Randi() % (uint)_selectable.Count);
        return free[(int)(GD.Randi() % (uint)free.Count)];
    }

    private void OnBack() => GetTree().ChangeSceneToFile(Scenes.MainMenu);
}
