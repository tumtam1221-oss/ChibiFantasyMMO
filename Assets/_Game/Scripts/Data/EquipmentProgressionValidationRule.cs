using System.Collections.Generic;
using ChibiFantasy.Core;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// Checks that authored equipment progression is coherent.
    /// </summary>
    /// <remarks>
    /// <b>Why content validation and not just service rejections.</b> The services already
    /// refuse malformed rules at runtime, but a player discovering that a sword cannot be
    /// enhanced past +3 because level 4's step is missing is the wrong place to find out.
    /// These checks fail in the content pass, pointing at the row somebody typed wrong.
    ///
    /// Plugs into the existing <see cref="DefinitionValidator"/> through
    /// <see cref="IDefinitionValidationRule"/>, so a malformed enhancement track is caught
    /// alongside every other content problem rather than by a separate tool.
    ///
    /// The line between error and warning is whether the content is <em>wrong</em> or
    /// merely <em>inert</em>: a step with a negative cost is an error, a track nothing
    /// references is not this rule's business at all.
    /// </remarks>
    public sealed class EquipmentProgressionValidationRule : IDefinitionValidationRule
    {
        public void Validate(IDefinition definition, IDefinitionLookup lookup,
            ValidationReport report)
        {
            var enhancement = definition as EnhancementDefinition;
            if (enhancement != null)
            {
                ValidateEnhancement(enhancement, lookup, report);
                return;
            }

            var equipment = definition as EquipmentDefinition;
            if (equipment != null)
            {
                ValidateEquipment(equipment, report);
                return;
            }

            var rarity = definition as RarityDefinition;
            if (rarity != null)
            {
                ValidateRarity(rarity, report);
                return;
            }

            var item = definition as ItemDefinition;
            if (item != null) ValidateStone(item, report);
        }

        // ---- enhancement ---------------------------------------------------------------

        private static void ValidateEnhancement(EnhancementDefinition rule,
            IDefinitionLookup lookup, ValidationReport report)
        {
            if (rule.MaxLevel < rule.MinLevel)
            {
                report.AddError(ValidationCode.InvalidConfiguration, rule.Id,
                    "Maximum level " + rule.MaxLevel + " is below minimum level "
                    + rule.MinLevel + ", so no level is reachable.");
            }

            EnhancementStep[] steps = rule.Steps;
            if (steps == null || steps.Length == 0)
            {
                report.AddWarning(ValidationCode.InvalidConfiguration, rule.Id,
                    "The track authors no steps, so nothing using it can ever be enhanced.");
                return;
            }

            var seen = new HashSet<int>();

            for (int i = 0; i < steps.Length; i++)
            {
                EnhancementStep step = steps[i];

                if (!seen.Add(step.FromLevel))
                {
                    report.AddError(ValidationCode.InvalidConfiguration, rule.Id,
                        "Two steps advance from level " + step.FromLevel
                        + "; only one can ever be used and which is undefined.");
                }

                if (step.FromLevel < 0)
                {
                    report.AddError(ValidationCode.ValueOutOfRange, rule.Id,
                        "A step advances from negative level " + step.FromLevel + ".");
                }

                if (step.SuccessChance < 0f || step.SuccessChance > 1f)
                {
                    report.AddError(ValidationCode.ValueOutOfRange, rule.Id,
                        "Step from level " + step.FromLevel + " has success chance "
                        + step.SuccessChance + ", which is outside zero to one.");
                }

                if (step.MaterialAmount < 0)
                {
                    report.AddError(ValidationCode.ValueOutOfRange, rule.Id,
                        "Step from level " + step.FromLevel + " requires a negative amount of material.");
                }

                if (step.CurrencyCost < 0)
                {
                    report.AddError(ValidationCode.ValueOutOfRange, rule.Id,
                        "Step from level " + step.FromLevel + " has a negative currency cost.");
                }

                if (step.MaterialAmount > 0 && !step.MaterialItem.IsValid)
                {
                    report.AddError(ValidationCode.InvalidConfiguration, rule.Id,
                        "Step from level " + step.FromLevel
                        + " requires " + step.MaterialAmount + " of an unnamed material.");
                }

                if (step.MaterialItem.IsValid && step.MaterialAmount <= 0)
                {
                    report.AddWarning(ValidationCode.InvalidConfiguration, rule.Id,
                        "Step from level " + step.FromLevel
                        + " names a material but requires none of it.");
                }

                // A downgrade at level zero has nowhere to go. The service refuses it
                // rather than clamping, so a player would simply be unable to enhance.
                if (step.FailureBehavior == EnhancementFailureBehavior.DegradeLevel
                    && step.FromLevel <= 0)
                {
                    report.AddError(ValidationCode.InvalidConfiguration, rule.Id,
                        "Step from level 0 degrades on failure, but there is no level below zero.");
                }

                if (step.MaterialItem.IsValid) RequireItem(lookup, rule.Id, step.MaterialItem,
                    "Step from level " + step.FromLevel + " material", report);
            }

            if (rule.CurrencyItem.IsValid)
            {
                RequireItem(lookup, rule.Id, rule.CurrencyItem, "Currency item", report);
            }
            else
            {
                for (int i = 0; i < steps.Length; i++)
                {
                    if (steps[i].CurrencyCost <= 0) continue;

                    report.AddError(ValidationCode.InvalidConfiguration, rule.Id,
                        "Step from level " + steps[i].FromLevel
                        + " charges currency, but the track names no currency item.");
                    break;
                }
            }

            // The gap check: every level from the minimum to one below the maximum needs a
            // step, or enhancement stops dead at the first missing one.
            for (int level = rule.MinLevel; level < rule.MaxLevel; level++)
            {
                if (seen.Contains(level)) continue;

                report.AddError(ValidationCode.InvalidConfiguration, rule.Id,
                    "No step advances from level " + level
                    + ", so enhancement cannot pass it even though the maximum is "
                    + rule.MaxLevel + ".");
            }
        }

        // ---- equipment -----------------------------------------------------------------

        private static void ValidateEquipment(EquipmentDefinition equipment,
            ValidationReport report)
        {
            if (equipment.MaxEnhancementLevel < 0)
            {
                report.AddError(ValidationCode.ValueOutOfRange, equipment.Id,
                    "Maximum enhancement level is negative.");
            }

            if (equipment.StatusStoneSlots < 0)
            {
                report.AddError(ValidationCode.ValueOutOfRange, equipment.Id,
                    "Status stone slot count is negative.");
            }

            if (equipment.Enhanceable && !equipment.EnhancementRule.IsValid)
            {
                report.AddError(ValidationCode.InvalidConfiguration, equipment.Id,
                    "Marked enhanceable but names no enhancement track, so it can never be enhanced.");
            }

            if (!equipment.Enhanceable && equipment.MaxEnhancementLevel > 0)
            {
                report.AddWarning(ValidationCode.InvalidConfiguration, equipment.Id,
                    "Not enhanceable, but authors a maximum enhancement level of "
                    + equipment.MaxEnhancementLevel + ".");
            }
        }

        // ---- rarity --------------------------------------------------------------------

        private static void ValidateRarity(RarityDefinition rarity, ValidationReport report)
        {
            if (rarity.BonusEnchantSlots < 0)
            {
                report.AddError(ValidationCode.ValueOutOfRange, rarity.Id,
                    "Bonus enchant slots is negative; a tier may only widen a piece.");
            }

            if (rarity.MaxEnhancementLevel < 0)
            {
                report.AddError(ValidationCode.ValueOutOfRange, rarity.Id,
                    "Maximum enhancement level is negative.");
            }
        }

        // ---- status stones -------------------------------------------------------------

        private static void ValidateStone(ItemDefinition item, ValidationReport report)
        {
            if (!item.IsStatusStone) return;

            StatusStoneConfig config = item.StoneConfig;

            if (config.SuccessChance < 0f || config.SuccessChance > 1f)
            {
                report.AddError(ValidationCode.ValueOutOfRange, item.Id,
                    "Socketing chance " + config.SuccessChance + " is outside zero to one.");
            }

            if (config.MinimumItemLevel < 0)
            {
                report.AddError(ValidationCode.ValueOutOfRange, item.Id,
                    "Minimum item level is negative.");
            }

            if (config.StatModifiers.Length == 0)
            {
                report.AddWarning(ValidationCode.InvalidConfiguration, item.Id,
                    "A status stone that grants no modifiers does nothing when socketed.");
            }
        }

        private static void RequireItem(IDefinitionLookup lookup, DefinitionId owner,
            DefinitionId reference, string what, ValidationReport report)
        {
            if (lookup == null || !reference.IsValid) return;

            if (lookup.Contains(reference)) return;

            report.AddError(ValidationCode.MissingReference, owner,
                what + " '" + reference + "' does not resolve to any definition.");
        }
    }
}
