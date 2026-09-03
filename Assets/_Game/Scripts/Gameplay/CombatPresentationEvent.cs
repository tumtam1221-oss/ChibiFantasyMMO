using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>What a presentation event is announcing.</summary>
    /// <remarks>
    /// Facts in the past tense, every one of them. Nothing here is an instruction: there is
    /// no PlayAttackAnimation and no DealDamage, because an event that told presentation
    /// what to do would eventually let presentation decide what happened.
    ///
    /// <see cref="Hit"/> is separate from <see cref="Executed"/> because an action can
    /// resolve without landing on anybody -- a rejected revalidation, a skill whose only
    /// effect was unsupported -- and a hit reaction must fire for the second case only.
    /// </remarks>
    public enum CombatPresentationEventKind
    {
        None = 0,

        /// <summary>An action was accepted and began.</summary>
        Started = 1,

        /// <summary>Its effect has been applied. The gameplay result is already final.</summary>
        Executed = 2,

        /// <summary>Somebody took damage or healing from it.</summary>
        Hit = 3,

        /// <summary>A combatant went from alive to not alive.</summary>
        Death = 4,

        /// <summary>The action finished normally.</summary>
        Completed = 5,

        /// <summary>The action stopped before finishing.</summary>
        Cancelled = 6,

        /// <summary>The request never started.</summary>
        Rejected = 7
    }

    /// <summary>
    /// Something combat did, described for presentation.
    /// </summary>
    /// <remarks>
    /// <b>A report, never a command.</b> Every field is a copy of an outcome that has
    /// already happened; acting on one cannot change a fight, and ignoring one cannot
    /// either. That is the whole boundary this phase exists to draw: gameplay is
    /// authoritative and this is what it says afterwards.
    ///
    /// <b>Identities, not combatants.</b> Holding a live <see cref="ICombatant"/> would let
    /// a presenter reach through an event and mutate the thing it is meant to be drawing,
    /// and would keep entities alive past their death. Ids are enough to find a view.
    ///
    /// <b>Immutable, and in the engine-free assembly.</b> No Animator, no ParticleSystem,
    /// no AudioClip and no prefab appears here, so the combat rules can publish without
    /// depending on Unity and a headless server could publish the same events to nobody.
    ///
    /// <b>Built, never raised, here.</b> The factories assemble an event from a resolved
    /// <see cref="CombatAction"/>; when to publish is the client bridge's decision. That
    /// keeps <see cref="CombatActionRunner"/> exactly as PHASE 07.4 left it.
    /// </remarks>
    public readonly struct CombatPresentationEvent
    {
        private CombatPresentationEvent(CombatPresentationEventKind kind, CombatActionType actionType,
            InstanceId actorId, InstanceId targetId, DefinitionId skill, int rank,
            DamageType damageType, int amount, int targetHealthBefore, int targetHealthAfter,
            bool targetDied, CombatActionCancelReason cancelReason,
            CombatActionRejection rejection, SkillUseRejection skillRejection,
            AttackRejection attackRejection)
        {
            Kind = kind;
            ActionType = actionType;
            ActorId = actorId;
            TargetId = targetId;
            Skill = skill;
            Rank = rank;
            DamageType = damageType;
            Amount = amount;
            TargetHealthBefore = targetHealthBefore;
            TargetHealthAfter = targetHealthAfter;
            TargetDied = targetDied;
            CancelReason = cancelReason;
            Rejection = rejection;
            SkillRejection = skillRejection;
            AttackRejection = attackRejection;
        }

        public CombatPresentationEventKind Kind { get; }

        public CombatActionType ActionType { get; }

        public InstanceId ActorId { get; }

        /// <summary>Who it landed on. May be absent for a rejected request.</summary>
        public InstanceId TargetId { get; }

        /// <summary>The skill, or <see cref="DefinitionId.None"/> for a basic attack.</summary>
        public DefinitionId Skill { get; }

        public int Rank { get; }

        /// <summary>
        /// Which defence answered it.
        /// </summary>
        /// <remarks>The existing PHASE 07.4 <see cref="Data.DamageType"/>, reused so a
        /// presenter can pick a physical or a magical impact without a second enum and
        /// without recomputing anything.</remarks>
        public DamageType DamageType { get; }

        /// <summary>Damage or healing, as gameplay computed it. Never recomputed here.</summary>
        public int Amount { get; }

        public int TargetHealthBefore { get; }

        public int TargetHealthAfter { get; }

        /// <summary>Whether this took the target from alive to not alive.</summary>
        public bool TargetDied { get; }

        public CombatActionCancelReason CancelReason { get; }

        public CombatActionRejection Rejection { get; }

        /// <summary>The skill rules' own refusal, when they were the refuser.</summary>
        public SkillUseRejection SkillRejection { get; }

        /// <summary>The attack rules' own refusal, when they were the refuser.</summary>
        public AttackRejection AttackRejection { get; }

        /// <summary>How much health actually moved. Negative for damage, positive for a heal.</summary>
        public int HealthChange => TargetHealthAfter - TargetHealthBefore;

        /// <summary>Whether the amount healed rather than hurt.</summary>
        public bool IsHeal => HealthChange > 0;

        // ------------------------------------------------------------- factories

        public static CombatPresentationEvent Started(CombatAction action)
        {
            return new CombatPresentationEvent(CombatPresentationEventKind.Started,
                action.Type, Id(action.Actor), Id(action.Target), action.Skill, action.Rank,
                Data.DamageType.None, 0, 0, 0, false,
                CombatActionCancelReason.None, CombatActionRejection.None,
                SkillUseRejection.None, Gameplay.AttackRejection.None);
        }

        public static CombatPresentationEvent Completed(CombatAction action)
        {
            return new CombatPresentationEvent(CombatPresentationEventKind.Completed,
                action.Type, Id(action.Actor), Id(action.Target), action.Skill, action.Rank,
                Data.DamageType.None, 0, 0, 0, false,
                CombatActionCancelReason.None, CombatActionRejection.None,
                SkillUseRejection.None, Gameplay.AttackRejection.None);
        }

        public static CombatPresentationEvent Cancelled(CombatAction action)
        {
            return new CombatPresentationEvent(CombatPresentationEventKind.Cancelled,
                action.Type, Id(action.Actor), Id(action.Target), action.Skill, action.Rank,
                Data.DamageType.None, 0, 0, 0, false,
                action.CancelReason, CombatActionRejection.None,
                SkillUseRejection.None, Gameplay.AttackRejection.None);
        }

        public static CombatPresentationEvent Rejected(CombatActionType actionType,
            InstanceId actorId, InstanceId targetId, DefinitionId skill,
            in CombatActionResult result)
        {
            return new CombatPresentationEvent(CombatPresentationEventKind.Rejected,
                actionType, actorId, targetId, skill, 0,
                Data.DamageType.None, 0, 0, 0, false,
                CombatActionCancelReason.None, result.Reason,
                result.SkillReason, result.AttackReason);
        }

        /// <summary>The action's effect landed. Amounts are copied, never recomputed.</summary>
        public static CombatPresentationEvent Executed(CombatAction action)
        {
            int amount;
            int before;
            int after;
            bool died;
            DamageType damageType = Data.DamageType.None;

            if (action.Type == CombatActionType.BasicAttack)
            {
                AttackResult r = action.AttackResult;
                amount = r.Damage;
                before = r.TargetHealthBefore;
                after = r.TargetHealthAfter;
                died = r.TargetDied;
                damageType = Data.DamageType.Physical;
            }
            else
            {
                SkillExecutionResult r = action.SkillResult;
                before = r.TargetHealthBefore;
                after = r.TargetHealthAfter;
                died = r.TargetDied;
                amount = 0;

                // The first effect that actually moved a pool is the one worth showing.
                // Nothing is summed or recalculated; this only chooses which figure to
                // hand a presenter that can draw one number.
                for (int i = 0; i < r.Effects.Count; i++)
                {
                    SkillEffectOutcome outcome = r.Effects[i];

                    if (outcome.Status != SkillEffectStatus.Applied) continue;

                    amount = outcome.Amount;
                    damageType = outcome.DamageType;   // as authored, never inferred
                    break;
                }
            }

            return new CombatPresentationEvent(CombatPresentationEventKind.Executed,
                action.Type, Id(action.Actor), Id(action.Target), action.Skill, action.Rank,
                damageType, amount, before, after, died,
                CombatActionCancelReason.None, CombatActionRejection.None,
                SkillUseRejection.None, Gameplay.AttackRejection.None);
        }

        /// <summary>Somebody was struck. Derived from an executed event, with the type stated.</summary>
        public static CombatPresentationEvent Hit(in CombatPresentationEvent executed,
            DamageType damageType)
        {
            return new CombatPresentationEvent(CombatPresentationEventKind.Hit,
                executed.ActionType, executed.ActorId, executed.TargetId,
                executed.Skill, executed.Rank, damageType, executed.Amount,
                executed.TargetHealthBefore, executed.TargetHealthAfter, executed.TargetDied,
                CombatActionCancelReason.None, CombatActionRejection.None,
                SkillUseRejection.None, Gameplay.AttackRejection.None);
        }

        /// <summary>A combatant died. Published because gameplay says so, never an animation.</summary>
        public static CombatPresentationEvent Death(in CombatPresentationEvent executed)
        {
            return new CombatPresentationEvent(CombatPresentationEventKind.Death,
                executed.ActionType, executed.ActorId, executed.TargetId,
                executed.Skill, executed.Rank, executed.DamageType, executed.Amount,
                executed.TargetHealthBefore, executed.TargetHealthAfter, true,
                CombatActionCancelReason.None, CombatActionRejection.None,
                SkillUseRejection.None, Gameplay.AttackRejection.None);
        }

        private static InstanceId Id(ICombatant combatant)
        {
            return combatant == null ? default : combatant.CombatantId;
        }

        public override string ToString()
        {
            switch (Kind)
            {
                case CombatPresentationEventKind.Rejected:
                    return "Rejected " + ActionType + ": " + Rejection;
                case CombatPresentationEventKind.Cancelled:
                    return "Cancelled " + ActionType + ": " + CancelReason;
                case CombatPresentationEventKind.Hit:
                case CombatPresentationEventKind.Executed:
                    return Kind + " " + ActionType + " " + DamageType + " amount=" + Amount
                        + " (" + TargetHealthBefore + " -> " + TargetHealthAfter + ")";
                default:
                    return Kind + " " + ActionType;
            }
        }
    }
}
