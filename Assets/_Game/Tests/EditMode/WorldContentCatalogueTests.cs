using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The production content pipeline: what ships, and what refuses to.
    /// </summary>
    /// <remarks>
    /// <b>18.8B failed on this.</b> There was no way for a build to obtain a definition and
    /// no authored content to obtain, so the shipped server could not honestly be composed.
    /// These tests are the standing proof that both now exist and that neither can quietly
    /// regress into a folder scan or a prototype asset.
    ///
    /// <b>The fresh-clone checks matter more than they look.</b> Content that resolves on the
    /// machine that authored it and nowhere else is the failure mode this whole gate exists
    /// to prevent, and "it worked in the editor" cannot detect it.
    /// </remarks>
    [TestFixture]
    internal sealed class WorldContentCatalogueTests
    {
        private const string CataloguePath =
            "Assets/_Game/Data/Production/WorldContentCatalogue.asset";

        private const string ServerScene = "Assets/_Game/Scenes/World/World_Server.unity";

        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object created in _created)
            {
                if (created != null) Object.DestroyImmediate(created);
            }

            _created.Clear();
        }

        // ---- the shipped catalogue -----------------------------------------------------------

        [Test]
        public void TheProductionCatalogueValidates()
        {
            var faults = new List<string>();

            Assert.That(Catalogue().Validate(faults), Is.True,
                "shipped content faults: " + string.Join("; ", faults));
        }

        [Test]
        public void ItBuildsTheRegistriesEveryServiceAlreadyTakes()
        {
            WorldContentCatalogue catalogue = Catalogue();

            Assert.That(catalogue.BuildStats().Count, Is.GreaterThanOrEqualTo(10),
                "six primary attributes and the derived stats a world computes");
            Assert.That(catalogue.Formulas.Count, Is.GreaterThanOrEqualTo(4));
            Assert.That(catalogue.BuildMaps().Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(catalogue.BuildSpawnPoints().Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(catalogue.BuildMonsters().Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(catalogue.BuildClasses().Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(catalogue.BuildProgressions().Count, Is.GreaterThanOrEqualTo(1));

            // The registry type every authority already takes, not a second lookup API.
            Assert.That(catalogue.BuildStats(),
                Is.InstanceOf<IDefinitionRegistry<StatDefinition>>());
        }

        [Test]
        public void TheSixPrimaryAttributesAreAllThere()
        {
            IDefinitionRegistry<StatDefinition> stats = Catalogue().BuildStats();

            foreach (string id in new[]
                     {
                         "stat.str", "stat.agi", "stat.vit",
                         "stat.int", "stat.dex", "stat.luk",
                     })
            {
                Assert.That(stats.TryGet(new DefinitionId(id), out StatDefinition stat),
                    Is.True, "missing primary attribute " + id);
                Assert.That(stat.IsPrimary, Is.True, id + " is not marked primary");
            }
        }

        [Test]
        public void EveryStatRoleResolvesAndHasAFormulaBehindIt()
        {
            WorldContentCatalogue catalogue = Catalogue();

            IDefinitionRegistry<StatDefinition> stats = catalogue.BuildStats();

            foreach (DefinitionId role in new[]
                     {
                         catalogue.MaxHealthStat, catalogue.MaxManaStat,
                         catalogue.AttackStat, catalogue.DefenceStat,
                     })
            {
                Assert.That(role.IsValid, Is.True, "a stat role is unnamed");
                Assert.That(stats.TryGet(role, out StatDefinition _), Is.True,
                    role + " names a stat that does not exist");

                var produced = false;

                foreach (DerivedStatFormulaDefinition formula in catalogue.Formulas)
                {
                    if (formula != null && formula.DerivedStat == role) produced = true;
                }

                Assert.That(produced, Is.True,
                    "no formula produces " + role + ", so it would compute as nothing");
            }
        }

        [Test]
        public void AStarterCharacterWouldHaveARealHealthCeiling()
        {
            // The failure this guards: a formula set that validates structurally and still
            // produces a character who enters the world dead.
            WorldContentCatalogue catalogue = Catalogue();

            IDefinitionRegistry<ClassDefinition> classes = catalogue.BuildClasses();

            Assert.That(classes.TryGet(new DefinitionId("class.swordsman"),
                out ClassDefinition swordsman), Is.True);

            var stats = new CharacterStatsState(new CharacterId("probe"));

            foreach (StatValue value in swordsman.BaseStats)
            {
                stats.Set(value.Stat, Mathf.RoundToInt(value.Value));
            }

            DerivedStatsResult derived = new DerivedStatsCalculator()
                .Calculate(stats, catalogue.Formulas, catalogue.BuildStats(),
                    new List<StatModifier>());

            Assert.That(derived.TryGet(catalogue.MaxHealthStat, out int health), Is.True);
            Assert.That(health, Is.GreaterThan(0),
                "a starter with no health ceiling cannot enter the world alive");

            Assert.That(derived.TryGet(catalogue.MaxManaStat, out int mana), Is.True);
            Assert.That(mana, Is.GreaterThanOrEqualTo(0));

            Assert.That(derived.TryGet(catalogue.AttackStat, out int attack), Is.True);
            Assert.That(attack, Is.GreaterThan(0), "a starter who cannot hurt anything");

            Assert.That(derived.TryGet(catalogue.DefenceStat, out int _), Is.True);
        }

        [Test]
        public void ThePlayerSpawnResolvesItsOwnMap()
        {
            WorldContentCatalogue catalogue = Catalogue();

            IDefinitionRegistry<MapDefinition> maps = catalogue.BuildMaps();

            var players = 0;

            foreach (SpawnPointDefinition spawn in All<SpawnPointDefinition>(
                "Assets/_Game/Data/Production/Spawns"))
            {
                Assert.That(maps.TryGet(spawn.Map, out MapDefinition map), Is.True,
                    spawn.Id + " is on a map this world does not have");

                if (spawn.SpawnType != SpawnType.Player) continue;

                players++;

                // Inside the map's own movement radius, or the first step would be refused.
                float distance = new Vector3(spawn.X, 0f, spawn.Z).magnitude;

                Assert.That(distance, Is.LessThanOrEqualTo(map.MovementRadius),
                    spawn.Id + " stands outside the movement radius of " + map.Id);
            }

            Assert.That(players, Is.GreaterThanOrEqualTo(1),
                "with no player spawn an admitted character is refused entry");
        }

        // ---- what must not be in it ------------------------------------------------------------

        [Test]
        public void NothingTheWorldNeedsIsPrototypeContent()
        {
            // World content: nothing the simulation resolves may be prototype data.
            foreach (string dependency in UnityEditor.AssetDatabase.GetDependencies(
                CataloguePath, true))
            {
                Assert.That(dependency, Does.Not.Contain("/Prototype/"),
                    "the world runs on prototype content: " + dependency);
            }

            // The whole shipped scene: nothing may reach validation content or a test.
            //
            // Prototype is not asserted here, and deliberately: the character prefab reaches
            // Proto_Locomotion through the visual catalogue, which is 18.6 reusing the one
            // validated animator controller rather than authoring a second. It is presentation
            // a headless server never builds, it was reported as a limitation then, and
            // renaming it is asset churn this gate has no business doing.
            foreach (string dependency in Dependencies())
            {
                Assert.That(dependency, Does.Not.Contain("/Validation/"),
                    "the shipped world depends on validation content: " + dependency);
                Assert.That(dependency, Does.Not.Contain("/Tests/"),
                    "the shipped world depends on a test asset: " + dependency);
            }
        }

        [Test]
        public void NoProductionDefinitionIdUsesThePrototypeConvention()
        {
            foreach (GameDefinition definition in AllProductionDefinitions())
            {
                string id = definition.Id.Value ?? string.Empty;

                Assert.That(id.ToLowerInvariant(), Does.Not.StartWith("proto"),
                    definition.name + " carries a prototype id");
                Assert.That(id, Is.Not.Empty, definition.name + " has no id");

                // A stable readable id, not a GUID and not an index.
                Assert.That(id, Does.Match("^[a-z][a-z0-9_]*(\\.[a-z0-9_]+)+$"),
                    id + " is not a stable namespaced id");
            }
        }

        [Test]
        public void EveryProductionDefinitionIdIsUniqueAcrossTheWholeSet()
        {
            // Unique within a kind, always: two stats or two monsters claiming one id is a
            // content mistake that nothing downstream could resolve.
            var byKind = new Dictionary<System.Type, HashSet<string>>();

            // And unique across kinds too, with exactly one deliberate exception. A card
            // item and the card it becomes share an id on purpose: CardSocketService looks
            // the CardDefinition up by the item's own DefinitionId, so an item that did not
            // match its card could never be socketed at all. Anything else that repeats an
            // id across kinds is still a collision.
            var everything = new Dictionary<string, GameDefinition>();

            foreach (GameDefinition definition in AllProductionDefinitions())
            {
                System.Type kind = definition is CardDefinition
                    ? typeof(CardDefinition)
                    : definition is ItemDefinition ? typeof(ItemDefinition)
                    : definition.GetType();

                if (!byKind.TryGetValue(kind, out HashSet<string> seen))
                {
                    seen = new HashSet<string>();
                    byKind[kind] = seen;
                }

                Assert.That(seen.Add(definition.Id.Value), Is.True,
                    "duplicate production " + kind.Name + " id " + definition.Id);

                if (!everything.TryGetValue(definition.Id.Value, out GameDefinition other))
                {
                    everything[definition.Id.Value] = definition;

                    continue;
                }

                Assert.That(IsCardPairing(definition, other), Is.True,
                    "duplicate production id " + definition.Id + " shared by "
                    + definition.GetType().Name + " and " + other.GetType().Name);
            }
        }

        /// <summary>Whether two definitions sharing an id are a card and the item form of it.</summary>
        /// <remarks>The only pairing allowed to share an id, and only when the item is
        /// actually authored as a card -- an ordinary item colliding with a card is still a
        /// mistake.</remarks>
        private static bool IsCardPairing(GameDefinition one, GameDefinition other)
        {
            return (one is CardDefinition && other is ItemDefinition item
                    && item.Category == ItemCategory.Card)
                || (other is CardDefinition && one is ItemDefinition mirrored
                    && mirrored.Category == ItemCategory.Card);
        }

        // ---- validation refuses rather than limps -------------------------------------------------

        [Test]
        public void AnEmptyCatalogueIsRefused()
        {
            var empty = ScriptableObject.CreateInstance<WorldContentCatalogue>();
            _created.Add(empty);

            var faults = new List<string>();

            Assert.That(empty.Validate(faults), Is.False,
                "a world with no content must not report itself ready");
            Assert.That(faults, Is.Not.Empty);
        }

        [Test]
        public void ANullEntryAndADuplicateIdAreBothRefused()
        {
            var catalogue = ScriptableObject.CreateInstance<WorldContentCatalogue>();
            _created.Add(catalogue);

            StatDefinition first = Stat("stat.probe");
            StatDefinition duplicate = Stat("stat.probe");

            Set(catalogue, "_stats", new[] { first, null, duplicate });

            var faults = new List<string>();

            catalogue.Validate(faults);

            Assert.That(string.Join("; ", faults), Does.Contain("empty stat slot"));
            Assert.That(string.Join("; ", faults), Does.Contain("duplicate stat id"));
        }

        [Test]
        public void AFormulaNamingAStatNobodyDefinedIsRefused()
        {
            var catalogue = ScriptableObject.CreateInstance<WorldContentCatalogue>();
            _created.Add(catalogue);

            Set(catalogue, "_stats", new[] { Stat("stat.vit") });
            Set(catalogue, "_formulas", new[] { Formula("formula.ghost", "stat.nowhere") });

            var faults = new List<string>();

            catalogue.Validate(faults);

            Assert.That(string.Join("; ", faults), Does.Contain("unknown stat 'stat.nowhere'"),
                "a formula producing a stat nobody defined computes nothing, silently");
        }

        [Test]
        public void ASpawnOnAMapThisWorldDoesNotHaveIsRefused()
        {
            var catalogue = ScriptableObject.CreateInstance<WorldContentCatalogue>();
            _created.Add(catalogue);

            Set(catalogue, "_spawnPoints", new[] { Spawn("spawn.orphan", "map.nowhere") });

            var faults = new List<string>();

            catalogue.Validate(faults);

            Assert.That(string.Join("; ", faults), Does.Contain("unknown map 'map.nowhere'"));
        }

        // ---- the delivery mechanism -----------------------------------------------------------------

        [Test]
        public void NoRuntimeCodeScansForContent()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts", "*.cs",
                System.IO.SearchOption.AllDirectories);

            foreach (string file in files)
            {
                string named = file.Replace("\\", "/");

                // The editor scanner is allowed to scan: it validates authored content and
                // ships in no build.
                if (named.Contains("/Editor/")) continue;

                string source = Code(file);

                Assert.That(source, Does.Not.Contain("Resources.LoadAll"), named);
                Assert.That(source, Does.Not.Contain("AssetDatabase"), named);
                Assert.That(source, Does.Not.Contain("FindAssets"), named);
                Assert.That(source, Does.Not.Contain("UnityEditor"), named);
            }

            // And the catalogue itself resolves nothing by path or by name.
            string catalogue = Code("Assets/_Game/Scripts/Data/WorldContentCatalogue.cs");

            Assert.That(catalogue, Does.Not.Contain("Resources."), "no loader in the pipeline");
            Assert.That(catalogue, Does.Not.Contain("Assets/"), "no path convention");
        }

        [Test]
        public void AddressablesWasNotIntroduced()
        {
            string manifest = System.IO.File.ReadAllText("Packages/manifest.json");

            Assert.That(manifest.ToLowerInvariant(), Does.Not.Contain("addressable"),
                "the chosen mechanism is a serialized catalogue, not a second one");
        }

        // ---- the shipped scene ------------------------------------------------------------------------

        [Test]
        public void TheServerSceneReferencesTheProductionCatalogueAndNothingElse()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ServerScene,
                UnityEditor.SceneManagement.OpenSceneMode.Additive);

            try
            {
                var bootstraps = new List<ChibiFantasy.Server.WorldServerBootstrap>();
                var managers = new List<FishNet.Managing.NetworkManager>();
                var missing = new List<string>();

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    bootstraps.AddRange(
                        root.GetComponentsInChildren<ChibiFantasy.Server.WorldServerBootstrap>(true));
                    managers.AddRange(
                        root.GetComponentsInChildren<FishNet.Managing.NetworkManager>(true));

                    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    {
                        foreach (Component c in t.GetComponents<Component>())
                        {
                            if (c == null) missing.Add(t.name);
                        }
                    }
                }

                Assert.That(missing, Is.Empty, "a missing script in the server scene");
                Assert.That(bootstraps.Count, Is.EqualTo(1),
                    "one composition root, or two worlds would tick");
                Assert.That(managers.Count, Is.EqualTo(1), "exactly one NetworkManager");

                var serialized = new UnityEditor.SerializedObject(bootstraps[0]);

                Object content = serialized.FindProperty("_content").objectReferenceValue;

                Assert.That(content, Is.Not.Null,
                    "the shipped server has no world content and would simulate nothing");
                Assert.That(UnityEditor.AssetDatabase.GetAssetPath(content),
                    Is.EqualTo(CataloguePath));

                Assert.That(serialized.FindProperty("_characterPrefab").objectReferenceValue,
                    Is.Not.Null, "an admitted player would have no object");
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void TheServerSceneIsInTheBuildAndIsNotTheClientsFirstScene()
        {
            UnityEditor.EditorBuildSettingsScene[] scenes = UnityEditor.EditorBuildSettings.scenes;

            var index = -1;

            for (var i = 0; i < scenes.Length; i++)
            {
                if (scenes[i].path == ServerScene) index = i;
            }

            Assert.That(index, Is.GreaterThanOrEqualTo(0),
                "a dedicated-server build cannot include a scene the build list has never "
                + "heard of");
            Assert.That(scenes[index].enabled, Is.True);
            Assert.That(index, Is.Not.Zero,
                "scene zero is what a client boots into; the server scene must not be it");
        }

        [Test]
        public void EveryAssetTheShippedWorldNeedsIsTracked()
        {
            // The check that "works on this machine" cannot make: git, not the file system.
            var untracked = new List<string>();

            foreach (string dependency in Dependencies())
            {
                if (!dependency.StartsWith("Assets/")) continue;

                if (!IsTracked(dependency)) untracked.Add(dependency);

                string meta = dependency + ".meta";

                if (!IsTracked(meta)) untracked.Add(meta);
            }

            Assert.That(untracked, Is.Empty,
                "a fresh clone would be missing: " + string.Join(", ", untracked));
        }

        // ---- helpers ------------------------------------------------------------------------------------

        /// <summary>Everything the shipped world reaches, through the scene and the catalogue.</summary>
        private static string[] Dependencies()
        {
            return UnityEditor.AssetDatabase.GetDependencies(
                new[] { ServerScene, CataloguePath }, true);
        }

        private static bool IsTracked(string path)
        {
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo("git",
                    "ls-files --error-unmatch \"" + path + "\"")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };

            process.Start();
            process.WaitForExit(10000);

            return process.ExitCode == 0;
        }

        private static IEnumerable<GameDefinition> AllProductionDefinitions()
        {
            foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:GameDefinition",
                new[] { "Assets/_Game/Data/Production" }))
            {
                var definition = UnityEditor.AssetDatabase.LoadAssetAtPath<GameDefinition>(
                    UnityEditor.AssetDatabase.GUIDToAssetPath(guid));

                if (definition != null) yield return definition;
            }
        }

        private static IEnumerable<T> All<T>(string folder) where T : GameDefinition
        {
            foreach (string guid in UnityEditor.AssetDatabase.FindAssets(
                "t:" + typeof(T).Name, new[] { folder }))
            {
                var definition = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(
                    UnityEditor.AssetDatabase.GUIDToAssetPath(guid));

                if (definition != null) yield return definition;
            }
        }

        private static WorldContentCatalogue Catalogue()
        {
            var catalogue = UnityEditor.AssetDatabase
                .LoadAssetAtPath<WorldContentCatalogue>(CataloguePath);

            Assert.That(catalogue, Is.Not.Null, "no catalogue at " + CataloguePath);

            return catalogue;
        }

        private static string Code(string path)
        {
            var kept = new List<string>();

            foreach (string line in System.IO.File.ReadAllLines(path))
            {
                string trimmed = line.TrimStart();

                if (trimmed.StartsWith("///") || trimmed.StartsWith("//")) continue;

                kept.Add(line);
            }

            return string.Join(" ", kept);
        }

        private static void Set(Object target, string field, object value)
        {
            target.GetType().GetField(field, System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance).SetValue(target, value);
        }

        private StatDefinition Stat(string id)
        {
            var definition = ScriptableObject.CreateInstance<StatDefinition>();
            _created.Add(definition);

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_minValue\":0,\"_maxValue\":9999}",
                definition);

            return definition;
        }

        private DerivedStatFormulaDefinition Formula(string id, string derived)
        {
            var definition = ScriptableObject.CreateInstance<DerivedStatFormulaDefinition>();
            _created.Add(definition);

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_derivedStat\":{\"_value\":\""
                + derived + "\"},\"_constant\":0}", definition);

            return definition;
        }

        private SpawnPointDefinition Spawn(string id, string map)
        {
            var definition = ScriptableObject.CreateInstance<SpawnPointDefinition>();
            _created.Add(definition);

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_map\":{\"_value\":\"" + map
                + "\"},\"_spawnType\":" + (int)SpawnType.Player + "}", definition);

            return definition;
        }
    }
}
