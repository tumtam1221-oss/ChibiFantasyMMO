using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// Turns an authored effect amount into a number.
    /// </summary>
    /// <remarks>
    /// <b>It evaluates the existing schema, it does not invent one.</b>
    /// <see cref="SkillEffect"/> already describes an amount as a flat integer plus a sum
    /// of <see cref="StatTerm"/> scaling, and this reads exactly that. No expression
    /// language, no second formula shape and no new authoring concept appears here.
    ///
    /// <b>The arithmetic deliberately matches <see cref="DerivedStatsCalculator"/>.</b>
    /// Same integer form, same <c>source * numerator / denominator</c> per term, same
    /// accumulation in <c>long</c>. That is a shared convention rather than shared code
    /// because the two read different inputs -- a derived-stat formula against
    /// <c>CharacterStatsState</c>, an effect against an <see cref="ICombatant"/> -- and
    /// forcing one method to serve both would drag character-only types into the combat
    /// contract that monsters implement.
    ///
    /// <b>Stats come from the combatant.</b> Reading through
    /// <see cref="ICombatant.TryGetCombatStat"/> rather than a character's stat state is
    /// what lets a monster's skill scale off its own stats with no special case.
    ///
    /// Integers throughout, so no NaN and no infinity can exist, and a
    /// <see cref="StatTerm"/> with a non-positive denominator is skipped rather than
    /// throwing mid-fight; content validation is where that fault belongs.
    /// </remarks>
    public static class SkillAmountCalculator
    {
        /// <summary>
        /// Computes flat plus scaling for one effect.
        /// </summary>
        /// <param name="effect">The authored effect.</param>
        /// <param name="caster">Whose stats the scaling reads. Null yields the flat amount alone.</param>
        /// <returns>The amount, clamped into int range. May be negative if authored so.</returns>
        public static int Calculate(in SkillEffect effect, ICombatant caster)
        {
            long value = effect.FlatAmount;

            StatTerm[] terms = effect.Scaling;

            if (terms != null && caster != null)
            {
                for (int i = 0; i < terms.Length; i++)
                {
                    StatTerm term = terms[i];

                    // Content validation rejects this; a fight must not throw over it.
                    if (term.Denominator <= 0) continue;

                    if (!caster.TryGetCombatStat(term.Source, out int source)) continue;

                    value += (long)source * term.Numerator / term.Denominator;
                }
            }

            if (value > int.MaxValue) return int.MaxValue;
            if (value < int.MinValue) return int.MinValue;

            return (int)value;
        }

        /// <summary>The amount as a non-negative magnitude, for effects that cannot be negative.</summary>
        public static int CalculateMagnitude(in SkillEffect effect, ICombatant caster)
        {
            int value = Calculate(effect, caster);
            return value < 0 ? 0 : value;
        }
    }
}
