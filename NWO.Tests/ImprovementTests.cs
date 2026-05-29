using System.Collections.Generic;
using System.Linq;
using Godot;
using NWO.Core;
using NWO.Entities;
using NWO.Map;
using Xunit;

namespace NWO.Tests;

// M2b — Worker tile improvements: build validity/tech gating, the multi-turn
// build task (tick down, complete, cancel-on-move), road movement, and the
// worked-tile improvement yields.
public class ImprovementTests
{
    private static UnitData Worker() => new()
    {
        Id = "worker", Name = "Worker", Attack = 0, Defense = 0, Movement = 2,
        Range = 0, Sight = 2, ProductionCost = 70, Special = "build_improvement",
    };

    // ── CanBuild / BuildableOptions ───────────────────────────────────────────────

    [Fact]
    public void CanBuild_FarmOnPlains_NotOnHills()
    {
        var session = TestWorlds.StandardSession(out var human, out _);
        var state   = session.State;
        var plains  = new Vector2I(5, 5);                  // FlatMap is all Plains
        var hills   = new Vector2I(6, 5);
        state.Map.Tiles[hills] = TerrainType.Hills;

        Assert.True(ImprovementService.CanBuild(state, human, plains, ImprovementType.Farm));
        Assert.False(ImprovementService.CanBuild(state, human, hills, ImprovementType.Farm));
    }

    [Fact]
    public void CanBuild_MineRequiresMiningTech()
    {
        var session = TestWorlds.StandardSession(out var human, out _);
        var state   = session.State;
        var hills   = new Vector2I(6, 5);
        state.Map.Tiles[hills] = TerrainType.Hills;

        Assert.False(ImprovementService.CanBuild(state, human, hills, ImprovementType.Mine));
        state.Civ(human).ResearchedTechs.Add("mining");
        Assert.True(ImprovementService.CanBuild(state, human, hills, ImprovementType.Mine));
    }

    [Fact]
    public void CanBuild_FalseWhenAlreadyBuilt()
    {
        var session = TestWorlds.StandardSession(out var human, out _);
        var state   = session.State;
        var plains  = new Vector2I(5, 5);
        state.Map.Improvements[plains] = ImprovementType.Farm;
        Assert.False(ImprovementService.CanBuild(state, human, plains, ImprovementType.Farm));
    }

    [Fact]
    public void BuildableOptions_OnPlains_OffersFarmAndRoad_GatesPastureAndMine()
    {
        var session = TestWorlds.StandardSession(out var human, out _);
        var state   = session.State;
        var plains  = new Vector2I(5, 5);

        var types = ImprovementService.BuildableOptions(state, human, plains).Select(o => o.Type).ToList();
        Assert.Contains(ImprovementType.Farm, types);
        Assert.Contains(ImprovementType.Road, types);
        Assert.DoesNotContain(ImprovementType.Mine, types);    // Plains, not Hills
        Assert.DoesNotContain(ImprovementType.Pasture, types); // needs Animal Husbandry

        state.Civ(human).ResearchedTechs.Add("animal_husbandry");
        var withAH = ImprovementService.BuildableOptions(state, human, plains).Select(o => o.Type).ToList();
        Assert.Contains(ImprovementType.Pasture, withAH);
    }

    // ── Starting a build ──────────────────────────────────────────────────────────

    [Fact]
    public void TryStartImprovement_SetsTaskAndEndsMove()
    {
        var session = TestWorlds.StandardSession(out var human, out _);
        var worker  = new Unit(Worker(), human, new Vector2I(5, 5));
        session.State.Units.Add(worker);

        Assert.True(session.TryStartImprovement(worker, ImprovementType.Farm));
        Assert.NotNull(worker.CurrentTask);
        Assert.Equal(ImprovementType.Farm, worker.CurrentTask!.Type);
        Assert.Equal(0, worker.MovementRemaining);
    }

    [Fact]
    public void TryStartImprovement_RejectsNonWorker()
    {
        var session = TestWorlds.StandardSession(out var human, out _);
        var warrior = new Unit(TestWorlds.Warrior(), human, new Vector2I(5, 5));
        session.State.Units.Add(warrior);
        Assert.False(session.TryStartImprovement(warrior, ImprovementType.Farm));
    }

    [Fact]
    public void BusyWorker_DoesNotNeedAttention()
    {
        var worker = new Unit(Worker(), new Player { Id = 0 }, new Vector2I(5, 5));
        Assert.True(worker.NeedsAttention);                       // idle, has moves
        worker.CurrentTask = new ImprovementTask(new Vector2I(5, 5), ImprovementType.Farm, 3);
        Assert.False(worker.NeedsAttention);                      // busy building
    }

    // ── Task progression ────────────────────────────────────────────────────────

    [Fact]
    public void ImprovementTask_TicksDownThenCompletes()
    {
        var session = TestWorlds.StandardSession(out var human, out _);
        var tile    = new Vector2I(5, 5);
        var worker  = new Unit(Worker(), human, tile) { CurrentTask = new ImprovementTask(tile, ImprovementType.Farm, 2) };
        session.State.Units.Add(worker);

        session.EndTurn();                                        // 2 → 1
        Assert.Equal(ImprovementType.None, session.State.Map.ImprovementAt(tile));
        Assert.Equal(1, worker.CurrentTask!.TurnsRemaining);

        session.EndTurn();                                        // 1 → 0, completes
        Assert.Null(worker.CurrentTask);
        Assert.Equal(ImprovementType.Farm, session.State.Map.ImprovementAt(tile));
    }

    [Fact]
    public void MovingWorker_CancelsTask()
    {
        var session = TestWorlds.StandardSession(out var human, out _);
        var tile    = new Vector2I(5, 5);
        var worker  = new Unit(Worker(), human, tile) { CurrentTask = new ImprovementTask(tile, ImprovementType.Farm, 3) };
        session.State.Units.Add(worker);

        var r = session.TryMove(worker, new Vector2I(6, 5));
        Assert.True(r.Success);
        Assert.Null(worker.CurrentTask);
    }

    // ── Movement + yields ────────────────────────────────────────────────────────

    [Fact]
    public void Road_HalvesMovementCost()
    {
        var session = TestWorlds.StandardSession(out _, out _);
        var state   = session.State;
        var hills   = new Vector2I(6, 5);
        state.Map.Tiles[hills] = TerrainType.Hills;

        Assert.Equal(2, state.MovementCost(hills));               // Hills base
        state.Map.Improvements[hills] = ImprovementType.Road;
        Assert.Equal(1, state.MovementCost(hills));               // halved (min 1)
    }

    [Fact]
    public void Farm_AddsFoodToWorkedTile()
    {
        var session = TestWorlds.StandardSession(out var human, out _);
        var state   = session.State;
        var city    = new City("Rome", human, new Vector2I(5, 5)) { Population = 1 };
        state.Cities.Add(city);

        var tile = new Vector2I(6, 5);
        city.Workforce.Locked.Add(tile);
        CityWorkforceService.Recompute(state, city);
        int withoutFarm = city.FoodYield;

        state.Map.Improvements[tile] = ImprovementType.Farm;
        CityWorkforceService.Recompute(state, city);
        Assert.Equal(withoutFarm + 1, city.FoodYield);
    }
}
