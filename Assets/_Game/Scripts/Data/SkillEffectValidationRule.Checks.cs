using ChibiFantasy.Core;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// The per-kind requirements of a skill effect.
    /// </summary>
    /// <remarks>
    /// Each kind needs different data, and these say which. An amount-bearing effect must
    /// actually specify an amount, by flat value or by scaling; a resource effect must name
    /// a pool; a status application must name a status; a stat change must name a stat.
    ///
    /// Amounts are required to be non-negative because direction belongs to the kind, not
    /// the number. Damage that heals because someone authored a negative would be a bug
    /// nobody sees until it is live.
    /// </remarks>
    public sealed partial class SkillEffectValidationRule
    {
        private static void ValidateAmount(SkillEffect effect, DefinitionId id, string where,
            string what, ValidationReport report)
        {
            bool hasScaling = effect.Scaling != null && effect.Scaling.Length > 0;

            if (effect.FlatAmount == 0 && !hasScaling)
            {
                report.AddError(ValidationCode.InvalidConfiguration, id,
                    where + "a " + what + " effect has neither a flat amount nor scaling.");
            }

            if (effect.FlatAmount < 0)
            {
                report.AddError(ValidationCode.ValueOutOfRange, id,
                    where + "a " + what + " effect has a negative amount; direction belongs "
                    + "to the kind, not the number.");
            }
        }

        private static void ValidateResource(SkillEffect effect, DefinitionId id, string where,
            string what, ValidationReport report)
        {
            if (effect.Resource == SkillResourceType.None)
            {
                report.AddError(ValidationCode.InvalidConfiguration, id,
                    where + "a " + what + " effect names no resource.");
            }
        }

        private void ValidateScaling(SkillEffect effect, DefinitionId id, string where,
            ValidationReport report)
        {
            StatTerm[] scaling = effect.Scaling;

            if (scaling == null)
            {
                return;
            }

            for (int i = 0; i < scaling.Length; i++)
            {
                StatTerm term = scaling[i];

                if (term.Denominator <= 0)
                {
                    report.AddError(ValidationCode.InvalidConfiguration, id,
                        where + "a scaling term has denominator " + term.Denominator
                        + "; it must be positive.");
                }

                if (!term.Source.IsValid)
                {
                    report.AddError(ValidationCode.MissingDefinitionId, id,
                        where + "a scaling term names no stat.");
                    continue;
                }

                if (!_stats.Contains(term.Source))
                {
                    report.AddError(ValidationCode.MissingReference, id,
                        where + "scales from '" + term.Source + "', which is not a known stat.");
                }
            }
        }

        private void ValidateStatusEffect(SkillEffect effect, DefinitionId id, string where,
            ValidationReport report)
        {
            if (!effect.Reference.IsValid)
            {
                report.AddError(ValidationCode.MissingDefinitionId, id,
                    where + "a status application names no status effect.");
                return;
            }

            if (!_statusEffects.Contains(effect.Reference))
            {
                report.AddError(ValidationCode.MissingReference, id,
                    where + "applies '" + effect.Reference
                    + "', which is not a known status effect.");
            }
        }

        private void ValidateStat(SkillEffect effect, DefinitionId id, string where,
            ValidationReport report)
        {
            DefinitionId stat = effect.StatModifier.Stat;

            if (!stat.IsValid)
            {
                report.AddError(ValidationCode.MissingDefinitionId, id,
                    where + "a stat modification names no stat.");
                return;
            }

            if (!_stats.Contains(stat))
            {
                report.AddError(ValidationCode.MissingReference, id,
                    where + "modifies '" + stat + "', which is not a known stat.");
            }
        }
    }
}
