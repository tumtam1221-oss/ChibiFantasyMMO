using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>Why using a skill was refused.</summary>
    /// <remarks>
    /// A reason rather than a bare false, matching <see cref="SkillLearnRejection"/>,
    /// <see cref="TargetRejection"/> and <see cref="AttackRejection"/>: each value needs a
    /// different message to a player and a different response from a server.
    ///
    /// Distinct from <see cref="SkillLearnRejection"/> on purpose. That answers "may I
    /// learn this"; this answers "may I use it right now". The two overlap in wording and
    /// not in meaning -- a skill can be perfectly learnable and completely unusable -- so
    /// folding them together would give each evaluator reasons it cannot produce.
    /// </remarks>
    public enum SkillUseRejection
    {
        /// <summary>Not a rejection.</summary>
        None = 0,

        /// <summary>No caster was supplied.</summary>
        NoCaster = 1,

        /// <summary>The request named no skill.</summary>
        NoSkill = 2,

        /// <summary>No definition exists for the named skill.</summary>
        UnknownSkill = 3,

        /// <summary>The caster has not learned the skill.</summary>
        NotLearned = 4,

        /// <summary>The requested rank is above the rank the caster holds, or below one.</summary>
        RankNotAvailable = 5,

        /// <summary>The skill defines no level table entry for the requested rank.</summary>
        RankNotDefined = 6,

        /// <summary>The caster has not reached the character level this rank demands.</summary>
        LevelTooLow = 7,

        /// <summary>The skill's target type has no runtime implementation yet.</summary>
        TargetTypeUnsupported = 8,

        /// <summary>A target was required and none was supplied.</summary>
        NoTarget = 9,

        /// <summary>The caster is not alive.</summary>
        CasterDead = 10,

        /// <summary>The target is not alive.</summary>
        TargetDead = 11,

        /// <summary>At least one side has no team, so no relationship exists.</summary>
        UndefinedRelationship = 12,

        /// <summary>The relationship is real but this skill does not accept it.</summary>
        RelationshipNotPermitted = 13,

        /// <summary>The target is further away than the skill reaches.</summary>
        OutOfRange = 14,

        /// <summary>A position was not a real number, so range could not be judged.</summary>
        InvalidPosition = 15,

        /// <summary>The caster cannot pay the skill's resource cost.</summary>
        InsufficientResource = 16,

        /// <summary>The caster has no pool of the type the skill costs.</summary>
        ResourcePoolUnavailable = 17,

        /// <summary>The skill has not finished cooling down.</summary>
        OnCooldown = 18
    }

    /// <summary>
    /// The answer to whether a skill may be used right now.
    /// </summary>
    /// <remarks>
    /// Shaped after <see cref="SkillLearnEligibility"/> and
    /// <see cref="TargetEligibility"/>. It carries the resolved rank, level entry and
    /// target whether or not they were the thing that failed, so a caller that is allowed
    /// to proceed does not have to look any of them up a second time -- and so the
    /// executor cannot resolve them differently than the validator did.
    /// </remarks>
    public readonly struct SkillUseEligibility
    {
        private SkillUseEligibility(bool allowed, SkillUseRejection reason, SkillDefinition skill,
            SkillLevelEntry level, int rank, ICombatant resolvedTarget,
            CombatRelationship relationship, int resourceCost)
        {
            IsAllowed = allowed;
            Reason = reason;
            Skill = skill;
            Level = level;
            Rank = rank;
            ResolvedTarget = resolvedTarget;
            Relationship = relationship;
            ResourceCost = resourceCost;
        }

        public bool IsAllowed { get; }

        /// <summary><see cref="SkillUseRejection.None"/> when allowed.</summary>
        public SkillUseRejection Reason { get; }

        /// <summary>The resolved definition, or null when it could not be found.</summary>
        public SkillDefinition Skill { get; }

        /// <summary>The level table entry for <see cref="Rank"/>.</summary>
        public SkillLevelEntry Level { get; }

        /// <summary>The rank that was validated.</summary>
        public int Rank { get; }

        /// <summary>
        /// Who the skill actually lands on.
        /// </summary>
        /// <remarks>The caster for a self skill, even if the request named somebody else.
        /// The executor uses this rather than the request's target, which is what stops a
        /// self-only skill being redirected.</remarks>
        public ICombatant ResolvedTarget { get; }

        /// <summary>How caster and target stand to each other, as far as it was resolved.</summary>
        public CombatRelationship Relationship { get; }

        /// <summary>The cost in whole units of the skill's resource, as charged.</summary>
        public int ResourceCost { get; }

        public static SkillUseEligibility Allowed(SkillDefinition skill, SkillLevelEntry level,
            int rank, ICombatant resolvedTarget, CombatRelationship relationship, int resourceCost)
        {
            return new SkillUseEligibility(true, SkillUseRejection.None, skill, level, rank,
                resolvedTarget, relationship, resourceCost);
        }

        public static SkillUseEligibility Rejected(SkillUseRejection reason,
            SkillDefinition skill = null, int rank = 0,
            CombatRelationship relationship = CombatRelationship.None)
        {
            return new SkillUseEligibility(false, reason, skill, default, rank, null,
                relationship, 0);
        }

        /// <summary>Maps a targeting refusal onto the skill vocabulary.</summary>
        /// <remarks>Explicit rather than a cast, so the enums may diverge later without
        /// silently mapping onto each other's numbers.</remarks>
        public static SkillUseRejection FromTarget(TargetRejection reason)
        {
            switch (reason)
            {
                case TargetRejection.NoAttacker: return SkillUseRejection.NoCaster;
                case TargetRejection.NoTarget: return SkillUseRejection.NoTarget;
                case TargetRejection.AttackerDead: return SkillUseRejection.CasterDead;
                case TargetRejection.TargetDead: return SkillUseRejection.TargetDead;
                case TargetRejection.UndefinedRelationship: return SkillUseRejection.UndefinedRelationship;
                case TargetRejection.RelationshipNotPermitted: return SkillUseRejection.RelationshipNotPermitted;
                default: return SkillUseRejection.None;
            }
        }

        public override string ToString()
        {
            return IsAllowed
                ? "allowed (rank " + Rank + ", " + Relationship + ", cost " + ResourceCost + ")"
                : Reason.ToString();
        }
    }
}
