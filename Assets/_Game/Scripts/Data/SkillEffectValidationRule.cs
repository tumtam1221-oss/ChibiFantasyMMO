using System;
using ChibiFantasy.Core;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// Checks that a skill's effects carry the data their kind requires and point only at
    /// content that exists.
    /// </summary>
    /// <remarks>
    /// A separate rule from <see cref="SkillValidationRule"/> rather than an extension of
    /// it, because effect checking needs stat and status content that skill structure does
    /// not. Keeping them apart left the 06.1 rule untouched and lets either run alone.
    ///
    /// <b>Structure only.</b> It asks whether an effect is describable, never whether using
    /// it would be legal: nothing here considers a target's health, range, resistance,
    /// immunity, cost affordability or cooldown. Those are runtime questions for a combat
    /// system that does not exist, and answering them here would be guesswork.
    ///
    /// Reports, never repairs. Deterministic: levels and effects are read in authored order.
    /// </remarks>
    public sealed partial class SkillEffectValidationRule : IDefinitionValidationRule
    {
        private readonly IDefinitionRegistry<StatDefinition> _stats;
        private readonly IDefinitionRegistry<StatusEffectDefinition> _statusEffects;

        public SkillEffectValidationRule(
            IDefinitionRegistry<StatDefinition> stats,
            IDefinitionRegistry<StatusEffectDefinition> statusEffects)
        {
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
            _statusEffects = statusEffects ?? throw new ArgumentNullException(nameof(statusEffects));
        }

        public void Validate(IDefinition definition, IDefinitionLookup lookup, ValidationReport report)
        {
            var skill = definition as SkillDefinition;

            if (skill == null)
            {
                return;
            }

            SkillLevelEntry[] levels = skill.Levels;

            if (levels == null)
            {
                return;
            }

            for (int i = 0; i < levels.Length; i++)
            {
                SkillEffect[] effects = levels[i].Effects;

                if (effects == null)
                {
                    continue;
                }

                for (int e = 0; e < effects.Length; e++)
                {
                    ValidateEffect(effects[e], skill.Id, levels[i].Level, report);
                }
            }
        }

        private void ValidateEffect(SkillEffect effect, DefinitionId id, int level,
            ValidationReport report)
        {
            string where = "Level " + level + ": ";

            switch (effect.Kind)
            {
                case SkillEffectKind.None:
                    report.AddError(ValidationCode.InvalidConfiguration, id,
                        where + "an effect has no kind.");
                    break;

                case SkillEffectKind.Damage:
                    ValidateAmount(effect, id, where, "damage", report);
                    ValidateScaling(effect, id, where, report);
                    break;

                case SkillEffectKind.Heal:
                    ValidateResource(effect, id, where, "heal", report);
                    ValidateAmount(effect, id, where, "heal", report);
                    ValidateScaling(effect, id, where, report);
                    break;

                case SkillEffectKind.ModifyResource:
                    ValidateResource(effect, id, where, "resource change", report);
                    ValidateScaling(effect, id, where, report);
                    break;

                case SkillEffectKind.ApplyStatusEffect:
                    ValidateStatusEffect(effect, id, where, report);
                    break;

                case SkillEffectKind.ModifyStat:
                    ValidateStat(effect, id, where, report);
                    break;
            }
        }
    }
}
