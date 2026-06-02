using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using NWO.Core;
using NWO.Entities;
using NWO.Map;
using Xunit;

namespace NWO.Tests;

// Save/load round-trip coverage. SaveSerializer is the headless half of the save
// system (no Godot file IO), so it's unit-testable directly; SaveService's
// user:// file access is exercised manually in the Godot run, not here.
public class SaveSerializerTests
{
    // Builds a richly-populated state: improvements, resources, a worker task,
    // a city with buildings + workforce, research/gold, and discovered fog.
    private static GameState BuildState(out Player human, out Player ai)
    {
        var session = TestWorlds.StandardSession(out human, out ai, combatSeed: 777);
        var state   = session.State;

        state.Map.Improvements[new Vector2I(3, 3)] = ImprovementType.Farm;
        state.Map.Resources[new Vector2I(4, 4)]    = ResourceType.Horses;
        state.Map.Rivers.Add((new Vector2I(2, 2), 0)); // E edge of (2,2)

        var civ = state.Civ(human);
        civ.Treasury           = 123;
        civ.ScienceAccumulated = 17;
        civ.CurrentResearch    = "bronze_working";
        civ.ResearchedTechs.Add("mining");
        civ.ResearchedTechs.Add("pottery");

        state.Units.Add(new Unit(TestWorlds.Warrior(), human, new Vector2I(5, 5))
        {
            HP = 42, MovementRemaining = 1, Fortified = true, ActedThisTurn = true,
            CurrentTask = new ImprovementTask(new Vector2I(5, 5), ImprovementType.Mine, 2),
        });
        // Negative coords exercise the Vector2I converter through a real save.
        state.Units.Add(new Unit(TestWorlds.Warrior(), ai, new Vector2I(-3, 2)));

        var city = new City("Rome", human, new Vector2I(6, 6))
        {
            IsCapital = true, Population = 4, FoodAccumulated = 8.5f,
            ProductionItem = "unit:warrior", ProductionProgress = 12,
            HP = 80, AttackedSinceTurn = true,
        };
        city.Buildings.Add("granary");
        city.Workforce.Focus = CityFocus.Production;
        city.Workforce.Locked.Add(new Vector2I(6, 7));
        state.Cities.Add(city);

        state.RecomputeFog(human);
        state.RecomputeFog(ai);
        state.RestoreTurnPointer(turnNumber: 5, currentPlayerIndex: 0, nextCityName: 3);
        return state;
    }

    private static GameState RoundTrip(GameState state, out string json)
    {
        json = SaveSerializer.Serialize(state, "round-trip");
        return SaveSerializer.Deserialize(json, TestWorlds.StandardCatalog());
    }

    [Fact]
    public void RoundTrip_PreservesCoreState()
    {
        var original = BuildState(out _, out _);
        var loaded   = RoundTrip(original, out _);

        Assert.Equal(5, loaded.TurnManager.TurnNumber);
        Assert.Equal(0, loaded.CurrentPlayerIndex);
        Assert.Equal(3, loaded.NextCityNameIndex);
        Assert.Equal(777, loaded.CombatSeed);
        Assert.Equal(2, loaded.Players.Count);
    }

    [Fact]
    public void RoundTrip_PreservesMap()
    {
        var loaded = RoundTrip(BuildState(out _, out _), out _);

        Assert.Equal(ImprovementType.Farm, loaded.Map.ImprovementAt(new Vector2I(3, 3)));
        Assert.Equal(ResourceType.Horses,  loaded.Map.ResourceAt(new Vector2I(4, 4)));
        Assert.Equal(TerrainType.Plains,   loaded.Map.Tiles[new Vector2I(0, 0)]);

        // River edge survives, and adjacency resolves from both sides of the edge.
        Assert.Contains((new Vector2I(2, 2), 0), loaded.Map.Rivers);
        Assert.True(loaded.Map.IsRiverAdjacent(new Vector2I(2, 2)));
        Assert.True(loaded.Map.IsRiverAdjacent(new Vector2I(3, 2))); // neighbour E of (2,2)
    }

    [Fact]
    public void RoundTrip_PreservesCivEconomy()
    {
        var original = BuildState(out var human, out _);
        var loaded   = RoundTrip(original, out _);

        var loadedHuman = loaded.Players.First(p => p.Id == human.Id);
        var civ         = loaded.Civ(loadedHuman);
        Assert.Equal(123, civ.Treasury);
        Assert.Equal(17,  civ.ScienceAccumulated);
        Assert.Equal("bronze_working", civ.CurrentResearch);
        Assert.Contains("mining",  civ.ResearchedTechs);
        Assert.Contains("pottery", civ.ResearchedTechs);
    }

