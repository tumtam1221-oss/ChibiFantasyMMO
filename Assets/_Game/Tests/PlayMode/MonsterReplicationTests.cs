// Editor-only, and only this fixture.
//
// It loads the committed DefaultPrefabObjects registry through AssetDatabase, because the
// point is to prove the SHIPPED configuration replicates -- a registry built at runtime
// would prove only that the test can build one. AssetDatabase does not exist in a player,
// so the fixture is compiled out there while the assembly itself stays cross-platform.
//
// Making the whole assembly Editor-only was tried first and was wrong: Unity then classifies
// it as an EditMode assembly and the eight existing FishNet PlayMode tests silently stopped
// running.
#if UNITY_EDITOR

using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Network;
using ChibiFantasy.Server;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using FishNet.Transporting;
using FishNet.Transporting.Tugboat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ChibiFantasy.Tests.PlayMode
{
    /// <summary>
    /// Monster replication over a real FishNet socket.
    /// </summary>
    /// <remarks>
    /// <b>Nothing here is simulated with C# events.</b> A real server spawns a real
    /// <see cref="NetworkObject"/> from the committed registry, and a real client on a real
    /// loopback connection observes it. That is the only way to establish that the prefab is
    /// registered correctly, that the id resolves on both sides, and that a despawn actually
    /// reaches the client -- none of which a mock can tell you.
    ///
    /// The authoritative monster is the Phase 10/17.10 runtime. What is checked here is that
    /// the shadow follows it, and that a client can only watch.
    /// </remarks>
    [TestFixture]
    internal sealed class MonsterReplicationTests
    {
        private const string RegistryPath = "Assets/DefaultPrefabObjects.asset";
        private const string MonsterPrefabPath =
            "Assets/_Game/Prefabs/Network/WorldEntity_Monster.prefab";

        private sealed class FakeStore : ICharacterStateStore
        {
            public CharacterPersistenceResult Load(SessionId s) =>
                CharacterPersistenceResult.Failed(CharacterPersistenceFailure.NotOwned);

            public CharacterPersistenceResult Save(SessionId s, PersistedCharacter c, int r) =>
                CharacterPersistenceResult.Saved(r + 1);
        }

        private GameObject _serverObject;
        private GameObject _clientObject;
        private NetworkManager _server;
        private NetworkManager _client;
        private MonsterWorldRuntime _runtime;
        private MonsterReplicationService _replication;
        private readonly List<Object> _created = new List<Object>();
        private ushort _port;

        private const string MaxHp = "stat.maxhp";
        private const string HomeMap = "map.home";
        private const string Grunt = "monster.grunt";

        private static ushort NextPort() => (ushort)Random.Range(30100, 34000);

        /// <summary>Builds a NetworkManager against the committed production registry.</summary>
        /// <remarks>
        /// The real registry, not a runtime-created empty one: this test exists to prove the
        /// shipped configuration works, and an invented registry would prove only that the
        /// test can invent one.
        ///
        /// Built inactive then activated, and with persistence relaxed, for the reasons the
        /// Phase 16 fixture documents.
        /// </remarks>
        private NetworkManager BuildManager(string name, bool listening, out GameObject host)
        {
            host = new GameObject(name);
            host.SetActive(false);

            LogAssert.Expect(LogType.Error, new Regex("SpawnablePrefabs is null"));

            NetworkManager manager = host.AddComponent<NetworkManager>();

            manager.SpawnablePrefabs =
                UnityEditor.AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>(RegistryPath);

            typeof(NetworkManager)
                .GetField("_persistence", System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance)
                ?.SetValue(manager, NetworkManager.PersistenceType.AllowMultiple);

            var transport = host.AddComponent<Tugboat>();
            transport.SetPort(_port);

            if (listening) transport.SetServerBindAddress("127.0.0.1", IPAddressType.IPv4);
            else transport.SetClientAddress("127.0.0.1");

            return manager;
        }

        [SetUp]
        public void SetUp()
        {
            _port = NextPort();

            _server = BuildManager("ReplicationServer", true, out _serverObject);
            _serverObject.SetActive(true);

            _client = BuildManager("ReplicationClient", false, out _clientObject);
            _clientObject.SetActive(true);

            var spawns = new DefinitionRegistry<SpawnPointDefinition>();
            var monsters = new DefinitionRegistry<MonsterDefinition>();
            monsters.Register(Monster(Grunt));

            _runtime = new MonsterWorldRuntime(
                new WorldCharacterRegistry(new FakeStore(), spawns),
                monsters, new DefinitionId(MaxHp), new CombatTeam(2));

            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(MonsterPrefabPath);

            Assert.That(prefab, Is.Not.Null, "no registered monster prefab");

            _replication = new MonsterReplicationService(_server, _runtime,
                prefab.GetComponent<NetworkObject>());
        }

        [TearDown]
        public void TearDown()
        {
            _replication?.DespawnAll();

            if (_client != null) _client.ClientManager.StopConnection();
            if (_server != null) _server.ServerManager.StopConnection(true);

            if (_clientObject != null) Object.DestroyImmediate(_clientObject);
            if (_serverObject != null) Object.DestroyImmediate(_serverObject);

            foreach (Object created in _created)
            {
                if (created != null) Object.DestroyImmediate(created);
            }

            _created.Clear();
        }

        private MonsterDefinition Monster(string id)
        {
            var definition = ScriptableObject.CreateInstance<MonsterDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_level\":5,\"_aggressionType\":2,"
                + "\"_detectionRange\":10,\"_attackRange\":2,\"_attackCooldownSeconds\":2,"
                + "\"_leashRange\":50,\"_moveSpeed\":2,"
                + "\"_respawn\":{\"_respawnDelaySeconds\":0,\"_maxAliveInArea\":1}}", definition);

            SetPrivate(definition, "_baseStats",
                new[] { new StatValue(new DefinitionId(MaxHp), 100f) });

            _created.Add(definition);

            return definition;
        }

        private static void SetPrivate(Object target, string field, object value)
        {
            System.Type type = target.GetType();

            while (type != null)
            {
                System.Reflection.FieldInfo info = type.GetField(field,
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.DeclaredOnly);

                if (info != null)
                {
                    info.SetValue(target, value);

                    return;
                }

                type = type.BaseType;
            }
        }

        private static MonsterSpawnPoint Nest()
        {
            return new MonsterSpawnPoint(new DefinitionId(Grunt),
                new CombatPosition(3f, 0f, 4f), 0f, 1, 0f, new DefinitionId(HomeMap));
        }

        private static IEnumerator Until(System.Func<bool> condition, int frames = 300)
        {
            for (int i = 0; i < frames && !condition(); i++) yield return null;
        }

        private IEnumerator StartServerAndClient()
        {
            Assert.That(_server.ServerManager.StartConnection(), Is.True, "server did not start");

            yield return Until(() => _server.ServerManager.Started);

            Assert.That(_client.ClientManager.StartConnection(), Is.True, "client did not start");

            yield return Until(() => _client.ClientManager.Started);

            Assert.That(_client.ClientManager.Started, Is.True, "client never connected");
        }

        /// <summary>The monster entity the client can see, if any.</summary>
        private MonsterNetworkEntity ClientObserved()
        {
            foreach (KeyValuePair<int, NetworkObject> pair in _client.ClientManager.Objects.Spawned)
            {
                var entity = pair.Value == null
                    ? null
                    : pair.Value.GetComponent<MonsterNetworkEntity>();

                if (entity != null) return entity;
            }

            return null;
        }

        // ---- 4, 5: server spawns, client observes ------------------------------------------

        [UnityTest]
        public IEnumerator TheServerSpawnsAnAuthoritativeObjectAndTheClientObservesIt()
        {
            yield return StartServerAndClient();

            _runtime.AddSpawnPoint(Nest());
            _runtime.PopulateAll();

            Assert.That(_runtime.AliveCount, Is.EqualTo(1), "precondition");

            Assert.That(_replication.Synchronise(), Is.EqualTo(1));
            Assert.That(_replication.SpawnedCount, Is.EqualTo(1));

            yield return Until(() => ClientObserved() != null);

            MonsterNetworkEntity observed = ClientObserved();

            Assert.That(observed, Is.Not.Null, "the client never saw the monster");
            Assert.That(observed.Definition.Value, Is.EqualTo(Grunt),
                "and it knows what it is looking at");
            Assert.That(observed.Map.Value, Is.EqualTo(HomeMap));
            Assert.That(observed.MaxHealth, Is.EqualTo(100));
            Assert.That(observed.IsAlive, Is.True);
        }

        [UnityTest]
        public IEnumerator TheObservedPositionIsTheOneTheServerDecided()
        {
            yield return StartServerAndClient();

            _runtime.AddSpawnPoint(Nest());
            _runtime.PopulateAll();
            _replication.Synchronise();

            yield return Until(() => ClientObserved() != null && ClientObserved().X != 0f);

            MonsterNetworkEntity observed = ClientObserved();

            Assert.That(observed, Is.Not.Null);
            Assert.That(observed.X, Is.EqualTo(3f).Within(0.001f));
            Assert.That(observed.Z, Is.EqualTo(4f).Within(0.001f));
        }

        [UnityTest]
        public IEnumerator TheClientSeesHealthTheServerChanged()
        {
            yield return StartServerAndClient();

            _runtime.AddSpawnPoint(Nest());
            _runtime.PopulateAll();
            _replication.Synchronise();

            yield return Until(() => ClientObserved() != null);

            LivingMonster monster = _runtime.All()[0];

            // Damage applied by the authoritative runtime, not by replication.
            monster.State.ApplyHealthDelta(-40);
            _replication.Synchronise();

            yield return Until(() => ClientObserved() != null && ClientObserved().Health == 60);

            Assert.That(ClientObserved().Health, Is.EqualTo(60));
        }

        // ---- 6: the client cannot become authoritative ---------------------------------------

        [UnityTest]
        public IEnumerator AClientWritingToTheEntityChangesNothingOnTheServer()
        {
            yield return StartServerAndClient();

            _runtime.AddSpawnPoint(Nest());
            _runtime.PopulateAll();
            _replication.Synchronise();

            yield return Until(() => ClientObserved() != null);

            MonsterNetworkEntity observed = ClientObserved();
            LivingMonster monster = _runtime.All()[0];

            int serverHealthBefore = monster.State.CurrentHealth;
            float serverXBefore = monster.State.Position.X;

            // The client tries the only thing it could: calling the server-guarded publisher.
            // FishNet's [Server] attribute refuses it on a client, and the write permission
            // would make any local change stay local anyway.
            LogAssert.ignoreFailingMessages = true;

            try
            {
                observed.ServerPublishState(999f, 999f, 999f, 1);
            }
            catch (System.Exception)
            {
                // Refused is the correct outcome; how loudly is FishNet's business.
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }

            yield return null;

            Assert.That(monster.State.CurrentHealth, Is.EqualTo(serverHealthBefore),
                "a client must not be able to set health");
            Assert.That(monster.State.Position.X, Is.EqualTo(serverXBefore),
                "nor position");
        }

        // ---- 7: despawn ------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator RemovingAMonsterDespawnsItsObjectOnTheClient()
        {
            yield return StartServerAndClient();

            _runtime.AddSpawnPoint(Nest());
            _runtime.PopulateAll();
            _replication.Synchronise();

            yield return Until(() => ClientObserved() != null);

            Assert.That(ClientObserved(), Is.Not.Null, "precondition");

            // The whole world goes away, which is how a shutdown or an area reset looks.
            _runtime.Clear();

            Assert.That(_replication.Synchronise(), Is.EqualTo(1), "one despawn");
            Assert.That(_replication.SpawnedCount, Is.Zero);

            yield return Until(() => ClientObserved() == null);

            Assert.That(ClientObserved(), Is.Null, "the client still sees a ghost");
        }

        [UnityTest]
        public IEnumerator DespawnAllLeavesNothingBehind()
        {
            yield return StartServerAndClient();

            _runtime.AddSpawnPoint(Nest());
            _runtime.PopulateAll();
            _replication.Synchronise();

            yield return Until(() => ClientObserved() != null);

            Assert.That(_replication.DespawnAll(), Is.EqualTo(1));

            yield return Until(() => ClientObserved() == null);

            Assert.That(ClientObserved(), Is.Null);
            Assert.That(_replication.SpawnedCount, Is.Zero);
        }

        // ---- lifecycle -------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator SynchronisingTwiceDoesNotSpawnTwice()
        {
            yield return StartServerAndClient();

            _runtime.AddSpawnPoint(Nest());
            _runtime.PopulateAll();

            _replication.Synchronise();
            _replication.Synchronise();
            _replication.Synchronise();

            yield return Until(() => ClientObserved() != null);

            Assert.That(_replication.SpawnedCount, Is.EqualTo(1),
                "one monster is one object, however often the tick runs");
        }

        [UnityTest]
        public IEnumerator NothingIsSpawnedWhileTheServerIsStopped()
        {
            // No StartServerAndClient: the server never listens.
            _runtime.AddSpawnPoint(Nest());
            _runtime.PopulateAll();

            Assert.That(_replication.Synchronise(), Is.Zero);
            Assert.That(_replication.SpawnedCount, Is.Zero);

            yield return null;
        }

        [UnityTest]
        public IEnumerator TheMonsterRuntimeWorksWithNoReplicationAtAll()
        {
            // Replication is presentation. A monster with no network object is still a fully
            // working monster, which is the test that this layer decides nothing.
            _runtime.AddSpawnPoint(Nest());
            _runtime.PopulateAll();

            MonsterTickResult result = _runtime.Tick(0.5f);

            Assert.That(_runtime.AliveCount, Is.EqualTo(1));
            Assert.That(result.Retired, Is.Zero);

            yield return null;
        }
    }
}

#endif
