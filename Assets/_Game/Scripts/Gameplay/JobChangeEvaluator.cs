using System;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// Decides whether a character may take a job, and is the only thing that applies one.
    /// </summary>
    /// <remarks>
    /// <b>The tree is data.</b> Nothing here names Swordsman, Cleric, Mage or Archer, and
    /// no switch enumerates jobs. The rules walk whatever
    /// <see cref="ClassDefinition.NextJobs"/> and <see cref="JobDefinition.NextJobs"/>
    /// contain, so a designer adds a class, a branch or a fourth tier by authoring assets.
    /// Branch counts are whatever a definition lists; two at level thirty-five is content,
    /// not an assumption.
    ///
    /// <b>The target's level requirement governs.</b> Every job carries its own, so tier
    /// one, two and three are checked identically and there is one source for the gate.
    /// <see cref="ClassDefinition.JobChangeLevel"/> remains the class's advertised
    /// first-change threshold for presentation; it is not consulted here, because
    /// duplicating a gate is how the two drift apart.
    ///
    /// <b>Evaluation is pure.</b> <see cref="Evaluate"/> reads state and content and
    /// changes neither. Level arrives as a parameter from
    /// <see cref="CharacterProgressionState"/> rather than being looked up, so no second
    /// level system exists and the rules reach into nothing global.
    ///
    /// <see cref="TryApply"/> is the only mutation path, and it evaluates first, so a job
    /// cannot be set without the check having run. A server remains free to call it and
    /// reject anything a client claims.
    ///
    /// No stats are touched. Class and job modifiers exist on the definitions and will
    /// reach the derived-stat layer later as StatModifier inputs; nothing here computes or
    /// applies them.
    /// </remarks>
    public sealed class JobChangeEvaluator
    {
        /// <summary>
        /// Answers whether a job change is permitted, without changing anything.
        /// </summary>
        /// <param name="classState">The character's class and current job.</param>
        /// <param name="characterLevel">Level from the character's progression state.</param>
        /// <param name="targetJob">The job being sought.</param>
        /// <param name="classes">Class content.</param>
        /// <param name="jobs">Job content.</param>
        public JobChangeEligibility Evaluate(
            CharacterClassState classState,
            int characterLevel,
            DefinitionId targetJob,
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

            if (!targetJob.IsValid || !jobs.TryGet(targetJob, out JobDefinition target))
            {
                return JobChangeEligibility.Rejected(JobChangeRejection.UnknownJob, 0);
            }

            int required = target.LevelRequirement;

            if (targetJob == classState.CurrentJob)
            {
                return JobChangeEligibility.Rejected(JobChangeRejection.AlreadyHeld, required);
            }

            if (!classes.TryGet(classState.BaseClass, out ClassDefinition baseClass))
            {
                return JobChangeEligibility.Rejected(JobChangeRejection.UnknownClass, required);
            }

            if (target.BaseClass != baseClass.Id)
            {
                return JobChangeEligibility.Rejected(JobChangeRejection.WrongBaseClass, required);
            }

            JobChangeRejection pathFault = classState.HasChangedJob
                ? CheckFromJob(classState, target, targetJob, jobs)
                : CheckFromClass(baseClass, target, targetJob);

            if (pathFault != JobChangeRejection.None)
            {
                return JobChangeEligibility.Rejected(pathFault, required);
            }

            if (characterLevel < required)
            {
                return JobChangeEligibility.Rejected(JobChangeRejection.LevelTooLow, required);
            }

            return JobChangeEligibility.Allowed(required);
        }

        /// <summary>
        /// Applies a job change if it is permitted, and reports what was decided.
        /// </summary>
        /// <remarks>The only route to <see cref="CharacterClassState.SetJob"/> that runs
        /// the rules. A refusal leaves the state, its job and its revision untouched.</remarks>
        /// <returns>True when the change was applied.</returns>
        public bool TryApply(
            CharacterClassState classState,
            int characterLevel,
            DefinitionId targetJob,
            IDefinitionRegistry<ClassDefinition> classes,
            IDefinitionRegistry<JobDefinition> jobs,
            out JobChangeEligibility eligibility)
        {
            eligibility = Evaluate(classState, characterLevel, targetJob, classes, jobs);

            if (!eligibility.IsAllowed)
            {
                return false;
            }

            classState.SetJob(targetJob);
            return true;
        }

        /// <summary>First advancement: the class must offer the job, and it must be a root job.</summary>
        private static JobChangeRejection CheckFromClass(ClassDefinition baseClass,
            JobDefinition target, DefinitionId targetJob)
        {
            if (!Contains(baseClass.NextJobs, targetJob))
            {
                return JobChangeRejection.NotOffered;
            }

            // A first job cannot expect a predecessor the character has never held.
            return target.PrerequisiteJob.IsValid
                ? JobChangeRejection.PrerequisiteNotMet
                : JobChangeRejection.None;
        }

        /// <summary>Later advancement: the held job must offer it, and be its prerequisite.</summary>
        private static JobChangeRejection CheckFromJob(CharacterClassState classState,
            JobDefinition target, DefinitionId targetJob, IDefinitionRegistry<JobDefinition> jobs)
        {
            if (!jobs.TryGet(classState.CurrentJob, out JobDefinition current))
            {
                return JobChangeRejection.UnknownCurrentJob;
            }

            if (!Contains(current.NextJobs, targetJob))
            {
                return JobChangeRejection.NotOffered;
            }

            return target.PrerequisiteJob == current.Id
                ? JobChangeRejection.None
                : JobChangeRejection.PrerequisiteNotMet;
        }

        private static bool Contains(DefinitionId[] ids, DefinitionId id)
        {
            if (ids == null)
            {
                return false;
            }

            for (int i = 0; i < ids.Length; i++)
            {
                if (ids[i] == id)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
