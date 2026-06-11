using System.Collections.Generic;

namespace NWO.Entities;

// Per-player civ-wide state — treasury, science accumulation, and research
// progress. Mirrors the Civ 5 model where these are global to a civ rather
// than per-city. Cities still own their per-turn yields; Civilization sums
// them at end of turn (see CivEconomyService).
public class Civilization
{
    public Player          Owner             { get; }
    public int             Treasury          { get; set; }
    public int             ScienceAccumulated{ get; set; }
    public string?         CurrentResearch   { get; set; }
    public HashSet<string> ResearchedTechs   { get; } = new();

    // Lifetime culture generated across all cities. Feeds civ score
    // (ScoreService); border growth is banked per-city (City.AddCulture).
    public int             CultureAccumulated { get; set; }

    public Civilization(Player owner) => Owner = owner;
}
