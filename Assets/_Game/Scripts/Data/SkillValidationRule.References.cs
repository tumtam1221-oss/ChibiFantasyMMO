using ChibiFantasy.Core;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// The reference-checking half of skill validation: who may learn a skill, and what
    /// must be known first.
    /// </summary>
    /// <remarks>
    /// Availability reuses the existing class and job definitions rather than any list of
    /// skills per class. A skill with neither reference set is common to everyone, one with
    /// a class is that class's, one with a job is that job's; the four starting classes
    /// need no code and no per-class array.
    ///
    /// A job named alongside a class must actually belong to it, which stops a skill
    /// claiming to be for a Mage job while its class says Archer.
    /// </remarks>
    public sealed partial class SkillValidationRule
    {
        private void ValidateAvailability(SkillDefinition skill, DefinitionId id, ValidationReport report)
        {
            bool classResolved = false;

            if (skill.RequiredClass.IsValid)
            {
                if (_classes.Contains(skill.RequiredClass))
                {
                    classResolved = true;
                }
                else
                {
                    report.AddError(ValidationCode.MissingReference, id,
                        "Requires class '" + skill.RequiredClass + "', which does not exist.");
                }
            }

            if (!skill.RequiredJob.IsValid)
            {
                return;
            }

            if (!_jobs.TryGet(skill.RequiredJob, out JobDefinition job))
            {
                report.AddError(ValidationCode.MissingReference, id,
                    "Requires job '" + skill.RequiredJob + "', which does not exist.");
                return;
            }

            if (classResolved && job.BaseClass != skill.RequiredClass)
            {
                report.AddError(ValidationCode.InvalidConfiguration, id,
                    "Requires job '" + skill.RequiredJob + "', which belongs to class '"
                    + job.BaseClass + "' rather than the required class.");
            }
        }

        private void ValidatePrerequisites(SkillDefinition skill, DefinitionId id, ValidationReport report)
        {
            SkillPrerequisite[] prerequisites = skill.Prerequisites;

            if (prerequisites == null)
            {
                return;
            }

            for (int i = 0; i < prerequisites.Length; i++)
            {
                SkillPrerequisite prerequisite = prerequisites[i];

                if (!prerequisite.Skill.IsValid)
                {
                    report.AddError(ValidationCode.MissingDefinitionId, id,
                        "A prerequisite names no skill.");
                    continue;
                }

                if (prerequisite.Skill == id)
                {
                    report.AddError(ValidationCode.InvalidConfiguration, id,
                        "The skill lists itself as its own prerequisite.");
                    continue;
                }

                if (!_skills.TryGet(prerequisite.Skill, out SkillDefinition required))
                {
                    report.AddError(ValidationCode.MissingReference, id,
                        "Requires '" + prerequisite.Skill + "', which does not exist.");
                    continue;
                }

                if (prerequisite.Level < 1)
                {
                    report.AddError(ValidationCode.ValueOutOfRange, id,
                        "Requires '" + prerequisite.Skill + "' at level "
                        + prerequisite.Level + ", which is below one.");
                }
                else if (prerequisite.Level > required.MaxLevel)
                {
                    report.AddError(ValidationCode.ValueOutOfRange, id,
                        "Requires '" + prerequisite.Skill + "' at level " + prerequisite.Level
                        + " but that skill only reaches " + required.MaxLevel + ".");
                }
            }
        }
    }
}
