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
        state.Map.Features[new Vector2I(2, 4)]     = Feature.Hills;
        state.Map.Rivers.Add((new Vector2I(2, 2), 0)); // E edge of (2,2)

        var civ = state.Civ(human);
        civ.Treasury           = 123;
        civ.ScienceAccumulated = 17;
        civ.CultureAccumulated = 9;
        civ.CurrentResearch    = "bronze_working";
        civ.ResearchedTechs.Add("mining");
        civ.ResearchedTechs.Add("pottery");

        state.Units.Add(new Unit(TestWorlds.Warrior(), human, new Vector2I(5, 5))
        {
            HP = 42, MovementRemaining = 1, Fortified = true, ActedThisTurn = true,
            Experience = 30,
            CurrentTask = new ImprovementTask(new Vector2I(5, 5), ImprovementType.Mine, 2),
        });
        // Negative coords exercise the Vector2I converter through a real save.
        state.Units.Add(new Unit(TestWorlds.Warrior(), ai, new Vector2I(-3, 2)));

        var city = new City("Rome", human, new Vector2I(6, 6))
        {
            IsCapital = true, Population = 4, FoodAccumulated = 8.5f,
            ProductionItem = "unit:warrior", ProductionProgress = 12,
            HP = 80, AttackedSinceTurn = true,
            CultureAccumulated = 12, BorderRadius = 3,
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
        Assert.True(loaded.Map.IsHill(new Vector2I(2, 4)));        // Hills feature round-trips

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
        Assert.Equal(9,   civ.CultureAccumulated);
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
        Assert.Equal(30, worker.Experience);
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
        Assert.Equal(12, city.CultureAccumulated);
        Assert.Equal(3,  city.BorderRadius);
    }

    [Fact]
    public void Load_LegacySaveWithoutBorderRadius_DefaultsToInitialRing()
    {
        var original = BuildState(out _, out _);
        var json     = SaveSerializer.Serialize(original, "legacy");

        // Simulate a pre-culture save: the field deserializes to 0.
        json = json.Replace("\"BorderRadius\": 3", "\"BorderRadius\": 0");
        var loaded = SaveSerializer.Deserialize(json, TestWorlds.StandardCatalog());

        Assert.Equal(City.InitialBorderRadius, loaded.Cities.Single().BorderRadius);
    }

    // ── Combat RNG stream position ───────────────────────────────────────────

    // A small duel world: one full-HP warrior per side, adjacent.
    private static GameState DuelState()
    {
        var state = new GameState(TestWorlds.FlatMap(10, 10), TestWorlds.StandardCatalog(), 4242);
        var h     = state.AddPlayer(new Player { Id = 0, Name = "H", IsHuman = true });
        var a     = state.AddPlayer(new Player { Id = 1, Name = "A", IsHuman = false });
        state.Units.Add(new Unit(TestWorlds.Warrior(), h, new Vector2I(5, 5)));
        state.Units.Add(new Unit(TestWorlds.Warrior(), a, new Vector2I(6, 5)));
        state.RestoreTurnPointer(turnNumber: 1, currentPlayerIndex: 0, nextCityName: 0);
        return state;
    }

    // One round of the duel with both fighters topped up, so each call feeds the
    // resolver identical inputs and the only variable is the RNG stream.
    private static GameState.AttackResult Duel(GameState state)
    {
        var atk = state.Units.First(u => u.Owner.Id == 0);
        var def = state.Units.First(u => u.Owner.Id == 1);
        atk.HP = 100; def.HP = 100; atk.MovementRemaining = 1;
        return state.TryAttack(atk, def);
    }

    [Fact]
    public void RoundTrip_ContinuesCombatRngStream()
    {
        // Twin states with the same seed; one is saved+reloaded mid-stream. The
        // post-reload fight must roll exactly what the uninterrupted twin rolls
        // (before this fix a reload re-based the RNG at the seed: reload-scumming).
        var control = DuelState();
        var saved   = DuelState();
        Duel(control);
        Duel(saved);

        var loaded = SaveSerializer.Deserialize(
            SaveSerializer.Serialize(saved, "rng"), TestWorlds.StandardCatalog());
        Assert.Equal(saved.CombatRngDraws, loaded.CombatRngDraws);
        Assert.True(loaded.CombatRngDraws > 0);

        var expected = Duel(control);
        var actual   = Duel(loaded);
        Assert.Equal(expected.AttackerDmg, actual.AttackerDmg);
        Assert.Equal(expected.DefenderDmg, actual.DefenderDmg);
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
    public void RoundTrip_PreservesDiplomacy()
    {
        var original = BuildState(out var human, out var ai);
        original.Diplomacy.Set(human.Id, ai.Id, DiplomaticStance.Alliance);

        var loaded = RoundTrip(original, out _);

        Assert.Equal(DiplomaticStance.Alliance, loaded.Diplomacy.Between(human.Id, ai.Id));
    }

    [Fact]
    public void RoundTrip_PreservesFactionId()
    {
        var state = new GameState(TestWorlds.FlatMap(10, 10), TestWorlds.StandardCatalog(), 1);
        state.AddPlayer(new Player { Id = 0, Name = "P", IsHuman = true,  FactionId = "voyagers" });
        state.AddPlayer(new Player { Id = 1, Name = "R", IsHuman = false, FactionId = null }); // legacy/untyped
        state.RestoreTurnPointer(turnNumber: 1, currentPlayerIndex: 0, nextCityName: 0);

        var json   = SaveSerializer.Serialize(state, "factions");
        var loaded = SaveSerializer.Deserialize(json, TestWorlds.StandardCatalog());

        Assert.Equal("voyagers", loaded.Players.First(p => p.Id == 0).FactionId);
        Assert.Null(loaded.Players.First(p => p.Id == 1).FactionId);
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
