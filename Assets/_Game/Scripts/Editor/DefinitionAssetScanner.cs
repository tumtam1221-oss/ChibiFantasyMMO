using System.Collections.Generic;
using ChibiFantasy.Data;
using UnityEditor;
using UnityEngine;

namespace ChibiFantasy.Editor
{
    /// <summary>
    /// Finds authored definition assets in the project and validates them.
    /// </summary>
    /// <remarks>
    /// Editor-only by design. AssetDatabase exists nowhere in Core or Data, so the runtime
    /// validators stay usable by the client, the dedicated server and tests without
    /// UnityEditor and without a scene.
    ///
    /// <b>It now runs the specialised rules.</b> Until this step it constructed a bare
    /// DefinitionValidator, which meant every rule written since 04.6 -- progression,
    /// class, job, stat, derived formula, skill and skill effect -- was never reached by a
    /// project scan. A scan reported success on content those rules would have rejected,
    /// which is worse than no scan at all. Assets are partitioned into typed registries
    /// first so the rules that need them have them.
    ///
    /// This is a development convenience for checking authored content, not the production
    /// content pipeline. No Resources.LoadAll, no folder convention, no Addressables. The
    /// registry cares about definition objects and their stable ids, not their origin, so
    /// content delivered later from bundles, a patch or a server flows through the same
    /// validators unchanged.
    ///
    /// Read and report only. Nothing here modifies an asset.
    /// </remarks>
    public static class DefinitionAssetScanner
    {
        /// <summary>Loads every GameDefinition asset in the project, in stable path order.</summary>
        public static IReadOnlyList<GameDefinition> FindAll()
        {
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(GameDefinition));
            var paths = new List<string>(guids.Length);

            foreach (string guid in guids)
            {
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            }

            // Sorted so a scan is reproducible regardless of GUID ordering.
            paths.Sort(System.StringComparer.Ordinal);

            var found = new List<GameDefinition>(paths.Count);

            foreach (string path in paths)
            {
                var definition = AssetDatabase.LoadAssetAtPath<GameDefinition>(path);

                if (definition != null)
                {
                    found.Add(definition);
                }
            }

            return found;
        }

        /// <summary>
        /// Scans the project and validates everything found against itself.
        /// </summary>
        /// <remarks>
        /// Registration uses TryRegister so a duplicate id does not abort the scan; the
        /// validator reports it instead, alongside every other problem in the set.
        /// </remarks>
        public static ValidationReport ScanAndValidate()
        {
            IReadOnlyList<GameDefinition> definitions = FindAll();

            var all = new DefinitionRegistry<GameDefinition>();
            var skills = new DefinitionRegistry<SkillDefinition>();
            var classes = new DefinitionRegistry<ClassDefinition>();
            var jobs = new DefinitionRegistry<JobDefinition>();
            var stats = new DefinitionRegistry<StatDefinition>();
            var statusEffects = new DefinitionRegistry<StatusEffectDefinition>();

            foreach (GameDefinition definition in definitions)
            {
                all.TryRegister(definition);

                Sort(definition, skills, classes, jobs, stats, statusEffects);
            }

            var validator = new DefinitionValidator(BuildRules(skills, classes, jobs, stats, statusEffects));

            return validator.Validate(definitions, new CompositeDefinitionLookup(all));
        }

        private static void Sort(GameDefinition definition,
            DefinitionRegistry<SkillDefinition> skills,
            DefinitionRegistry<ClassDefinition> classes,
            DefinitionRegistry<JobDefinition> jobs,
            DefinitionRegistry<StatDefinition> stats,
            DefinitionRegistry<StatusEffectDefinition> statusEffects)
        {
            switch (definition)
            {
                case SkillDefinition skill:
                    skills.TryRegister(skill);
                    break;
                case ClassDefinition characterClass:
                    classes.TryRegister(characterClass);
                    break;
                case JobDefinition job:
                    jobs.TryRegister(job);
                    break;
                case StatDefinition stat:
                    stats.TryRegister(stat);
                    break;
                case StatusEffectDefinition status:
                    statusEffects.TryRegister(status);
                    break;
            }
        }

        /// <summary>Every specialised rule the project has, in a fixed order.</summary>
        private static IDefinitionValidationRule[] BuildRules(
            DefinitionRegistry<SkillDefinition> skills,
            DefinitionRegistry<ClassDefinition> classes,
            DefinitionRegistry<JobDefinition> jobs,
            DefinitionRegistry<StatDefinition> stats,
            DefinitionRegistry<StatusEffectDefinition> statusEffects)
        {
            return new IDefinitionValidationRule[]
            {
                new StatDefinitionValidationRule(),
                new CharacterProgressionValidationRule(),
                new DerivedStatFormulaValidationRule(stats),
                new ClassProgressionValidationRule(jobs),
                new JobProgressionValidationRule(jobs, classes),
                new SkillValidationRule(skills, classes, jobs),
                new SkillEffectValidationRule(stats, statusEffects)
            };
        }

        [MenuItem("ChibiFantasy/Content/Validate Definitions")]
        private static void ValidateFromMenu()
        {
            ValidationReport report = ScanAndValidate();

            foreach (ValidationMessage message in report.Messages)
            {
                if (message.Severity == ValidationSeverity.Error)
                {
                    Debug.LogError(message.ToString());
                }
                else
                {
                    Debug.LogWarning(message.ToString());
                }
            }

            Debug.Log(
                "Definition validation finished. Errors: " + report.ErrorCount +
                ", warnings: " + report.WarningCount + ".");
        }
    }
}
