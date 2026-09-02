using System;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// Decides whether a learned skill may advance one rank, and is the only thing that
    /// advances one.
    /// </summary>
    /// <remarks>
    /// <b>The sibling of <see cref="SkillLearningEvaluator"/>.</b> Same conventions
    /// throughout: a pure <see cref="Evaluate"/> that changes nothing, a
    /// <see cref="TryUpgrade"/> that is the only mutation path and evaluates first, and one
    /// deterministic reason rather than a list, because a domain rule answers one question
    /// about one attempt. Accumulating every fault is what
    /// <see cref="Data.ValidationReport"/> is for, and the two concepts stay apart.
    ///
    /// <b>One rank at a time, never further.</b> The target is always exactly the current
    /// rank plus one. There is no method that jumps to a rank, so a rank cannot be skipped
    /// by a caller passing a bigger number, and the requirements of the ranks in between
    /// can never go unchecked. Reaching rank five means five decisions, each one gated.
    ///
    /// <b>Each rank uses its own gate.</b> The character-level requirement is authored per
    /// rank on <see cref="SkillLevelEntry.RequiredCharacterLevel"/>, so this reads the entry
    /// for the rank being moved to, never rank one's. Class, job and prerequisites are
    /// authored on the skill rather than per rank, so they are checked through the shared
    /// <see cref="SkillRequirements"/> and mean the same thing at every rank. That split is
    /// the schema's, not an assumption: <see cref="SkillLevelEntry"/> has no class, job or
    /// prerequisite field, and inventing per-rank versions of them would be a requirement
    /// system no designer can author.
    ///
    /// <b>Nothing is duplicated.</b> Rank is the integer already held by
    /// <see cref="CharacterSkillsState"/>; no second rank representation exists. Level
    /// arrives as a parameter, class and job come from <see cref="CharacterClassState"/>.
    /// This type owns no state.
    ///
    /// <b>Not implemented because nothing represents it.</b> No skill point, rank-up cost or
    /// learning currency exists anywhere in the project, so no such requirement is checked
    /// and none was invented.
    /// </remarks>
    public sealed class SkillUpgradeEvaluator
    {
        /// <summary>
        /// Answers whether a skill may advance one rank, without changing anything.
        /// </summary>
        /// <param name="learned">What the character knows, and at what ranks.</param>
        /// <param name="classState">The character's class and current job.</param>
        /// <param name="characterLevel">Level from the character's progression state.</param>
        /// <param name="targetSkill">The skill being advanced.</param>
        /// <param name="skills">Skill content.</param>
        public SkillUpgradeEligibility Evaluate(
            CharacterSkillsState learned,
            CharacterClassState classState,
            int characterLevel,
            DefinitionId targetSkill,
            IDefinitionRegistry<SkillDefinition> skills)
        {
            if (learned == null)
            {
                throw new ArgumentNullException(nameof(learned));
            }

            if (classState == null)
            {
                throw new ArgumentNullException(nameof(classState));
            }

            if (skills == null)
            {
                throw new ArgumentNullException(nameof(skills));
            }

            if (!targetSkill.IsValid || !skills.TryGet(targetSkill, out SkillDefinition target))
            {
                return SkillUpgradeEligibility.Rejected(
                    SkillUpgradeRejection.UnknownSkill, 0, 0, 0);
            }

            if (!learned.TryGetRank(targetSkill, out int currentRank))
            {
                return SkillUpgradeEligibility.Rejected(
                    SkillUpgradeRejection.NotLearned, 0, 0, 0);
            }

            if (currentRank >= target.MaxLevel)
            {
                return SkillUpgradeEligibility.Rejected(
                    SkillUpgradeRejection.AlreadyMaxRank, currentRank, 0, 0);
            }

            int nextRank = currentRank + 1;

            if (!target.TryGetLevel(nextRank, out SkillLevelEntry entry))
            {
                // The skill claims to reach this rank but describes no entry for it.
                return SkillUpgradeEligibility.Rejected(
                    SkillUpgradeRejection.NextRankUnavailable, currentRank, nextRank, 0);
            }

            int required = entry.RequiredCharacterLevel;

            SkillRequirementOutcome requirements =
                SkillRequirements.Check(target, classState, learned);

            if (!requirements.IsSatisfied)
            {
                return Translate(requirements, currentRank, nextRank, required);
            }

            if (characterLevel < required)
            {
                return SkillUpgradeEligibility.Rejected(
                    SkillUpgradeRejection.LevelTooLow, currentRank, nextRank, required);
            }

            return SkillUpgradeEligibility.Allowed(currentRank, nextRank, required);
        }

        /// <summary>
        /// Advances a skill one rank if it is permitted, and reports what was decided.
        /// </summary>
        /// <remarks>
        /// The only route to a rank increase that runs the rules. Evaluation completes
        /// before anything is written, so a refusal has no partial change to undo and
        /// leaves the rank, every other learned skill and the revision exactly as they
        /// were. A success writes one rank through
        /// <see cref="CharacterSkillsState.SetRank"/>, which replaces the existing entry in
        /// place, so no duplicate can be created and the revision advances exactly once.
        /// </remarks>
        /// <returns>True when the rank was advanced.</returns>
        public bool TryUpgrade(
            CharacterSkillsState learned,
            CharacterClassState classState,
            int characterLevel,
            DefinitionId targetSkill,
            IDefinitionRegistry<SkillDefinition> skills,
            out SkillUpgradeEligibility eligibility)
        {
            eligibility = Evaluate(learned, classState, characterLevel, targetSkill, skills);

            if (!eligibility.IsAllowed)
            {
                return false;
            }

            learned.SetRank(targetSkill, eligibility.NextRank);
            return true;
        }

        /// <summary>States a shared requirement failure in rank progression's vocabulary.</summary>
        private static SkillUpgradeEligibility Translate(SkillRequirementOutcome outcome,
            int currentRank, int nextRank, int requiredLevel)
        {
            switch (outcome.Fault)
            {
                case SkillRequirementFault.Class:
                    return SkillUpgradeEligibility.Rejected(
                        SkillUpgradeRejection.ClassRequirementNotMet,
                        currentRank, nextRank, requiredLevel);

                case SkillRequirementFault.Job:
                    return SkillUpgradeEligibility.Rejected(
                        SkillUpgradeRejection.JobRequirementNotMet,
                        currentRank, nextRank, requiredLevel);

                case SkillRequirementFault.PrerequisiteRankTooLow:
                    return SkillUpgradeEligibility.RejectedByPrerequisite(
                        SkillUpgradeRejection.PrerequisiteRankTooLow,
                        currentRank, nextRank, requiredLevel,
                        outcome.Prerequisite, outcome.RequiredRank);

                default:
                    return SkillUpgradeEligibility.RejectedByPrerequisite(
                        SkillUpgradeRejection.PrerequisiteNotLearned,
                        currentRank, nextRank, requiredLevel,
                        outcome.Prerequisite, outcome.RequiredRank);
            }
        }
    }
}
