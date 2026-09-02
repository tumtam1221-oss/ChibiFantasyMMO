using ChibiFantasy.Core;

namespace ChibiFantasy.Gameplay
{
    /// <summary>Where a character stands with one skill.</summary>
    /// <remarks>
    /// Six states rather than a bool, because "unavailable" covers two genuinely different
    /// situations -- a skill the character cannot yet learn, and one they hold but cannot
    /// yet advance -- and a caller listing a skill tree has to draw them differently.
    ///
    /// Closed technical category: each value is a distinct branch a caller must handle.
    /// </remarks>
    public enum SkillAvailabilityStatus
    {
        /// <summary>The skill has no definition.</summary>
        Unknown = 0,

        /// <summary>Not known, and every requirement passes.</summary>
        Learnable = 1,

        /// <summary>Not known, and some requirement blocks it.</summary>
        Blocked = 2,

        /// <summary>Known, with a next rank whose requirements all pass.</summary>
        Upgradeable = 3,

        /// <summary>Known, with a next rank that some requirement blocks.</summary>
        UpgradeBlocked = 4,

        /// <summary>Known, and already at the highest rank the skill defines.</summary>
        MaxRank = 5
    }

    /// <summary>
    /// Where a character stands with one skill, and why.
    /// </summary>
    /// <remarks>
    /// <b>It restates nothing.</b> The reason a skill is unavailable already has two
    /// precise vocabularies -- <see cref="SkillLearnEligibility"/> from 06.6 and
    /// <see cref="SkillUpgradeEligibility"/> from 06.7 -- so this carries both rather than
    /// flattening them into a third set of fields that would immediately be a second truth.
    /// <see cref="Status"/> is derived from them, never decided independently.
    ///
    /// <b>Both are always populated.</b> Whichever one does not apply still answers
    /// usefully: an unlearned skill's <see cref="Upgrade"/> reports
    /// <see cref="SkillUpgradeRejection.NotLearned"/>, and a known skill's
    /// <see cref="Learn"/> reports <see cref="SkillLearnRejection.AlreadyLearned"/>. Nothing
    /// is left at a default a caller could misread.
    /// </remarks>
    public readonly struct SkillAvailability
    {
        public SkillAvailability(DefinitionId skill, SkillAvailabilityStatus status,
            int currentRank, SkillLearnEligibility learn, SkillUpgradeEligibility upgrade)
        {
            Skill = skill;
            Status = status;
            CurrentRank = currentRank;
            Learn = learn;
            Upgrade = upgrade;
        }

        /// <summary>The skill this describes.</summary>
        public DefinitionId Skill { get; }

        public SkillAvailabilityStatus Status { get; }

        /// <summary>Rank the character holds, or zero when the skill is not known.</summary>
        public int CurrentRank { get; }

        /// <summary>The 06.6 answer to whether it may be learned.</summary>
        public SkillLearnEligibility Learn { get; }

        /// <summary>The 06.7 answer to whether it may advance a rank.</summary>
        public SkillUpgradeEligibility Upgrade { get; }

        /// <summary>Whether the character knows the skill at all.</summary>
        public bool IsLearned =>
            Status == SkillAvailabilityStatus.Upgradeable
            || Status == SkillAvailabilityStatus.UpgradeBlocked
            || Status == SkillAvailabilityStatus.MaxRank;

        /// <summary>
        /// Whether something can be done with the skill right now.
        /// </summary>
        /// <remarks>True for a skill that can be learned and for one that can be advanced;
        /// false for everything else, including a skill already taken as far as it goes.</remarks>
        public bool IsActionable =>
            Status == SkillAvailabilityStatus.Learnable
            || Status == SkillAvailabilityStatus.Upgradeable;

        public override string ToString()
        {
            switch (Status)
            {
                case SkillAvailabilityStatus.Learnable:
                case SkillAvailabilityStatus.Blocked:
                    return Skill + ": " + Status + " (" + Learn + ")";

                case SkillAvailabilityStatus.Unknown:
                    return Skill + ": " + Status;

                default:
                    return Skill + ": " + Status + " at rank " + CurrentRank
                        + " (" + Upgrade + ")";
            }
        }
    }
}
