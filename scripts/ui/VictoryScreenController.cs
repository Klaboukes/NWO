using Godot;
using NWO.Core;

namespace NWO.UI;

// End-of-match result screen. Reads the GameResult handed across the scene change
// via GameLaunch.LastResult and shows whether the human won or lost, the victory
// type, and the winner's score. "Play Again" starts a fresh match; "Quit" exits.
// (P6.2 replaces "Play Again" with a route back to the main menu.)
public partial class VictoryScreenController : Control
{
    private const string WorldScene = "res://scenes/world/WorldMap.tscn";

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
        }

        GetNode<Button>("CenterPanel/VBox/Buttons/PlayAgainButton").Pressed += OnPlayAgain;
        GetNode<Button>("CenterPanel/VBox/Buttons/QuitButton").Pressed     += OnQuit;
    }

    private void OnPlayAgain()
    {
        GameLaunch.LastResult = null;
        GetTree().ChangeSceneToFile(WorldScene);
    }

    private void OnQuit() => GetTree().Quit();
}
