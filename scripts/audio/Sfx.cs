namespace NWO.Audio;

// The game's small palette of sound effects. AudioManager maps each to a clip
// (a real res://assets/audio/<name>.ogg if present, else a synthesized tone).
public enum Sfx
{
    Click,      // UI button presses
    Move,       // a unit is ordered to move
    Attack,     // combat resolves (unit or city)
    CityFound,  // a city is founded
    Win,        // result screen — the human won
    Lose,       // result screen — the human lost
}
