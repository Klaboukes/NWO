using Godot;
using NWO.Audio;
using NWO.Core;

namespace NWO.UI;

// End-of-match result screen. Reads the GameResult handed across the scene change
// via GameLaunch.LastResult and shows whether the human won or lost, the victory
// type, and the winner's score. "Main Menu" returns to the menu; "Quit" exits.
public partial class VictoryScreenController : Control
{
    private const string MainMenuScene = "res://scenes/ui/MainMenu.tscn";

    public override void _Ready()
    {
        var title  = GetNode<Label>("CenterPanel/VBox/TitleLabel");
        var detail = GetNode<Label>("CenterPanel/VBox/ResultLabel");

        var result = GameLaunch.LastResult;
        if (result == null)
        {
            title.Text  = "Game Over";
            detail.Text = "";
        }
        else
        {
            bool humanWon = result.Winner.IsHuman;
            title.Text  = humanWon ? "Victory!" : "Defeat";
            string how  = result.Type == VictoryService.VictoryType.Domination
                ? "Domination victory"
                : "Score victory (turn limit)";
            detail.Text = $"{how}\n{result.Winner.Name} wins with {result.Score} points.";
            AudioManager.Instance?.Play(humanWon ? Sfx.Win : Sfx.Lose);
        }

        GetNode<Button>("CenterPanel/VBox/Buttons/MainMenuButton").Pressed += () => { Click(); OnMainMenu(); };
        GetNode<Button>("CenterPanel/VBox/Buttons/QuitButton").Pressed     += () => { Click(); OnQuit(); };
    }

    private static void Click() => AudioManager.Instance?.Play(Sfx.Click);

    private void OnMainMenu()
    {
        GameLaunch.LastResult = null;
        GetTree().ChangeSceneToFile(MainMenuScene);
    }

    private void OnQuit() => GetTree().Quit();
}
