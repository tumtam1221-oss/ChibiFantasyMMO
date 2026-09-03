using System.Collections.Generic;
using ChibiFantasy.Core;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// Checks that authored fusion recipes are coherent.
    /// </summary>
    /// <remarks>
    /// A recipe is a promise about a player's items, so the checks here are about the ways
    /// a typo could take something and give nothing back: no inputs, an output that does
    /// not exist, a cost with no currency named.
    ///
    /// One check is deliberately absent. Whether the result is "better" than the inputs is
    /// a balance question, and nothing in the schema knows what better means -- there is no
    /// stone tier and no rank arithmetic anywhere in this system, on purpose. Inventing a
    /// heuristic here would be the code learning about grades through the back door.
    /// </remarks>
    public sealed class StoneFusionValidationRule : IDefinitionValidationRule
    {
        public void Validate(IDefinition definition, IDefinitionLookup lookup,
            ValidationReport report)
        {
            var recipe = definition as StoneFusionDefinition;
            if (recipe == null) return;

            FusionIngredient[] inputs = recipe.Inputs;

            if (inputs.Length == 0)
            {
                report.AddError(ValidationCode.InvalidConfiguration, recipe.Id,
                    "The recipe consumes nothing, so it would create something from nothing.");
            }

            var seen = new HashSet<string>();

            for (int i = 0; i < inputs.Length; i++)
            {
                FusionIngredient input = inputs[i];

                if (!input.Item.IsValid)
                {
                    report.AddError(ValidationCode.InvalidConfiguration, recipe.Id,
                        "Input " + i + " names no item.");
                    continue;
                }

                if (input.Quantity <= 0)
                {
                    report.AddError(ValidationCode.ValueOutOfRange, recipe.Id,
                        "Input '" + input.Item + "' requires " + input.Quantity
                        + ", which cannot be consumed.");
                }

                // Listing an item twice is legal -- both rows are real demands and the
                // service adds them up -- but it is far more often a mistake.
                if (!seen.Add(input.Item.ToString()))
                {
                    report.AddWarning(ValidationCode.InvalidConfiguration, recipe.Id,
                        "Input '" + input.Item + "' is listed more than once; the quantities add up.");
                }

                Require(lookup, recipe.Id, input.Item, "Input", report);
            }

            if (!recipe.Result.IsValid)
            {
                report.AddError(ValidationCode.InvalidConfiguration, recipe.Id,
                    "The recipe names no result, so a successful fusion would produce nothing.");
            }
            else
            {
                Require(lookup, recipe.Id, recipe.Result, "Result", report);
            }

            if (recipe.SuccessChance < 0f || recipe.SuccessChance > 1f)
            {
                report.AddError(ValidationCode.ValueOutOfRange, recipe.Id,
                    "Success chance " + recipe.SuccessChance + " is outside zero to one.");
            }

            if (recipe.CurrencyCost < 0)
            {
                report.AddError(ValidationCode.ValueOutOfRange, recipe.Id,
                    "Currency cost is negative.");
            }

            if (recipe.CurrencyCost > 0 && !recipe.CurrencyItem.IsValid)
            {
                report.AddError(ValidationCode.InvalidConfiguration, recipe.Id,
                    "The recipe charges currency but names no currency item.");
            }

            if (recipe.CurrencyItem.IsValid)
            {
                Require(lookup, recipe.Id, recipe.CurrencyItem, "Currency item", report);
            }

            if (recipe.FailureResult.IsValid)
            {
                Require(lookup, recipe.Id, recipe.FailureResult, "Failure result", report);
            }

            // A recipe that always succeeds has no use for a consolation prize, and
            // authoring one usually means the odds were forgotten.
            if (recipe.SuccessChance <= 0f && recipe.FailureResult.IsValid)
            {
                report.AddWarning(ValidationCode.InvalidConfiguration, recipe.Id,
                    "A failure result is authored, but the recipe always succeeds.");
            }
        }

        private static void Require(IDefinitionLookup lookup, DefinitionId owner,
            DefinitionId reference, string what, ValidationReport report)
        {
            if (lookup == null || !reference.IsValid) return;
            if (lookup.Contains(reference)) return;

            report.AddError(ValidationCode.MissingReference, owner,
                what + " '" + reference + "' does not resolve to any definition.");
        }
    }
}
