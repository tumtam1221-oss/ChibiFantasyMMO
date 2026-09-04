using System.Collections.Generic;
using System.IO;
using ChibiFantasy.Network;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The production spawnable-prefab registry, checked as a shipped asset.
    /// </summary>
    /// <remarks>
    /// A prefab id is resolved by index on both sides of the wire, so a registry that
    /// differs between two builds is a client spawning the wrong thing. These assert the
    /// committed asset rather than a runtime-built one, because the committed asset is what
    /// a build reads.
    /// </remarks>
    [TestFixture]
    internal sealed class SpawnablePrefabRegistryTests
    {
        private const string RegistryPath = "Assets/DefaultPrefabObjects.asset";

        private const string MonsterPrefabPath =
            "Assets/_Game/Prefabs/Network/WorldEntity_Monster.prefab";

        private const string BootstrapPrefabPath =
            "Assets/_Game/Prefabs/Network/World_NetworkManager.prefab";

        private static DefaultPrefabObjects Registry()
        {
            var registry = AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>(RegistryPath);

            Assert.That(registry, Is.Not.Null, "no registry at " + RegistryPath);

            return registry;
        }

        // ---- 1, 2: the registry and what is in it -------------------------------------------

        [Test]
        public void The_production_registry_exists_and_is_populated()
        {
            Assert.That(Registry().GetObjectCount(), Is.GreaterThan(0),
                "17.1 left it empty on purpose; 17.2 fills it");
        }

        [Test]
        public void Every_registered_entry_is_a_real_network_object()
        {
            DefaultPrefabObjects registry = Registry();

            for (int i = 0; i < registry.GetObjectCount(); i++)
            {
                NetworkObject entry = registry.GetObject(true, i);

                Assert.That(entry, Is.Not.Null, "entry " + i + " is null");
                Assert.That(entry.GetComponent<NetworkObject>(), Is.Not.Null,
                    entry.name + " is not a NetworkObject");
            }
        }

        [Test]
        public void The_monster_entity_prefab_exists_and_is_registered()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MonsterPrefabPath);

            Assert.That(prefab, Is.Not.Null, MonsterPrefabPath);

            NetworkObject nob = prefab.GetComponent<NetworkObject>();

            Assert.That(nob, Is.Not.Null, "the prefab must carry a NetworkObject");
            Assert.That(prefab.GetComponent<MonsterNetworkEntity>(), Is.Not.Null,
                "and the behaviour that carries the replicated state");

            bool found = false;
            DefaultPrefabObjects registry = Registry();

            for (int i = 0; i < registry.GetObjectCount(); i++)
            {
                if (registry.GetObject(true, i) == nob) found = true;
            }

            Assert.That(found, Is.True, "the prefab is not in the registry");
        }

        // ---- 3: no duplicates ----------------------------------------------------------------

        [Test]
        public void No_prefab_is_registered_twice()
        {
            // A duplicate shifts every id after it, so one stale registration makes a client
            // spawn the wrong entity for everything below it.
            var seen = new HashSet<int>();
            DefaultPrefabObjects registry = Registry();

            for (int i = 0; i < registry.GetObjectCount(); i++)
            {
                NetworkObject entry = registry.GetObject(true, i);

                Assert.That(entry, Is.Not.Null);
                Assert.That(seen.Add(entry.GetInstanceID()), Is.True,
                    entry.name + " is registered more than once");
            }
        }

        [Test]
        public void The_bootstrap_prefab_is_not_registered_as_a_spawnable_entity()
        {
            // The NetworkManager configuration is not a world entity and must never be
            // spawnable; registering it would let a client be told to instantiate a second
            // NetworkManager.
            var bootstrap = AssetDatabase.LoadAssetAtPath<GameObject>(BootstrapPrefabPath);

            Assert.That(bootstrap, Is.Not.Null);
            Assert.That(bootstrap.GetComponent<NetworkObject>(), Is.Null,
                "the bootstrap prefab must not be a NetworkObject at all");
        }

        // ---- 12: still exactly one NetworkManager ----------------------------------------------

        [Test]
        public void No_registered_entity_carries_a_network_manager()
        {
            DefaultPrefabObjects registry = Registry();

            for (int i = 0; i < registry.GetObjectCount(); i++)
            {
                NetworkObject entry = registry.GetObject(true, i);

                Assert.That(entry.GetComponentsInChildren<NetworkManager>(true), Is.Empty,
                    entry.name + " would spawn a second NetworkManager");
            }
        }

        // ---- 6: the client cannot become authoritative -------------------------------------------

        [Test]
        public void Every_replicated_value_is_server_write_only()
        {
            // FishNet's default WritePermission is ServerOnly, and this design relies on it:
            // a client assigning to one of these writes to its own copy and nothing leaves
            // the machine. A SyncVar constructed with different settings would break that
            // silently, so the source is checked for one.
            string source = File.ReadAllText(
                "Assets/_Game/Scripts/Network/MonsterNetworkEntity.cs");

            Assert.That(source, Does.Not.Contain("WritePermission.ClientUnsynchronized"));
            Assert.That(source, Does.Not.Contain("WritePermission.ClientPublish"));
        }

        [Test]
        public void The_entity_exposes_no_public_setter_a_client_could_use()
        {
            System.Type type = typeof(MonsterNetworkEntity);

            foreach (System.Reflection.PropertyInfo property in type.GetProperties(
                System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.DeclaredOnly))
            {
                Assert.That(property.CanWrite, Is.False,
                    property.Name + " must be read-only to everybody");
            }
        }

        [Test]
        public void Only_server_guarded_methods_publish_state()
        {
            System.Type type = typeof(MonsterNetworkEntity);

            foreach (System.Reflection.MethodInfo method in type.GetMethods(
                System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.DeclaredOnly))
            {
                if (!method.Name.StartsWith("ServerPublish")) continue;

                Assert.That(method.GetCustomAttributes(typeof(ServerAttribute), true),
                    Is.Not.Empty, method.Name + " must carry [Server]");
            }
        }

        [Test]
        public void The_entity_carries_no_art_and_claims_none()
        {
            // The project has no monster model -- Art/Monsters holds a .gitkeep and nothing
            // else -- so this prefab is the network identity alone. A placeholder mesh here
            // would be the fake asset the gate forbids.
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MonsterPrefabPath);

            Assert.That(prefab.GetComponentsInChildren<Renderer>(true), Is.Empty,
                "art attaches later; it is not invented here");
        }

        // ---- the replication service owns no rules -------------------------------------------------

        [Test]
        public void The_replication_service_computes_no_gameplay_value()
        {
            // It copies what the authoritative runtime already decided. A damage formula, a
            // speed or a reward appearing here would be a second source of truth.
            foreach (string line in File.ReadAllLines(
                "Assets/_Game/Scripts/Server/MonsterReplicationService.cs"))
            {
                string code = line.Trim();

                if (code.StartsWith("//") || code.StartsWith("///") || code.StartsWith("*"))
                {
                    continue;
                }

                foreach (string forbidden in new[]
                         { "ApplyHealthDelta", "MonsterMovement.", "MonsterDefeatService",
                           "DropResolver", "SkillExecutor" })
                {
                    Assert.That(code, Does.Not.Contain(forbidden),
                        "replication must not perform gameplay: " + code);
                }
            }
        }
    }
}
