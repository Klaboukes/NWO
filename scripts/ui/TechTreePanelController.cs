using System;
using Godot;
using NWO.Core;
using NWO.Entities;

namespace NWO.UI;

// Owns the tech-tree popup. Each Show(...) call wipes and repopulates the
// TechList VBox — cheap (8 techs in MVP) and keeps the controller stateless
// between opens.
public partial class TechTreePanelController : Panel
{
    [Export] private NodePath _titlePath    = "VBox/Title";
    [Export] private NodePath _techListPath = "VBox/TechList";

    private Label         _title    = null!;
    private VBoxContainer _techList = null!;

    public override void _Ready()
    {
        _title    = GetNode<Label>(_titlePath);
        _techList = GetNode<VBoxContainer>(_techListPath);
    }

    public void Show(GameState state, Player player, Action<string> onSetResearch)
    {
        Visible = true;
        var civ      = state.Civ(player);
        int perTurn  = CivEconomyService.SciencePerTurn(state, player);
        _title.Text  = $"Research — +{perTurn} science/turn";

        foreach (var child in _techList.GetChildren()) child.QueueFree();

        foreach (var tech in state.Catalog.Techs)
        {
            var row = new HBoxContainer();

            var label = new Label { Text = $"{tech.Name} ({tech.ScienceCost})" };
            label.CustomMinimumSize = new Vector2(220, 0);
            row.AddChild(label);

            string status;
            bool canPick = false;
            if (civ.ResearchedTechs.Contains(tech.Id))
            {
                status = "Researched";
            }
            else if (civ.CurrentResearch == tech.Id)
            {
                status = $"In progress {civ.ScienceAccumulated}/{tech.ScienceCost}";
            }
            else if (AllPrereqsMet(civ, tech))
            {
                status  = "Available";
                canPick = true;
            }
            else
            {
                status = $"Locked (needs {string.Join(", ", tech.Prerequisites)})";
            }

            var statusLabel = new Label { Text = status };
            statusLabel.CustomMinimumSize = new Vector2(200, 0);
            row.AddChild(statusLabel);

            if (canPick)
            {
                var btn = new Button { Text = "Set", FocusMode = Control.FocusModeEnum.None };
                var captured = tech.Id;
                btn.Pressed += () => onSetResearch(captured);
                row.AddChild(btn);
            }

            _techList.AddChild(row);
        }
    }

    public new void Hide() => Visible = false;

    public void Toggle(GameState state, Player player, Action<string> onSetResearch)
    {
        if (Visible) Hide();
        else         Show(state, player, onSetResearch);
    }

    private static bool AllPrereqsMet(Civilization civ, TechData tech)
    {
        foreach (var p in tech.Prerequisites)
            if (!civ.ResearchedTechs.Contains(p)) return false;
        return true;
    }
}
