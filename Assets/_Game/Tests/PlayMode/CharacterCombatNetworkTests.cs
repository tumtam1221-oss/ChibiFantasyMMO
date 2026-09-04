// Editor-only, and only this fixture, for the reason MonsterReplicationTests documents:
// it loads the committed prefab registry through AssetDatabase, because the point is to
// prove the SHIPPED configuration works. Making the whole assembly Editor-only was tried in
// 17.2 and was wrong -- Unity reclassifies it and the PlayMode tests silently stop running.
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
using FishNet.Connection;
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
    /// A real client asking a real server to attack, and getting back what the server decided.
    /// </summary>
    /// <remarks>
    /// <b>This is the link 18.1 said was missing.</b> That gate proved the server could kill
    /// a monster and pay for it; it could not prove a client had any way to ask, because
    /// there was no character network object and no combat message anywhere in the project.
    /// Here the request crosses a real socket, through a real <c>ServerRpc</c>, on an object
    /// the server spawned for that connection.
    ///
    /// <b>Integration-only bootstrap, and labelled as such.</b> The character is admitted
    /// directly into the world registry rather than through login, server select, channel
    /// select and enter-world -- that flow exists and has its own live suite against real
    /// PHP, and dragging it in here would test it a third time instead of testing this. What
    /// is real: the socket, the ownership, the RPC, the pipeline, the damage, the death, the
    /// experience, the loot and the replication back.
    /// </remarks>
    [TestFixture]
    internal sealed class CharacterCombatNetworkTests
    {
        private const string RegistryPath = "Assets/DefaultPrefabObjects.asset";
        private const string MonsterPrefabPath =
            "Assets/_Game/Prefabs/Network/WorldEntity_Monster.prefab";
        private const string CharacterPrefabPath =
            "Assets/_Game/Prefabs/Network/WorldEntity_Character.prefab";

        private const string MaxHp = "stat.maxhp";
        private const string Atk = "stat.atk";
        private const string Def = "stat.def";
        private const string HomeMap = "map.home";
        private const string OtherMap = "map.other";
        private const string Grunt = "monster.grunt";
        private const string Coin = "item.coin";
        private const string Table = "drop.grunt";
        private const string Character = "char-a";

        private sealed class FakeStore : ICharacterStateStore
        {
            public readonly Dictionary<string, PersistedCharacter> Rows =
                new Dictionary<string, PersistedCharacter>();

            public int Saves;

            public CharacterPersistenceResult Load(SessionId s)
            {
                return Rows.TryGetValue(s.Value, out PersistedCharacter row)
                    ? CharacterPersistenceResult.Loaded(row)
                    : CharacterPersistenceResult.Failed(CharacterPersistenceFailure.NotOwned);
            }

            public CharacterPersistenceResult Save(SessionId s, PersistedCharacter c, int r)
            {
                Saves++;

                return CharacterPersistenceResult.Saved(r + 1);
            }
        }

        private GameObject _serverObject;
        private GameObject _clientObject;
        private NetworkManager _server;
        private NetworkManager _client;

        private FakeStore _store;
        private WorldCharacterRegistry _players;
        private MonsterWorldRuntime _runtime;
        private MonsterLootRegistry _loot;
        private MonsterRewardAuthority _rewards;
        private ServerCombatPipeline _pipeline;
        private CharacterCombatRequestHandler _handler;
        private MonsterReplicationService _monsterReplication;
        private CharacterReplicationService _characterReplication;

        private readonly List<Object> _created = new List<Object>();
        private ushort _port;

        private static ushort NextPort() => (ushort)Random.Range(34100, 38000);

        [SetUp]
        public void SetUp()
        {
            _port = NextPort();

            _server = BuildManager("CombatServer", true, out _serverObject);
            _serverObject.SetActive(true);

            _client = BuildManager("CombatClient", false, out _clientObject);
            _clientObject.SetActive(true);

            var items = new DefinitionRegistry<ItemDefinition>();
            items.Register(Item(Coin));

            var tables = new DefinitionRegistry<DropTableDefinition>();
            tables.Register(DropTable(Table));

            var monsters = new DefinitionRegistry<MonsterDefinition>();
            monsters.Register(Monster(Grunt));

            var spawns = new DefinitionRegistry<SpawnPointDefinition>();
            spawns.Register(PlayerSpawn());

            _store = new FakeStore();
            _players = new WorldCharacterRegistry(_store, spawns, items, 20);

            _runtime = new MonsterWorldRuntime(_players, monsters, new DefinitionId(MaxHp),
                new CombatTeam(2));

            _loot = new MonsterLootRegistry(_players, items);

            _rewards = new MonsterRewardAuthority(_runtime, _players, Curve(), _loot, items,
                tables);

            var commands = new CombatCommandAuthority(_players, _ => true, _runtime);

            _pipeline = new ServerCombatPipeline(commands, _runtime, _rewards,
                BasicAttackRules.Melee(new DefinitionId(Atk), new DefinitionId(Def), 1, 8f));

            _monsterReplication = new MonsterReplicationService(_server, _runtime,
                Prefab(MonsterPrefabPath));

            // The handler drives replication after each request, which is what the server's
            // own tick does: a result the client cannot see has not reached the player.
            _handler = new CharacterCombatRequestHandler(_pipeline, () =>
            {
                _characterReplication.Synchronise();
                _monsterReplication.Synchronise();
            });

            _characterReplication = new CharacterReplicationService(_server, _players,
                Prefab(CharacterPrefabPath), _handler);
        }

        [TearDown]
        public void TearDown()
        {
            _characterReplication?.DespawnAll();
            _monsterReplication?.DespawnAll();

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

        // ---- harness ---------------------------------------------------------------------

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

        private static NetworkObject Prefab(string path)
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);

            Assert.That(prefab, Is.Not.Null, "no prefab at " + path);

            return prefab.GetComponent<NetworkObject>();
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

            yield return Until(() => _client.ClientManager.Started
                && _server.ServerManager.Clients.Count > 0);

            Assert.That(_server.ServerManager.Clients.Count, Is.GreaterThan(0),
                "the server never saw the client connect");
        }

        /// <summary>The connection id the server gave this client. Never client-supplied.</summary>
        private int ConnectionId()
        {
            foreach (KeyValuePair<int, NetworkConnection> pair in _server.ServerManager.Clients)
            {
                return pair.Key;
            }

            Assert.Fail("no connected client");

            return -1;
        }

        /// <summary>Admits the character into the world on the real connection.</summary>
        private LivingCharacter EnterWorld(string character = Character,
            string map = HomeMap, int level = 5, long experience = 0)
        {
            int connection = ConnectionId();

            _store.Rows["session-" + character] = new PersistedCharacter(
                new CharacterId(character), new AccountId("acc-a"), new ServerId("srv-1"),
                "Ayla", 2, level, experience, 100, 50, new DefinitionId("class.novice"),
                default,
                new DefinitionId(map), default,
                new[]
                {
                    new PersistedStat(new DefinitionId(MaxHp), 100),
                    new PersistedStat(new DefinitionId(Atk), 25),
                    new PersistedStat(new DefinitionId(Def), 5),
                },
                null, null, 1);

            WorldSpawnResult spawned = _players.Spawn(connection,
                WorldAdmission.Admitted(new SessionId("session-" + character),
                    new AccountId("acc-a"), new CharacterId(character),
                    new ServerId("srv-1"), new ChannelId("ch-1"), new DefinitionId(map),
                    new Revision(1), new Revision(1), SessionState.EnteringWorld),
                new ResourceLimits(100, 50), new CombatTeam(1));

            Assert.That(spawned.IsSpawned, Is.True, spawned.Detail);

            return spawned.Character;
        }

        private LivingMonster SpawnMonster(string map = HomeMap)
        {
            _runtime.AddSpawnPoint(new MonsterSpawnPoint(new DefinitionId(Grunt),
                new CombatPosition(1f, 0f, 0f), 0f, 1, 0f, new DefinitionId(map)));

            _runtime.PopulateAll();

            foreach (LivingMonster living in _runtime.All())
            {
                if (living.IsAlive) return living;
            }

            Assert.Fail("nothing spawned");

            return null;
        }

        /// <summary>The character object this client owns, as the client sees it.</summary>
        private CharacterNetworkEntity ClientCharacter()
        {
            foreach (KeyValuePair<int, NetworkObject> pair in _client.ClientManager.Objects.Spawned)
            {
                var entity = pair.Value == null
                    ? null
                    : pair.Value.GetComponent<CharacterNetworkEntity>();

                if (entity != null) return entity;
            }

            return null;
        }

        /// <summary>The monster this client can see, if any.</summary>
        private MonsterNetworkEntity ClientMonster()
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

        // ---- the character object -----------------------------------------------------------

        [UnityTest]
        public IEnumerator AnAdmittedCharacterIsSpawnedAndOwnedByItsOwnConnection()
        {
            yield return StartServerAndClient();

            EnterWorld();

            Assert.That(_characterReplication.Synchronise(), Is.EqualTo(1));

            yield return Until(() => ClientCharacter() != null);

            CharacterNetworkEntity observed = ClientCharacter();

            Assert.That(observed, Is.Not.Null, "the client never saw its character");
            Assert.That(observed.Character.Value, Is.EqualTo(Character));
            Assert.That(observed.IsOwner, Is.True,
                "ownership is what lets this client -- and only this client -- ask");
        }

        [UnityTest]
        public IEnumerator TheReplicatedCharacterCarriesTheServersHealthLevelAndExperience()
        {
            yield return StartServerAndClient();

            LivingCharacter character = EnterWorld();

            _characterReplication.Synchronise();

            yield return Until(() => ClientCharacter() != null
                && ClientCharacter().MaxHealth > 0);

            CharacterNetworkEntity observed = ClientCharacter();

            Assert.That(observed.Health, Is.EqualTo(100));
            Assert.That(observed.MaxHealth, Is.EqualTo(100));
            Assert.That(observed.Level, Is.EqualTo(5));
            Assert.That(observed.Experience, Is.Zero);
            Assert.That(observed.Map.Value, Is.EqualTo(HomeMap));
            Assert.That(observed.IsAlive, Is.True);

            // The server's position, not a transform the client moved.
            Assert.That(observed.X, Is.EqualTo(character.Combatant.Position.X).Within(0.001f));
        }

        [UnityTest]
        public IEnumerator ACharacterWithNoConnectionGetsNoObject()
        {
            yield return StartServerAndClient();

            // Admitted on a connection the server does not hold.
            _store.Rows["session-ghost"] = new PersistedCharacter(
                new CharacterId("char-ghost"), new AccountId("acc-a"),
                new ServerId("srv-1"), "Ghost", 2, 5, 0, 100, 50,
                new DefinitionId("class.novice"), default, new DefinitionId(HomeMap),
                default, null, null, null, 1);

            _players.Spawn(9999,
                WorldAdmission.Admitted(new SessionId("session-ghost"),
                    new AccountId("acc-a"), new CharacterId("char-ghost"),
                    new ServerId("srv-1"), new ChannelId("ch-1"),
                    new DefinitionId(HomeMap), new Revision(1), new Revision(1),
                    SessionState.EnteringWorld),
                new ResourceLimits(100, 50), new CombatTeam(1));

            Assert.That(_characterReplication.Synchronise(), Is.Zero,
                "an unowned character object is one anybody could talk through");
            Assert.That(_characterReplication.SpawnedCount, Is.Zero);

            yield return null;
        }

        // ---- the real request ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator AClientAttackCrossesTheWireAndTheServerDecidesTheDamage()
        {
            yield return StartServerAndClient();

            EnterWorld();

            LivingMonster monster = SpawnMonster();

            _characterReplication.Synchronise();
            _monsterReplication.Synchronise();

            yield return Until(() => ClientCharacter() != null && ClientMonster() != null);

            Assert.That(ClientMonster().Health, Is.EqualTo(100), "precondition");

            // The client asks. It names a target and nothing else.
            ClientCharacter().RequestAttack(monster.Instance.Value, string.Empty, 0, 1);

            yield return Until(() => _handler.Handled > 0);

            Assert.That(_handler.Handled, Is.EqualTo(1),
                "the request never reached the server");
            Assert.That(_handler.LastResult.IsAccepted, Is.True,
                _handler.LastResult.ToString());
            Assert.That(_handler.LastResult.Damage, Is.EqualTo(20),
                "attack 25 less the monster's defence of 5, decided server-side");
            Assert.That(monster.State.CurrentHealth, Is.EqualTo(80));

            yield return Until(() => ClientMonster() != null && ClientMonster().Health == 80);

            Assert.That(ClientMonster().Health, Is.EqualTo(80),
                "and the client sees the health the server decided");
        }

        [UnityTest]
        public IEnumerator AClientCanFightAMonsterToDeathAndSeeWhatItEarned()
        {
            yield return StartServerAndClient();

            LivingCharacter character = EnterWorld();

            LivingMonster monster = SpawnMonster();

            _characterReplication.Synchronise();
            _monsterReplication.Synchronise();

            yield return Until(() => ClientCharacter() != null && ClientMonster() != null);

            var sequence = 0;

            while (monster.IsAlive && sequence < 20)
            {
                ClientCharacter().RequestAttack(monster.Instance.Value, string.Empty, 0,
                    ++sequence);

                yield return Until(() => _handler.Handled >= sequence);
            }

            Assert.That(monster.IsAlive, Is.False, "the client never managed to kill it");
            Assert.That(_handler.LastResult.TargetDefeated, Is.True);

            // 17.14 paid, once.
            Assert.That(_handler.LastResult.ExperienceGranted, Is.EqualTo(50));
            Assert.That(character.Domain.Progression.Experience, Is.EqualTo(50));
            Assert.That(_rewards.GrantedCount, Is.EqualTo(1));

            // 17.15 dropped, once, and the pile is in the world rather than in a bag.
            Assert.That(_handler.LastResult.LootCount, Is.EqualTo(1));
            Assert.That(_loot.Count, Is.EqualTo(1));
            Assert.That(character.Inventory.OccupiedSlots, Is.Zero);

            // The client sees its own earnings, replicated.
            yield return Until(() => ClientCharacter() != null
                && ClientCharacter().Experience == 50);

            Assert.That(ClientCharacter().Experience, Is.EqualTo(50));

            // And the corpse leaves through the existing lifecycle.
            _runtime.Tick(0.1f);
            _monsterReplication.Synchronise();

            yield return Until(() => ClientMonster() == null || !ClientMonster().IsAlive);

            Assert.That(_runtime.AliveCount, Is.EqualTo(1),
                "the nest refilled, which is the authored respawn doing its job");
        }

        [UnityTest]
        public IEnumerator ARepeatedRequestCannotDamageOrPayTwice()
        {
            yield return StartServerAndClient();

            LivingCharacter character = EnterWorld();

            LivingMonster monster = SpawnMonster();

            _characterReplication.Synchronise();
            _monsterReplication.Synchronise();

            yield return Until(() => ClientCharacter() != null);

            ClientCharacter().RequestAttack(monster.Instance.Value, string.Empty, 0, 1);

            yield return Until(() => _handler.Handled > 0);

            int afterFirst = monster.State.CurrentHealth;

            // The same sequence again: a duplicated packet, a retried send.
            ClientCharacter().RequestAttack(monster.Instance.Value, string.Empty, 0, 1);

            yield return Until(() => _handler.Handled > 1);

            Assert.That(_handler.LastResult.IsAccepted, Is.False);
            Assert.That(_handler.LastResult.Rejection,
                Is.EqualTo(CombatCommandRejection.OutOfOrder));
            Assert.That(monster.State.CurrentHealth, Is.EqualTo(afterFirst),
                "the same swing must not land twice");
            Assert.That(character.Domain.Progression.Experience, Is.Zero);
        }

        [UnityTest]
        public IEnumerator AClientCannotAttackAMonsterOnAnotherMap()
        {
            yield return StartServerAndClient();

            EnterWorld(map: HomeMap);

            LivingMonster elsewhere = SpawnMonster(OtherMap);

            _characterReplication.Synchronise();

            yield return Until(() => ClientCharacter() != null);

            ClientCharacter().RequestAttack(elsewhere.Instance.Value, string.Empty, 0, 1);

            yield return Until(() => _handler.Handled > 0);

            Assert.That(_handler.LastResult.Rejection,
                Is.EqualTo(CombatCommandRejection.DifferentMap));
            Assert.That(elsewhere.State.CurrentHealth, Is.EqualTo(100));
        }

        [UnityTest]
        public IEnumerator AClientCannotAttackSomethingTheServerDoesNotHold()
        {
            yield return StartServerAndClient();

            EnterWorld();

            _characterReplication.Synchronise();

            yield return Until(() => ClientCharacter() != null);

            ClientCharacter().RequestAttack("monster-i-invented", string.Empty, 0, 1);

            yield return Until(() => _handler.Handled > 0);

            Assert.That(_handler.LastResult.Rejection,
                Is.EqualTo(CombatCommandRejection.UnknownTarget));
        }

        [UnityTest]
        public IEnumerator AClientCannotWriteItsOwnHealthOrExperience()
        {
            yield return StartServerAndClient();

            LivingCharacter character = EnterWorld();

            _characterReplication.Synchronise();

            yield return Until(() => ClientCharacter() != null
                && ClientCharacter().Level == 5);

            // The only writes that exist are server-guarded. FishNet refuses them on a
            // client, and server-only write permission would keep any local change local.
            LogAssert.ignoreFailingMessages = true;

            try
            {
                ClientCharacter().ServerPublishState(99f, 99f, 99f, 1, 60, 999999);
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

            _characterReplication.Synchronise();

            yield return null;

            Assert.That(character.Domain.Progression.Level, Is.EqualTo(5),
                "a client cannot level itself");
            Assert.That(character.Domain.Progression.Experience, Is.Zero);
            Assert.That(character.Combatant.CurrentHealth, Is.EqualTo(100));
            Assert.That(ClientCharacter().Level, Is.EqualTo(5),
                "and what it observes is still the server's number");
        }

        // ---- leaving and coming back ---------------------------------------------------------

        [UnityTest]
        public IEnumerator LeavingDespawnsTheObjectAndKeepsWhatWasEarned()
        {
            yield return StartServerAndClient();

            LivingCharacter character = EnterWorld();

            LivingMonster monster = SpawnMonster();

            _characterReplication.Synchronise();
            _monsterReplication.Synchronise();

            yield return Until(() => ClientCharacter() != null);

            var sequence = 0;

            while (monster.IsAlive && sequence < 20)
            {
                ClientCharacter().RequestAttack(monster.Instance.Value, string.Empty, 0,
                    ++sequence);

                yield return Until(() => _handler.Handled >= sequence);
            }

            int level = character.Domain.Progression.Level;
            long earned = character.Domain.Progression.Experience;

            Assert.That(earned, Is.EqualTo(50), "precondition: it earned something");

            // The player leaves. The existing registry saves and removes them.
            _players.Despawn(character.ConnectionId);

            Assert.That(_characterReplication.Synchronise(), Is.EqualTo(1), "one despawn");
            Assert.That(_characterReplication.SpawnedCount, Is.Zero);

            yield return Until(() => ClientCharacter() == null);

            Assert.That(_store.Saves, Is.GreaterThan(0),
                "leaving must have written the progression down");

            // They come back, and what the database holds is what they get.
            LivingCharacter returned = EnterWorld(level: level, experience: earned);

            Assert.That(returned.Domain.Progression.Experience, Is.EqualTo(earned),
                "reconnecting is not a reset");
            Assert.That(returned.Domain.Progression.Level, Is.EqualTo(level));

            // And the kill it already paid for cannot pay again.
            Assert.That(_rewards.GrantedCount, Is.EqualTo(1));
            Assert.That(_loot.Count, Is.EqualTo(1), "one pile, not two");
        }

        // ---- fixtures -------------------------------------------------------------------------

        private CharacterProgressionDefinition Curve()
        {
            var definition = ScriptableObject.CreateInstance<CharacterProgressionDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"progression.net\"},\"_minLevel\":1,\"_maxLevel\":20,"
                + "\"_experienceToNextLevel\":[100,100,100,100,100,100,100,100,100,100,100,"
                + "100,100,100,100,100,100,100,100]}", definition);

            _created.Add(definition);

            return definition;
        }

        private SpawnPointDefinition PlayerSpawn()
        {
            var spawn = ScriptableObject.CreateInstance<SpawnPointDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"spawn.home\"},\"_map\":{\"_value\":\"" + HomeMap
                + "\"},\"_spawnType\":" + (int)SpawnType.Player
                + ",\"_x\":0,\"_y\":0,\"_z\":0}", spawn);

            _created.Add(spawn);

            return spawn;
        }

        private ItemDefinition Item(string id)
        {
            var definition = ScriptableObject.CreateInstance<ItemDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_stackable\":true,"
                + "\"_maxStackSize\":999}", definition);

            _created.Add(definition);

            return definition;
        }

        private DropTableDefinition DropTable(string id)
        {
            var definition = ScriptableObject.CreateInstance<DropTableDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_maxEntries\":0}", definition);

            SetPrivate(definition, "_entries", new[]
            {
                new DropEntry(new DefinitionId(Coin), 1, 1),
            });

            _created.Add(definition);

            return definition;
        }

        private MonsterDefinition Monster(string id)
        {
            var definition = ScriptableObject.CreateInstance<MonsterDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_level\":5,\"_aggressionType\":0,"
                + "\"_experienceReward\":50,\"_attackRange\":2,\"_moveSpeed\":0,"
                + "\"_lootTable\":{\"_value\":\"" + Table + "\"},"
                + "\"_respawn\":{\"_respawnDelaySeconds\":0,\"_maxAliveInArea\":1}}",
                definition);

            SetPrivate(definition, "_baseStats", new[]
            {
                new StatValue(new DefinitionId(MaxHp), 100f),
                new StatValue(new DefinitionId(Atk), 10f),
                new StatValue(new DefinitionId(Def), 5f),
            });

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

            Assert.Fail("no field '" + field + "' on " + target.GetType().Name);
        }
    }
}

#endif
