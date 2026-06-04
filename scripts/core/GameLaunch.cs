using NWO.Map;

namespace NWO.Core;

// Cross-scene handoff. A plain C# static survives GetTree().ChangeSceneToFile,
// and the project defines no autoloads, so this is the lightweight channel scenes
// use to pass data across a transition.
//
// P6.1 uses it to carry the game result into the victory screen. P6.2 adds the
// pending new-game / load-game launch request consumed by WorldMap._Ready.
public static class GameLaunch
{
    // The win/loss that ended the last match, set by WorldMap before changing to
    // the victory screen and consumed by VictoryScreenController on load.
    public static VictoryService.GameResult? LastResult { get; set; }

    // The launch request WorldMap reads on load. If LoadedGame is non-null it
    // resumes that deserialized state; otherwise it starts a fresh match using
    // NewGameSeed (or a random seed when null). WorldMap clears both once read.
    public static GameState? LoadedGame  { get; set; }
    public static int?       NewGameSeed { get; set; }

    // The faction roster chosen on the setup screen (Phase 10.2). Null falls back to
    // GameFactory.DefaultRoster (human vs Reavers). Cleared by WorldMap once read.
    public static System.Collections.Generic.IReadOnlyList<FactionChoice>? NewGameRoster { get; set; }

    // Map script and size chosen on the setup screen (Phase 11). Default values are
    // used when launching from a path that bypasses FactionSetup (e.g. save-load).
    public static MapScript NewGameMapScript { get; set; } = MapScript.Continents;
    public static MapSize   NewGameMapSize   { get; set; } = MapSize.Standard;
}
