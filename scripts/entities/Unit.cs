using System.Collections.Generic;
using Godot;
using NWO.Core;
using NWO.Map;

namespace NWO.Entities;

// An in-progress Worker improvement: building Type on Tile, TurnsRemaining ticks
// down at end of the owner's turn (see GameState.AdvanceImprovementTask).
public record ImprovementTask(Vector2I Tile, ImprovementType Type, int TurnsRemaining);

// Runtime state for one unit instance. Holds mutable gameplay values only.
// Visual/animation position is owned by the renderer (WorldMap), not here.
public class Unit : IEndTurnItem
{
    public UnitData Data              { get; }
    public Player   Owner             { get; }
    public Vector2I Position          { get; set; }
    public int      HP                { get; set; } = 100;
    public int      MovementRemaining { get; set; }
    public bool     Fortified         { get; set; }
    public bool     ActedThisTurn     { get; set; } // set on move/attack; gates end-of-turn healing
    public bool     SkippedThisTurn   { get; set; } // [Space] skip — done this turn, but still heals
    public bool     SleepUntilHealed  { get; set; } // [H] fortify until HP is full, then auto-wake

    // Combat veterancy (Phase 10). XP accrues on surviving combat (GameState.TryAttack /
    // TryAttackCity), faster for factions with FactionData.XpGainMult > 1 (Iron Pact).
    // Each level grants a flat combat-strength bonus applied in the combat seam.
    public int      Experience        { get; set; }

    // XP thresholds for levels 1..3. Level is the count crossed; capped at 3.
    private static readonly int[] LevelThresholds = { 15, 45, 90 };

    public int Level
    {
        get
        {
            int level = 0;
            foreach (var t in LevelThresholds)
                if (Experience >= t) level++;
            return level;
        }
    }

    // Combat-strength multiplier from veterancy: +10% per level (max +30%).
    public double VeterancyMult => 1.0 + 0.10 * Level;

    // Worker only: the improvement being built. Non-null while busy; cleared when
    // the build completes or the unit is given a different order (e.g. a move).
    public ImprovementTask? CurrentTask { get; set; }

    // Land units held aboard this transport. Empty for non-transport units.
    // Cargo units are removed from state.Units while aboard; they're destroyed if
    // the transport is killed, and returned to the map via TryUnload.
    public List<Unit> Cargo { get; } = new();

    public const int MaxHP = 100;

    public Unit(UnitData data, Player owner, Vector2I position)
    {
        Data              = data;
        Owner             = owner;
        Position          = position;
        MovementRemaining = data.Movement;
    }

    public void ResetForNewTurn()
    {
        ActedThisTurn   = false;
        SkippedThisTurn = false; // a one-turn pass; standing orders (Fortify/Sleep) persist
        if (!Fortified)
            MovementRemaining = Data.Movement;
    }

    // ── IEndTurnItem ─────────────────────────────────────────────────────────

    // A unit needs orders if it still has moves and hasn't been parked this turn
    // (fortified, sleeping-until-healed, skipped, or busy building an improvement).
    public bool     NeedsAttention =>
        MovementRemaining > 0 && !Fortified && !SkippedThisTurn && CurrentTask == null;
    public Vector2I FocusPosition  => Position;
}
