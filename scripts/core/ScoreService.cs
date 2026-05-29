using NWO.Entities;

namespace NWO.Core;

// Computes a single comparable "civ score" for a player, used to rank players
// for the turn-500 score victory (see VictoryService) and to show on the result
// screen. The weights are deliberately simple and live here as named constants
// so the formula is easy to find and retune.
public static class ScoreService
{
    public const int PerCity       = 10;
    public const int PerPopulation = 3;
    public const int PerTech       = 5;
    public const int GoldDivisor   = 10; // treasury contributes 1 point per 10 gold

    public static int Score(GameState state, Player player)
    {
        int cities     = 0;
        int population = 0;
        foreach (var city in state.Cities)
        {
            if (city.Owner != player) continue;
            cities++;
            population += city.Population;
        }

        var civ   = state.Civ(player);
        int techs = civ.ResearchedTechs.Count;
        int gold  = civ.Treasury;

        return cities * PerCity
             + population * PerPopulation
             + techs * PerTech
             + gold / GoldDivisor;
    }
}
