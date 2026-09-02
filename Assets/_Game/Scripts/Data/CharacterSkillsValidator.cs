using System;
using System.Collections.Generic;
using ChibiFantasy.Core;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// Checks that a character's learned skills name real skills and hold ranks those
    /// skills allow.
    /// </summary>
    /// <remarks>
    /// The same split stats use. <see cref="CharacterSkillsState"/> holds ids, not
    /// definitions, so on its own it can only refuse a rank below one; deciding that rank
    /// seven is too high needs <see cref="SkillDefinition.MaxLevel"/>, which is content the
    /// state deliberately cannot see. Supplying the registry here is what makes the ceiling
    /// provable, and it is the only reason this type takes one.
    ///
    /// It also detects the orphan case patch compatibility depends on: a skill a character
    /// still knows whose definition no longer ships. That is reported, never deleted, so
    /// migration tooling decides what to do with the rank rather than discovering it has
    /// already been thrown away.
    ///
    /// <b>What is deliberately out of reach.</b> Nothing here asks whether the character
    /// was ever entitled to the skill. Class and job availability, prerequisites and level
    /// gates would each need the character's class, job and level -- three aggregates this
    /// validator is not given -- and would amount to re-deriving acquisition rules that do
    /// not exist yet. Cooldown, cost affordability, target legality and damage are combat
    /// questions and are not asked either. What is checked is exactly what the state plus
    /// the skill registry can prove.
    ///
    /// Reuses the 04.6 validation vocabulary rather than adding a second framework, so a
    /// bad learned skill appears in the same report as a bad stat or a malformed level
    /// curve.
    ///
    /// Reports, never repairs. Deterministic: entries are examined in stored order.
    /// </remarks>
    public sealed class CharacterSkillsValidator
    {
        /// <summary>
        /// Validates every learned skill against the supplied skill content.
        /// </summary>
        public ValidationReport Validate(CharacterSkillsState skills,
            IDefinitionRegistry<SkillDefinition> definitions)
        {
            if (skills == null)
            {
                throw new ArgumentNullException(nameof(skills));
            }

            if (definitions == null)
            {
                throw new ArgumentNullException(nameof(definitions));
            }

            var report = new ValidationReport();

            if (!skills.CharacterId.IsValid)
            {
                report.AddError(ValidationCode.MissingDefinitionId, DefinitionId.None,
                    "Learned skills are not attached to a character.");
            }

            var seen = new HashSet<DefinitionId>();
            IReadOnlyList<CharacterSkillEntry> entries = skills.Skills;

            for (int i = 0; i < entries.Count; i++)
            {
                ValidateEntry(entries[i], definitions, seen, report);
            }

            return report;
        }

        private static void ValidateEntry(CharacterSkillEntry entry,
            IDefinitionRegistry<SkillDefinition> definitions,
            HashSet<DefinitionId> seen, ValidationReport report)
        {
            DefinitionId skill = entry.Skill;

            if (!skill.IsValid)
            {
                report.AddError(ValidationCode.MissingDefinitionId, DefinitionId.None,
                    "A learned skill entry has no skill id.");
                return;
            }

            if (!seen.Add(skill))
            {
                report.AddError(ValidationCode.DuplicateDefinitionId, skill,
                    "The skill '" + skill + "' is recorded more than once.");
                return;
            }

            if (entry.Rank < 1)
            {
                report.AddError(ValidationCode.ValueOutOfRange, skill,
                    "Rank " + entry.Rank + " is below one; a known skill is held at rank one "
                    + "or greater.");
            }

            if (!definitions.TryGet(skill, out SkillDefinition definition))
            {
                report.AddError(ValidationCode.MissingReference, skill,
                    "The skill '" + skill + "' has no definition; it may be orphaned by a patch.");
                return;
            }

            if (entry.Rank > definition.MaxLevel)
            {
                report.AddError(ValidationCode.ValueOutOfRange, skill,
                    "Rank " + entry.Rank + " is above the skill's maximum of "
                    + definition.MaxLevel + ".");
            }
        }
    }
}
