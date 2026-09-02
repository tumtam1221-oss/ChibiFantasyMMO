using System;
using System.Collections.Generic;
using ChibiFantasy.Core;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// Checks definitions and reports what is wrong with them.
    /// </summary>
    /// <remarks>
    /// Reports, never repairs. It does not modify assets, auto-fix data, generate missing
    /// identities or substitute references. Silently correcting content hides the authoring
    /// mistake and makes the corrected value the new source of truth, which is far worse
    /// than a failed build.
    ///
    /// Deterministic: definitions are examined in the order given, rules in the order
    /// supplied, so the same input always produces the same report.
    ///
    /// Runtime code with no editor dependency, so a dedicated server can validate a content
    /// set at startup without UnityEditor, without a scene, and without loading any of the
    /// presentation assets an <see cref="AssetRef"/> points at. AssetRef checking stays
    /// structural here for exactly that reason.
    /// </remarks>
    public sealed class DefinitionValidator
    {
        private readonly IDefinitionValidationRule[] _rules;

        public DefinitionValidator()
            : this(null)
        {
        }

        public DefinitionValidator(IEnumerable<IDefinitionValidationRule> rules)
        {
            if (rules == null)
            {
                _rules = new IDefinitionValidationRule[0];
                return;
            }

            var collected = new List<IDefinitionValidationRule>();
            foreach (IDefinitionValidationRule rule in rules)
            {
                if (rule != null)
                {
                    collected.Add(rule);
                }
            }

            _rules = collected.ToArray();
        }

        /// <summary>Validates one definition against a lookup that may be null.</summary>
        public ValidationReport Validate(IDefinition definition, IDefinitionLookup lookup)
        {
            var report = new ValidationReport();
            ValidateOne(definition, lookup, report);
            return report;
        }

        /// <summary>
        /// Validates a set, additionally reporting identities claimed more than once.
        /// </summary>
        /// <remarks>
        /// Duplicate detection is scoped to the set passed in, matching
        /// <see cref="DefinitionRegistry{T}"/>: identities are unique within a scope the
        /// caller chooses, not across the whole project.
        /// </remarks>
        public ValidationReport Validate(IEnumerable<IDefinition> definitions, IDefinitionLookup lookup)
        {
            if (definitions == null)
            {
                throw new ArgumentNullException(nameof(definitions));
            }

            var report = new ValidationReport();
            var seen = new HashSet<DefinitionId>();

            foreach (IDefinition definition in definitions)
            {
                ValidateOne(definition, lookup, report);

                if (ReferenceEquals(definition, null))
                {
                    continue;
                }

                DefinitionId id = definition.Id;

                if (id.IsValid && !seen.Add(id))
                {
                    report.AddError(
                        ValidationCode.DuplicateDefinitionId, id,
                        "More than one definition in this set claims the id '" + id + "'.");
                }
            }

            return report;
        }

        private void ValidateOne(IDefinition definition, IDefinitionLookup lookup, ValidationReport report)
        {
            if (ReferenceEquals(definition, null))
            {
                report.AddError(
                    ValidationCode.NullDefinition, DefinitionId.None,
                    "A null entry was supplied where a definition was expected.");
                return;
            }

            DefinitionId id = definition.Id;

            if (!id.IsValid)
            {
                report.AddError(
                    ValidationCode.MissingDefinitionId, DefinitionId.None,
                    definition.GetType().Name + " has no usable id.");
            }

            ValidateReferences(definition, lookup, report, id);

            for (int i = 0; i < _rules.Length; i++)
            {
                _rules[i].Validate(definition, lookup, report);
            }
        }

        private static void ValidateReferences(IDefinition definition, IDefinitionLookup lookup,
            ValidationReport report, DefinitionId owningId)
        {
            var referencing = definition as IReferencingDefinition;

            if (referencing == null || lookup == null)
            {
                return;
            }

            IEnumerable<DefinitionId> required = referencing.GetRequiredReferences();

            if (required == null)
            {
                return;
            }

            foreach (DefinitionId reference in required)
            {
                if (!reference.IsValid)
                {
                    report.AddError(
                        ValidationCode.MissingReference, owningId,
                        "Declares a required reference that was never set.");
                    continue;
                }

                if (!lookup.Contains(reference))
                {
                    report.AddError(
                        ValidationCode.MissingReference, owningId,
                        "References '" + reference + "', which does not resolve.");
                }
            }
        }
    }
}
