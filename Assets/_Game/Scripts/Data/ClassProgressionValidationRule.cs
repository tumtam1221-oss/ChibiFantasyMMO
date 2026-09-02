using System;
using ChibiFantasy.Core;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// Checks that a class is a usable starting point and that its advertised job-change
    /// level agrees with the jobs it leads to.
    /// </summary>
    /// <remarks>
    /// <b>Resolving a duplication.</b> Two fields could describe the same rule:
    /// <see cref="ClassDefinition.JobChangeLevel"/> and the
    /// <see cref="JobDefinition.LevelRequirement"/> of the jobs a class offers. Only one
    /// can be authoritative, and it is the job's, because the same field governs every
    /// later tier and a single source cannot drift from itself. The class field remains as
    /// the advertised threshold shown to a player before they have a target in mind.
    ///
    /// Rather than delete a field other content and tests already write, this rule makes
    /// the two impossible to disagree quietly: the class must advertise exactly the lowest
    /// requirement among the jobs it offers. A designer who retunes one and forgets the
    /// other gets an error instead of a slow mystery.
    ///
    /// <b>Reachability.</b> Only the shallow case is checked, that a class's own next jobs
    /// exist and belong to it. Whether every job in a tree is reachable from some root is a
    /// tree-wide question and deliberately out of scope; the per-job rule from 05.7 still
    /// guards each node.
    ///
    /// Reports, never repairs.
    /// </remarks>
    public sealed class ClassProgressionValidationRule : IDefinitionValidationRule
    {
        private readonly IDefinitionRegistry<JobDefinition> _jobs;

        public ClassProgressionValidationRule(IDefinitionRegistry<JobDefinition> jobs)
        {
            _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        }

        public void Validate(IDefinition definition, IDefinitionLookup lookup, ValidationReport report)
        {
            var characterClass = definition as ClassDefinition;

            if (characterClass == null)
            {
                return;
            }

            DefinitionId id = characterClass.Id;
            DefinitionId[] next = characterClass.NextJobs;

            if (next == null || next.Length == 0)
            {
                // A class with no advancement is legal; it simply never changes job.
                return;
            }

            bool anyResolved = false;
            int lowest = int.MaxValue;

            for (int i = 0; i < next.Length; i++)
            {
                DefinitionId candidate = next[i];

                if (!_jobs.TryGet(candidate, out JobDefinition job))
                {
                    report.AddError(ValidationCode.MissingReference, id,
                        "Leads to '" + candidate + "', which does not exist.");
                    continue;
                }

                if (job.BaseClass != id)
                {
                    report.AddError(ValidationCode.InvalidConfiguration, id,
                        "Leads to '" + candidate + "', which belongs to another class.");
                    continue;
                }

                if (job.PrerequisiteJob.IsValid)
                {
                    report.AddError(ValidationCode.InvalidConfiguration, id,
                        "Leads to '" + candidate
                        + "', which requires a previous job a new character cannot have held.");
                    continue;
                }

                anyResolved = true;

                if (job.LevelRequirement < lowest)
                {
                    lowest = job.LevelRequirement;
                }
            }

            if (anyResolved && characterClass.JobChangeLevel != lowest)
            {
                report.AddError(ValidationCode.InvalidConfiguration, id,
                    "Advertises job change at level " + characterClass.JobChangeLevel
                    + " but its earliest job requires " + lowest
                    + ". The job's requirement is authoritative.");
            }
        }
    }
}
