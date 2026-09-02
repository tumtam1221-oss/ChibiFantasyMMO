using System;
using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// Checks that everything belonging to one character agrees with everything else.
    /// </summary>
    /// <remarks>
    /// <b>Observational only.</b> It reads and reports. It never repairs, regenerates an
    /// identity, adjusts a level, clamps a resource or touches a revision. Silently fixing
    /// malformed state would hide how it became malformed and make the fix the new truth.
    ///
    /// <b>Nothing is recalculated twice.</b> Derived stats come from the existing
    /// calculator and ceilings from the existing ResourceLimits, so this owns no formula.
    /// Derived stats are not persisted anywhere in this architecture, which is why there is
    /// no stored-versus-computed comparison to make: the check is that the resources a
    /// character holds fit the ceilings their current base stats imply.
    ///
    /// <b>The aggregate invariant nothing else could check.</b> Each state type validates
    /// itself in isolation and none can see the others, so only here can it be confirmed
    /// that all six belong to the same character. A mismatched CharacterId is the one fault
    /// that would otherwise pass every existing check.
    ///
    /// Reuses the 04.6 report and the appearance and stat validators rather than restating
    /// their rules, so a fault reads the same wherever it is found.
    /// </remarks>
    public sealed partial class CharacterConsistencyValidator
    {
        private static readonly StatModifier[] NoModifiers = new StatModifier[0];

        private readonly DerivedStatsCalculator _calculator = new DerivedStatsCalculator();
        private readonly CharacterStatsValidator _statsValidator = new CharacterStatsValidator();

        /// <summary>
        /// Validates a whole character against the content it references.
        /// </summary>
        /// <param name="character">The aggregate to inspect. Not modified.</param>
        /// <param name="content">Class, stat, appearance and progression content.</param>
        /// <param name="jobs">Job content, needed only once a job has been taken. Creation
        /// has no use for it, which is why it is not part of the content bundle.</param>
        public ValidationReport Validate(Character character, CharacterCreationContent content,
            IDefinitionRegistry<JobDefinition> jobs)
        {
            if (character == null)
            {
                throw new ArgumentNullException(nameof(character));
            }

            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            if (jobs == null)
            {
                throw new ArgumentNullException(nameof(jobs));
            }

            var report = new ValidationReport();

            ValidateSharedIdentity(character, report);
            ValidateIdentity(character, report);
            ValidateProgression(character, content, report);
            ValidateClassAndJob(character, content, jobs, report);

            Merge(_statsValidator.Validate(character.Stats, content.Stats), report);
            Merge(new CharacterAppearanceValidator(false).Validate(
                character.Appearance, content.AppearanceOptions, character.Identity.Gender), report);

            ValidateResources(character, content, report);

            return report;
        }

        /// <summary>Every part of a character must belong to the same character.</summary>
        private static void ValidateSharedIdentity(Character character, ValidationReport report)
        {
            CharacterId id = character.Identity.CharacterId;

            CheckOwnership(id, character.Class.CharacterId, "class", report);
            CheckOwnership(id, character.Appearance.CharacterId, "appearance", report);
            CheckOwnership(id, character.Progression.CharacterId, "progression", report);
            CheckOwnership(id, character.Stats.CharacterId, "stats", report);
            CheckOwnership(id, character.Resources.CharacterId, "resources", report);
        }

        private static void CheckOwnership(CharacterId expected, CharacterId actual, string part,
            ValidationReport report)
        {
            if (expected != actual)
            {
                report.AddError(ValidationCode.InvalidConfiguration, DefinitionId.None,
                    "The " + part + " state belongs to a different character.");
            }
        }

        private static void ValidateIdentity(Character character, ValidationReport report)
        {
            CharacterState identity = character.Identity;

            if (!identity.CharacterId.IsValid)
            {
                report.AddError(ValidationCode.MissingDefinitionId, DefinitionId.None,
                    "The character has no identity.");
            }

            if (!identity.Owner.IsValid)
            {
                report.AddError(ValidationCode.MissingDefinitionId, DefinitionId.None,
                    "The character has no owner.");
            }

            if (string.IsNullOrWhiteSpace(identity.Name))
            {
                report.AddError(ValidationCode.InvalidConfiguration, DefinitionId.None,
                    "The character has no name.");
            }

            if (identity.Gender == CharacterGender.Unspecified)
            {
                report.AddError(ValidationCode.InvalidConfiguration, DefinitionId.None,
                    "The character has no chosen gender.");
            }
        }

        private void ValidateResources(Character character, CharacterCreationContent content,
            ValidationReport report)
        {
            DerivedStatsResult derived = _calculator.Calculate(
                character.Stats, content.DerivedFormulas, content.Stats, NoModifiers);

            ResourceLimits limits = ResourceLimits.From(
                derived, content.MaxHealthStat, content.MaxManaStat);

            CharacterResourceState resources = character.Resources;

            if (resources.CurrentHealth < 0 || resources.CurrentHealth > limits.MaxHealth)
            {
                report.AddError(ValidationCode.ValueOutOfRange, content.MaxHealthStat,
                    "Health " + resources.CurrentHealth + " is outside 0 to " + limits.MaxHealth + ".");
            }

            if (resources.CurrentMana < 0 || resources.CurrentMana > limits.MaxMana)
            {
                report.AddError(ValidationCode.ValueOutOfRange, content.MaxManaStat,
                    "Mana " + resources.CurrentMana + " is outside 0 to " + limits.MaxMana + ".");
            }
        }

        private static void Merge(ValidationReport source, ValidationReport destination)
        {
            IReadOnlyList<ValidationMessage> messages = source.Messages;

            for (int i = 0; i < messages.Count; i++)
            {
                destination.Add(messages[i]);
            }
        }
    }
}
