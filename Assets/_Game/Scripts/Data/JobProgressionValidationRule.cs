using System;
using System.Collections.Generic;
using ChibiFantasy.Core;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// Checks that a job sits correctly in its class tree and that the tree cannot loop.
    /// </summary>
    /// <remarks>
    /// Plugs into the existing <see cref="DefinitionValidator"/>, so a malformed job is
    /// reported alongside every other content fault.
    ///
    /// Enforced:
    /// <list type="bullet">
    /// <item>the job names a class, and that class exists;</item>
    /// <item>its prerequisite exists, is not itself, and belongs to the same class;</item>
    /// <item>every next job exists, is not itself, and belongs to the same class;</item>
    /// <item>the level requirement is not negative and the tier is at least one;</item>
    /// <item>following next jobs never returns to where it started.</item>
    /// </list>
    ///
    /// <b>Cycles.</b> Progression advances, so a loop would let a character circle forever
    /// and would make "which job comes next" unanswerable. Detection is a depth-first walk
    /// from the job being validated looking for a way back to itself, which is enough for
    /// an acyclic tree and stops well short of a general graph engine.
    ///
    /// Reports, never repairs. Deterministic: entries are examined in authored order.
    /// </remarks>
    public sealed class JobProgressionValidationRule : IDefinitionValidationRule
    {
        private readonly IDefinitionRegistry<JobDefinition> _jobs;
        private readonly IDefinitionRegistry<ClassDefinition> _classes;

        public JobProgressionValidationRule(
            IDefinitionRegistry<JobDefinition> jobs,
            IDefinitionRegistry<ClassDefinition> classes)
        {
            _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
            _classes = classes ?? throw new ArgumentNullException(nameof(classes));
        }

        public void Validate(IDefinition definition, IDefinitionLookup lookup, ValidationReport report)
        {
            var job = definition as JobDefinition;

            if (job == null)
            {
                return;
            }

            DefinitionId id = job.Id;

            if (job.Tier < 1)
            {
                report.AddError(ValidationCode.InvalidConfiguration, id,
                    "Tier " + job.Tier + " is below one.");
            }

            if (job.LevelRequirement < 0)
            {
                report.AddError(ValidationCode.InvalidConfiguration, id,
                    "Level requirement " + job.LevelRequirement + " is negative.");
            }

            ValidateClass(job, id, report);
            ValidatePrerequisite(job, id, report);
            ValidateNextJobs(job, id, report);
            ValidateNoCycle(job, id, report);
        }

        private void ValidateClass(JobDefinition job, DefinitionId id, ValidationReport report)
        {
            if (!job.BaseClass.IsValid)
            {
                report.AddError(ValidationCode.MissingDefinitionId, id,
                    "The job does not say which class it belongs to.");
            }
            else if (!_classes.Contains(job.BaseClass))
            {
                report.AddError(ValidationCode.MissingReference, id,
                    "Belongs to class '" + job.BaseClass + "', which does not exist.");
            }
        }

        private void ValidatePrerequisite(JobDefinition job, DefinitionId id, ValidationReport report)
        {
            DefinitionId prerequisite = job.PrerequisiteJob;

            if (!prerequisite.IsValid)
            {
                return;
            }

            if (prerequisite == id)
            {
                report.AddError(ValidationCode.InvalidConfiguration, id,
                    "The job lists itself as its own prerequisite.");
                return;
            }

            if (!_jobs.TryGet(prerequisite, out JobDefinition previous))
            {
                report.AddError(ValidationCode.MissingReference, id,
                    "Requires '" + prerequisite + "', which does not exist.");
                return;
            }

            if (previous.BaseClass != job.BaseClass)
            {
                report.AddError(ValidationCode.InvalidConfiguration, id,
                    "Requires '" + prerequisite + "', which belongs to a different class.");
            }
        }

        private void ValidateNextJobs(JobDefinition job, DefinitionId id, ValidationReport report)
        {
            DefinitionId[] next = job.NextJobs;

            if (next == null)
            {
                return;
            }

            for (int i = 0; i < next.Length; i++)
            {
                DefinitionId candidate = next[i];

                if (candidate == id)
                {
                    report.AddError(ValidationCode.InvalidConfiguration, id,
                        "The job lists itself as one of its own next jobs.");
                    continue;
                }

                if (!_jobs.TryGet(candidate, out JobDefinition successor))
                {
                    report.AddError(ValidationCode.MissingReference, id,
                        "Leads to '" + candidate + "', which does not exist.");
                    continue;
                }

                if (successor.BaseClass != job.BaseClass)
                {
                    report.AddError(ValidationCode.InvalidConfiguration, id,
                        "Leads to '" + candidate + "', which belongs to a different class.");
                }
            }
        }

        private void ValidateNoCycle(JobDefinition job, DefinitionId id, ValidationReport report)
        {
            var visited = new HashSet<DefinitionId>();
            var pending = new Stack<DefinitionId>();

            Push(pending, visited, job.NextJobs);

            while (pending.Count > 0)
            {
                DefinitionId current = pending.Pop();

                if (current == id)
                {
                    report.AddError(ValidationCode.InvalidConfiguration, id,
                        "Following next jobs leads back to '" + id + "'; progression must advance.");
                    return;
                }

                if (_jobs.TryGet(current, out JobDefinition step))
                {
                    Push(pending, visited, step.NextJobs);
                }
            }
        }

        private static void Push(Stack<DefinitionId> pending, HashSet<DefinitionId> visited,
            DefinitionId[] ids)
        {
            if (ids == null)
            {
                return;
            }

            for (int i = 0; i < ids.Length; i++)
            {
                if (ids[i].IsValid && visited.Add(ids[i]))
                {
                    pending.Push(ids[i]);
                }
            }
        }
    }
}
