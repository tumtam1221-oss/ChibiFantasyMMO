using System;
using ChibiFantasy.Core;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// Checks that a character's appearance selections point at real, correctly categorised
    /// options their gender is allowed to use.
    /// </summary>
    /// <remarks>
    /// <b>This is where cross-category safety actually lives.</b> Because every reference in
    /// this project is an untyped <see cref="DefinitionId"/>, nothing stops a hair id being
    /// written into the face slot at compile time. Rather than pretend otherwise with five
    /// near-identical definition classes, the guarantee is enforced here against the
    /// content registry, where a mismatch can genuinely be detected.
    ///
    /// Reuses the existing validation vocabulary from 04.6 rather than introducing a second
    /// framework: the same <see cref="ValidationReport"/>, severities and codes, so
    /// appearance problems appear alongside every other content problem in one report.
    ///
    /// Reports, never repairs. It does not substitute a default option for a broken
    /// selection; silently rewriting a player's appearance would hide the fault and make
    /// the substitution the new truth.
    ///
    /// Deterministic: slots are examined in a fixed order, so the same input always yields
    /// the same report.
    /// </remarks>
    public sealed class CharacterAppearanceValidator
    {
        /// <summary>Slots examined, in report order.</summary>
        private static readonly AppearanceSlot[] Slots =
        {
            AppearanceSlot.Face,
            AppearanceSlot.Eyes,
            AppearanceSlot.Hair,
            AppearanceSlot.HairColor,
            AppearanceSlot.SkinTone
        };

        private readonly bool _requireAllSlots;

        /// <summary>Creates a validator that requires every slot to be filled.</summary>
        public CharacterAppearanceValidator()
            : this(true)
        {
        }

        /// <param name="requireAllSlots">False while a character is still being created and
        /// selections are legitimately incomplete.</param>
        public CharacterAppearanceValidator(bool requireAllSlots)
        {
            _requireAllSlots = requireAllSlots;
        }

        /// <summary>
        /// Validates every selection against the supplied appearance content.
        /// </summary>
        /// <param name="appearance">The selections to check.</param>
        /// <param name="options">Registry of authored appearance options.</param>
        /// <param name="gender">The character's gender, taken from
        /// <see cref="CharacterState"/> rather than stored on the appearance, so there is
        /// only ever one source of truth for it.</param>
        public ValidationReport Validate(CharacterAppearanceState appearance,
            IDefinitionRegistry<AppearanceOptionDefinition> options, CharacterGender gender)
        {
            if (appearance == null)
            {
                throw new ArgumentNullException(nameof(appearance));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            var report = new ValidationReport();

            for (int i = 0; i < Slots.Length; i++)
            {
                ValidateSlot(appearance, options, gender, Slots[i], report);
            }

            return report;
        }

        private void ValidateSlot(CharacterAppearanceState appearance,
            IDefinitionRegistry<AppearanceOptionDefinition> options, CharacterGender gender,
            AppearanceSlot slot, ValidationReport report)
        {
            DefinitionId selected = appearance.Get(slot);

            if (!selected.IsValid)
            {
                if (_requireAllSlots)
                {
                    report.AddError(
                        ValidationCode.MissingDefinitionId, DefinitionId.None,
                        "No " + slot + " option is selected.");
                }

                return;
            }

            if (!options.TryGet(selected, out AppearanceOptionDefinition option))
            {
                report.AddError(
                    ValidationCode.MissingReference, selected,
                    "The " + slot + " selection does not resolve to an appearance option.");
                return;
            }

            if (option.Slot != slot)
            {
                report.AddError(
                    ValidationCode.SlotMismatch, selected,
                    "Selected for " + slot + " but authored as " + option.Slot + ".");
            }

            if (!IsAllowedFor(option.GenderAvailability, gender))
            {
                report.AddError(
                    ValidationCode.GenderIncompatible, selected,
                    "The " + slot + " option is restricted to " + option.GenderAvailability
                    + " and this character is " + gender + ".");
            }
        }

        /// <summary>
        /// Whether a character of the given gender may use content with the given
        /// availability.
        /// </summary>
        /// <remarks>Unspecified matches nothing but Any: a character with no chosen gender
        /// cannot satisfy a gender-restricted option.</remarks>
        public static bool IsAllowedFor(GenderAvailability availability, CharacterGender gender)
        {
            switch (availability)
            {
                case GenderAvailability.Any:
                    return true;
                case GenderAvailability.MaleOnly:
                    return gender == CharacterGender.Male;
                case GenderAvailability.FemaleOnly:
                    return gender == CharacterGender.Female;
                default:
                    return false;
            }
        }
    }
}
