using System;
using System.Collections.Generic;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// Validates skills completely, in one call.
    /// </summary>
    /// <remarks>
    /// Skill checking is split across two rules for good reasons: structure needs class and
    /// job content, effects need stat and status content, and either can run alone. The
    /// cost of that split was that a caller had to remember both, and a caller who
    /// remembered one got a clean report on a broken skill. That is the worst possible
    /// outcome for a validator, so this composes them.
    ///
    /// It adds no rules of its own and owns no logic. It assembles the existing rules, a
    /// <see cref="CompositeDefinitionLookup"/> over the registries they need, and the
    /// existing <see cref="DefinitionValidator"/>, so identity and duplicate checks come
    /// along too.
    ///
    /// Reports, never repairs. Deterministic: rules run in a fixed order and skills are
    /// examined in the order given.
    /// </remarks>
    public sealed class SkillContentValidator
    {
        private readonly DefinitionValidator _validator;
        private readonly CompositeDefinitionLookup _lookup;

        public SkillContentValidator(
            IDefinitionRegistry<SkillDefinition> skills,
            IDefinitionRegistry<ClassDefinition> classes,
            IDefinitionRegistry<JobDefinition> jobs,
            IDefinitionRegistry<StatDefinition> stats,
            IDefinitionRegistry<StatusEffectDefinition> statusEffects)
        {
            if (skills == null)
            {
                throw new ArgumentNullException(nameof(skills));
            }

            if (classes == null)
            {
                throw new ArgumentNullException(nameof(classes));
            }

            if (jobs == null)
            {
                throw new ArgumentNullException(nameof(jobs));
            }

            if (stats == null)
            {
                throw new ArgumentNullException(nameof(stats));
            }

            if (statusEffects == null)
            {
                throw new ArgumentNullException(nameof(statusEffects));
            }

            _validator = new DefinitionValidator(new IDefinitionValidationRule[]
            {
                new SkillValidationRule(skills, classes, jobs),
                new SkillEffectValidationRule(stats, statusEffects)
            });

            _lookup = new CompositeDefinitionLookup(skills, classes, jobs, stats, statusEffects);
        }

        /// <summary>Validates one skill against every skill rule.</summary>
        public ValidationReport Validate(SkillDefinition skill)
        {
            return _validator.Validate(skill, _lookup);
        }

        /// <summary>
        /// Validates a set of skills, additionally reporting duplicate identities.
        /// </summary>
        /// <remarks>Duplicate detection comes from the existing validator and is scoped to
        /// the set passed in, matching how registries scope identity.</remarks>
        public ValidationReport Validate(IEnumerable<SkillDefinition> skills)
        {
            if (skills == null)
            {
                throw new ArgumentNullException(nameof(skills));
            }

            var definitions = new List<IDefinition>();

            foreach (SkillDefinition skill in skills)
            {
                definitions.Add(skill);
            }

            return _validator.Validate(definitions, _lookup);
        }
    }
}
