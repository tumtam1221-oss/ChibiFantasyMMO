using System;
using System.Collections.Generic;
using ChibiFantasy.Core;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// Checks that a character's stats name real stats and hold values those stats allow.
    /// </summary>
    /// <remarks>
    /// This is where the per-stat ceiling is enforced. <see cref="CharacterStatsState"/>
    /// holds ids, not definitions, so it can only reject a negative value; deciding that
    /// 400 strength is too much needs the content the state deliberately cannot see.
    ///
    /// Reuses the 04.6 validation vocabulary rather than adding a second framework, so a
    /// bad stat appears in the same report as a bad appearance selection or a malformed
    /// level curve.
    ///
    /// It also detects the orphan case that patch compatibility depends on: a stat a
    /// character still holds whose definition no longer ships. That is reported, never
    /// deleted, so migration tooling can decide what to do with the value rather than
    /// discovering it has already been thrown away.
    ///
    /// Reports, never repairs. Deterministic: entries are examined in stored order.
    /// </remarks>
    public sealed class CharacterStatsValidator
    {
        /// <summary>
        /// Validates every stat entry against the supplied stat content.
        /// </summary>
        public ValidationReport Validate(CharacterStatsState stats,
            IDefinitionRegistry<StatDefinition> definitions)
        {
            if (stats == null)
            {
                throw new ArgumentNullException(nameof(stats));
            }

            if (definitions == null)
            {
                throw new ArgumentNullException(nameof(definitions));
            }

            var report = new ValidationReport();

            if (!stats.CharacterId.IsValid)
            {
                report.AddError(ValidationCode.MissingDefinitionId, DefinitionId.None,
                    "Stats are not attached to a character.");
            }

            var seen = new HashSet<DefinitionId>();
            IReadOnlyList<CharacterStatEntry> entries = stats.Stats;

            for (int i = 0; i < entries.Count; i++)
            {
                ValidateEntry(entries[i], definitions, seen, report);
            }

            return report;
        }

        private static void ValidateEntry(CharacterStatEntry entry,
            IDefinitionRegistry<StatDefinition> definitions,
            HashSet<DefinitionId> seen, ValidationReport report)
        {
            DefinitionId stat = entry.Stat;

            if (!stat.IsValid)
            {
                report.AddError(ValidationCode.MissingDefinitionId, DefinitionId.None,
                    "A stat entry has no stat id.");
                return;
            }

            if (!seen.Add(stat))
            {
                report.AddError(ValidationCode.DuplicateDefinitionId, stat,
                    "The stat '" + stat + "' is recorded more than once.");
                return;
            }

            if (!definitions.TryGet(stat, out StatDefinition definition))
            {
                report.AddError(ValidationCode.MissingReference, stat,
                    "The stat '" + stat + "' has no definition; it may be orphaned by a patch.");
                return;
            }

            if (entry.Value < definition.MinValue || entry.Value > definition.MaxValue)
            {
                report.AddError(ValidationCode.ValueOutOfRange, stat,
                    "Value " + entry.Value + " is outside the allowed range "
                    + definition.MinValue + " to " + definition.MaxValue + ".");
            }
        }
    }
}
