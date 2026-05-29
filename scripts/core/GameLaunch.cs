namespace NWO.Core;

// Cross-scene handoff. A plain C# static survives GetTree().ChangeSceneToFile,
// and the project defines no autoloads, so this is the lightweight channel scenes
// use to pass data across a transition.
//
// P6.1 uses it only to carry the game result into the victory screen. P6.2 will
// extend it with the pending new-game / load-game launch request.
public static class GameLaunch
{
    // The win/loss that ended the last match, set by WorldMap before changing to
    // the victory screen and consumed by VictoryScreenController on load.
    public static VictoryService.GameResult? LastResult { get; set; }
}
