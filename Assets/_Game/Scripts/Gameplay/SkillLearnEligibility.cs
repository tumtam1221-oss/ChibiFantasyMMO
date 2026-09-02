using ChibiFantasy.Core;

namespace ChibiFantasy.Gameplay
{
    /// <summary>Why learning a skill was refused.</summary>
    /// <remarks>
    /// A reason rather than a bare false, for the same purpose
    /// <see cref="JobChangeRejection"/> serves: each value needs a different message to a
    /// player and a different response from a server. Closed technical category, each value
    /// a distinct branch a caller must handle.
    ///
    /// Named to sit alongside the job-change vocabulary rather than beside it --
    /// <see cref="UnknownSkill"/> mirrors UnknownJob, <see cref="LevelTooLow"/> is the same
    /// gate under the same name -- so a reader who knows one enum can read the other.
    /// </remarks>
    public enum SkillLearnRejection
    {
        /// <summary>Not a rejection.</summary>
        None = 0,

        /// <summary>The skill has no definition.</summary>
        UnknownSkill = 1,

        /// <summary>The character already knows the skill.</summary>
        AlreadyLearned = 2,

        /// <summary>The skill belongs to a class the character did not start as.</summary>
        ClassRequirementNotMet = 3,

        /// <summary>The skill belongs to a job the character does not currently hold.</summary>
        JobRequirementNotMet = 4,

        /// <summary>A skill that must be known first is not known at all.</summary>
        PrerequisiteNotLearned = 5,

        /// <summary>A prerequisite is known, but not to the rank it demands.</summary>
        PrerequisiteRankTooLow = 6,

        /// <summary>The character has not reached the level the skill demands.</summary>
        LevelTooLow = 7
    }

    /// <summary>
    /// The answer to whether one skill may be learned.
    /// </summary>
    /// <remarks>
    /// Shaped after <see cref="JobChangeEligibility"/>, and carries what a caller needs to
    /// tell a player what they are working toward rather than only that they failed. Where
    /// a job change has one thing to say -- the level -- a skill has two, because a
    /// prerequisite failure is about a different skill entirely, and "you need something
    /// else first" is useless without naming it.
    ///
    /// Every field is populated whether or not it was the thing that failed, so a caller
    /// can show a requirement list rather than one line at a time.
    /// </remarks>
    public readonly struct SkillLearnEligibility
    {
        private SkillLearnEligibility(bool allowed, SkillLearnRejection reason, int requiredLevel,
            DefinitionId blockingPrerequisite, int requiredPrerequisiteRank)
        {
            IsAllowed = allowed;
            Reason = reason;
            RequiredLevel = requiredLevel;
            BlockingPrerequisite = blockingPrerequisite;
            RequiredPrerequisiteRank = requiredPrerequisiteRank;
        }

        public bool IsAllowed { get; }

        /// <summary><see cref="SkillLearnRejection.None"/> when allowed.</summary>
        public SkillLearnRejection Reason { get; }

        /// <summary>
        /// Character level the skill demands to be learned at rank one.
        /// </summary>
        /// <remarks>Zero when the skill could not be resolved, and zero when the skill
        /// authors no level table, which is a skill with no level gate rather than a skill
        /// gated at zero.</remarks>
        public int RequiredLevel { get; }

        /// <summary>
        /// The prerequisite that blocked, or <see cref="DefinitionId.None"/>.
        /// </summary>
        /// <remarks>Set only for <see cref="SkillLearnRejection.PrerequisiteNotLearned"/>
        /// and <see cref="SkillLearnRejection.PrerequisiteRankTooLow"/>. Only the first
        /// blocking prerequisite is named; see <see cref="SkillLearningEvaluator"/> for why
        /// evaluation stops at the first fault.</remarks>
        public DefinitionId BlockingPrerequisite { get; }

        /// <summary>Rank <see cref="BlockingPrerequisite"/> demands. Zero when none blocked.</summary>
        public int RequiredPrerequisiteRank { get; }

        public static SkillLearnEligibility Allowed(int requiredLevel)
        {
            return new SkillLearnEligibility(
                true, SkillLearnRejection.None, requiredLevel, DefinitionId.None, 0);
        }

        public static SkillLearnEligibility Rejected(SkillLearnRejection reason, int requiredLevel)
        {
            return new SkillLearnEligibility(
                false, reason, requiredLevel, DefinitionId.None, 0);
        }

        /// <summary>A refusal caused by a specific prerequisite.</summary>
        public static SkillLearnEligibility RejectedByPrerequisite(SkillLearnRejection reason,
            int requiredLevel, DefinitionId prerequisite, int requiredRank)
        {
            return new SkillLearnEligibility(false, reason, requiredLevel, prerequisite, requiredRank);
        }

        public override string ToString()
        {
            if (IsAllowed)
            {
                return "allowed (requires level " + RequiredLevel + ")";
            }

            return BlockingPrerequisite.IsValid
                ? Reason + " (requires '" + BlockingPrerequisite + "' at rank "
                  + RequiredPrerequisiteRank + ")"
                : Reason + " (requires level " + RequiredLevel + ")";
        }
    }
}
