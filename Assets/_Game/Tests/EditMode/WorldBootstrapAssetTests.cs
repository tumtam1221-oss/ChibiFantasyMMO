using System.Collections.Generic;
using System.IO;
using ChibiFantasy.Client;
using ChibiFantasy.Server;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Transporting.Tugboat;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The production world bootstrap: real Unity assets, checked as assets.
    /// </summary>
    /// <remarks>
    /// <b>These assert against the files on disk, not against a graph built in a test.</b>
    /// Phase 16's PlayMode tests construct a NetworkManager at runtime, which proves the
    /// wiring works but proves nothing about what ships. A production bootstrap that exists
    /// only inside a test is not a production bootstrap, so this loads the prefab and the
    /// scenes and checks what an actual build would get.
    ///
    /// <b>One configuration, two roles.</b> The prefab is the single NetworkManager
    /// configuration; the two scenes differ only in which bootstrap starts itself. That is
    /// what makes "exactly one production NetworkManager" checkable rather than aspirational.
    /// </remarks>
    [TestFixture]
    internal sealed class WorldBootstrapAssetTests
    {
        private const string PrefabPath =
            "Assets/_Game/Prefabs/Network/World_NetworkManager.prefab";

        private const string ServerScenePath = "Assets/_Game/Scenes/World/World_Server.unity";
        private const string ClientScenePath = "Assets/_Game/Scenes/World/World_Client.unity";

        private const string RegistryPath = "Assets/DefaultPrefabObjects.asset";

        private static GameObject Prefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

            Assert.That(prefab, Is.Not.Null, "no production bootstrap prefab at " + PrefabPath);

            return prefab;
        }

        // ---- 4: the production asset exists ------------------------------------------------

        [Test]
        public void The_production_bootstrap_prefab_exists()
        {
            Assert.That(File.Exists(PrefabPath), Is.True);
            Assert.That(Prefab().name, Is.EqualTo("World_NetworkManager"));
        }

        [Test]
        public void Both_role_scenes_exist()
        {
            Assert.That(File.Exists(ServerScenePath), Is.True, ServerScenePath);
            Assert.That(File.Exists(ClientScenePath), Is.True, ClientScenePath);
        }

        // ---- 1, 5: exactly one NetworkManager --------------------------------------------------

        [Test]
        public void The_prefab_holds_exactly_one_network_manager()
        {
            Assert.That(Prefab().GetComponents<NetworkManager>().Length, Is.EqualTo(1));
        }

        [Test]
        public void No_other_prefab_in_the_project_holds_a_network_manager()
        {
            // Two NetworkManagers in a build is the failure mode FishNet's own persistence
            // rule exists to survive; the project should not create one in the first place.
            var offenders = new List<string>();

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                var candidate = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                if (candidate == null) continue;

                if (candidate.GetComponentsInChildren<NetworkManager>(true).Length > 0
                    && path != PrefabPath)
                {
                    offenders.Add(path);
                }
            }

            Assert.That(offenders, Is.Empty, "only the production prefab may hold one");
        }

        [Test]
        public void Each_role_scene_holds_exactly_one_network_manager()
        {
            foreach (string scene in new[] { ServerScenePath, ClientScenePath })
            {
                string text = File.ReadAllText(scene);

                // The scene references the prefab rather than duplicating its components,
                // so one instance is one configuration.
                Assert.That(CountOccurrences(text, "m_SourcePrefab"), Is.EqualTo(1), scene);
            }
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            int count = 0;
            int index = 0;

            while ((index = haystack.IndexOf(needle, index, System.StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }

            return count;
        }

        // ---- 2, 3: server and client configuration ------------------------------------------------

        [Test]
        public void The_prefab_carries_a_transport()
        {
            Assert.That(Prefab().GetComponent<Tugboat>(), Is.Not.Null,
                "a NetworkManager with no transport cannot listen or connect");
        }

        [Test]
        public void The_prefab_carries_the_server_bootstrap_and_the_authenticator()
        {
            GameObject prefab = Prefab();

            Assert.That(prefab.GetComponent<WorldServerBootstrap>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<WorldAuthenticator>(), Is.Not.Null,
                "the Phase 16 session handshake must still be installed");
        }

        [Test]
        public void The_prefab_carries_the_client_bootstrap()
        {
            Assert.That(Prefab().GetComponent<WorldClientBootstrap>(), Is.Not.Null,
                "a client must use the production configuration, not a parallel one");
        }

        [Test]
        public void The_shared_configuration_is_inert_until_a_scene_gives_it_a_role()
        {
            // A prefab that starts listening the moment it is instantiated is a hazard: a
            // role belongs to a scene, not to the configuration. The first version of this
            // prefab defaulted to being a server, which is how that was noticed.
            GameObject prefab = Prefab();

            Assert.That(Flag(prefab.GetComponent<WorldServerBootstrap>(), "_startOnAwake"),
                Is.False, "the shared prefab must not start a server by itself");
            Assert.That(Flag(prefab.GetComponent<WorldClientBootstrap>(), "_connectOnAwake"),
                Is.False, "nor connect a client by itself");
        }

        [Test]
        public void Exactly_one_role_starts_itself_in_each_scene()
        {
            // Read from the opened scenes rather than parsed out of YAML: a value that only
            // exists as an override reads differently from one that matches the prefab, and
            // what matters is what the component actually holds.
            AssertRoleFlags(ServerScenePath, expectServer: true);
            AssertRoleFlags(ClientScenePath, expectServer: false);
        }

        private static void AssertRoleFlags(string scenePath, bool expectServer)
        {
            UnityEngine.SceneManagement.Scene scene =
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath,
                    UnityEditor.SceneManagement.OpenSceneMode.Additive);

            try
            {
                WorldServerBootstrap server = null;
                WorldClientBootstrap client = null;

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (server == null) server = root.GetComponentInChildren<WorldServerBootstrap>(true);
                    if (client == null) client = root.GetComponentInChildren<WorldClientBootstrap>(true);
                }

                Assert.That(server, Is.Not.Null, scenePath + " has no server bootstrap");
                Assert.That(client, Is.Not.Null, scenePath + " has no client bootstrap");

                Assert.That(Flag(server, "_startOnAwake"), Is.EqualTo(expectServer),
                    scenePath + " _startOnAwake");
                Assert.That(Flag(client, "_connectOnAwake"), Is.EqualTo(!expectServer),
                    scenePath + " _connectOnAwake");
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
            }
        }

        /// <summary>Reads a private serialized bool the way the inspector would.</summary>
        private static bool Flag(Object component, string field)
        {
            Assert.That(component, Is.Not.Null, field);

            var serialized = new SerializedObject(component);
            SerializedProperty property = serialized.FindProperty(field);

            Assert.That(property, Is.Not.Null, "no serialized field " + field);

            return property.boolValue;
        }

        // ---- spawnable prefabs: real registry, legitimately empty ---------------------------------

        [Test]
        public void The_network_manager_uses_the_real_prefab_registry()
        {
            var registry = AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>(RegistryPath);

            Assert.That(registry, Is.Not.Null, "no registry asset at " + RegistryPath);
            Assert.That(Prefab().GetComponent<NetworkManager>().SpawnablePrefabs,
                Is.SameAs(registry),
                "the configuration must point at the committed registry, not a runtime one");
        }

        [Test]
        public void The_registry_is_empty_until_the_next_gate()
        {
            var registry = AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>(RegistryPath);

            // An empty registry is a valid FishNet configuration -- Phase 16's loopback tests
            // run against one. Registering entities is 17.2's job, and inventing a prefab
            // here to make the registry look populated would be exactly the fake asset this
            // gate forbids.
            Assert.That(registry.GetObjectCount(), Is.Zero,
                "17.2 populates this; 17.1 must not invent an entity to fill it");
        }

        // ---- 6: the server does not depend on client-only systems -----------------------------------

        [Test]
        public void The_server_assembly_does_not_reference_the_client_or_the_ui()
        {
            // Structural, from the assembly definition: a headless server build cannot be
            // dragged into needing a UI, because the assembly cannot see one.
            string asmdef = File.ReadAllText(
                "Assets/_Game/Scripts/Server/ChibiFantasy.Server.asmdef");

            Assert.That(asmdef, Does.Not.Contain("ChibiFantasy.Client"));
            Assert.That(asmdef, Does.Not.Contain("ChibiFantasy.UI"));
        }

        [Test]
        public void The_server_bootstrap_names_no_client_type()
        {
            foreach (string line in File.ReadAllLines(
                "Assets/_Game/Scripts/Server/WorldServerBootstrap.cs"))
            {
                string code = line.Trim();

                if (code.StartsWith("//") || code.StartsWith("*") || code.StartsWith("///")) continue;

                Assert.That(code, Does.Not.Contain("WorldClientBootstrap"));
                Assert.That(code, Does.Not.Contain("ChibiFantasy.UI"));
            }
        }

        // ---- 7: no secrets ---------------------------------------------------------------------------

        [Test]
        public void No_secret_or_credential_appears_in_any_production_bootstrap_asset()
        {
            foreach (string path in new[] { PrefabPath, ServerScenePath, ClientScenePath,
                RegistryPath })
            {
                string text = File.ReadAllText(path).ToLowerInvariant();

                foreach (string forbidden in new[]
                         { "password", "db_password", "connectionstring", "bearer",
                           "mysql", "token:" })
                {
                    Assert.That(text, Does.Not.Contain(forbidden), path + " contains " + forbidden);
                }
            }
        }

        [Test]
        public void The_client_bootstrap_never_logs_and_never_exposes_its_token()
        {
            string source = File.ReadAllText(
                "Assets/_Game/Scripts/Client/WorldClientBootstrap.cs");

            // The join request carries a session token. A token that can be read back off a
            // component is one a UI can display and a bug report can contain.
            System.Reflection.PropertyInfo token =
                typeof(WorldClientBootstrap).GetProperty("Token");

            Assert.That(token, Is.Not.Null);
            Assert.That(token.GetGetMethod(), Is.Null, "Token must be write-only from outside");

            foreach (string line in source.Split('\n'))
            {
                string code = line.Trim();

                if (code.StartsWith("//") || code.StartsWith("*") || code.StartsWith("///")) continue;

                if (!code.Contains("Debug.Log")) continue;

                Assert.That(code.ToLowerInvariant(), Does.Not.Contain("token"), code);
            }
        }

        // ---- the client cannot become authoritative -----------------------------------------------------

        [Test]
        public void The_client_bootstrap_has_no_way_to_set_authoritative_state()
        {
            System.Type type = typeof(WorldClientBootstrap);

            foreach (string absent in new[]
                     { "SetPosition", "SetHealth", "SetExperience", "ApplyDamage",
                       "SetCharacter", "Teleport" })
            {
                Assert.That(type.GetMethod(absent), Is.Null,
                    absent + " must not exist on a client");
            }
        }
    }
}
