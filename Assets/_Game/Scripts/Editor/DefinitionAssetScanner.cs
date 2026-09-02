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
    /// validator stays usable by the client, the dedicated server and tests without
    /// UnityEditor and without a scene.
    ///
    /// This is a development convenience for checking authored content, not the production
    /// content pipeline. Nothing here uses Resources.LoadAll, and nothing assumes assets
    /// live in any particular folder. The registry it builds cares only about definition
    /// objects and their stable ids, so content delivered later from bundles, a patch or a
    /// server flows through the same validator unchanged.
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

            var registry = new DefinitionRegistry<GameDefinition>();

            foreach (GameDefinition definition in definitions)
            {
                registry.TryRegister(definition);
            }

            var validator = new DefinitionValidator();
            return validator.Validate(definitions, registry);
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
