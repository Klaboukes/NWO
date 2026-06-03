using System;
using System.Linq;
using Godot;
using NWO.Audio;
using NWO.Core;

namespace NWO.UI;

// The in-game Civilopedia browser (Phase 12). Content-only: it renders the categories
// and entries from CivilopediaService and never changes scenes itself. The Back button
// (and Escape) raise CloseRequested — the in-game overlay subscribes to hide the panel,
// while the standalone main-menu scene has no subscriber and falls back to MainMenu.
// Layout skeleton lives in Civilopedia.tscn; the category/entry lists are built here.
public partial class CivilopediaController : Control
{
    public event Action? CloseRequested;

    private HFlowContainer _categories = null!;
    private LineEdit       _search     = null!;
    private VBoxContainer  _entryList  = null!;
    private Label          _detail     = null!;

    private CivilopediaService _service = null!;
    private int _activeCategory;

    public override void _Ready()
    {
        _categories = GetNode<HFlowContainer>("Panel/VBox/Categories");
        _search     = GetNode<LineEdit>("Panel/VBox/SearchEdit");
        _entryList  = GetNode<VBoxContainer>("Panel/VBox/Body/EntryScroll/EntryList");
        _detail     = GetNode<Label>("Panel/VBox/Body/DetailScroll/DetailLabel");

        GetNode<Button>("Panel/VBox/Header/BackButton").Pressed += OnBack;
        _search.TextChanged += _ => RebuildEntries();

        _service = CivilopediaService.Load();
        BuildCategoryButtons();
        SelectCategory(0);
    }

    private static void Click() => AudioManager.Instance?.Play(Sfx.Click);

    private void BuildCategoryButtons()
    {
        for (int i = 0; i < _service.Categories.Count; i++)
        {
            int idx = i;
            var btn = new Button
            {
                Text = _service.Categories[i].Name,
                FocusMode = FocusModeEnum.None,
                ToggleMode = true,
            };
            btn.Pressed += () => { Click(); SelectCategory(idx); };
            _categories.AddChild(btn);
        }
    }

    private void SelectCategory(int idx)
    {
        _activeCategory = idx;
        var kids = _categories.GetChildren();
        for (int i = 0; i < kids.Count; i++)
            if (kids[i] is Button b) b.ButtonPressed = i == idx;

        _search.Text = ""; // fresh filter per category (setting Text emits no signal)
        RebuildEntries();
    }

    // Rebuilds the entry list for the active category under the current search filter,
    // then shows the first match so the detail pane is never blank.
    private void RebuildEntries()
    {
        foreach (var c in _entryList.GetChildren()) c.QueueFree();

        var filter  = _search.Text.Trim().ToLowerInvariant();
        var matches = _service.Categories[_activeCategory].Entries
            .Where(e => filter.Length == 0 || e.Title.ToLowerInvariant().Contains(filter))
            .ToList();

        foreach (var e in matches)
        {
            var entry = e;
            var btn = new Button
            {
                Text = e.Title,
                FocusMode = FocusModeEnum.None,
                Alignment = HorizontalAlignment.Left,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            btn.Pressed += () => { Click(); ShowDetail(entry); };
            _entryList.AddChild(btn);
        }

        ShowDetail(matches.Count > 0 ? matches[0] : null);
    }

    private void ShowDetail(CivilopediaEntry? entry)
        => _detail.Text = entry == null ? "No entries." : $"{entry.Title}\n\n{entry.Detail}";

    private void OnBack()
    {
        Click();
        if (CloseRequested != null) CloseRequested.Invoke();
        else GetTree().ChangeSceneToFile(Scenes.MainMenu);
    }

    // Escape closes the Civilopedia the same way Back does.
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (Visible && @event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            OnBack();
            GetViewport().SetInputAsHandled();
        }
    }
}