    [Fact]
    public void RoundTrip_PreservesUnitsIncludingWorkerTask()
    {
        var loaded = RoundTrip(BuildState(out _, out _), out _);

        var worker = loaded.Units.First(u => u.Position == new Vector2I(5, 5));
        Assert.Equal(42, worker.HP);
        Assert.Equal(1,  worker.MovementRemaining);
        Assert.True(worker.Fortified);
        Assert.True(worker.ActedThisTurn);
        Assert.NotNull(worker.CurrentTask);
        Assert.Equal(ImprovementType.Mine, worker.CurrentTask!.Type);
        Assert.Equal(2, worker.CurrentTask.TurnsRemaining);

        // Negative-coordinate unit survives (Vector2I converter through a save).
        Assert.Contains(loaded.Units, u => u.Position == new Vector2I(-3, 2));
    }

    [Fact]
    public void RoundTrip_PreservesCity()
    {
        var loaded = RoundTrip(BuildState(out _, out _), out _);

        var city = loaded.Cities.Single();
        Assert.Equal("Rome", city.Name);
        Assert.True(city.IsCapital);
        Assert.Equal(4, city.Population);
        Assert.Equal(8.5f, city.FoodAccumulated);
        Assert.Equal("unit:warrior", city.ProductionItem);
        Assert.Equal(12, city.ProductionProgress);
        Assert.Equal(80, city.HP);
        Assert.True(city.AttackedSinceTurn);
        Assert.Contains("granary", city.Buildings);
        Assert.Equal(CityFocus.Production, city.Workforce.Focus);
        Assert.Contains(new Vector2I(6, 7), city.Workforce.Locked);
    }

    [Fact]
    public void RoundTrip_RebindsOwnershipToPlayerObjects()
    {
        var loaded = RoundTrip(BuildState(out _, out _), out _);

        // Every unit/city owner must be one of the loaded Player instances (by ref),
        // not a stray object — so Civ()/Fog() dictionary lookups resolve.
        foreach (var unit in loaded.Units)
            Assert.Contains(loaded.Players, p => ReferenceEquals(p, unit.Owner));
        foreach (var city in loaded.Cities)
        {
            Assert.Contains(loaded.Players, p => ReferenceEquals(p, city.Owner));
            Assert.NotNull(loaded.Civ(city.Owner)); // would throw if owner weren't registered
        }
    }

    [Fact]
    public void RoundTrip_PreservesDiscoveredFog()
    {
        var original = BuildState(out var human, out _);
        var expected = original.Fog(human).Discovered.ToHashSet();

        var loaded      = RoundTrip(original, out _);
        var loadedHuman = loaded.Players.First(p => p.Id == human.Id);
        var loadedFog   = loaded.Fog(loadedHuman).Discovered;

        Assert.NotEmpty(loadedFog);
        Assert.True(expected.IsSubsetOf(loadedFog));
    }

    [Fact]
    public void LoadedState_KeepsPlaying()
    {
        var loaded  = RoundTrip(BuildState(out _, out _), out _);
        var viewer  = loaded.Players.First(p => p.IsHuman);
        var session = new GameSession(loaded, viewer);

        int before = loaded.TurnManager.TurnNumber;
        var summary = session.EndTurn(); // runs viewer + AI turns without throwing

        Assert.NotNull(summary);
        Assert.Equal(before + 1, loaded.TurnManager.TurnNumber);
    }

    [Fact]
    public void Vector2IConverter_RoundTripsValuesAndKeys()
    {
        var opts = new JsonSerializerOptions { Converters = { new Vector2IJsonConverter() } };

        var v   = new Vector2I(-7, 13);
        var back = JsonSerializer.Deserialize<Vector2I>(JsonSerializer.Serialize(v, opts), opts);
        Assert.Equal(v, back);

        var dict = new Dictionary<Vector2I, int> { [new Vector2I(-1, -2)] = 5, [new Vector2I(3, 4)] = 9 };
        var json = JsonSerializer.Serialize(dict, opts);
        var back2 = JsonSerializer.Deserialize<Dictionary<Vector2I, int>>(json, opts)!;
        Assert.Equal(5, back2[new Vector2I(-1, -2)]);
        Assert.Equal(9, back2[new Vector2I(3, 4)]);
    }
}
