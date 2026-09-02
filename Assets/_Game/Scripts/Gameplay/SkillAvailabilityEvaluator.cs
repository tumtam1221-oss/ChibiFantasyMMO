using System;
using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// Answers where a character stands with a skill, or with every skill their class and
    /// job offer.
    /// </summary>
    /// <remarks>
    /// <b>It composes; it decides nothing itself.</b> Every requirement -- level, class,
    /// job, prerequisite, prerequisite rank, rank ceiling -- is answered by
    /// <see cref="SkillLearningEvaluator"/> and <see cref="SkillUpgradeEvaluator"/>, which
    /// already own those rules and share their skill-wide checks through
    /// <see cref="SkillRequirements"/>. This type runs both and reads the result. There is
    /// no second implementation of a level check, a class check, a job check or a
    /// prerequisite walk here, and adding one would immediately be a rule that could
    /// disagree with the one that actually gates the mutation.
    ///
    /// <b>Read-only, and provably so.</b> It calls only the pure Evaluate methods, never
    /// TryLearn or TryUpgrade, so no learned skill, character state or revision can move
    /// while a caller is asking a question. A UI polling this every frame would change
    /// nothing.
    ///
    /// <b>Deterministic.</b> The same state and content always give the same answer, and a
    /// collection comes back in the order it was asked for. Nothing here reads a clock, a
    /// random value, a Unity instance id, a frame counter or a dictionary's iteration
    /// order.
    ///
    /// <b>What it does not do.</b> It grants nothing. Knowing that a class offers a skill
    /// at creation, or that a job unlocks one, is not the same as handing it over; that
    /// acquisition step belongs to character creation and job change, which are later
    /// systems. Nothing here scans assets, holds a cache, or reaches for a singleton;
    /// content arrives as registries the caller already has.
    /// </remarks>
    public sealed class SkillAvailabilityEvaluator
    {
        private readonly SkillLearningEvaluator _learning = new SkillLearningEvaluator();
        private readonly SkillUpgradeEvaluator _upgrade = new SkillUpgradeEvaluator();

        /// <summary>
        /// Answers where the character stands with one skill.
        /// </summary>
        /// <param name="learned">What the character knows, and at what ranks.</param>
        /// <param name="classState">The character's class and current job.</param>
        /// <param name="characterLevel">Level from the character's progression state.</param>
        /// <param name="skill">The skill being asked about.</param>
        /// <param name="skills">Skill content.</param>
        public SkillAvailability Evaluate(
            CharacterSkillsState learned,
            CharacterClassState classState,
            int characterLevel,
            DefinitionId skill,
            IDefinitionRegistry<SkillDefinition> skills)
        {
            // Both rule layers validate their own arguments; calling them is the check.
            SkillLearnEligibility learn =
                _learning.Evaluate(learned, classState, characterLevel, skill, skills);
            SkillUpgradeEligibility upgrade =
                _upgrade.Evaluate(learned, classState, characterLevel, skill, skills);

            return new SkillAvailability(
                skill, StatusOf(learn, upgrade), upgrade.CurrentRank, learn, upgrade);
        }

        /// <summary>
        /// Answers for a set of skills, in the order given.
        /// </summary>
        /// <remarks>
        /// Duplicates in the input are answered once each and left in place; it is the
        /// caller's list, and silently collapsing it would misreport what was asked.
        ///
        /// Arguments are checked here rather than left to the first
        /// <see cref="Evaluate"/> call, so an empty list is rejected on the same terms as a
        /// full one instead of quietly returning nothing.
        /// </remarks>
        public IReadOnlyList<SkillAvailability> EvaluateAll(
            CharacterSkillsState learned,
            CharacterClassState classState,
            int characterLevel,
            IEnumerable<DefinitionId> candidates,
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

            if (candidates == null)
            {
                throw new ArgumentNullException(nameof(candidates));
            }

            var results = new List<SkillAvailability>();

            foreach (DefinitionId candidate in candidates)
            {
                results.Add(Evaluate(learned, classState, characterLevel, candidate, skills));
            }

            return results;
        }

        /// <summary>
        /// Answers for every skill the character's class and current job offer.
        /// </summary>
        /// <remarks>
        /// <b>The candidate set is authored, not invented.</b> It is
        /// <see cref="ClassDefinition.StartingSkills"/> followed by
        /// <see cref="JobDefinition.Skills"/> for the job currently held, in authored order,
        /// with a skill named by both answered once. Those are the only two places the
        /// schema states which skills belong to whom, and 06.4's grant validation already
        /// checks that they resolve and agree with each skill's own requirements.
        ///
        /// <b>Only the job actually held contributes.</b> <see cref="CharacterClassState"/>
        /// records a base class and a current job and keeps no history, so the skills of a
        /// job the character advanced through earlier cannot be gathered here. Walking
        /// backwards through <see cref="JobDefinition.PrerequisiteJob"/> to guess at that
        /// history would assume a rule about retained skills that no part of the project has
        /// stated.
        ///
        /// A class or job whose definition does not resolve contributes nothing rather than
        /// throwing: an orphaned reference is a content fault for validation to report, and
        /// a character should not become unreadable because of one.
        /// </remarks>
        public IReadOnlyList<SkillAvailability> EvaluateGranted(
            CharacterSkillsState learned,
            CharacterClassState classState,
            int characterLevel,
            IDefinitionRegistry<SkillDefinition> skills,
            IDefinitionRegistry<ClassDefinition> classes,
            IDefinitionRegistry<JobDefinition> jobs)
        {
            if (classState == null)
            {
                throw new ArgumentNullException(nameof(classState));
            }

            if (classes == null)
            {
                throw new ArgumentNullException(nameof(classes));
            }

            if (jobs == null)
            {
                throw new ArgumentNullException(nameof(jobs));
            }

            var candidates = new List<DefinitionId>();
            var seen = new HashSet<DefinitionId>();

            if (classes.TryGet(classState.BaseClass, out ClassDefinition baseClass))
            {
                Collect(baseClass.StartingSkills, candidates, seen);
            }

            if (classState.CurrentJob.IsValid
                && jobs.TryGet(classState.CurrentJob, out JobDefinition job))
            {
                Collect(job.Skills, candidates, seen);
            }

            return EvaluateAll(learned, classState, characterLevel, candidates, skills);
        }

        private static void Collect(DefinitionId[] granted, List<DefinitionId> candidates,
            HashSet<DefinitionId> seen)
        {
            if (granted == null)
            {
                return;
            }

            for (int i = 0; i < granted.Length; i++)
            {
                if (granted[i].IsValid && seen.Add(granted[i]))
                {
                    candidates.Add(granted[i]);
                }
            }
        }

        /// <summary>
        /// Reads a status off the two rule answers.
        /// </summary>
        /// <remarks>
        /// Derived rather than decided: whether the skill is known is the upgrade rule's
        /// <see cref="SkillUpgradeRejection.NotLearned"/>, and whether it is finished is its
        /// <see cref="SkillUpgradeRejection.AlreadyMaxRank"/>. Asking the learned state
        /// again here would be a second opinion that could differ from the one that gates
        /// the mutation.
        /// </remarks>
        private static SkillAvailabilityStatus StatusOf(SkillLearnEligibility learn,
            SkillUpgradeEligibility upgrade)
        {
            if (upgrade.Reason == SkillUpgradeRejection.UnknownSkill)
            {
                return SkillAvailabilityStatus.Unknown;
            }

            if (upgrade.Reason == SkillUpgradeRejection.NotLearned)
            {
                return learn.IsAllowed
                    ? SkillAvailabilityStatus.Learnable
                    : SkillAvailabilityStatus.Blocked;
            }

            if (upgrade.IsAllowed)
            {
                return SkillAvailabilityStatus.Upgradeable;
            }

            return upgrade.Reason == SkillUpgradeRejection.AlreadyMaxRank
                ? SkillAvailabilityStatus.MaxRank
                : SkillAvailabilityStatus.UpgradeBlocked;
        }
    }
}
