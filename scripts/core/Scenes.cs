namespace NWO.Core;

// Central registry of scene resource paths used in ChangeSceneToFile calls, so
// the literals aren't duplicated (and mistyped) across the controllers.
public static class Scenes
{
    public const string World         = "res://scenes/world/WorldMap.tscn";
    public const string MainMenu      = "res://scenes/ui/MainMenu.tscn";
    public const string VictoryScreen = "res://scenes/ui/VictoryScreen.tscn";
}
