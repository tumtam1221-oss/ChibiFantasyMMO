using ChibiFantasy.Core;

namespace ChibiFantasy.Gameplay
{
    /// <summary>Why taking a skill to its next rank was refused.</summary>
    /// <remarks>
    /// A sibling of <see cref="SkillLearnRejection"/> rather than an extension of it. The
    /// two overlap but neither contains the other: a skill cannot be ranked up because it
    /// was never learned, which is meaningless when learning, and cannot be learned because
    /// it already is, which is meaningless when ranking up. Merging them would produce one
    /// enum in which half the values are unreachable for either caller, and a caller cannot
    /// tell which half.
    ///
    /// Names follow the vocabulary already established by
    /// <see cref="JobChangeRejection"/> and <see cref="SkillLearnRejection"/>, so
    /// <see cref="UnknownSkill"/> and <see cref="LevelTooLow"/> mean here exactly what they
    /// mean there. Closed technical category: each value is a distinct branch a caller must
    /// handle.
    /// </remarks>
    public enum SkillUpgradeRejection
    {
        /// <summary>Not a rejection.</summary>
        None = 0,

        /// <summary>The skill has no definition.</summary>
        UnknownSkill = 1,

        /// <summary>The character does not know the skill, so there is no rank to raise.</summary>
        NotLearned = 2,

        /// <summary>The skill is already at the highest rank it defines.</summary>
        AlreadyMaxRank = 3,

        /// <summary>
        /// The next rank is within the skill's maximum but authors no entry.
        /// </summary>
        /// <remarks>A content gap rather than a player's problem: the skill claims to reach
        /// a rank its level table does not describe.</remarks>
        NextRankUnavailable = 4,

        /// <summary>The skill belongs to a class the character did not start as.</summary>
        ClassRequirementNotMet = 5,

        /// <summary>The skill belongs to a job the character does not currently hold.</summary>
        JobRequirementNotMet = 6,

        /// <summary>A skill that must be known first is not known at all.</summary>
        PrerequisiteNotLearned = 7,

        /// <summary>A prerequisite is known, but not to the rank it demands.</summary>
        PrerequisiteRankTooLow = 8,

        /// <summary>The character has not reached the level the next rank demands.</summary>
        LevelTooLow = 9
    }

    /// <summary>
    /// The answer to whether one skill may advance one rank.
    /// </summary>
    /// <remarks>
    /// Shaped after <see cref="SkillLearnEligibility"/> and
    /// <see cref="JobChangeEligibility"/>, and carries what a caller needs to show a player
    /// what they are working toward rather than only that they failed.
    ///
    /// <see cref="CurrentRank"/> and <see cref="NextRank"/> are reported whether or not the
    /// advance is allowed, because "rank 3 of 5" is what a caller wants to display either
    /// way, and because <see cref="NextRank"/> is the value a successful upgrade writes --
    /// exactly one above the current one, never further.
    /// </remarks>
    public readonly struct SkillUpgradeEligibility
    {
        private SkillUpgradeEligibility(bool allowed, SkillUpgradeRejection reason,
            int currentRank, int nextRank, int requiredLevel,
            DefinitionId blockingPrerequisite, int requiredPrerequisiteRank)
        {
            IsAllowed = allowed;
            Reason = reason;
            CurrentRank = currentRank;
            NextRank = nextRank;
            RequiredLevel = requiredLevel;
            BlockingPrerequisite = blockingPrerequisite;
            RequiredPrerequisiteRank = requiredPrerequisiteRank;
        }

        public bool IsAllowed { get; }

        /// <summary><see cref="SkillUpgradeRejection.None"/> when allowed.</summary>
        public SkillUpgradeRejection Reason { get; }

        /// <summary>Rank the character holds. Zero when the skill is unknown or unresolved.</summary>
        public int CurrentRank { get; }

        /// <summary>
        /// Rank the advance targets, always one above <see cref="CurrentRank"/>.
        /// </summary>
        /// <remarks>Zero when there is no next rank to reach, which is the case for an
        /// unknown or unlearned skill and for one already at its maximum.</remarks>
        public int NextRank { get; }

        /// <summary>
        /// Character level the next rank demands.
        /// </summary>
        /// <remarks>Read from that rank's own <see cref="Data.SkillLevelEntry"/>, never
        /// from rank one's. Zero when no next-rank entry could be read.</remarks>
        public int RequiredLevel { get; }

        /// <summary>
        /// The prerequisite that blocked, or <see cref="DefinitionId.None"/>.
        /// </summary>
        /// <remarks>Set only for the two prerequisite rejections. Only the first blocking
        /// prerequisite is named; evaluation stops at the first fault.</remarks>
        public DefinitionId BlockingPrerequisite { get; }

        /// <summary>Rank <see cref="BlockingPrerequisite"/> demands. Zero when none blocked.</summary>
        public int RequiredPrerequisiteRank { get; }

        public static SkillUpgradeEligibility Allowed(int currentRank, int nextRank,
            int requiredLevel)
        {
            return new SkillUpgradeEligibility(true, SkillUpgradeRejection.None,
                currentRank, nextRank, requiredLevel, DefinitionId.None, 0);
        }

        public static SkillUpgradeEligibility Rejected(SkillUpgradeRejection reason,
            int currentRank, int nextRank, int requiredLevel)
        {
            return new SkillUpgradeEligibility(false, reason,
                currentRank, nextRank, requiredLevel, DefinitionId.None, 0);
        }

        /// <summary>A refusal caused by a specific prerequisite.</summary>
        public static SkillUpgradeEligibility RejectedByPrerequisite(SkillUpgradeRejection reason,
            int currentRank, int nextRank, int requiredLevel,
            DefinitionId prerequisite, int requiredRank)
        {
            return new SkillUpgradeEligibility(false, reason,
                currentRank, nextRank, requiredLevel, prerequisite, requiredRank);
        }

        public override string ToString()
        {
            if (IsAllowed)
            {
                return "allowed " + CurrentRank + " to " + NextRank
                    + " (requires level " + RequiredLevel + ")";
            }

            return BlockingPrerequisite.IsValid
                ? Reason + " (requires '" + BlockingPrerequisite + "' at rank "
                  + RequiredPrerequisiteRank + ")"
                : Reason + " at rank " + CurrentRank + " (requires level " + RequiredLevel + ")";
        }
    }
}
