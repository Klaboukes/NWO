using Godot;
using NWO.Audio;
using NWO.Core;

namespace NWO.UI;

// The boot scene (project.godot run/main_scene). New Game hands a fresh seed to
// WorldMap via GameLaunch; Load Game opens the SaveBrowser and, on pick, hands the
// deserialized state across the scene change the same way. Both routes converge in
// WorldMap.ResolveLaunch.
public partial class MainMenuController : Control
{
    private SaveBrowserController _browser = null!;

    public override void _Ready()
    {
        GetNode<Button>("CenterPanel/VBox/NewGameButton").Pressed  += () => { Click(); OnNewGame(); };
        GetNode<Button>("CenterPanel/VBox/LoadGameButton").Pressed += () => { Click(); OnLoadGame(); };
        GetNode<Button>("CenterPanel/VBox/QuitButton").Pressed     += () => { Click(); GetTree().Quit(); };

        _browser = GetNode<SaveBrowserController>("SaveBrowser");
        _browser.LoadChosen     += OnLoadChosen;
        _browser.CloseRequested += () => _browser.Hide();
    }

    private static void Click() => AudioManager.Instance?.Play(Sfx.Click);

    private void OnNewGame()
    {
        GameLaunch.LoadedGame  = null;
        GameLaunch.NewGameSeed = (int)GD.Randi();
        GetTree().ChangeSceneToFile(Scenes.World);
    }

    private void OnLoadGame() => _browser.Open(saveMode: false);

    private void OnLoadChosen(string file)
    {
        GameLaunch.LoadedGame = SaveService.Load(file, DataCatalog.Load());
        GetTree().ChangeSceneToFile(Scenes.World);
    }
}
