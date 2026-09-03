using ChibiFantasy.Core;

namespace ChibiFantasy.Gameplay
{
    /// <summary>What kind of thing a combatant is doing.</summary>
    /// <remarks>
    /// Two entry points into combat, one lifecycle. A basic attack and a skill differ in
    /// what they validate and what they execute, never in how the action is timed, so they
    /// share <see cref="CombatActionRunner"/> rather than each owning a state machine.
    /// </remarks>
    public enum CombatActionType
    {
        None = 0,
        BasicAttack = 1,
        Skill = 2
    }

    /// <summary>Where an action is in its lifecycle.</summary>
    /// <remarks>
    /// <b>Casting and Recovery are separate for a reason.</b> Casting is before the effect
    /// and can be cancelled with nothing to undo; recovery is after it and cannot. Folding
    /// them into one busy state would lose exactly the distinction cancellation needs.
    ///
    /// <see cref="Rejected"/> and <see cref="Cancelled"/> are terminal and distinct:
    /// rejected means it never started, cancelled means it started and was stopped. A
    /// caller that treats them alike would report a refused request as an interruption.
    /// </remarks>
    public enum CombatActionPhase
    {
        /// <summary>No action. A new one may be requested.</summary>
        Idle = 0,

        /// <summary>Accepted and winding up. The effect has not happened.</summary>
        Casting = 1,

        /// <summary>The effect has happened; the actor is still occupied.</summary>
        Recovery = 2,

        /// <summary>Finished normally. Equivalent to idle for availability.</summary>
        Completed = 3,

        /// <summary>Never started. Nothing was mutated.</summary>
        Rejected = 4,

        /// <summary>Started and stopped. Whether anything was mutated depends on when.</summary>
        Cancelled = 5
    }

    /// <summary>Why an action was cancelled.</summary>
    public enum CombatActionCancelReason
    {
        None = 0,

        /// <summary>Somebody asked for it to stop.</summary>
        Explicit = 1,

        /// <summary>The actor stopped being alive.</summary>
        ActorDied = 2,

        /// <summary>The target stopped being a legal one before the effect landed.</summary>
        TargetInvalidated = 3,

        /// <summary>The runner was reset.</summary>
        Reset = 4
    }

    /// <summary>
    /// One thing a combatant is doing, and how far through it they are.
    /// </summary>
    /// <remarks>
    /// <b>A record of an attempt, not a request.</b> <see cref="AttackIntent"/> and
    /// <see cref="SkillUseRequest"/> are what a caller asks for; this is what the runtime
    /// made of it. Creating one mutates nothing, and in particular costs nothing and deals
    /// no damage: the effect happens when <see cref="CombatActionRunner"/> executes it,
    /// once.
    ///
    /// <b><see cref="HasExecuted"/> is the guard that makes "exactly once" true.</b> Cast
    /// time, a large frame delta and a cancellation all funnel through it, so no path can
    /// apply an effect twice and none can apply one after the action stopped.
    ///
    /// Mutable because it is advanced in place each tick; it is runtime working state and
    /// is never persisted.
    /// </remarks>
    public sealed class CombatAction
    {
        public CombatAction(CombatActionType type, ICombatant actor, ICombatant target,
            DefinitionId skill, int rank, float castTimeSeconds, float recoverySeconds)
        {
            Type = type;
            Actor = actor;
            Target = target;
            Skill = skill;
            Rank = rank;
            CastTimeSeconds = castTimeSeconds > 0f ? castTimeSeconds : 0f;
            RecoverySeconds = recoverySeconds > 0f ? recoverySeconds : 0f;
            Phase = CombatActionPhase.Casting;
            Elapsed = 0f;
        }

        public CombatActionType Type { get; }

        public ICombatant Actor { get; }

        /// <summary>Who it is aimed at. Null for an action that needs no separate target.</summary>
        public ICombatant Target { get; }

        /// <summary>The skill, or <see cref="DefinitionId.None"/> for a basic attack.</summary>
        public DefinitionId Skill { get; }

        public int Rank { get; }

        /// <summary>Authored wind-up. Zero means the effect lands the moment it starts.</summary>
        public float CastTimeSeconds { get; }

        /// <summary>Occupied time after the effect.</summary>
        public float RecoverySeconds { get; }

        public CombatActionPhase Phase { get; internal set; }

        /// <summary>Seconds spent in the current phase.</summary>
        public float Elapsed { get; internal set; }

        /// <summary>Whether the effect has already been applied. Never becomes false again.</summary>
        public bool HasExecuted { get; internal set; }

        public CombatActionCancelReason CancelReason { get; internal set; }

        /// <summary>Result of a basic attack, once executed.</summary>
        public AttackResult AttackResult { get; internal set; }

        /// <summary>Result of a skill use, once executed.</summary>
        public SkillExecutionResult SkillResult { get; internal set; }

        /// <summary>Whether the action still occupies the actor.</summary>
        public bool IsBusy => Phase == CombatActionPhase.Casting || Phase == CombatActionPhase.Recovery;

        /// <summary>Whether the action has stopped, however it stopped.</summary>
        public bool IsFinished =>
            Phase == CombatActionPhase.Completed
            || Phase == CombatActionPhase.Rejected
            || Phase == CombatActionPhase.Cancelled;

        public override string ToString()
        {
            return Type + " " + Phase
                + (Skill.IsValid ? " '" + Skill + "' r" + Rank : string.Empty)
                + " elapsed=" + Elapsed.ToString("0.###")
                + (HasExecuted ? " (executed)" : string.Empty);
        }
    }
}
