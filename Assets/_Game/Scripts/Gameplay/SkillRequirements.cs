using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>Which skill-wide requirement failed, if any.</summary>
    /// <remarks>
    /// Deliberately neutral about what the caller is trying to do. Learning and ranking up
    /// ask the same questions but answer to different vocabularies
    /// (<see cref="SkillLearnRejection"/> and <see cref="SkillUpgradeRejection"/>), so this
    /// reports the fault and each evaluator names it in its own terms.
    /// </remarks>
    internal enum SkillRequirementFault
    {
        None = 0,
        Class = 1,
        Job = 2,
        PrerequisiteNotLearned = 3,
        PrerequisiteRankTooLow = 4
    }

    /// <summary>A skill-wide requirement failure, and which prerequisite caused it.</summary>
    internal readonly struct SkillRequirementOutcome
    {
        private SkillRequirementOutcome(SkillRequirementFault fault, DefinitionId prerequisite,
            int requiredRank)
        {
            Fault = fault;
            Prerequisite = prerequisite;
            RequiredRank = requiredRank;
        }

        public SkillRequirementFault Fault { get; }

        /// <summary>Set only for the two prerequisite faults.</summary>
        public DefinitionId Prerequisite { get; }

        /// <summary>Rank <see cref="Prerequisite"/> demands. Zero when none blocked.</summary>
        public int RequiredRank { get; }

        public bool IsSatisfied => Fault == SkillRequirementFault.None;

        public static SkillRequirementOutcome Satisfied()
        {
            return new SkillRequirementOutcome(SkillRequirementFault.None, DefinitionId.None, 0);
        }

        public static SkillRequirementOutcome Failed(SkillRequirementFault fault)
        {
            return new SkillRequirementOutcome(fault, DefinitionId.None, 0);
        }

        public static SkillRequirementOutcome FailedOn(SkillRequirementFault fault,
            DefinitionId prerequisite, int requiredRank)
        {
            return new SkillRequirementOutcome(fault, prerequisite, requiredRank);
        }
    }

    /// <summary>
    /// The requirements a skill states about its holder, independent of rank.
    /// </summary>
    /// <remarks>
    /// <b>Why this is shared rather than written twice.</b> Learning a skill and taking it
    /// to the next rank ask exactly the same three questions -- is this the right class, the
    /// right job, and are the prerequisite skills held to the ranks they demand -- because
    /// the schema states all three on <see cref="SkillDefinition"/> itself rather than per
    /// rank. Two copies of that loop would be two places for the rules to drift, and a
    /// character who could rank up a skill they could no longer learn is exactly the sort of
    /// inconsistency that costs a day to find.
    ///
    /// <b>Rank-specific requirements are not here.</b> The one requirement the schema does
    /// author per rank is <see cref="SkillLevelEntry.RequiredCharacterLevel"/>, and it is
    /// deliberately left to each evaluator, which reads it from the entry it cares about:
    /// rank one for learning, the next rank for ranking up. Folding it in here would have
    /// forced one rank's gate onto every rank.
    ///
    /// Pure: reads state and content, changes neither. Deterministic: prerequisites are
    /// examined in authored order and the first fault wins.
    /// </remarks>
    internal static class SkillRequirements
    {
        /// <summary>Checks class, job and prerequisites, in that order.</summary>
        public static SkillRequirementOutcome Check(SkillDefinition skill,
            CharacterClassState classState, CharacterSkillsState learned)
        {
            if (skill.RequiredClass.IsValid && skill.RequiredClass != classState.BaseClass)
            {
                return SkillRequirementOutcome.Failed(SkillRequirementFault.Class);
            }

            if (skill.RequiredJob.IsValid && skill.RequiredJob != classState.CurrentJob)
            {
                return SkillRequirementOutcome.Failed(SkillRequirementFault.Job);
            }

            return CheckPrerequisites(skill, learned);
        }

        /// <summary>
        /// Checks every skill that must be known first, in authored order.
        /// </summary>
        /// <remarks>
        /// One level deep, and that is correct: the question is whether the character holds
        /// the prerequisite now, which <see cref="CharacterSkillsState"/> answers directly,
        /// and how they came to hold it is settled history. Nothing recurses into a
        /// prerequisite's own prerequisites, so an authored cycle -- A requiring B requiring
        /// A -- terminates here rather than looping or overflowing the stack. It leaves both
        /// skills unreachable, which is a content fault for skill validation to report.
        ///
        /// A prerequisite naming no skill is a content fault too, reported by skill
        /// validation; it is not a reason to refuse a player, so it is skipped.
        /// </remarks>
        private static SkillRequirementOutcome CheckPrerequisites(SkillDefinition skill,
            CharacterSkillsState learned)
        {
            SkillPrerequisite[] prerequisites = skill.Prerequisites;

            if (prerequisites == null)
            {
                return SkillRequirementOutcome.Satisfied();
            }

            for (int i = 0; i < prerequisites.Length; i++)
            {
                SkillPrerequisite prerequisite = prerequisites[i];

                if (!prerequisite.Skill.IsValid)
                {
                    continue;
                }

                if (!learned.TryGetRank(prerequisite.Skill, out int rank))
                {
                    return SkillRequirementOutcome.FailedOn(
                        SkillRequirementFault.PrerequisiteNotLearned,
                        prerequisite.Skill, prerequisite.Level);
                }

                if (rank < prerequisite.Level)
                {
                    return SkillRequirementOutcome.FailedOn(
                        SkillRequirementFault.PrerequisiteRankTooLow,
                        prerequisite.Skill, prerequisite.Level);
                }
            }

            return SkillRequirementOutcome.Satisfied();
        }
    }
}
