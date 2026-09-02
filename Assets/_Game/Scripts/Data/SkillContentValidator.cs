using System;
using System.Collections.Generic;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// Validates skills completely, in one call.
    /// </summary>
    /// <remarks>
    /// Skill checking is split across three rules for good reasons: structure needs class
    /// and job content, effects need stat and status content, grants hang off the class and
    /// job rather than the skill, and any of them can run alone. The cost of that split was
    /// that a caller had to remember all of them, and a caller who remembered one got a
    /// clean report on a broken skill. That is the worst possible outcome for a validator,
    /// so this composes them.
    ///
    /// It adds no rules of its own and owns no logic. It assembles the existing rules, a
    /// <see cref="CompositeDefinitionLookup"/> over the registries they need, and the
    /// existing <see cref="DefinitionValidator"/>, so identity and duplicate checks come
    /// along too.
    ///
    /// <b>Skill content is not only skills.</b> A skill nobody can reach and a class
    /// handing out a skill that does not exist are both broken content, and the second
    /// lives on <see cref="ClassDefinition"/> rather than on any skill. So the entry points
    /// cover classes and jobs as well, and <see cref="ValidateAll"/> checks a whole
    /// authored set in one call -- the authoring workflow this type exists to serve.
    ///
    /// Reports, never repairs. Deterministic: rules run in a fixed order and definitions
    /// are examined in the order given.
    /// </remarks>
    public sealed class SkillContentValidator
    {
        private readonly DefinitionValidator _validator;
        private readonly CompositeDefinitionLookup _lookup;
        private readonly IDefinitionRegistry<SkillDefinition> _skills;
        private readonly IDefinitionRegistry<ClassDefinition> _classes;
        private readonly IDefinitionRegistry<JobDefinition> _jobs;

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
                new SkillEffectValidationRule(stats, statusEffects),
                new SkillGrantValidationRule(skills)
            });

            _lookup = new CompositeDefinitionLookup(skills, classes, jobs, stats, statusEffects);
            _skills = skills;
            _classes = classes;
            _jobs = jobs;
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

        /// <summary>Validates the skills one class hands a new character.</summary>
        public ValidationReport Validate(ClassDefinition characterClass)
        {
            return _validator.Validate(characterClass, _lookup);
        }

        /// <summary>Validates the skills one job unlocks.</summary>
        public ValidationReport Validate(JobDefinition job)
        {
            return _validator.Validate(job, _lookup);
        }

        /// <summary>
        /// Validates every skill, class and job the registries hold.
        /// </summary>
        /// <remarks>
        /// The authoring entry point: asset to registry to validator to report, in one
        /// call, with nothing for a caller to forget.
        ///
        /// The three types are validated as three sets rather than one, because duplicate
        /// identity is scoped per registry -- a skill and a class may both be called
        /// "guard" without conflict, exactly as <see cref="DefinitionRegistry{T}"/> allows
        /// -- and one combined set would invent a clash that content does not have. Their
        /// findings are then concatenated in a fixed type order, so the report stays a
        /// single diffable result.
        /// </remarks>
        public ValidationReport ValidateAll()
        {
            var report = new ValidationReport();

            Append(report, _validator.Validate(AsDefinitions(_skills.All), _lookup));
            Append(report, _validator.Validate(AsDefinitions(_classes.All), _lookup));
            Append(report, _validator.Validate(AsDefinitions(_jobs.All), _lookup));

            return report;
        }

        private static List<IDefinition> AsDefinitions<T>(IReadOnlyList<T> definitions)
            where T : IDefinition
        {
            var collected = new List<IDefinition>(definitions.Count);

            for (int i = 0; i < definitions.Count; i++)
            {
                collected.Add(definitions[i]);
            }

            return collected;
        }

        private static void Append(ValidationReport target, ValidationReport source)
        {
            IReadOnlyList<ValidationMessage> messages = source.Messages;

            for (int i = 0; i < messages.Count; i++)
            {
                target.Add(messages[i]);
            }
        }
    }
}
