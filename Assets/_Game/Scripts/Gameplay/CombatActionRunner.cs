using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>Why an action request was refused before it began.</summary>
    /// <remarks>
    /// Carries the existing vocabularies rather than replacing them: a refused skill still
    /// reports its <see cref="SkillUseRejection"/> and a refused attack its
    /// <see cref="AttackRejection"/>. Only the reasons the <em>runner itself</em> can
    /// produce are named here, which is why the list is short.
    /// </remarks>
    public enum CombatActionRejection
    {
        None = 0,

        /// <summary>No actor was supplied.</summary>
        NoActor = 1,

        /// <summary>The actor is not alive.</summary>
        ActorDead = 2,

        /// <summary>The actor is already casting or recovering.</summary>
        AlreadyBusy = 3,

        /// <summary>The underlying skill rules refused it. See <see cref="CombatActionResult.SkillReason"/>.</summary>
        SkillRejected = 4,

        /// <summary>The underlying attack rules refused it. See <see cref="CombatActionResult.AttackReason"/>.</summary>
        AttackRejected = 5
    }

    /// <summary>The answer to an action request.</summary>
    public readonly struct CombatActionResult
    {
        private CombatActionResult(bool accepted, CombatActionRejection reason,
            SkillUseRejection skillReason, AttackRejection attackReason, CombatAction action)
        {
            IsAccepted = accepted;
            Reason = reason;
            SkillReason = skillReason;
            AttackReason = attackReason;
            Action = action;
        }

        /// <summary>True when the action started. It may still fail later at execution.</summary>
        public bool IsAccepted { get; }

        public CombatActionRejection Reason { get; }

        /// <summary>The skill rules' own reason, when they were the refuser.</summary>
        public SkillUseRejection SkillReason { get; }

        /// <summary>The attack rules' own reason, when they were the refuser.</summary>
        public AttackRejection AttackReason { get; }

        /// <summary>The started action, or null when rejected.</summary>
        public CombatAction Action { get; }

        public static CombatActionResult Accepted(CombatAction action)
        {
            return new CombatActionResult(true, CombatActionRejection.None,
                SkillUseRejection.None, AttackRejection.None, action);
        }

        public static CombatActionResult Rejected(CombatActionRejection reason)
        {
            return new CombatActionResult(false, reason,
                SkillUseRejection.None, AttackRejection.None, null);
        }

        public static CombatActionResult RejectedBySkill(SkillUseRejection reason)
        {
            return new CombatActionResult(false, CombatActionRejection.SkillRejected,
                reason, AttackRejection.None, null);
        }

        public static CombatActionResult RejectedByAttack(AttackRejection reason)
        {
            return new CombatActionResult(false, CombatActionRejection.AttackRejected,
                SkillUseRejection.None, reason, null);
        }

        public override string ToString()
        {
            if (IsAccepted) return "accepted: " + Action;

            switch (Reason)
            {
                case CombatActionRejection.SkillRejected: return "rejected: " + SkillReason;
                case CombatActionRejection.AttackRejected: return "rejected: " + AttackReason;
                default: return "rejected: " + Reason;
            }
        }
    }

    /// <summary>
    /// Owns the one action a combatant is performing.
    /// </summary>
    /// <remarks>
    /// <b>It decides nothing about combat.</b> Targets are judged by
    /// <see cref="TargetEvaluator"/> through the existing validators, damage by
    /// <see cref="BasicDamageFormula"/>, skills by <see cref="SkillUseValidator"/> and
    /// <see cref="SkillExecutor"/>, health and mana by
    /// <see cref="CharacterResourceState"/>, cooldowns by
    /// <see cref="SkillCooldownState"/>. This type contributes timing and sequencing, and
    /// nothing else. Every number it uses arrives from a definition.
    ///
    /// <b>One lifecycle for both entry points.</b> A basic attack and a skill are the same
    /// <see cref="CombatAction"/> with different execution, so there is no separate attack
    /// state machine and no separate skill state machine in the production runtime.
    ///
    /// <b>One active action per combatant.</b> A second request while busy is rejected
    /// rather than queued; queueing is a design decision with visible consequences and
    /// nothing has asked for it.
    ///
    /// <b>The target is judged twice.</b> Once when the action is requested, and again in
    /// the instant before the effect lands, because a cast gives the world time to change:
    /// the target can die, walk out of range or change side. The second check uses the same
    /// validator as the first, so there is no way for the two to disagree.
    ///
    /// <b>Time comes from the caller.</b> <see cref="Advance"/> takes a delta and never
    /// reads <c>UnityEngine.Time</c>, so this assembly stays engine-free and the same
    /// deltas always produce the same phases. A delta longer than the whole action still
    /// executes the effect exactly once, because <see cref="CombatAction.HasExecuted"/>
    /// guards it.
    ///
    /// <b>Nothing is charged for an action that does not land.</b> Cost and cooldown are
    /// consequences of execution, which is <see cref="SkillExecutor"/>'s doing, so a
    /// rejected or cancelled cast pays nothing by construction rather than by unwinding.
    /// </remarks>
    public sealed class CombatActionRunner
    {
        /// <summary>See <see cref="AttackStateMachine"/> for why a settle epsilon is needed at all.</summary>
        private const float SettleEpsilon = 1e-5f;

        private readonly ICombatant _actor;

        public CombatActionRunner(ICombatant actor)
        {
            _actor = actor;
        }

        public ICombatant Actor => _actor;

        /// <summary>The action in progress, or the last one that finished. Null before the first.</summary>
        public CombatAction Current { get; private set; }

        public CombatActionPhase Phase =>
            Current == null || Current.IsFinished ? CombatActionPhase.Idle : Current.Phase;

        /// <summary>Whether a new action may be started right now.</summary>
        public bool IsAvailable => _actor.IsAlive() && (Current == null || !Current.IsBusy);

        // ---------------------------------------------------------------- requests

        /// <summary>
        /// Requests a basic attack.
        /// </summary>
        /// <remarks>Validated immediately through the existing attack rules so a hopeless
        /// swing never occupies the actor, then run through the shared lifecycle with the
        /// caller's timing.</remarks>
        public CombatActionResult RequestBasicAttack(ICombatant target,
            in BasicAttackRules rules, float windUpSeconds, float recoverySeconds)
        {
            CombatActionResult guard = CheckAvailability();
            if (!guard.IsAccepted) return guard;

            // Dry run: the executor is the only writer, and this is not it. Validation is
            // reproduced by asking the same evaluator the executor will ask.
            TargetEligibility eligibility =
                TargetEvaluator.Evaluate(_actor, target, rules.PermittedTargets);

            if (!eligibility.IsAllowed)
            {
                return CombatActionResult.RejectedByAttack(
                    AttackResult.FromTarget(eligibility.Reason));
            }

            AttackRejection range = CheckRange(target, rules.RangeSquared);
            if (range != AttackRejection.None) return CombatActionResult.RejectedByAttack(range);

            var action = new CombatAction(CombatActionType.BasicAttack, _actor, target,
                DefinitionId.None, 0, windUpSeconds, recoverySeconds);

            Current = action;
            _pendingAttackRules = rules;

            SettleIfCastComplete();
            return CombatActionResult.Accepted(action);
        }

        /// <summary>
        /// Requests a skill.
        /// </summary>
        /// <remarks>
        /// Cast time, cooldown, cost and range all come from the authored definition, which
        /// is why this method takes no numbers of its own. Validation is the existing
        /// <see cref="SkillUseValidator"/>, so nothing here can accept a skill the rules
        /// would refuse.
        /// </remarks>
        public CombatActionResult RequestSkill(in SkillUseRequest request,
            in SkillUseContext context, in SkillExecutionRules rules, float recoverySeconds)
        {
            CombatActionResult guard = CheckAvailability();
            if (!guard.IsAccepted) return guard;

            SkillUseEligibility eligibility = SkillUseValidator.Evaluate(request, context);

            if (!eligibility.IsAllowed)
            {
                return CombatActionResult.RejectedBySkill(eligibility.Reason);
            }

            // Authored cast time. Per-level when the level table states one, otherwise the
            // skill's own value; nothing here invents a duration.
            float castTime = eligibility.Skill.CastTimeSeconds;

            var action = new CombatAction(CombatActionType.Skill, _actor,
                eligibility.ResolvedTarget, request.Skill, eligibility.Rank,
                castTime, recoverySeconds);

            Current = action;
            _pendingRequest = request;
            _pendingContext = context;
            _pendingSkillRules = rules;

            SettleIfCastComplete();
            return CombatActionResult.Accepted(action);
        }

        // ---------------------------------------------------------------- ticking

        /// <summary>
        /// Moves the current action forward.
        /// </summary>
        /// <remarks>Ignores non-finite and non-positive deltas, matching
        /// <see cref="AttackStateMachine"/> and <see cref="SkillCooldownState"/>: a corrupt
        /// frame time must not wind an action backwards or freeze it at NaN.</remarks>
        public void Advance(float deltaSeconds)
        {
            if (Current == null || !Current.IsBusy) return;

            if (float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds) || deltaSeconds <= 0f)
            {
                return;
            }

            // Death mid-cast stops the action before its effect can land.
            if (!_actor.IsAlive())
            {
                Cancel(CombatActionCancelReason.ActorDied);
                return;
            }

            Current.Elapsed += deltaSeconds;
            SettleIfCastComplete();
        }

        /// <summary>Advances the phases whose time has run out, executing at most once.</summary>
        private void SettleIfCastComplete()
        {
            CombatAction action = Current;
            if (action == null || !action.IsBusy) return;

            if (action.Phase == CombatActionPhase.Casting)
            {
                if (action.Elapsed + SettleEpsilon < action.CastTimeSeconds) return;

                float carry = action.Elapsed - action.CastTimeSeconds;

                if (!Execute(action)) return;   // cancelled by revalidation

                action.Phase = CombatActionPhase.Recovery;
                action.Elapsed = carry < 0f ? 0f : carry;
            }

            if (action.Phase == CombatActionPhase.Recovery
                && action.Elapsed + SettleEpsilon >= action.RecoverySeconds)
            {
                action.Phase = CombatActionPhase.Completed;
                action.Elapsed = 0f;
            }
        }

        /// <summary>
        /// Applies the effect, exactly once.
        /// </summary>
        /// <returns>False when revalidation refused it and the action was cancelled.</returns>
        private bool Execute(CombatAction action)
        {
            if (action.HasExecuted) return true;

            if (action.Type == CombatActionType.Skill)
            {
                // Revalidate: a cast gives the target time to die, move or change side.
                // The same validator as the request, so the two cannot disagree.
                SkillUseEligibility recheck =
                    SkillUseValidator.Evaluate(_pendingRequest, _pendingContext);

                if (!recheck.IsAllowed)
                {
                    Cancel(CombatActionCancelReason.TargetInvalidated);
                    return false;
                }

                action.HasExecuted = true;
                action.SkillResult = SkillExecutor.Execute(
                    _pendingRequest, _pendingContext, _pendingSkillRules);
                return true;
            }

            TargetEligibility recheckTarget = TargetEvaluator.Evaluate(
                _actor, action.Target, _pendingAttackRules.PermittedTargets);

            if (!recheckTarget.IsAllowed
                || CheckRange(action.Target, _pendingAttackRules.RangeSquared) != AttackRejection.None)
            {
                Cancel(CombatActionCancelReason.TargetInvalidated);
                return false;
            }

            action.HasExecuted = true;
            action.AttackResult = BasicAttackExecutor.Execute(
                new AttackIntent(_actor, action.Target), _pendingAttackRules);
            return true;
        }

        // ---------------------------------------------------------------- control

        /// <summary>
        /// Stops the current action.
        /// </summary>
        /// <remarks>
        /// Policy: cancelling before the effect prevents it entirely -- no damage, no heal,
        /// no cost, no cooldown -- because none of those has happened yet. Cancelling after
        /// the effect stops the recovery only; already-applied effects are never rolled
        /// back, and no rollback machinery exists to do it with.
        /// </remarks>
        public bool Cancel(CombatActionCancelReason reason = CombatActionCancelReason.Explicit)
        {
            if (Current == null || !Current.IsBusy) return false;

            Current.Phase = CombatActionPhase.Cancelled;
            Current.CancelReason = reason;
            Current.Elapsed = 0f;
            return true;
        }

        /// <summary>
        /// Clears the runner to a known idle state.
        /// </summary>
        /// <remarks>Runtime only. It touches no identity, no learned skill, no authored
        /// stat and no persistent state; see <see cref="CombatRuntimeReset"/> for the
        /// wider reset and what it deliberately leaves alone.</remarks>
        public void Reset()
        {
            if (Current != null && Current.IsBusy) Cancel(CombatActionCancelReason.Reset);
            Current = null;
        }

        // ---------------------------------------------------------------- helpers

        private CombatActionResult CheckAvailability()
        {
            if (_actor == null) return CombatActionResult.Rejected(CombatActionRejection.NoActor);
            if (!_actor.IsAlive()) return CombatActionResult.Rejected(CombatActionRejection.ActorDead);

            if (Current != null && Current.IsBusy)
            {
                return CombatActionResult.Rejected(CombatActionRejection.AlreadyBusy);
            }

            return CombatActionResult.Accepted(null);
        }

        private AttackRejection CheckRange(ICombatant target, float rangeSquared)
        {
            CombatPosition from = _actor.Position;
            CombatPosition to = target.Position;

            if (!from.IsFinite || !to.IsFinite) return AttackRejection.InvalidPosition;

            return from.SqrDistanceTo(to) > rangeSquared
                ? AttackRejection.OutOfRange
                : AttackRejection.None;
        }

        private BasicAttackRules _pendingAttackRules;
        private SkillUseRequest _pendingRequest;
        private SkillUseContext _pendingContext;
        private SkillExecutionRules _pendingSkillRules;
    }
}
