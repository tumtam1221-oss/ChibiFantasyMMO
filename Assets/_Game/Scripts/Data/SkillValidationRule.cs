using System;
using ChibiFantasy.Core;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// Checks that a skill is internally coherent and points only at content that exists.
    /// </summary>
    /// <remarks>
    /// Plugs into the existing <see cref="DefinitionValidator"/>, so a malformed skill is
    /// reported alongside every other content fault. Reports, never repairs.
    ///
    /// The level table is where faults hide, so it is checked hardest: ranks must run one
    /// upward with no gaps and no repeats, the table must agree with
    /// <see cref="SkillDefinition.MaxLevel"/>, and costs and cooldowns must be
    /// non-negative. Ranks out of order or repeated would make "the entry for level three"
    /// ambiguous, and that surfaces far from its cause.
    ///
    /// Deterministic: entries and prerequisites are examined in authored order.
    /// </remarks>
    public sealed partial class SkillValidationRule : IDefinitionValidationRule
    {
        private readonly IDefinitionRegistry<SkillDefinition> _skills;
        private readonly IDefinitionRegistry<ClassDefinition> _classes;
        private readonly IDefinitionRegistry<JobDefinition> _jobs;

        public SkillValidationRule(
            IDefinitionRegistry<SkillDefinition> skills,
            IDefinitionRegistry<ClassDefinition> classes,
            IDefinitionRegistry<JobDefinition> jobs)
        {
            _skills = skills ?? throw new ArgumentNullException(nameof(skills));
            _classes = classes ?? throw new ArgumentNullException(nameof(classes));
            _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        }

        public void Validate(IDefinition definition, IDefinitionLookup lookup, ValidationReport report)
        {
            var skill = definition as SkillDefinition;

            if (skill == null)
            {
                return;
            }

            DefinitionId id = skill.Id;

            ValidateScalars(skill, id, report);
            ValidateCombinations(skill, id, report);
            ValidateLevels(skill, id, report);
            ValidateAvailability(skill, id, report);
            ValidatePrerequisites(skill, id, report);
        }

        private static void ValidateScalars(SkillDefinition skill, DefinitionId id, ValidationReport report)
        {
            if (skill.MaxLevel < 1)
            {
                report.AddError(ValidationCode.InvalidConfiguration, id,
                    "Maximum level " + skill.MaxLevel + " is below one.");
            }

            if (skill.BaseResourceCost < 0)
            {
                report.AddError(ValidationCode.ValueOutOfRange, id, "Resource cost is negative.");
            }

            if (skill.CooldownSeconds < 0)
            {
                report.AddError(ValidationCode.ValueOutOfRange, id, "Cooldown is negative.");
            }

            if (skill.CastTimeSeconds < 0)
            {
                report.AddError(ValidationCode.ValueOutOfRange, id, "Cast time is negative.");
            }

            if (skill.Range < 0)
            {
                report.AddError(ValidationCode.ValueOutOfRange, id, "Range is negative.");
            }
        }

        private static void ValidateLevels(SkillDefinition skill, DefinitionId id, ValidationReport report)
        {
            SkillLevelEntry[] levels = skill.Levels;

            if (levels == null || levels.Length == 0)
            {
                // A skill with no table is a single-rank skill described by its own fields.
                // Claiming more ranks than that is a content fault: the ranks above one
                // describe nothing, so nothing can say what they cost or what they do. Left
                // unreported it surfaced far away, as a rank-up refused at runtime for a
                // reason no player caused and no designer was told about.
                if (skill.MaxLevel > 1)
                {
                    report.AddError(ValidationCode.InvalidConfiguration, id,
                        "Maximum level is " + skill.MaxLevel
                        + " but no level table is authored, so ranks above one describe nothing.");
                }

                return;
            }

            if (levels.Length != skill.MaxLevel)
            {
                report.AddError(ValidationCode.InvalidConfiguration, id,
                    "Maximum level is " + skill.MaxLevel + " but the table holds "
                    + levels.Length + " entries.");
            }

            for (int i = 0; i < levels.Length; i++)
            {
                SkillLevelEntry entry = levels[i];

                if (entry.Level != i + 1)
                {
                    report.AddError(ValidationCode.InvalidConfiguration, id,
                        "Table entry " + i + " is level " + entry.Level
                        + "; ranks must run one upward without gaps or repeats.");
                }

                if (entry.RequiredCharacterLevel < 1)
                {
                    report.AddError(ValidationCode.ValueOutOfRange, id,
                        "Level " + entry.Level + " requires character level "
                        + entry.RequiredCharacterLevel + ", which is below one.");
                }

                if (entry.ResourceCost < 0)
                {
                    report.AddError(ValidationCode.ValueOutOfRange, id,
                        "Level " + entry.Level + " has a negative resource cost.");
                }

                if (entry.CooldownSeconds < 0)
                {
                    report.AddError(ValidationCode.ValueOutOfRange, id,
                        "Level " + entry.Level + " has a negative cooldown.");
                }
            }
        }
    }
}
