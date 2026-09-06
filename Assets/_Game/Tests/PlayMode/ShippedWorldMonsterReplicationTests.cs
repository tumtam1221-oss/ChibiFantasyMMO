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
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ChibiFantasy.Tests.PlayMode
{
    /// <summary>
    /// Whether a real client is told about the monsters the server is running.
    /// </summary>
    /// <remarks>
    /// <b>The defect this exists to stop coming back.</b> Until 18.18B the shipped
    /// <c>WorldServerBootstrap</c> composed <c>CharacterReplicationService</c> and never
    /// composed <c>MonsterReplicationService</c>. Monsters spawned, chased, fought, died and
    /// paid out -- entirely on the server, with no client ever receiving one. Every test
    /// that covered monster replication composed the service itself, so the gap lived
    /// exactly where nothing was looking: the production composition.
    ///
    /// <b>The shipped scene, a real socket, and counts.</b> This loads
    /// <c>World_Server</c> as it is committed, connects a real client to it, and counts the
    /// monster objects that actually arrive. Nothing here composes a replication service; if
    /// the scene stops wiring one, these fail.
    ///
    /// <b>What it does not claim.</b> The monster prefab carries a network identity and no
    /// renderer, because this project has no monster art. These tests prove that monster
    /// objects and their state reach the client; they say nothing about anything being
    /// drawn.
    /// </remarks>
    [TestFixture]
    internal sealed class ShippedWorldMonsterReplicationTests
    {
        private const string ServerScene = "Assets/_Game/Scenes/World/World_Server.unity";
        private const string RegistryPath = "Assets/DefaultPrefabObjects.asset";

        private const string StarterMap = "map.harbor_town";
        private const string StarterClass = "class.swordsman";
        private const string Boss = "monster.ancient_slime_king";

        private sealed class CharacterStore : ICharacterStateStore
        {
            public readonly Dictionary<string, PersistedCharacter> Rows =
                new Dictionary<string, PersistedCharacter>();

            public CharacterPersistenceResult Load(SessionId s)
            {
                return Rows.TryGetValue(s.Value, out PersistedCharacter row)
                    ? CharacterPersistenceResult.Loaded(row)
                    : CharacterPersistenceResult.Failed(CharacterPersistenceFailure.NotOwned);
            }

            public CharacterPersistenceResult Save(SessionId s, PersistedCharacter c, int r)
            {
                Rows[s.Value] = c;

                return CharacterPersistenceResult.Saved(r + 1);
            }
        }

        /// <summary>
        /// Admits whoever asks, with a character of their own.
        /// </summary>
        /// <remarks>The account API is not part of this test, but authentication is: a
        /// connection that is never admitted is never authenticated, and FishNet sends an
        /// unauthenticated connection nothing at all. Refusing here would make every
        /// assertion below pass or fail for the wrong reason.</remarks>
        private sealed class AlwaysAdmits : IWorldSessionAuthority
        {
            private readonly CharacterStore _store;

            private int _next;

            public AlwaysAdmits(CharacterStore store) => _store = store;

            public WorldAdmission Admit(WorldJoinClaim claim)
            {
                string character = "char-guest-" + _next++;
                string session = "session-" + character;

                _store.Rows[session] = new PersistedCharacter(
                    new CharacterId(character), new AccountId("acc-" + character),
                    new ServerId("srv-1"), character, (int)CharacterGender.Male, 20, 0,
                    200, 60, new DefinitionId(StarterClass), default,
                    new DefinitionId(StarterMap), default, new[]
                    {
                        new PersistedStat(new DefinitionId("stat.str"), 20),
                        new PersistedStat(new DefinitionId("stat.vit"), 15),
                        new PersistedStat(new DefinitionId("stat.int"), 5),
                    }, null, null, 1, null, 0, default, null, null, default);

                return WorldAdmission.Admitted(new SessionId(session),
                    new AccountId("acc-" + character), new CharacterId(character),
                    new ServerId("srv-1"), new ChannelId("ch-1"),
                    new DefinitionId(StarterMap), new Revision(1), new Revision(1),
                    SessionState.EnteringWorld);
            }

            public bool ConfirmArrival(SessionId session) => true;

            public bool Release(SessionId session) => true;
        }

        private Scene _scene;
        private WorldServerBootstrap _bootstrap;
        private CharacterStore _characters;

        private GameObject _clientObject;
        private NetworkManager _client;

        private long _sequence;

        [SetUp]
        public void SetUp()
        {
            _characters = new CharacterStore();
            _sequence = 0;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_client != null) _client.ClientManager.StopConnection();

            yield return null;

            if (_clientObject != null) Object.DestroyImmediate(_clientObject);

            if (_bootstrap != null)
            {
                _bootstrap.StopServer();
                Object.DestroyImmediate(_bootstrap.gameObject);
                _bootstrap = null;
            }

            if (_scene.IsValid() && _scene.isLoaded)
            {
                yield return SceneManager.UnloadSceneAsync(_scene);
            }
        }

        // ---- the composition itself -------------------------------------------------------

        [UnityTest]
        public IEnumerator TheShippedWorldComposesMonsterReplication()
        {
            yield return LoadWorld();

            Assert.That(_bootstrap.MonsterReplication, Is.Not.Null,
                "the shipped world composes no monster replication, so no client will ever "
                + "be told a monster exists");
        }

        // ---- what a real client receives --------------------------------------------------

        [UnityTest]
        public IEnumerator ARealClientReceivesTheMonstersTheServerIsRunning()
        {
            yield return LoadWorld();

            yield return ConnectClient();

            int spawned = Spawn(6);

            Assert.That(spawned, Is.EqualTo(6), "the server did not spawn six monsters");

            yield return Tick(30);

            Assert.That(_bootstrap.MonsterReplication.SpawnedCount, Is.EqualTo(6),
                "the server holds monsters it never gave network objects");

            yield return Until(() => MonstersOn(_client) >= 6, 600);

            Assert.That(MonstersOn(_client), Is.EqualTo(6),
                "a real client received " + MonstersOn(_client) + " of six monsters");

            // And what arrived is the server's own state, not something invented.
            foreach (MonsterNetworkEntity monster in MonsterEntities(_client))
            {
                Assert.That(monster.Definition.Value, Is.EqualTo(Boss));
                Assert.That(monster.Map.Value, Is.EqualTo(StarterMap));
                Assert.That(monster.MaxHealth, Is.GreaterThan(0));
                Assert.That(monster.IsAlive, Is.True);
            }
        }

        [UnityTest]
        public IEnumerator MonsterStateChangesReachTheClient()
        {
            yield return LoadWorld();

            yield return ConnectClient();

            Spawn(1);

            yield return Tick(20);

            yield return Until(() => MonstersOn(_client) >= 1, 600);

            MonsterNetworkEntity shadow = FirstMonster(_client);

            Assert.That(shadow, Is.Not.Null);

            LivingMonster living = FirstLivingMonster();

            float startX = shadow.X;

            // The server moves it. Nothing client-side decides this.
            living.State.Position = new CombatPosition(startX + 25f, 0f, 0f);

            yield return Tick(20);

            yield return Until(() => Mathf.Abs(shadow.X - startX) > 1f, 600);

            // Compared against where the server says it is now, not against where it was
            // put: it is alive, and a living monster keeps moving. What is asserted is that
            // the client's copy tracks the authoritative value, which is the whole claim.
            Assert.That(shadow.X, Is.EqualTo(living.State.Position.X).Within(1.5f),
                "the client's monster did not follow the server's position");

            Assert.That(Mathf.Abs(shadow.X - startX), Is.GreaterThan(1f),
                "the client's monster never moved at all");

            int fullHealth = shadow.Health;

            living.State.ApplyHealthDelta(-(fullHealth / 2));

            yield return Tick(20);

            yield return Until(() => shadow.Health < fullHealth, 600);

            Assert.That(shadow.Health, Is.LessThan(fullHealth),
                "the client's monster did not follow the server's health");
        }

        [UnityTest]
        public IEnumerator ADefeatedMonsterIsDespawnedOnTheClient()
        {
            yield return LoadWorld();

            yield return ConnectClient();

            Spawn(1);

            yield return Tick(20);

            yield return Until(() => MonstersOn(_client) >= 1, 600);

            Assert.That(MonstersOn(_client), Is.EqualTo(1));

            LivingMonster living = FirstLivingMonster();

            // Killed the way the world kills things: damage, then the claim that retires it.
            living.State.SetHealth(0);

            MonsterWorldRuntime monsters = _bootstrap.Simulation.Monsters();

            monsters.ClaimDefeat(living.Instance, new InstanceId("nobody"), default,
                new List<LootResult>());

            yield return Tick(40);

            yield return Until(() => MonstersOn(_client) == 0, 600);

            Assert.That(MonstersOn(_client), Is.Zero,
                "a monster the server retired is still on the client");
        }

        // ---- combat through the real client path -------------------------------------------

        /// <summary>
        /// A client asks the server to attack a monster it can see, and the server decides.
        /// </summary>
        /// <remarks>The request goes through the owned character's own
        /// <c>RequestAttack</c> -- the same door a player's input reaches -- naming a monster
        /// by the instance id the client learned from replication. Nothing about damage,
        /// health or death is decided here.</remarks>
        [UnityTest]
        public IEnumerator AClientCanAskTheServerToAttackAMonsterItWasSent()
        {
            yield return LoadWorld();

            yield return ConnectClient();

            Spawn(1);

            yield return Tick(20);

            yield return Until(() => MonstersOn(_client) >= 1 && OwnedOn(_client) != null, 600);

            MonsterNetworkEntity target = FirstMonster(_client);
            CharacterNetworkEntity owned = OwnedOn(_client);

            Assert.That(target, Is.Not.Null, "the client was sent no monster to attack");
            Assert.That(owned, Is.Not.Null, "the client owns no character to attack with");

            Assert.That(_bootstrap.Characters.TryGetByCharacter(owned.Character, out LivingCharacter hero),
                Is.True, "the server has no character for the object this client owns");

            LivingMonster living = FirstLivingMonster();

            // Melee reach is authored and enforced by the server; the server places the
            // character, because a client that placed itself would be authoring position.
            hero.Combatant.Position = living.State.Position;

            int before = living.State.CurrentHealth;

            for (var i = 0; i < 40 && living.State.CurrentHealth >= before; i++)
            {
                owned.RequestAttack(target.Instance.Value, string.Empty, 0, ++_sequence);

                yield return Tick(2);
            }

            Assert.That(living.State.CurrentHealth, Is.LessThan(before),
                "the server never applied damage from a client's attack request");

            yield return Until(() => FirstMonster(_client) != null
                && FirstMonster(_client).Health < before, 600);

            Assert.That(FirstMonster(_client).Health, Is.LessThan(before),
                "the client was never told the monster it hit had lost health");
        }

        // ---- the world -----------------------------------------------------------------------

        private IEnumerator LoadWorld()
        {
            _scene = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                ServerScene, new LoadSceneParameters(LoadSceneMode.Additive));

            yield return Until(() => _scene.isLoaded);
            yield return null;

            WorldServerBootstrap[] found = Object.FindObjectsByType<WorldServerBootstrap>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            Assert.That(found.Length, Is.EqualTo(1));

            _bootstrap = found[0];

            _bootstrap.StopServer();
            _bootstrap.Compose(new AlwaysAdmits(_characters), default, _characters, null,
                null, null);

            Assert.That(_bootstrap.IsWorldReady, Is.True,
                "shipped content faults: " + string.Join("; ", _bootstrap.ContentFaults));

            Assert.That(_bootstrap.StartServer(), Is.True);

            yield return null;
        }

        private IEnumerator ConnectClient()
        {
            _clientObject = new GameObject("MonsterReplicationClient");
            _clientObject.SetActive(false);

            LogAssert.Expect(LogType.Error, new Regex("SpawnablePrefabs is null"));

            _client = _clientObject.AddComponent<NetworkManager>();

            _client.SpawnablePrefabs =
                UnityEditor.AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>(RegistryPath);

            typeof(NetworkManager)
                .GetField("_persistence", System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance)
                ?.SetValue(_client, NetworkManager.PersistenceType.AllowMultiple);

            var transport = _clientObject.AddComponent<Tugboat>();
            transport.SetPort(ServerPort());
            transport.SetClientAddress("127.0.0.1");

            _clientObject.SetActive(true);

            Assert.That(_client.ClientManager.StartConnection(), Is.True);

            yield return Until(() => _client.ClientManager.Started, 600);

            Assert.That(_client.ClientManager.Started, Is.True, "the client never connected");

            // The join a shipped client sends. Without it the connection is never
            // authenticated, and FishNet sends an unauthenticated connection nothing --
            // which would look exactly like a replication failure.
            _client.ClientManager.Broadcast(new WorldJoinRequestMessage
            {
                Token = "monster-replication-test",
                ClientVersion = "1.0.0",
                ProtocolVersion = "1.0.0",
                ContentVersion = "1.0.0",
            });

            var manager = _bootstrap.GetComponent<NetworkManager>();

            yield return Until(() => manager.ServerManager.Clients.Count >= 1, 600);

            Assert.That(manager.ServerManager.Clients.Count, Is.GreaterThanOrEqualTo(1),
                "the server never admitted the client");
        }

        private ushort ServerPort()
        {
            // The port the shipped scene actually listens on, read from the scene's own
            // serialized value rather than assumed.
            return (ushort)typeof(WorldServerBootstrap)
                .GetField("_port", System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance)
                .GetValue(_bootstrap);
        }

        private int FirstConnection()
        {
            var manager = _bootstrap.GetComponent<NetworkManager>();

            foreach (KeyValuePair<int, NetworkConnection> pair in
                manager.ServerManager.Clients)
            {
                return pair.Key;
            }

            return -1;
        }

        private int Spawn(int count)
        {
            MonsterWorldRuntime monsters = _bootstrap.Simulation.Monsters();

            for (var i = 0; i < count; i++)
            {
                monsters.AddSpawnPoint(new MonsterSpawnPoint(new DefinitionId(Boss),
                    new CombatPosition(i * 6f, 0f, 0f), 0f, 1, 0f,
                    new DefinitionId(StarterMap)));
            }

            return monsters.PopulateAll();
        }

        private LivingMonster FirstLivingMonster()
        {
            IReadOnlyList<LivingMonster> all = _bootstrap.Simulation.Monsters().All();

            for (var i = 0; i < all.Count; i++)
            {
                if (all[i].IsAlive) return all[i];
            }

            return null;
        }

        private LivingCharacter Admit(string character, int connection)
        {
            string session = "session-" + character;

            _characters.Rows[session] = new PersistedCharacter(
                new CharacterId(character), new AccountId("acc-" + character),
                new ServerId("srv-1"), character, (int)CharacterGender.Male, 20, 0, 200, 60,
                new DefinitionId(StarterClass), default, new DefinitionId(StarterMap),
                default, new[]
                {
                    new PersistedStat(new DefinitionId("stat.str"), 20),
                    new PersistedStat(new DefinitionId("stat.vit"), 15),
                    new PersistedStat(new DefinitionId("stat.int"), 5),
                }, null, null, 1, null, 0, default, null, null, default);

            WorldSpawnResult spawned = _bootstrap.Simulation.Admit(connection,
                WorldAdmission.Admitted(new SessionId(session),
                    new AccountId("acc-" + character), new CharacterId(character),
                    new ServerId("srv-1"), new ChannelId("ch-1"),
                    new DefinitionId(StarterMap), new Revision(1), new Revision(1),
                    SessionState.EnteringWorld),
                new CombatTeam(1));

            Assert.That(spawned.IsSpawned, Is.True, spawned.Detail);

            return spawned.Character;
        }

        // ---- reading the client ----------------------------------------------------------

        private static int MonstersOn(NetworkManager client)
        {
            var counted = 0;

            foreach (MonsterNetworkEntity monster in MonsterEntities(client)) counted++;

            return counted;
        }

        private static IEnumerable<MonsterNetworkEntity> MonsterEntities(NetworkManager client)
        {
            foreach (KeyValuePair<int, NetworkObject> pair in
                client.ClientManager.Objects.Spawned)
            {
                if (pair.Value == null) continue;

                var monster = pair.Value.GetComponent<MonsterNetworkEntity>();

                if (monster != null) yield return monster;
            }
        }

        private static MonsterNetworkEntity FirstMonster(NetworkManager client)
        {
            foreach (MonsterNetworkEntity monster in MonsterEntities(client)) return monster;

            return null;
        }

        private static CharacterNetworkEntity OwnedOn(NetworkManager client)
        {
            foreach (NetworkObject owned in client.ClientManager.Connection.Objects)
            {
                if (owned == null) continue;

                if (owned.TryGetComponent(out CharacterNetworkEntity entity)) return entity;
            }

            return null;
        }

        private static IEnumerator Tick(int frames)
        {
            for (var i = 0; i < frames; i++) yield return null;
        }

        private static IEnumerator Until(System.Func<bool> condition, int frames = 400)
        {
            for (int i = 0; i < frames && !condition(); i++) yield return null;
        }
    }
}

#endif
