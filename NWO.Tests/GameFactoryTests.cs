using System.Linq;
using NWO.Core;
using NWO.Entities;
using NWO.Map;
using Xunit;
using Xunit.Abstractions;

namespace NWO.Tests;

// Phase 10.2 — GameFactory.Populate spawns one player per roster entry on a shared
// landmass. (NewGame itself isn't unit-tested: MapGenerator needs the Godot noise
// engine; Populate is the headless-testable half.)
public class GameFactoryTests
{
    private static DataCatalog Catalog() => new(
        new[]
        {
            new UnitData { Id = "scout",   Name = "Scout",   Movement = 3, Sight = 3, IgnoresTerrainCost = true },
            new UnitData { Id = "settler", Name = "Settler", Movement = 2, Special = "found_city" },
        },
        System.Array.Empty<BuildingData>(),
        null,
        new[]
        {
            new FactionData { Id = "dominion", Name = "The Dominion" },
            new FactionData { Id = "voyagers", Name = "The Voyagers" },
            new FactionData { Id = "reavers",  Name = "The Reavers" },
        });

    private static GameState State() => new(TestWorlds.FlatMap(40, 30), Catalog(), 7);

    [Fact]
    public void Populate_SpawnsOnePlayerPerRosterEntry_WithFactionsAndUnits()
    {
        var state  = State();
        var roster = new[]
        {
            new FactionChoice("dominion", IsHuman: true),
            new FactionChoice("voyagers", IsHuman: false),
            new FactionChoice("reavers",  IsHuman: false),
        };

        var viewer = GameFactory.Populate(state, roster);

        Assert.Equal(3, state.Players.Count);
        Assert.True(viewer.IsHuman);
        Assert.Equal("dominion", viewer.FactionId);
        // Each player gets a scout + settler.
        foreach (var p in state.Players)
            Assert.Equal(2, state.Units.Count(u => u.Owner == p));
        // Distinct owner tints.
        Assert.Equal(3, state.Players.Select(p => p.Color).Distinct().Count());
    }

    [Fact]
    public void Populate_SpreadsStartsApart()
    {
        var state  = State();
        var roster = Enumerable.Range(0, 4)
            .Select(i => new FactionChoice(i == 0 ? "dominion" : "voyagers", IsHuman: i == 0))
            .ToList();

        GameFactory.Populate(state, roster);

        // Scout positions are each player's start; all distinct and reasonably spread.
        var starts = state.Units.Where(u => u.Data.Id == "scout").Select(u => u.Position).ToList();
        Assert.Equal(4, starts.Count);
        Assert.Equal(4, starts.Distinct().Count());
        // Farthest-point sampling keeps the nearest pair clearly apart on a 40×30 map.
        int minPair = int.MaxValue;
        for (int a = 0; a < starts.Count; a++)
        for (int b = a + 1; b < starts.Count; b++)
            minPair = System.Math.Min(minPair, HexGrid.Distance(starts[a], starts[b]));
        Assert.True(minPair >= 5, $"closest pair only {minPair} apart");
    }

    [Fact]
    public void Populate_NullRoster_FallsBackToHumanVsReavers()
    {
        var state  = State();
        var viewer = GameFactory.Populate(state, null);

        Assert.Equal(2, state.Players.Count);
        Assert.True(viewer.IsHuman);
        Assert.Null(viewer.FactionId);                                  // neutral human slot
        Assert.Contains(state.Players, p => p.FactionId == "reavers");  // AI Reavers
    }

    // ── Phase 11: MapScript / MapSize data tests (no Godot engine needed) ────────

    [Fact]
    public void MapScriptParams_PangaeaHasMoreLandThanContinents()
    {
        var continents  = MapScriptParams.For(MapScript.Continents);
        var pangaea     = MapScriptParams.For(MapScript.Pangaea);
        Assert.True(pangaea.TargetLandPercent > continents.TargetLandPercent,
            "Pangaea should target more land than Continents");
        Assert.True(pangaea.RadialFalloff < continents.RadialFalloff,
            "Pangaea should have a smaller radial falloff (less ocean push)");
    }

    [Fact]
    public void MapScriptParams_ArchipelagoHasLessLandThanContinents()
    {
        var continents  = MapScriptParams.For(MapScript.Continents);
        var archipelago = MapScriptParams.For(MapScript.Archipelago);
        Assert.True(archipelago.TargetLandPercent < continents.TargetLandPercent,
            "Archipelago should target less land than Continents");
        Assert.True(archipelago.RadialFalloff > continents.RadialFalloff,
            "Archipelago should have a larger radial falloff (more ocean push)");
    }

    [Fact]
    public void MapScriptParams_HighlandsHasMoreMountainsThanContinents()
    {
        var continents = MapScriptParams.For(MapScript.Continents);
        var highlands  = MapScriptParams.For(MapScript.Highlands);
        Assert.True(highlands.MountainBoost > continents.MountainBoost,
            "Highlands should have a higher mountain boost");
        Assert.True(highlands.MountainLevel < continents.MountainLevel,
            "Highlands should have a lower mountain height threshold (more mountains)");
    }

    [Fact]
    public void MapDimensions_StandardIsDefault()
    {
        var (w, h) = GameFactory.MapDimensions(MapSize.Standard);
        Assert.Equal(GameFactory.MapWidth,  w);
        Assert.Equal(GameFactory.MapHeight, h);
    }

    [Fact]
    public void MapDimensions_LargerThanSmaller()
    {
        var (ws, hs) = GameFactory.MapDimensions(MapSize.Small);
        var (wl, hl) = GameFactory.MapDimensions(MapSize.Large);
        Assert.True(wl > ws && hl > hs, "Large map must be bigger than Small in both dimensions");
    }
}
