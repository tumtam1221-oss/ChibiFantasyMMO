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
            /// <summary>Rows a test seeded. Empty means no character can enter.</summary>
            public readonly Dictionary<string, PersistedCharacter> Rows =
                new Dictionary<string, PersistedCharacter>();

            public CharacterPersistenceResult Load(SessionId s)
            {
                return Rows.TryGetValue(s.Value, out PersistedCharacter row)
                    ? CharacterPersistenceResult.Loaded(row)
                    : CharacterPersistenceResult.Failed(CharacterPersistenceFailure.NotOwned);
            }

            public CharacterPersistenceResult Save(SessionId s, PersistedCharacter c, int r) =>
                CharacterPersistenceResult.Saved(r + 1);
        }

        private GameObject _serverObject;
        private GameObject _clientObject;
        private NetworkManager _server;
        private NetworkManager _client;
        private MonsterWorldRuntime _runtime;
        private MonsterReplicationService _replication;
        private DefinitionRegistry<MapDefinition> _maps;
        private WorldCharacterRegistry _players;
        private FakeStore _store;
        private DefinitionRegistry<MonsterDefinition> _monsters;
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
            spawns.Register(PlayerSpawn());

            _monsters = new DefinitionRegistry<MonsterDefinition>();
            _monsters.Register(Monster(Grunt));

            _maps = new DefinitionRegistry<MapDefinition>();
            _maps.Register(Map(HomeMap));

            _store = new FakeStore();
            _players = new WorldCharacterRegistry(_store, spawns);

            _runtime = new MonsterWorldRuntime(_players, _monsters,
                new DefinitionId(MaxHp), new CombatTeam(2));

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

        /// <summary>A player spawn on the monster's map, so a character can enter.</summary>
        private SpawnPointDefinition PlayerSpawn()
        {
            var spawn = ScriptableObject.CreateInstance<SpawnPointDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"spawn.home\"},\"_map\":{\"_value\":\""
                + HomeMap + "\"},\"_spawnType\":" + (int)SpawnType.Player
                + ",\"_x\":3,\"_y\":0,\"_z\":4}", spawn);

            _created.Add(spawn);

            return spawn;
        }

        private MapDefinition Map(string id)
        {
            var definition = ScriptableObject.CreateInstance<MapDefinition>();

            JsonUtility.FromJsonOverwrite("{\"_id\":{\"_value\":\"" + id + "\"}}",
                definition);

            _created.Add(definition);

            return definition;
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

        // ---- 18.1: a real kill, over a real socket -------------------------------------------

        private const string Atk = "stat.atk";
        private const string Def = "stat.def";

        /// <summary>Puts the fixture character in the world, standing on the monster.</summary>
        private LivingCharacter EnterCharacter()
        {
            _store.Rows["session-a"] = new PersistedCharacter(
                new CharacterId("char-a"), new AccountId("acc-a"), new ServerId("srv-1"),
                "Ayla", 2, 5, 0, 100, 50, new DefinitionId("class.novice"), default,
                new DefinitionId(HomeMap), default,
                new[]
                {
                    new PersistedStat(new DefinitionId(MaxHp), 100),
                    new PersistedStat(new DefinitionId(Atk), 30),
                    new PersistedStat(new DefinitionId(Def), 5),
                },
                null, null, 1);

            WorldSpawnResult spawned = _players.Spawn(1,
                WorldAdmission.Admitted(new SessionId("session-a"), new AccountId("acc-a"),
                    new CharacterId("char-a"), new ServerId("srv-1"), new ChannelId("ch-1"),
                    new DefinitionId(HomeMap), new Revision(1), new Revision(1),
                    SessionState.EnteringWorld),
                new ResourceLimits(100, 50), new CombatTeam(1));

            Assert.That(spawned.IsSpawned, Is.True, spawned.Detail);

            return spawned.Character;
        }

        /// <summary>
        /// The production composition: a command boundary, the runtime, and a reward
        /// authority.
        /// </summary>
        /// <remarks>The reward authority is not optional in practice even on a server that
        /// pays nothing, because it owns the defeat claim -- and Phase 10 deliberately
        /// refuses to retire a corpse whose defeat nobody claimed, so a world composed
        /// without one accumulates monsters that are dead and will never go away.</remarks>
        private ServerCombatPipeline NewPipeline()
        {
            var commands = new CombatCommandAuthority(_players, _ => true, _runtime);

            var rewards = new MonsterRewardAuthority(_runtime, _players, Curve());

            return new ServerCombatPipeline(commands, _runtime, rewards,
                BasicAttackRules.Melee(new DefinitionId(Atk), new DefinitionId(Def), 1, 5f));
        }

        /// <summary>A level curve, so the reward authority can apply what it grants.</summary>
        private CharacterProgressionDefinition Curve()
        {
            var definition = ScriptableObject.CreateInstance<CharacterProgressionDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"progression.playmode\"},\"_minLevel\":1,"
                + "\"_maxLevel\":10,\"_experienceToNextLevel\":[100,100,100,100,100,100,"
                + "100,100,100]}", definition);

            _created.Add(definition);

            return definition;
        }

        [UnityTest]
        public IEnumerator AServerSideAttackIsVisibleToTheClientAsLostHealth()
        {
            yield return StartServerAndClient();

            LivingCharacter attacker = EnterCharacter();

            _runtime.AddSpawnPoint(Nest());
            _runtime.PopulateAll();
            _replication.Synchronise();

            yield return Until(() => ClientObserved() != null);

            MonsterNetworkEntity observed = ClientObserved();

            Assert.That(observed.Health, Is.EqualTo(100), "precondition");

            LivingMonster monster = _runtime.All()[0];

            ServerCombatResult result = NewPipeline().Execute(1,
                new CombatCommand(default, monster.Instance, default, 0, 1));

            Assert.That(result.IsAccepted, Is.True, result.ToString());
            Assert.That(result.Damage, Is.GreaterThan(0));

            _replication.Synchronise();

            yield return Until(() => ClientObserved() != null
                && ClientObserved().Health < 100);

            Assert.That(ClientObserved().Health,
                Is.EqualTo(monster.State.CurrentHealth),
                "the client sees the health the server decided, over a real socket");
            Assert.That(attacker.Combatant.CombatantId.Value, Is.EqualTo("char-a"));
        }

        [UnityTest]
        public IEnumerator AKilledMonsterDespawnsOnTheClientThroughTheExistingLifecycle()
        {
            yield return StartServerAndClient();

            EnterCharacter();

            // A nest with a respawn delay, so what the client stops seeing is the corpse
            // rather than a replacement arriving in the same tick. The default fixture nest
            // refills immediately, which is correct behaviour and would hide this.
            _runtime.AddSpawnPoint(new MonsterSpawnPoint(new DefinitionId(Grunt),
                new CombatPosition(3f, 0f, 4f), 0f, 1, 30f, new DefinitionId(HomeMap)));

            _runtime.PopulateAll();
            _replication.Synchronise();

            yield return Until(() => ClientObserved() != null);

            LivingMonster monster = _runtime.All()[0];
            ServerCombatPipeline pipeline = NewPipeline();

            // Swing until it dies. Nothing here decides when that is: the damage formula
            // does, and the monster's authored health does.
            var sequence = 0;

            while (monster.IsAlive && sequence < 20)
            {
                pipeline.Execute(1, new CombatCommand(default, monster.Instance, default, 0,
                    ++sequence));
            }

            Assert.That(monster.IsAlive, Is.False, "it never died");
            Assert.That(monster.State.IsDefeatClaimed, Is.True,
                "the defeat was claimed by the pipeline, not by a tick");

            // The existing lifecycle retires it, and replication follows the runtime.
            _runtime.Tick(0.1f);
            _replication.Synchronise();

            yield return Until(() => ClientObserved() == null);

            Assert.That(ClientObserved(), Is.Null,
                "the client no longer sees a monster the server retired");
            Assert.That(_runtime.AliveCount, Is.Zero);
            Assert.That(_replication.SpawnedCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator AClientWritingHealthChangesNothingAboutTheFight()
        {
            yield return StartServerAndClient();

            EnterCharacter();

            _runtime.AddSpawnPoint(Nest());
            _runtime.PopulateAll();
            _replication.Synchronise();

            yield return Until(() => ClientObserved() != null);

            LivingMonster monster = _runtime.All()[0];

            NewPipeline().Execute(1,
                new CombatCommand(default, monster.Instance, default, 0, 1));

            int serverHealth = monster.State.CurrentHealth;

            // The client tries the only thing it could: the server-guarded publisher. The
            // [Server] attribute refuses it, and server-only write permission would keep
            // any local change local anyway.
            LogAssert.ignoreFailingMessages = true;

            try
            {
                ClientObserved().ServerPublishState(0f, 0f, 0f, 100);
            }
            catch (System.Exception)
            {
                // Refused is the correct outcome.
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }

            yield return null;

            _replication.Synchronise();

            yield return Until(() => ClientObserved() != null
                && ClientObserved().Health == serverHealth);

            Assert.That(monster.State.CurrentHealth, Is.EqualTo(serverHealth),
                "a client cannot heal what the server hurt");
            Assert.That(ClientObserved().Health, Is.EqualTo(serverHealth),
                "and what it observes is still the server's number");
        }

        // ---- lifecycle -------------------------------------------------------------------------------

        // ---- 41-48: what the database configured is what the client sees -----------------

        /// <summary>One nest, exactly as a row of monster_spawn_point describes it.</summary>
        private static MapSpawnConfiguration Configured(int maxAlive = 1, int initial = 1)
        {
            return new MapSpawnConfiguration(new DefinitionId(HomeMap), new[]
            {
                new MonsterSpawnConfiguration("row-1", new DefinitionId(HomeMap),
                    new DefinitionId(Grunt), 3f, 0f, 4f, 0f, initial, maxAlive, 0f),
            }, null);
        }

        [UnityTest]
        public IEnumerator AMonsterConfiguredInTheDatabaseReplicatesToTheClient()
        {
            yield return StartServerAndClient();

            SpawnConfigurationResult applied =
                _runtime.ApplyConfiguration(Configured(), _maps);

            Assert.That(applied.Accepted, Is.EqualTo(1), "configuration was not applied");
            Assert.That(_runtime.PopulateToConfiguredCount(), Is.EqualTo(1));
            Assert.That(_replication.Synchronise(), Is.EqualTo(1));

            yield return Until(() => ClientObserved() != null);

            MonsterNetworkEntity observed = ClientObserved();

            Assert.That(observed, Is.Not.Null,
                "a monster that exists only because of a database row must still be a "
                + "real replicated monster");
            Assert.That(observed.Definition.Value, Is.EqualTo(Grunt));
            Assert.That(observed.Map.Value, Is.EqualTo(HomeMap));

            // The position came from the configuration, through the server, to the client.
            yield return Until(() => ClientObserved() != null && ClientObserved().X != 0f);

            Assert.That(ClientObserved().X, Is.EqualTo(3f).Within(0.001f));
            Assert.That(ClientObserved().Z, Is.EqualTo(4f).Within(0.001f));
        }

        [UnityTest]
        public IEnumerator ReloadingConfigurationDoesNotRespawnWhatTheClientAlreadySees()
        {
            yield return StartServerAndClient();

            _runtime.ApplyConfiguration(Configured(), _maps);
            _runtime.PopulateToConfiguredCount();
            _replication.Synchronise();

            yield return Until(() => ClientObserved() != null);

            MonsterNetworkEntity before = ClientObserved();

            Assert.That(before, Is.Not.Null, "precondition");

            int objectId = before.NetworkObject.ObjectId;

            _runtime.ApplyConfiguration(Configured(), _maps);

            Assert.That(_runtime.PopulateToConfiguredCount(), Is.Zero);
            Assert.That(_replication.Synchronise(), Is.Zero,
                "a reload that respawned everything would flicker every monster on every "
                + "client");

            yield return null;

            Assert.That(_replication.SpawnedCount, Is.EqualTo(1));
            Assert.That(ClientObserved(), Is.Not.Null);
            Assert.That(ClientObserved().NetworkObject.ObjectId, Is.EqualTo(objectId),
                "it is the same object, not a replacement");
        }

        [UnityTest]
        public IEnumerator ARaisedMaxAliveSpawnsTheDifferenceAndReplicatesIt()
        {
            yield return StartServerAndClient();

            _runtime.ApplyConfiguration(Configured(), _maps);
            _runtime.PopulateToConfiguredCount();
            _replication.Synchronise();

            yield return Until(() => _replication.SpawnedCount == 1);

            _runtime.ApplyConfiguration(Configured(maxAlive: 3, initial: 3), _maps);

            Assert.That(_runtime.PopulateToConfiguredCount(), Is.EqualTo(2),
                "only the difference, not a fresh three");
            Assert.That(_replication.Synchronise(), Is.EqualTo(2));

            yield return Until(() => CountObserved() == 3);

            Assert.That(CountObserved(), Is.EqualTo(3),
                "an operator raising a number in the database is visible to players");
        }

        /// <summary>How many monsters the client can currently see.</summary>
        private int CountObserved()
        {
            int count = 0;

            foreach (KeyValuePair<int, NetworkObject> pair in _client.ClientManager.Objects.Spawned)
            {
                if (pair.Value != null
                    && pair.Value.GetComponent<MonsterNetworkEntity>() != null)
                {
                    count++;
                }
            }

            return count;
        }

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
