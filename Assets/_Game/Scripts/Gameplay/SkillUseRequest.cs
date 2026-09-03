using ChibiFantasy.Core;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// A request that one combatant wants to use a learned skill.
    /// </summary>
    /// <remarks>
    /// <b>An intent, exactly like <see cref="AttackIntent"/>.</b> Constructing one
    /// validates nothing, computes nothing and changes nothing. It is shaped after the
    /// basic-attack request on purpose, so the two entry points into combat read the same
    /// way and a reader who knows one knows the other.
    ///
    /// <b>The skill is named, not carried.</b> <see cref="Skill"/> is a
    /// <see cref="DefinitionId"/>, never a live <c>SkillDefinition</c>. Content is
    /// referenced by id everywhere else in the project, a request that held an object
    /// would pin a ScriptableObject into runtime state, and a request that crossed a
    /// network boundary later could not carry one at all. Resolution is the validator's
    /// job.
    ///
    /// <b>Combatants are carried, because the executor needs them.</b> That mirrors
    /// <see cref="AttackIntent"/>; the identities on the result are what outlive the call.
    ///
    /// <b><see cref="Rank"/> is requested, not assumed.</b> A character who knows a skill
    /// at rank three may deliberately use rank one, and the caller says which. Whether the
    /// rank is one they actually hold is checked by <see cref="SkillUseValidator"/>.
    ///
    /// No damage, no health, no cost, no cooldown result and nothing presentational
    /// appears here. Those are outputs, and putting any of them on the request would let a
    /// caller assert an answer instead of asking a question.
    /// </remarks>
    public readonly struct SkillUseRequest
    {
        public SkillUseRequest(ICombatant caster, DefinitionId skill, ICombatant target, int rank = 1)
        {
            Caster = caster;
            Skill = skill;
            Target = target;
            Rank = rank;
        }

        /// <summary>Who wants to use the skill. May be null; validation reports it.</summary>
        public ICombatant Caster { get; }

        /// <summary>Reference to a <c>SkillDefinition</c>.</summary>
        public DefinitionId Skill { get; }

        /// <summary>
        /// Who it is aimed at.
        /// </summary>
        /// <remarks>May legitimately be null for a skill that needs no separate target;
        /// which those are is decided by <see cref="SkillTargetMapping"/>, not here.</remarks>
        public ICombatant Target { get; }

        /// <summary>Which rank to use. Ranks run from one upward, matching the level table.</summary>
        public int Rank { get; }

        /// <summary>Whether a caster and a skill id are present at all.</summary>
        /// <remarks>A shallow structural check. It says nothing about legality; that is
        /// <see cref="SkillUseValidator"/>'s job.</remarks>
        public bool IsStructurallyComplete => Caster != null && Skill.IsValid;

        public override string ToString()
        {
            string caster = Caster == null ? "<none>" : Caster.CombatantId.ToString();
            string target = Target == null ? "<none>" : Target.CombatantId.ToString();
            return caster + " uses '" + Skill + "' r" + Rank + " on " + target;
        }
    }
}
