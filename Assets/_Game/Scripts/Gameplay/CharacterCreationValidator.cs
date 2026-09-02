using System;
using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// Checks a set of creation choices before anything is built from them.
    /// </summary>
    /// <remarks>
    /// Pure: it reads the input and the content and changes neither, so a creation screen
    /// can ask why a name or a class is refused before the player commits.
    ///
    /// Reuses the 04.6 report and codes rather than adding a framework, and reuses the
    /// gender rule already written for appearance so class availability and option
    /// availability cannot answer the same question differently.
    /// </remarks>
    public sealed class CharacterCreationValidator
    {
        /// <summary>Validates an input, reporting every fault rather than the first.</summary>
        public ValidationReport Validate(CharacterCreationInput input, CharacterCreationContent content)
        {
            return Validate(input, content, out _);
        }

        internal ValidationReport Validate(CharacterCreationInput input,
            CharacterCreationContent content, out ClassDefinition startingClass)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            var report = new ValidationReport();
            startingClass = null;

            if (!input.Owner.IsValid)
            {
                report.AddError(ValidationCode.MissingDefinitionId, DefinitionId.None,
                    "A character must belong to an owner.");
            }

            if (string.IsNullOrWhiteSpace(input.Name))
            {
                report.AddError(ValidationCode.InvalidConfiguration, DefinitionId.None,
                    "A character requires a name.");
            }

            if (input.Gender == CharacterGender.Unspecified)
            {
                report.AddError(ValidationCode.InvalidConfiguration, DefinitionId.None,
                    "A character requires a chosen gender.");
            }

            ValidateClass(input, content, report, out startingClass);
            ValidateAppearance(input, content, report);

            return report;
        }

        private static void ValidateClass(CharacterCreationInput input,
            CharacterCreationContent content, ValidationReport report, out ClassDefinition startingClass)
        {
            startingClass = null;

            if (!input.StartingClass.IsValid)
            {
                report.AddError(ValidationCode.MissingDefinitionId, DefinitionId.None,
                    "A starting class must be chosen.");
                return;
            }

            if (!content.Classes.TryGet(input.StartingClass, out startingClass))
            {
                report.AddError(ValidationCode.MissingReference, input.StartingClass,
                    "The starting class does not exist.");
                return;
            }

            if (!CharacterAppearanceValidator.IsAllowedFor(startingClass.GenderAvailability, input.Gender))
            {
                report.AddError(ValidationCode.GenderIncompatible, input.StartingClass,
                    "The class is restricted to " + startingClass.GenderAvailability
                    + " and this character is " + input.Gender + ".");
            }
        }

        private static void ValidateAppearance(CharacterCreationInput input,
            CharacterCreationContent content, ValidationReport report)
        {
            IReadOnlyList<AppearanceChoice> choices = input.Appearance;

            for (int i = 0; i < choices.Count; i++)
            {
                AppearanceChoice choice = choices[i];

                if (choice.Slot == AppearanceSlot.None)
                {
                    report.AddError(ValidationCode.InvalidConfiguration, choice.Option,
                        "An appearance choice names no slot.");
                    continue;
                }

                if (!content.AppearanceOptions.TryGet(choice.Option, out AppearanceOptionDefinition option))
                {
                    report.AddError(ValidationCode.MissingReference, choice.Option,
                        "The " + choice.Slot + " option does not exist.");
                    continue;
                }

                if (option.Slot != choice.Slot)
                {
                    report.AddError(ValidationCode.SlotMismatch, choice.Option,
                        "Chosen for " + choice.Slot + " but authored as " + option.Slot + ".");
                }

                if (!CharacterAppearanceValidator.IsAllowedFor(option.GenderAvailability, input.Gender))
                {
                    report.AddError(ValidationCode.GenderIncompatible, choice.Option,
                        "The " + choice.Slot + " option is restricted to "
                        + option.GenderAvailability + ".");
                }
            }
        }
    }
}
