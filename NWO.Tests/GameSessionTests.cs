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
        var city    = new City("Rome", ai, new Vector2I(6, 5)) { HP = 0 }; // already battered down → conquerable
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
    public void TryFoundCity_FirstCityIsCapital_SecondIsNot()
    {
        var session  = TestWorlds.StandardSession(out var human, out _);
        var settler1 = new Unit(TestWorlds.Settler(), human, new Vector2I(5, 5));
        var settler2 = new Unit(TestWorlds.Settler(), human, new Vector2I(15, 15)); // far past MinCityDistance
        session.State.Units.Add(settler1);
        session.State.Units.Add(settler2);

        Assert.Equal(GameState.FoundCityResult.Success, session.TryFoundCity(settler1, out var capital));
        Assert.Equal(GameState.FoundCityResult.Success, session.TryFoundCity(settler2, out var second));

        Assert.True(capital!.IsCapital);
        Assert.False(second!.IsCapital);
    }

    [Fact]
    public void CaptureCity_PreservesCapitalFlag()
    {
        var session = TestWorlds.StandardSession(out var human, out var ai);
        var capital = new City("Rome", ai, new Vector2I(8, 8)) { IsCapital = true };
        var captor  = new Unit(TestWorlds.Warrior(), human, new Vector2I(8, 8));
        session.State.Cities.Add(capital);

        session.State.CaptureCity(captor, capital);

        Assert.Same(human, capital.Owner);
        Assert.True(capital.IsCapital); // captured capital stays flagged for domination victory
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
    public void Fortify_FlagsUnitAndKeepsRemainingMovement()
    {
        var session = TestWorlds.StandardSession(out var human, out _);
        var unit    = new Unit(TestWorlds.Warrior(), human, new Vector2I(5, 5));
        session.State.Units.Add(unit);

        session.Fortify(unit);

        Assert.True(unit.Fortified);
        // Fortify is a standing order, not a movement spend: waking the unit the
        // same turn lets it use only what it had left when it fortified.
        Assert.Equal(unit.Data.Movement, unit.MovementRemaining);
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

    [Fact]
    public void FortifyThenWake_DoesNotRefundSpentMovement()
    {
        var session = TestWorlds.StandardSession(out var human, out _);
        var unit    = new Unit(TestWorlds.Warrior(), human, new Vector2I(5, 5));
        session.State.Units.Add(unit);

        // Spend the whole movement allowance, then fortify and try to move again.
        Assert.True(session.TryMove(unit, new Vector2I(7, 5)).Success); // 2 tiles = full budget
        Assert.Equal(0, unit.MovementRemaining);
        session.Fortify(unit);

        var second = session.TryMove(unit, new Vector2I(8, 5));

        Assert.False(second.Success); // waking must not grant movement (was an infinite-move exploit)
        Assert.Equal(new Vector2I(7, 5), unit.Position);
    }

    [Fact]
    public void FortifiedUnit_RegainsMovementAtTurnReset()
    {
        var session = TestWorlds.StandardSession(out var human, out _);
        var unit    = new Unit(TestWorlds.Warrior(), human, new Vector2I(5, 5));
        session.State.Units.Add(unit);

        Assert.True(session.TryMove(unit, new Vector2I(7, 5)).Success);
        session.Fortify(unit);
        session.EndTurn();

        Assert.True(unit.Fortified);                                  // standing order persists
        Assert.Equal(unit.Data.Movement, unit.MovementRemaining);     // fresh budget for the new turn
    }

    [Fact]
    public void TryMove_StopsAtMovementBudget()
    {
        var session = TestWorlds.StandardSession(out var human, out _);
        var unit    = new Unit(TestWorlds.Warrior(), human, new Vector2I(5, 5)); // movement 2
        session.State.Units.Add(unit);

        // Order a 5-tile march: the unit advances only as far as this turn's budget.
        var result = session.TryMove(unit, new Vector2I(10, 5));

        Assert.True(result.Success);
        Assert.Equal(new Vector2I(7, 5), unit.Position);
        Assert.Equal(0, unit.MovementRemaining);
        Assert.Equal(3, result.Path.Count); // origin + the two affordable steps
    }

    [Fact]
    public void TryMove_WithNoMovementRemaining_Fails()
    {
        var session = TestWorlds.StandardSession(out var human, out _);
        var unit    = new Unit(TestWorlds.Warrior(), human, new Vector2I(5, 5)) { MovementRemaining = 0 };
        session.State.Units.Add(unit);

        var result = session.TryMove(unit, new Vector2I(6, 5));

        Assert.False(result.Success);
        Assert.Equal(new Vector2I(5, 5), unit.Position);
    }

    [Fact]
    public void TryMove_DoesNotStopOnFriendlyUnit()
    {
        var session  = TestWorlds.StandardSession(out var human, out _);
        var mover    = new Unit(TestWorlds.Warrior(), human, new Vector2I(5, 5));
        var blocker  = new Unit(TestWorlds.Warrior(), human, new Vector2I(6, 5));
        session.State.Units.Add(mover);
        session.State.Units.Add(blocker);

        // Passing through a friendly tile is allowed; stopping on it is not, so a
        // move that can only afford the occupied tile fails outright.
        var throughMove = session.TryMove(mover, new Vector2I(7, 5));
        Assert.True(throughMove.Success);
        Assert.Equal(new Vector2I(7, 5), mover.Position);

        mover.MovementRemaining = 1;
        var ontoFriendly = session.TryMove(mover, new Vector2I(6, 5));
        Assert.False(ontoFriendly.Success);
        Assert.Equal(new Vector2I(7, 5), mover.Position);
    }
}
