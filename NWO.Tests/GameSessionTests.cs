using Godot;
using NWO.Core;
using NWO.Entities;
using Xunit;

namespace NWO.Tests;

public class GameSessionTests
{
    [Fact]
    public void TryMove_MovesUnitAndDeductsMovement()
    {
        var session = TestWorlds.StandardSession(out var human, out _);
        var unit    = new Unit(TestWorlds.Warrior(), human, new Vector2I(5, 5));
        session.State.Units.Add(unit);

        var result = session.TryMove(unit, new Vector2I(6, 5));

        Assert.True(result.Success);
        Assert.Equal(new Vector2I(6, 5), unit.Position);
        Assert.Equal(1, unit.MovementRemaining);
        Assert.Null(result.CapturedOnArrival);
    }

    [Fact]
    public void TryMove_RejectsEnemyUnitTargetingThroughPath()
    {
        var session = TestWorlds.StandardSession(out var human, out var ai);
        var hUnit   = new Unit(TestWorlds.Warrior(), human, new Vector2I(5, 5));
        var aiUnit  = new Unit(TestWorlds.Warrior(), ai,    new Vector2I(6, 5));
        session.State.Units.Add(hUnit);
        session.State.Units.Add(aiUnit);

        // Cannot walk onto the enemy's tile via right-click move.
        var result = session.TryMove(hUnit, new Vector2I(6, 5));

        Assert.False(result.Success);
        Assert.Equal(new Vector2I(5, 5), hUnit.Position);
    }

    [Fact]
    public void TryMove_RejectsNonViewerUnit()
    {
        var session = TestWorlds.StandardSession(out _, out var ai);
        var aiUnit  = new Unit(TestWorlds.Warrior(), ai, new Vector2I(5, 5));
        session.State.Units.Add(aiUnit);

        var result = session.TryMove(aiUnit, new Vector2I(6, 5));

        Assert.False(result.Success);
        Assert.Equal(new Vector2I(5, 5), aiUnit.Position);
    }

    [Fact]
    public void MoveAndResolve_CapturesEnemyCityOnArrival()
    {
        var session = TestWorlds.StandardSession(out var human, out var ai);
        var captor  = new Unit(TestWorlds.Warrior(), human, new Vector2I(5, 5));
        var city    = new City("Rome", ai, new Vector2I(6, 5));
        session.State.Units.Add(captor);
        session.State.Cities.Add(city);

        var result = session.MoveAndResolve(captor, new Vector2I(6, 5));

        Assert.True(result.Success);
        Assert.Same(city, result.CapturedOnArrival);
        Assert.Same(human, city.Owner);
    }

    [Fact]
    public void TryAttack_AdjacentEnemy_DealsDamage()
    {
        var session  = TestWorlds.StandardSession(out var human, out var ai);
        var attacker = new Unit(TestWorlds.Warrior(), human, new Vector2I(5, 5));
        var defender = new Unit(TestWorlds.Warrior(), ai,    new Vector2I(6, 5));
        session.State.Units.Add(attacker);
        session.State.Units.Add(defender);

        var result = session.TryAttack(attacker, defender);

        Assert.NotEqual(GameState.AttackOutcome.Invalid, result.Outcome);
        Assert.True(defender.HP < 100 || !session.State.Units.Contains(defender));
    }

    [Fact]
    public void TryFoundCity_OnPlains_Succeeds()
    {
        var session = TestWorlds.StandardSession(out var human, out _);
        var settler = new Unit(TestWorlds.Settler(), human, new Vector2I(5, 5));
        session.State.Units.Add(settler);

        var result = session.TryFoundCity(settler, out var city);

        Assert.Equal(GameState.FoundCityResult.Success, result);
        Assert.NotNull(city);
        Assert.Same(human, city!.Owner);
        Assert.DoesNotContain(settler, session.State.Units);
    }

    [Fact]
    public void EndTurn_RunsAIAndReturnsControlToHuman()
    {
        var session = TestWorlds.StandardSession(out _, out _);
        Assert.Equal(0, session.State.CurrentPlayerIndex);

        session.EndTurn();

        Assert.Equal(0, session.State.CurrentPlayerIndex);
        Assert.Equal(2, session.State.TurnManager.TurnNumber);
    }

    [Fact]
    public void EndTurn_AdvancesProductionForOwnerCitiesOnly()
    {
        var session   = TestWorlds.StandardSession(out var human, out var ai);
        var humanCity = new City("H", human, new Vector2I(2, 2))
        {
            Population = 1, FoodYield = 5, ProductionYield = 10, ProductionItem = "unit:warrior",
        };
        var aiCity = new City("A", ai, new Vector2I(15, 15))
        {
            Population = 1, FoodYield = 5, ProductionYield = 10, ProductionItem = "unit:warrior",
        };
        session.State.Cities.Add(humanCity);
        session.State.Cities.Add(aiCity);

        session.EndTurn(); // human ends, AI auto-ends

        Assert.True(humanCity.ProductionProgress > 0 || humanCity.ProductionItem == null);
    }

    [Fact]
    public void Fortify_ZeroesMovementAndFlagsUnit()
    {
        var session = TestWorlds.StandardSession(out var human, out _);
        var unit    = new Unit(TestWorlds.Warrior(), human, new Vector2I(5, 5));
        session.State.Units.Add(unit);

        session.Fortify(unit);

        Assert.True(unit.Fortified);
        Assert.Equal(0, unit.MovementRemaining);
    }

    [Fact]
    public void TryMove_OnFortifiedUnit_WakesItUp()
    {
        var session = TestWorlds.StandardSession(out var human, out _);
        var unit    = new Unit(TestWorlds.Warrior(), human, new Vector2I(5, 5));
        session.State.Units.Add(unit);
        session.Fortify(unit);

        var result = session.TryMove(unit, new Vector2I(6, 5));

        Assert.True(result.Success);
        Assert.False(unit.Fortified);
    }
}
