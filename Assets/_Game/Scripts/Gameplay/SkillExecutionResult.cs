using System.Collections.Generic;
using ChibiFantasy.Core;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// What happened when a skill was used.
    /// </summary>
    /// <remarks>
    /// <b>Shaped after <see cref="AttackResult"/>.</b> Rejected and executed are
    /// distinguished the same way, identities are carried instead of live combatants for
    /// the same reason, and the target's health before and after is recorded so a killing
    /// blow reads correctly. A reader who knows the basic-attack result can read this one.
    ///
    /// <b>Per-effect outcomes are kept.</b> A skill is a list of effects and any one of
    /// them may be unsupported while the others apply, so a single success flag would be a
    /// lie. <see cref="Effects"/> holds one outcome per authored effect, in authored order.
    ///
    /// <b><see cref="IsExecuted"/> is not "everything worked".</b> It means the skill
    /// passed validation and its effects were run. Use
    /// <see cref="HasUnsupportedEffect"/> to ask whether any of them could not be
    /// performed.
    ///
    /// No UI, no VFX, no packet, no log sink.
    /// </remarks>
    public readonly struct SkillExecutionResult
    {
        private static readonly SkillEffectOutcome[] NoEffects = new SkillEffectOutcome[0];

        private readonly SkillEffectOutcome[] _effects;

        private SkillExecutionResult(bool executed, SkillUseRejection reason, DefinitionId skill,
            int rank, InstanceId casterId, InstanceId targetId, int resourceSpent,
            int targetHealthBefore, int targetHealthAfter, bool targetDied,
            SkillEffectOutcome[] effects)
        {
            IsExecuted = executed;
            Reason = reason;
            Skill = skill;
            Rank = rank;
            CasterId = casterId;
            TargetId = targetId;
            ResourceSpent = resourceSpent;
            TargetHealthBefore = targetHealthBefore;
            TargetHealthAfter = targetHealthAfter;
            TargetDied = targetDied;
            _effects = effects ?? NoEffects;
        }

        /// <summary>True when validation passed and the effects were run.</summary>
        public bool IsExecuted { get; }

        /// <summary><see cref="SkillUseRejection.None"/> when executed.</summary>
        public SkillUseRejection Reason { get; }

        public DefinitionId Skill { get; }

        public int Rank { get; }

        public InstanceId CasterId { get; }

        /// <summary>The resolved target, which is the caster for a self skill.</summary>
        public InstanceId TargetId { get; }

        /// <summary>Whole units of the skill's resource actually deducted.</summary>
        public int ResourceSpent { get; }

        public int TargetHealthBefore { get; }

        public int TargetHealthAfter { get; }

        /// <summary>Whether this use took the target from alive to not alive.</summary>
        public bool TargetDied { get; }

        /// <summary>One outcome per authored effect, in authored order.</summary>
        public IReadOnlyList<SkillEffectOutcome> Effects => _effects;

        /// <summary>Net health change on the target. Negative for damage, positive for a heal.</summary>
        public int TargetHealthChange => TargetHealthAfter - TargetHealthBefore;

        /// <summary>Whether any effect could not be performed by this runtime.</summary>
        public bool HasUnsupportedEffect
        {
            get
            {
                for (int i = 0; i < _effects.Length; i++)
                {
                    if (_effects[i].Status == SkillEffectStatus.Unsupported) return true;
                }

                return false;
            }
        }

        public static SkillExecutionResult Executed(DefinitionId skill, int rank,
            InstanceId casterId, InstanceId targetId, int resourceSpent,
            int healthBefore, int healthAfter, SkillEffectOutcome[] effects)
        {
            return new SkillExecutionResult(true, SkillUseRejection.None, skill, rank,
                casterId, targetId, resourceSpent, healthBefore, healthAfter,
                healthBefore > 0 && healthAfter <= 0, effects);
        }

        public static SkillExecutionResult Rejected(SkillUseRejection reason, DefinitionId skill,
            InstanceId casterId, InstanceId targetId)
        {
            return new SkillExecutionResult(false, reason, skill, 0, casterId, targetId,
                0, 0, 0, false, NoEffects);
        }

        public override string ToString()
        {
            if (!IsExecuted) return "rejected: " + Reason;

            return "'" + Skill + "' r" + Rank + " -> " + _effects.Length + " effect(s), health "
                + TargetHealthBefore + " -> " + TargetHealthAfter
                + (TargetDied ? " (died)" : string.Empty);
        }
    }
}
