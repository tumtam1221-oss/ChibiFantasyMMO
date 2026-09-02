using System;
using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// Turns a character's base stats and a set of modifiers into derived stats.
    /// </summary>
    /// <remarks>
    /// <b>Pure and explicit.</b> Every input is a parameter. It reaches into no registry,
    /// singleton, scene, network or database, so the same inputs always produce the same
    /// output and a test needs no world to run. It mutates nothing it is given: the base
    /// stats it reads come back untouched, revision included.
    ///
    /// <b>Order, fixed and documented.</b> For each formula:
    /// constant, then terms in authored order, then flat modifiers, then percent
    /// modifiers, then the clamp from the stat's own definition. Percent modifiers are
    /// additive with each other, not multiplicative: two twenty percent bonuses give forty,
    /// not forty-four. That follows from the existing two-kind
    /// <see cref="StatModifierKind"/>, which has no multiplicative kind, and a third kind
    /// was not invented for a stacking rule the game has not chosen.
    ///
    /// <b>Determinism.</b> All arithmetic is integer. Float appears only where
    /// <see cref="StatModifier"/> stores its value, and is converted once per modifier into
    /// whole units or basis points before anything is combined. Every division truncates
    /// toward zero. Accumulation is in long and the final clamp brings the value back into
    /// int range, so no intermediate can wrap.
    ///
    /// <b>Missing inputs.</b> A base stat the character has no entry for reads as zero,
    /// which is what a character with no recorded strength has. A formula naming an unknown
    /// stat produces nothing at all rather than a fabricated value, so the absence is
    /// visible; content faults of that kind are reported by
    /// <see cref="DerivedStatFormulaValidationRule"/> before they get here.
    /// </remarks>
    public sealed class DerivedStatsCalculator
    {
        private const long PercentScale = 10000;

        /// <summary>
        /// Computes every derived stat the supplied formulas describe.
        /// </summary>
        /// <param name="baseStats">The character's persisted attributes. Not modified.</param>
        /// <param name="formulas">Formulas to evaluate, in order.</param>
        /// <param name="statDefinitions">Stat content, used for clamps and identity.</param>
        /// <param name="modifiers">Contributions from equipment, cards, pets, buffs and
        /// anything else. May be empty; no such system exists yet.</param>
        public DerivedStatsResult Calculate(
            CharacterStatsState baseStats,
            IReadOnlyList<DerivedStatFormulaDefinition> formulas,
            IDefinitionRegistry<StatDefinition> statDefinitions,
            IReadOnlyList<StatModifier> modifiers)
        {
            if (baseStats == null)
            {
                throw new ArgumentNullException(nameof(baseStats));
            }

            if (formulas == null)
            {
                throw new ArgumentNullException(nameof(formulas));
            }

            if (statDefinitions == null)
            {
                throw new ArgumentNullException(nameof(statDefinitions));
            }

            var computed = new List<CharacterStatEntry>(formulas.Count);

            for (int i = 0; i < formulas.Count; i++)
            {
                DerivedStatFormulaDefinition formula = formulas[i];

                if (formula == null)
                {
                    continue;
                }

                DefinitionId target = formula.DerivedStat;

                // An unknown stat yields nothing rather than an invented value.
                if (!target.IsValid || !statDefinitions.TryGet(target, out StatDefinition definition))
                {
                    continue;
                }

                computed.Add(new CharacterStatEntry(
                    target, Evaluate(formula, definition, baseStats, modifiers)));
            }

            return new DerivedStatsResult(baseStats.CharacterId, computed);
        }

        private static int Evaluate(DerivedStatFormulaDefinition formula, StatDefinition definition,
            CharacterStatsState baseStats, IReadOnlyList<StatModifier> modifiers)
        {
            long value = formula.Constant;

            StatTerm[] terms = formula.Terms;

            for (int i = 0; i < terms.Length; i++)
            {
                StatTerm term = terms[i];

                if (term.Denominator <= 0)
                {
                    throw new InvalidOperationException(
                        "Formula '" + formula.Id + "' has a term with denominator "
                        + term.Denominator + "; validation should have rejected it.");
                }

                long source = baseStats.GetOrDefault(term.Source, 0);
                value += source * term.Numerator / term.Denominator;
            }

            value += SumFlat(formula.DerivedStat, modifiers);

            long percent = SumPercentBasisPoints(formula.DerivedStat, modifiers);

            if (percent != 0)
            {
                value = value * (PercentScale + percent) / PercentScale;
            }

            return Clamp(value, definition);
        }

        private static long SumFlat(DefinitionId stat, IReadOnlyList<StatModifier> modifiers)
        {
            if (modifiers == null)
            {
                return 0;
            }

            long total = 0;

            for (int i = 0; i < modifiers.Count; i++)
            {
                StatModifier modifier = modifiers[i];

                if (modifier.Kind == StatModifierKind.Flat && modifier.Stat == stat)
                {
                    total += (long)modifier.Value;
                }
            }

            return total;
        }

        private static long SumPercentBasisPoints(DefinitionId stat, IReadOnlyList<StatModifier> modifiers)
        {
            if (modifiers == null)
            {
                return 0;
            }

            long total = 0;

            for (int i = 0; i < modifiers.Count; i++)
            {
                StatModifier modifier = modifiers[i];

                if (modifier.Kind == StatModifierKind.Percent && modifier.Stat == stat)
                {
                    // One conversion per modifier, then integers the rest of the way.
                    total += (long)(modifier.Value * PercentScale);
                }
            }

            return total;
        }

        private static int Clamp(long value, StatDefinition definition)
        {
            long min = definition.MinValue;
            long max = definition.MaxValue;

            if (max < min)
            {
                // A definition this broken admits no legal value; the rule reports it.
                max = min;
            }

            if (value < min)
            {
                return (int)min;
            }

            return value > max ? (int)max : (int)value;
        }
    }
}
