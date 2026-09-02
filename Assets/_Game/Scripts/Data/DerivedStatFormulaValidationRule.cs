using System;
using ChibiFantasy.Core;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// Checks that a derived-stat formula is well formed and cannot create a cycle.
    /// </summary>
    /// <remarks>
    /// Plugs into the existing <see cref="DefinitionValidator"/>, so a malformed formula is
    /// reported in the same pass as every other content fault.
    ///
    /// Enforced:
    /// <list type="bullet">
    /// <item>the formula names a stat, and that stat exists;</item>
    /// <item>the produced stat is not primary, because a formula must not overwrite an
    /// attribute a character actually stores;</item>
    /// <item>every source exists and is primary, which is what keeps the dependency graph
    /// one level deep and acyclic;</item>
    /// <item>every denominator is positive, so no term divides by zero.</item>
    /// </list>
    ///
    /// Reports, never repairs.
    /// </remarks>
    public sealed class DerivedStatFormulaValidationRule : IDefinitionValidationRule
    {
        private readonly IDefinitionRegistry<StatDefinition> _stats;

        /// <param name="stats">Stat content, needed to tell primary from derived. Passed in
        /// rather than looked up, so the rule reaches into no global state.</param>
        public DerivedStatFormulaValidationRule(IDefinitionRegistry<StatDefinition> stats)
        {
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        }

        public void Validate(IDefinition definition, IDefinitionLookup lookup, ValidationReport report)
        {
            var formula = definition as DerivedStatFormulaDefinition;

            if (formula == null)
            {
                return;
            }

            DefinitionId id = formula.Id;

            ValidateTarget(formula, id, report);

            StatTerm[] terms = formula.Terms;

            for (int i = 0; i < terms.Length; i++)
            {
                ValidateTerm(terms[i], id, report);
            }
        }

        private void ValidateTarget(DerivedStatFormulaDefinition formula, DefinitionId id,
            ValidationReport report)
        {
            DefinitionId target = formula.DerivedStat;

            if (!target.IsValid)
            {
                report.AddError(ValidationCode.MissingDefinitionId, id,
                    "The formula does not say which stat it produces.");
                return;
            }

            if (!_stats.TryGet(target, out StatDefinition definition))
            {
                report.AddError(ValidationCode.MissingReference, id,
                    "Produces '" + target + "', which is not a known stat.");
                return;
            }

            if (definition.IsPrimary)
            {
                report.AddError(ValidationCode.InvalidConfiguration, id,
                    "Produces '" + target
                    + "', which is a primary attribute a character stores directly.");
            }
        }

        private void ValidateTerm(StatTerm term, DefinitionId id, ValidationReport report)
        {
            if (term.Denominator <= 0)
            {
                report.AddError(ValidationCode.InvalidConfiguration, id,
                    "A term has denominator " + term.Denominator + "; it must be positive.");
            }

            if (!term.Source.IsValid)
            {
                report.AddError(ValidationCode.MissingDefinitionId, id,
                    "A term does not name a source stat.");
                return;
            }

            if (!_stats.TryGet(term.Source, out StatDefinition source))
            {
                report.AddError(ValidationCode.MissingReference, id,
                    "Reads '" + term.Source + "', which is not a known stat.");
                return;
            }

            if (!source.IsPrimary)
            {
                report.AddError(ValidationCode.InvalidConfiguration, id,
                    "Reads '" + term.Source
                    + "', which is itself derived. Formulas may only read primary attributes.");
            }
        }
    }
}
