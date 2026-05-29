using System;
using Godot;
using NWO.Core;

namespace NWO.UI;

// Reusable modal for named save slots. Opened in load mode (pick a slot to load,
// or delete one) or save mode (also shows a name field + Save button). Lists are
// rebuilt from SaveService each Open(). Loading/saving needs state this doesn't
// own, so it raises LoadChosen / SaveChosen for the host (MainMenu or WorldMap's
// pause overlay) to act on; Delete it handles itself.
public partial class SaveBrowserController : Control
{
    public event Action<string>? LoadChosen;    // file name
    public event Action<string>? SaveChosen;     // display name
    public event Action?         CloseRequested;

    private Label         _title    = null!;
    private HBoxContainer _saveRow  = null!;
    private LineEdit      _nameEdit = null!;
    private VBoxContainer _slotList = null!;
    private Label         _empty    = null!;

    private ConfirmationDialog _overwriteDialog = null!;
    private string             _pendingName = "";

    public override void _Ready()
    {
        _title    = GetNode<Label>("CenterPanel/VBox/TitleLabel");
        _saveRow  = GetNode<HBoxContainer>("CenterPanel/VBox/SaveRow");
        _nameEdit = GetNode<LineEdit>("CenterPanel/VBox/SaveRow/NameEdit");
        _slotList = GetNode<VBoxContainer>("CenterPanel/VBox/ScrollContainer/SlotList");
        _empty    = GetNode<Label>("CenterPanel/VBox/EmptyLabel");

        GetNode<Button>("CenterPanel/VBox/SaveRow/SaveButton").Pressed += OnSavePressed;
        GetNode<Button>("CenterPanel/VBox/CloseButton").Pressed         += () => CloseRequested?.Invoke();
        _nameEdit.TextSubmitted += _ => OnSavePressed();

        _overwriteDialog = new ConfirmationDialog { DialogText = "Overwrite existing save?" };
        AddChild(_overwriteDialog);
        _overwriteDialog.Confirmed += () => SaveChosen?.Invoke(_pendingName);

        Visible = false;
    }

    // saveMode: show the name field + Save button (in addition to the slot list).
    public void Open(bool saveMode)
    {
        _saveRow.Visible = saveMode;
        _title.Text      = saveMode ? "Save Game" : "Load Game";
        if (saveMode) _nameEdit.Clear();
        RefreshList(saveMode);
        Visible = true;
    }

    public new void Hide() => Visible = false;

    private void OnSavePressed()
    {
        string name = _nameEdit.Text.Trim();
        if (name.Length == 0) return;
        if (SaveService.SlotExists(name))
        {
            _pendingName = name;
            _overwriteDialog.PopupCentered();
            return;
        }
        SaveChosen?.Invoke(name);
    }

    private void RefreshList(bool saveMode)
    {
        foreach (var child in _slotList.GetChildren()) child.QueueFree();

        var slots = SaveService.ListSaves();
        _empty.Visible = slots.Count == 0;

        foreach (var slot in slots)
        {
            var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };

            var label = new Label
            {
                Text                 = $"{slot.Header.Name}  —  turn {slot.Header.Turn}  ·  {slot.Header.Timestamp}",
                SizeFlagsHorizontal  = Control.SizeFlags.ExpandFill,
            };
            row.AddChild(label);

            if (!saveMode)
            {
                var load = new Button { Text = "Load", FocusMode = Control.FocusModeEnum.None };
                var file = slot.File;
                load.Pressed += () => LoadChosen?.Invoke(file);
                row.AddChild(load);
            }

            var del = new Button { Text = "Delete", FocusMode = Control.FocusModeEnum.None };
            var delFile = slot.File;
            del.Pressed += () => { SaveService.Delete(delFile); RefreshList(saveMode); };
            row.AddChild(del);

            _slotList.AddChild(row);
        }
    }
}
