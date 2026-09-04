// Editor-only, and only this fixture, for the reason the other network fixtures document:
// it loads the committed prefab registry through AssetDatabase, because the point is to
// prove the SHIPPED configuration works.
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
    /// Two real clients walking around a real server.
    /// </summary>
    /// <remarks>
    /// <b>Movement is the one system where "it works locally" proves nothing.</b> A client
    /// that moves its own transform looks perfect until somebody else has to see it, or
    /// until the server disagrees. So these run a real socket with two real clients: one
    /// walks, the other watches, and neither can touch the other's character.
    ///
    /// <b>Integration-only bootstrap, labelled as such.</b> Characters are admitted directly
    /// into the registry rather than through login and enter-world, which has its own live
    /// suite. What is real here: the sockets, the ownership, the RPCs, the server clock, the
    /// authoritative step, the replication, and the combat range that reads the result.
    /// </remarks>
    [TestFixture]
    internal sealed class CharacterMovementNetworkTests
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
        private const string Grunt = "monster.grunt";

        private const float Speed = 4f;

        /// <summary>250ms at 4 m/s: the most one accepted step may cover.</summary>
        private const float MaxStep = 1f;

        private sealed class FakeStore : ICharacterStateStore
        {
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
        private GameObject _clientAObject;
        private GameObject _clientBObject;
        private NetworkManager _server;
        private NetworkManager _clientA;
        private NetworkManager _clientB;

        private FakeStore _store;
        private WorldCharacterRegistry _players;
        private MonsterWorldRuntime _runtime;
        private CharacterMovementAuthority _movement;
        private ServerCombatPipeline _pipeline;
        private CharacterCombatRequestHandler _combat;
        private CharacterReplicationService _characters;
        private MonsterReplicationService _monsters;

        private readonly List<Object> _created = new List<Object>();
        private ushort _port;

        private static ushort NextPort() => (ushort)Random.Range(38100, 42000);

        [SetUp]
        public void SetUp()
        {
            _port = NextPort();

            _server = BuildManager("MoveServer", true, out _serverObject);
            _serverObject.SetActive(true);

            _clientA = BuildManager("MoveClientA", false, out _clientAObject);
            _clientAObject.SetActive(true);

            _clientB = BuildManager("MoveClientB", false, out _clientBObject);
            _clientBObject.SetActive(true);

            var monsters = new DefinitionRegistry<MonsterDefinition>();
            monsters.Register(Monster(Grunt));

            var maps = new DefinitionRegistry<MapDefinition>();
            maps.Register(Map(HomeMap));

            var spawns = new DefinitionRegistry<SpawnPointDefinition>();
            spawns.Register(PlayerSpawn());

            _store = new FakeStore();
            _players = new WorldCharacterRegistry(_store, spawns);

            _runtime = new MonsterWorldRuntime(_players, monsters, new DefinitionId(MaxHp),
                new CombatTeam(2));

            _movement = new CharacterMovementAuthority(_players, _ => true, maps, Speed);

            var commands = new CombatCommandAuthority(_players, _ => true, _runtime);

            // Reach of two metres, so a character must genuinely walk to be in range.
            _pipeline = new ServerCombatPipeline(commands, _runtime, null,
                BasicAttackRules.Melee(new DefinitionId(Atk), new DefinitionId(Def), 1, 2f));

            _monsters = new MonsterReplicationService(_server, _runtime,
                Prefab(MonsterPrefabPath));

            _combat = new CharacterCombatRequestHandler(_pipeline, () =>
            {
                _characters.Synchronise();
                _monsters.Synchronise();
            });

            _characters = new CharacterReplicationService(_server, _players,
                Prefab(CharacterPrefabPath), _combat, _movement);
        }

        [TearDown]
        public void TearDown()
        {
            _characters?.DespawnAll();
            _monsters?.DespawnAll();

            if (_clientB != null) _clientB.ClientManager.StopConnection();
            if (_clientA != null) _clientA.ClientManager.StopConnection();
            if (_server != null) _server.ServerManager.StopConnection(true);

            if (_clientBObject != null) Object.DestroyImmediate(_clientBObject);
            if (_clientAObject != null) Object.DestroyImmediate(_clientAObject);
            if (_serverObject != null) Object.DestroyImmediate(_serverObject);

            foreach (Object created in _created)
            {
                if (created != null) Object.DestroyImmediate(created);
            }

            _created.Clear();
        }

        // ---- harness ----------------------------------------------------------------------

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

        private static IEnumerator Until(System.Func<bool> condition, int frames = 400)
        {
            for (int i = 0; i < frames && !condition(); i++) yield return null;
        }

        private IEnumerator StartServerAndOneClient()
        {
            Assert.That(_server.ServerManager.StartConnection(), Is.True);

            yield return Until(() => _server.ServerManager.Started);

            Assert.That(_clientA.ClientManager.StartConnection(), Is.True);

            yield return Until(() => _clientA.ClientManager.Started
                && _server.ServerManager.Clients.Count >= 1);

            Assert.That(_server.ServerManager.Clients.Count, Is.GreaterThanOrEqualTo(1));
        }

        private IEnumerator StartSecondClient()
        {
            Assert.That(_clientB.ClientManager.StartConnection(), Is.True);

            yield return Until(() => _clientB.ClientManager.Started
                && _server.ServerManager.Clients.Count >= 2);

            Assert.That(_server.ServerManager.Clients.Count, Is.EqualTo(2),
                "the server never saw both clients");
        }

        /// <summary>Connection ids in the order the server accepted them.</summary>
        private List<int> Connections()
        {
            var ids = new List<int>();

            foreach (KeyValuePair<int, NetworkConnection> pair in _server.ServerManager.Clients)
            {
                ids.Add(pair.Key);
            }

            ids.Sort();

            return ids;
        }

        private LivingCharacter EnterWorld(string character, int connection,
            long experience = 0)
        {
            _store.Rows["session-" + character] = new PersistedCharacter(
                new CharacterId(character), new AccountId("acc-" + character),
                new ServerId("srv-1"), character, 2, 5, experience, 100, 50,
                new DefinitionId("class.novice"), default, new DefinitionId(HomeMap),
                default,
                new[]
                {
                    new PersistedStat(new DefinitionId(MaxHp), 100),
                    new PersistedStat(new DefinitionId(Atk), 30),
                    new PersistedStat(new DefinitionId(Def), 5),
                },
                null, null, 1);

            WorldSpawnResult spawned = _players.Spawn(connection,
                WorldAdmission.Admitted(new SessionId("session-" + character),
                    new AccountId("acc-" + character), new CharacterId(character),
                    new ServerId("srv-1"), new ChannelId("ch-1"),
                    new DefinitionId(HomeMap), new Revision(1), new Revision(1),
                    SessionState.EnteringWorld),
                new ResourceLimits(100, 50), new CombatTeam(1));

            Assert.That(spawned.IsSpawned, Is.True, spawned.Detail);

            return spawned.Character;
        }

        /// <summary>Every character object a client can see.</summary>
        private static List<CharacterNetworkEntity> CharactersSeenBy(NetworkManager client)
        {
            var seen = new List<CharacterNetworkEntity>();

            foreach (KeyValuePair<int, NetworkObject> pair in client.ClientManager.Objects.Spawned)
            {
                var entity = pair.Value == null
                    ? null
                    : pair.Value.GetComponent<CharacterNetworkEntity>();

                if (entity != null) seen.Add(entity);
            }

            return seen;
        }

        private static CharacterNetworkEntity SeenBy(NetworkManager client, string character)
        {
            foreach (CharacterNetworkEntity entity in CharactersSeenBy(client))
            {
                if (entity.Character.Value == character) return entity;
            }

            return null;
        }

        /// <summary>Advances the server's movement clock, as its loop would.</summary>
        private void ServerTick(float seconds = 0.25f)
        {
            _movement.Tick(seconds);
        }

        // ---- A: a client walks -----------------------------------------------------------------

        [UnityTest]
        public IEnumerator AClientRequestMovesTheCharacterAndTheMoveIsObserved()
        {
            yield return StartServerAndOneClient();

            LivingCharacter character = EnterWorld("char-a", Connections()[0]);

            _characters.Synchronise();

            yield return Until(() => SeenBy(_clientA, "char-a") != null);

            CharacterNetworkEntity observed = SeenBy(_clientA, "char-a");

            Assert.That(observed.IsOwner, Is.True, "precondition");
            Assert.That(observed.Z, Is.EqualTo(0f).Within(0.001f));

            ServerTick();

            // The client asks with input. It cannot ask to be anywhere.
            observed.RequestMove(0f, 1f, 1);

            yield return Until(() => _movement.Handled > 0);

            Assert.That(_movement.LastResult.IsAccepted, Is.True,
                _movement.LastResult.ToString());
            Assert.That(character.Location.Position.Z, Is.EqualTo(MaxStep).Within(0.01f),
                "the server decided how far a quarter second of input gets you");

            _characters.Synchronise();

            yield return Until(() => SeenBy(_clientA, "char-a") != null
                && SeenBy(_clientA, "char-a").Z > 0.5f);

            Assert.That(SeenBy(_clientA, "char-a").Z,
                Is.EqualTo(character.Location.Position.Z).Within(0.01f),
                "and the client observes the position the server computed");
        }

        // ---- B: two clients ---------------------------------------------------------------------

        [UnityTest]
        public IEnumerator OneClientSeesAnotherMoveAndCannotMoveThem()
        {
            yield return StartServerAndOneClient();
            yield return StartSecondClient();

            List<int> connections = Connections();

            LivingCharacter a = EnterWorld("char-a", connections[0]);
            LivingCharacter b = EnterWorld("char-b", connections[1]);

            _characters.Synchronise();

            yield return Until(() => CharactersSeenBy(_clientA).Count == 2
                && CharactersSeenBy(_clientB).Count == 2);

            // Each client owns exactly one of the two.
            CharacterNetworkEntity aOnA = SeenBy(_clientA, "char-a");
            CharacterNetworkEntity aOnB = SeenBy(_clientB, "char-a");

            Assert.That(aOnA.IsOwner, Is.True, "A owns its own character");
            Assert.That(aOnB.IsOwner, Is.False, "B does not own A's character");

            ServerTick();

            aOnA.RequestMove(0f, 1f, 1);

            yield return Until(() => _movement.Handled > 0);

            Assert.That(a.Location.Position.Z, Is.GreaterThan(0.5f));

            _characters.Synchronise();

            // B watches A move.
            yield return Until(() => SeenBy(_clientB, "char-a") != null
                && SeenBy(_clientB, "char-a").Z > 0.5f);

            Assert.That(SeenBy(_clientB, "char-a").Z,
                Is.EqualTo(a.Location.Position.Z).Within(0.01f),
                "a remote player is drawn from the server's position");

            // And B tries to walk A's character. FishNet refuses a request through an
            // object the sender does not own.
            int handledBefore = _movement.Handled;
            float aBefore = a.Location.Position.Z;

            ServerTick();

            LogAssert.ignoreFailingMessages = true;

            try
            {
                aOnB.RequestMove(0f, 1f, 99);
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
            yield return null;

            Assert.That(_movement.Handled, Is.EqualTo(handledBefore),
                "the request never reached the authority");
            Assert.That(a.Location.Position.Z, Is.EqualTo(aBefore),
                "and A did not move");
            Assert.That(b.Location.Position.Z, Is.Zero, "nor did B");
        }

        // ---- C: spoofing --------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator AClientCannotBuyDistanceWithAnOversizedOrBrokenInput()
        {
            yield return StartServerAndOneClient();

            LivingCharacter character = EnterWorld("char-a", Connections()[0]);

            _characters.Synchronise();

            yield return Until(() => SeenBy(_clientA, "char-a") != null);

            CharacterNetworkEntity entity = SeenBy(_clientA, "char-a");

            ServerTick();

            // A vector worth 100: the "walk a hundred times faster" attempt.
            entity.RequestMove(0f, 100f, 1);

            yield return Until(() => _movement.Handled > 0);

            Assert.That(_movement.LastResult.IsAccepted, Is.False);
            Assert.That(character.Location.Position.Z, Is.Zero);

            // NaN, which would poison every later comparison if it landed.
            ServerTick();

            entity.RequestMove(float.NaN, float.PositiveInfinity, 2);

            yield return Until(() => _movement.Handled > 1);

            Assert.That(_movement.LastResult.Reason,
                Is.EqualTo(MovementRejection.NotFinite));
            Assert.That(character.Location.Position.IsFinite, Is.True);
            Assert.That(character.Location.Position.Z, Is.Zero);

            // An un-normalised diagonal, worth 1.41.
            ServerTick();

            entity.RequestMove(1f, 1f, 3);

            yield return Until(() => _movement.Handled > 2);

            Assert.That(_movement.LastResult.IsAccepted, Is.False);
            Assert.That(character.Location.Position.Z, Is.Zero);

            // The map never changes, whatever is sent.
            Assert.That(character.Location.CurrentMap.Value, Is.EqualTo(HomeMap));
        }

        // ---- D: replay -----------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ADuplicateMovementRequestDoesNotMoveTwice()
        {
            yield return StartServerAndOneClient();

            LivingCharacter character = EnterWorld("char-a", Connections()[0]);

            _characters.Synchronise();

            yield return Until(() => SeenBy(_clientA, "char-a") != null);

            CharacterNetworkEntity entity = SeenBy(_clientA, "char-a");

            ServerTick();
            entity.RequestMove(0f, 1f, 1);

            yield return Until(() => _movement.Handled > 0);

            float afterFirst = character.Location.Position.Z;

            Assert.That(afterFirst, Is.GreaterThan(0.5f), "precondition");

            // The same packet again, after real time has passed on the server.
            ServerTick();
            entity.RequestMove(0f, 1f, 1);

            yield return Until(() => _movement.Handled > 1);

            Assert.That(_movement.LastResult.Reason,
                Is.EqualTo(MovementRejection.OutOfOrder));
            Assert.That(character.Location.Position.Z, Is.EqualTo(afterFirst),
                "a replayed packet must not walk the character again");
        }

        // ---- E: combat range reads the moved position ------------------------------------------------

        [UnityTest]
        public IEnumerator CombatRangeIsEvaluatedAgainstThePositionMovementProduced()
        {
            yield return StartServerAndOneClient();

            LivingCharacter character = EnterWorld("char-a", Connections()[0]);

            // Three metres away, against a two-metre reach: out of range from the spawn.
            _runtime.AddSpawnPoint(new MonsterSpawnPoint(new DefinitionId(Grunt),
                new CombatPosition(0f, 0f, 3f), 0f, 1, 0f, new DefinitionId(HomeMap)));

            _runtime.PopulateAll();

            LivingMonster monster = _runtime.All()[0];

            _characters.Synchronise();
            _monsters.Synchronise();

            yield return Until(() => SeenBy(_clientA, "char-a") != null);

            CharacterNetworkEntity entity = SeenBy(_clientA, "char-a");

            // Attacking from where it stands is refused for range.
            entity.RequestAttack(monster.Instance.Value, string.Empty, 0, 1);

            yield return Until(() => _combat.Handled > 0);

            Assert.That(_combat.LastResult.AttackRejection,
                Is.EqualTo(AttackRejection.OutOfRange), "precondition: too far to reach");
            Assert.That(monster.State.CurrentHealth, Is.EqualTo(100));

            // Walk two steps towards it, over the network.
            for (var i = 1; i <= 2; i++)
            {
                ServerTick();

                entity.RequestMove(0f, 1f, i);

                yield return Until(() => _movement.Handled >= i);
            }

            Assert.That(character.Location.Position.Z, Is.EqualTo(2f).Within(0.01f));
            Assert.That(character.Combatant.Position.Z, Is.EqualTo(2f).Within(0.01f),
                "the combatant moved with the character");

            // The same attack, from the new position, now lands.
            entity.RequestAttack(monster.Instance.Value, string.Empty, 0, 2);

            yield return Until(() => _combat.Handled > 1);

            Assert.That(_combat.LastResult.IsAccepted, Is.True,
                _combat.LastResult.ToString());
            Assert.That(monster.State.CurrentHealth, Is.LessThan(100),
                "combat read the position movement produced, not the one it spawned at");
        }

        // ---- F: leaving and coming back ------------------------------------------------------------

        [UnityTest]
        public IEnumerator AMovedPositionIsRestoredOnReconnectAndTheClientObservesIt()
        {
            yield return StartServerAndOneClient();

            int connection = Connections()[0];

            LivingCharacter character = EnterWorld("char-a", connection);

            _characters.Synchronise();

            yield return Until(() => SeenBy(_clientA, "char-a") != null);

            for (var i = 1; i <= 2; i++)
            {
                ServerTick();

                SeenBy(_clientA, "char-a").RequestMove(0f, 1f, i);

                yield return Until(() => _movement.Handled >= i);
            }

            CombatPosition moved = character.Location.Position;

            Assert.That(moved.Z, Is.EqualTo(2f).Within(0.01f), "precondition: it walked");

            // The player leaves.
            _players.Despawn(connection);

            Assert.That(_characters.Synchronise(), Is.EqualTo(1), "one despawn");

            yield return Until(() => SeenBy(_clientA, "char-a") == null);

            // A disconnected character cannot be walked.
            int handledBefore = _movement.Handled;

            ServerTick();
            _movement.Submit(connection, 0f, 1f, 99);

            Assert.That(_movement.Handled, Is.EqualTo(handledBefore + 1));
            Assert.That(_movement.LastResult.IsAccepted, Is.False,
                "there is no character on that connection any more");

            // They come back. The spawn resolves their position; the client observes it.
            LivingCharacter returned = EnterWorld("char-a", connection);

            _characters.Synchronise();

            yield return Until(() => SeenBy(_clientA, "char-a") != null);

            CharacterNetworkEntity observed = SeenBy(_clientA, "char-a");

            Assert.That(observed, Is.Not.Null);
            Assert.That(observed.X, Is.EqualTo(returned.Location.Position.X).Within(0.01f));
            Assert.That(observed.Z, Is.EqualTo(returned.Location.Position.Z).Within(0.01f),
                "the client is told where the server put them; it does not choose");

            // And it can walk again from wherever the server placed it.
            float restored = returned.Location.Position.Z;

            ServerTick();

            observed.RequestMove(0f, 1f, 1);

            yield return Until(() => _movement.Handled > handledBefore + 1);

            Assert.That(returned.Location.Position.Z, Is.GreaterThan(restored),
                "the movement stream was reset on arrival, so sequence 1 is fresh");
        }

        // ---- fixtures ------------------------------------------------------------------------------

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

        private MapDefinition Map(string id)
        {
            var definition = ScriptableObject.CreateInstance<MapDefinition>();

            JsonUtility.FromJsonOverwrite("{\"_id\":{\"_value\":\"" + id + "\"}}", definition);

            _created.Add(definition);

            return definition;
        }

        private MonsterDefinition Monster(string id)
        {
            var definition = ScriptableObject.CreateInstance<MonsterDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_level\":5,\"_aggressionType\":0,"
                + "\"_experienceReward\":10,\"_attackRange\":2,\"_moveSpeed\":0,"
                + "\"_respawn\":{\"_respawnDelaySeconds\":0,\"_maxAliveInArea\":1}}",
                definition);

            System.Reflection.FieldInfo stats = typeof(MonsterDefinition).GetField(
                "_baseStats", System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic);

            stats.SetValue(definition, new[]
            {
                new StatValue(new DefinitionId(MaxHp), 100f),
                new StatValue(new DefinitionId(Atk), 10f),
                new StatValue(new DefinitionId(Def), 5f),
            });

            _created.Add(definition);

            return definition;
        }
    }
}

#endif
