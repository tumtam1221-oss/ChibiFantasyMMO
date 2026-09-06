#if UNITY_EDITOR

using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Network;
using ChibiFantasy.Server;
using FishNet.Connection;
using FishNet.Editing.NetworkProfiler;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Managing.Statistic;
using FishNet.Object;
using FishNet.Transporting;
using FishNet.Transporting.Tugboat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace ChibiFantasy.Tests.PlayMode
{
    /// <summary>
    /// What the server actually sends, over real sockets, measured by the transport itself.
    /// </summary>
    /// <remarks>
    /// <b>Real clients, and the count is stated.</b> Every client below is its own
    /// <see cref="NetworkManager"/> with its own Tugboat socket on loopback -- real
    /// connections, real serialization, real packets. They share one process, which is the
    /// honest limit of this fixture and is why the counts are small and named rather than
    /// extrapolated.
    ///
    /// <b>The bytes come from FishNet, not from arithmetic.</b> FishNet's own
    /// <c>NetworkTrafficStatistics</c> counts socket bytes and attributes them per packet
    /// id. It is switched on here through the same serialized field its inspector writes;
    /// nothing about the production send path is changed to measure it, because a protocol
    /// that behaves differently when watched is not the protocol being measured.
    ///
    /// <b>What a shared process cannot tell you.</b> Loopback has no MTU pressure, no loss,
    /// no latency and no NIC. These figures are payload volume and message rate, which is
    /// what replication decisions turn on; they are not a throughput or latency benchmark
    /// and must not be quoted as one.
    /// </remarks>
    [TestFixture]
    internal sealed class NetworkBandwidthTests
    {
        private const string RegistryPath = "Assets/DefaultPrefabObjects.asset";

        private const string CharacterPrefabPath =
            "Assets/_Game/Prefabs/Network/WorldEntity_Character.prefab";

        private const string MonsterPrefabPath =
            "Assets/_Game/Prefabs/Network/WorldEntity_Monster.prefab";

        private const string HomeMap = "map.home";
        private const string FarMap = "map.far";
        private const string Grunt = "monster.net-grunt";
        private const string MaxHp = "stat.max_hp";

        /// <summary>Seconds of traffic measured, after the world has settled.</summary>
        private const float Window = 5f;

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

            public CharacterPersistenceResult Save(SessionId s, PersistedCharacter c, int r)
            {
                Rows[s.Value] = c;

                return CharacterPersistenceResult.Saved(r + 1);
            }
        }

        /// <summary>Reads FishNet's own counters without changing how it sends.</summary>
        private sealed class TrafficMeter
        {
            private readonly NetworkTrafficStatistics _statistics;

            private static readonly FieldInfo Outbound = typeof(BidirectionalNetworkTraffic)
                .GetField("OutboundTraffic", BindingFlags.NonPublic | BindingFlags.Instance
                    | BindingFlags.Public);

            private static readonly FieldInfo Inbound = typeof(BidirectionalNetworkTraffic)
                .GetField("InboundTraffic", BindingFlags.NonPublic | BindingFlags.Instance
                    | BindingFlags.Public);

            public TrafficMeter(NetworkTrafficStatistics statistics)
            {
                _statistics = statistics;
                _statistics.OnNetworkTraffic += OnTraffic;
            }

            /// <summary>Bytes the server sent, per packet kind.</summary>
            /// <remarks>FishNet groups what it sends by packet id -- object spawns,
            /// synchronised values, RPCs -- which is the only attribution available without
            /// changing the send path. Read the same way the byte totals are.</remarks>
            public readonly Dictionary<string, ulong> ServerOutByPacket =
                new Dictionary<string, ulong>();

            public ulong ServerOut { get; private set; }

            public ulong ServerIn { get; private set; }

            public ulong ClientIn { get; private set; }

            public ulong ClientOut { get; private set; }

            public int Ticks { get; private set; }

            public void Reset()
            {
                ServerOutByPacket.Clear();
                ServerOut = 0;
                ServerIn = 0;
                ClientIn = 0;
                ClientOut = 0;
                Ticks = 0;
            }

            public void Stop()
            {
                if (_statistics != null) _statistics.OnNetworkTraffic -= OnTraffic;
            }

            private void OnTraffic(uint tick, BidirectionalNetworkTraffic server,
                BidirectionalNetworkTraffic client)
            {
                Ticks++;

                ServerOut += BytesOf(server, Outbound);

                Attribute(server, Outbound, ServerOutByPacket);
                ServerIn += BytesOf(server, Inbound);
                ClientOut += BytesOf(client, Outbound);
                ClientIn += BytesOf(client, Inbound);
            }

            /// <summary>Adds up one direction's bytes under the name of each packet kind.</summary>
            private static void Attribute(BidirectionalNetworkTraffic traffic,
                FieldInfo field, Dictionary<string, ulong> into)
            {
                object direction = field?.GetValue(traffic);

                if (direction == null) return;

                FieldInfo groupsField = direction.GetType().GetField("_packetGroups",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                if (groupsField?.GetValue(direction) is not System.Collections.IDictionary
                    groups)
                {
                    return;
                }

                foreach (object value in groups.Values)
                {
                    if (value == null) continue;

                    System.Type type = value.GetType();

                    object id = type.GetProperty("PacketId")?.GetValue(value);
                    object bytes = type.GetProperty("Bytes")?.GetValue(value);

                    if (id == null || bytes == null) continue;

                    string name = id.ToString();

                    into.TryGetValue(name, out ulong already);

                    into[name] = already + (ulong)bytes;
                }
            }

            /// <summary>
            /// The byte total behind one direction.
            /// </summary>
            /// <remarks>Reflection because FishNet keeps these fields internal to its own
            /// assembly. Reading them changes nothing; this is a measurement fixture and the
            /// alternative -- instrumenting the production send path -- would alter the
            /// thing being measured.</remarks>
            private static ulong BytesOf(BidirectionalNetworkTraffic traffic, FieldInfo field)
            {
                object direction = field?.GetValue(traffic);

                if (direction == null) return 0UL;

                FieldInfo bytes = direction.GetType().GetField("Bytes",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                return bytes == null ? 0UL : (ulong)bytes.GetValue(direction);
            }
        }

        private GameObject _serverObject;
        private NetworkManager _server;
        private TrafficMeter _meter;

        private readonly List<GameObject> _clientObjects = new List<GameObject>();
        private readonly List<NetworkManager> _clients = new List<NetworkManager>();

        private FakeStore _store;
        private WorldCharacterRegistry _players;
        private MonsterWorldRuntime _monsterRuntime;
        private CharacterReplicationService _characters;
        private MonsterReplicationService _monsters;
        private CharacterMovementAuthority _movement;
        private WorldSimulation _world;

        private readonly List<Object> _created = new List<Object>();
        private ushort _port;

        private static ushort NextPort() => (ushort)Random.Range(49100, 51900);

        private T Track<T>(T created) where T : Object
        {
            _created.Add(created);

            return created;
        }

        [SetUp]
        public void SetUp()
        {
            _port = NextPort();

            _server = BuildManager("BandwidthServer", true, out _serverObject);
            _serverObject.SetActive(true);

            var maps = new DefinitionRegistry<MapDefinition>();
            maps.Register(Map(HomeMap));
            maps.Register(Map(FarMap));

            var spawns = new DefinitionRegistry<SpawnPointDefinition>();
            spawns.Register(PlayerSpawn(HomeMap, "spawn.home"));
            spawns.Register(PlayerSpawn(FarMap, "spawn.far"));

            var monsterDefinitions = new DefinitionRegistry<MonsterDefinition>();
            monsterDefinitions.Register(Monster());

            _store = new FakeStore();
            _players = new WorldCharacterRegistry(_store, spawns, null, 30);

            _movement = new CharacterMovementAuthority(_players, _ => true, maps, 4f);

            _monsterRuntime = new MonsterWorldRuntime(_players, monsterDefinitions,
                new DefinitionId(MaxHp), new CombatTeam(2), maps);

            _characters = new CharacterReplicationService(_server, _players,
                Prefab(CharacterPrefabPath), null, _movement);

            _monsters = new MonsterReplicationService(_server, _monsterRuntime,
                Prefab(MonsterPrefabPath));

            _world = new WorldSimulation(_players, null, null, null, _movement, null,
                _monsterRuntime, null);

            Assert.That(_server.StatisticsManager
                .TryGetNetworkTrafficStatistics(out NetworkTrafficStatistics statistics),
                Is.True, "FishNet reported no traffic statistics to read");

            _meter = new TrafficMeter(statistics);
        }

        [TearDown]
        public void TearDown()
        {
            _meter?.Stop();

            _monsters?.DespawnAll();
            _characters?.DespawnAll();

            for (var i = 0; i < _clients.Count; i++) _clients[i]?.ClientManager.StopConnection();

            if (_server != null) _server.ServerManager.StopConnection(true);

            for (var i = 0; i < _clientObjects.Count; i++)
            {
                if (_clientObjects[i] != null) Object.DestroyImmediate(_clientObjects[i]);
            }

            _clientObjects.Clear();
            _clients.Clear();

            if (_serverObject != null) Object.DestroyImmediate(_serverObject);

            foreach (Object created in _created)
            {
                if (created != null) Object.DestroyImmediate(created);
            }

            _created.Clear();
        }

        // ---- the fixtures --------------------------------------------------------------------

        [UnityTest]
        public IEnumerator NetworkSmall()
        {
            yield return Run("SMALL", clients: 2, monsters: 50);
        }

        [UnityTest]
        public IEnumerator NetworkMedium()
        {
            yield return Run("MEDIUM", clients: 8, monsters: 100);
        }

        /// <summary>
        /// What one client receives about a world it is not in.
        /// </summary>
        /// <remarks>The measurement that decides whether map-level observer scoping is worth
        /// introducing: two clients, two maps, and a count of how much of the far map's
        /// traffic reaches the client who cannot see it.</remarks>
        [UnityTest]
        public IEnumerator TwoMapsOneWorld()
        {
            yield return StartServer();

            yield return AddClients(2);

            List<int> connections = Connections();

            EnterWorld("char-home", connections[0], HomeMap);
            EnterWorld("char-far", connections[1], FarMap);

            // Every monster is on the home map. The far client can see none of them.
            AddMonsters(100, HomeMap);

            yield return Settle();

            _meter.Reset();

            yield return Traffic(Window);

            int homeObjects = SpawnedFor(_clients[0]);
            int farObjects = SpawnedFor(_clients[1]);

            Debug.Log("[net] TWO-MAPS clients=2 monsters=100(home)"
                + " homeClientObjects=" + homeObjects
                + " farClientObjects=" + farObjects
                + " serverOutBytesPerSecond=" + (_meter.ServerOut / Window).ToString("0")
                + " ticks=" + _meter.Ticks);

            // Recorded rather than asserted: this is the before-measurement that decides
            // whether scoping is worth introducing at all.
            Assert.That(homeObjects, Is.GreaterThan(0), "the home client observed nothing");
        }

        private IEnumerator Run(string fixture, int clients, int monsters)
        {
            yield return StartServer();

            yield return AddClients(clients);

            List<int> connections = Connections();

            for (var i = 0; i < connections.Count; i++)
            {
                EnterWorld("char-" + i, connections[i], HomeMap);
            }

            AddMonsters(monsters, HomeMap);

            yield return Settle();

            _meter.Reset();

            yield return Traffic(Window);

            var line = new StringBuilder();

            line.Append("[net] ").Append(fixture)
                .Append(" clients=").Append(clients)
                .Append(" characters=").Append(connections.Count)
                .Append(" monsters=").Append(monsters)
                .Append(" seconds=").Append(Window)
                .Append(" ticks=").Append(_meter.Ticks)
                .Append(" serverOutBytesPerSecond=")
                .Append((_meter.ServerOut / Window).ToString("0"))
                .Append(" serverOutBytesPerSecondPerClient=")
                .Append((_meter.ServerOut / Window / clients).ToString("0"))
                .Append(" serverInBytesPerSecond=")
                .Append((_meter.ServerIn / Window).ToString("0"))
                .Append(" clientObjects=").Append(SpawnedFor(_clients[0]));

            foreach (KeyValuePair<string, ulong> pair in _meter.ServerOutByPacket)
            {
                line.Append(' ').Append(pair.Key).Append('=')
                    .Append((pair.Value / Window).ToString("0")).Append("B/s");
            }

            Debug.Log(line.ToString());

            Assert.That(_meter.Ticks, Is.GreaterThan(0),
                "no traffic ticks were observed, so nothing was measured");

            Assert.That(_meter.ServerOut, Is.GreaterThan(0UL),
                "the server sent nothing in five seconds of a moving world");
        }

        // ---- driving -------------------------------------------------------------------------

        /// <summary>Runs the world for a while, moving everybody, and lets the meter count.</summary>
        private IEnumerator Traffic(float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;

            while (Time.realtimeSinceStartup < until)
            {
                Step();

                yield return null;
            }
        }

        private IEnumerator Settle()
        {
            for (var i = 0; i < 120; i++)
            {
                Step();

                yield return null;
            }
        }

        private void Step()
        {
            IReadOnlyList<LivingCharacter> all = _players.All();

            for (var i = 0; i < all.Count; i++)
            {
                if (all[i].Combatant == null) continue;

                float angle = Time.frameCount * 0.05f + i;

                all[i].Combatant.Position = new CombatPosition(
                    Mathf.Cos(angle) * 8f + i, 0f, Mathf.Sin(angle) * 8f);
            }

            _world.Tick(Time.deltaTime);

            _characters.Synchronise();
            _monsters.Synchronise();
        }

        private static int SpawnedFor(NetworkManager client)
        {
            var counted = 0;

            foreach (KeyValuePair<int, NetworkObject> pair in
                client.ClientManager.Objects.Spawned)
            {
                if (pair.Value != null) counted++;
            }

            return counted;
        }

        // ---- harness ---------------------------------------------------------------------------

        private IEnumerator StartServer()
        {
            Assert.That(_server.ServerManager.StartConnection(), Is.True);

            yield return Until(() => _server.ServerManager.Started);
        }

        private IEnumerator AddClients(int count)
        {
            for (var i = 0; i < count; i++)
            {
                NetworkManager client = BuildManager("BandwidthClient" + i, false,
                    out GameObject host);

                _clientObjects.Add(host);
                _clients.Add(client);

                host.SetActive(true);

                Assert.That(client.ClientManager.StartConnection(), Is.True);
            }

            yield return Until(() => _server.ServerManager.Clients.Count >= count, 900);

            Assert.That(_server.ServerManager.Clients.Count, Is.EqualTo(count),
                "not every client connected, so the stated count would be a lie");
        }

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

        private void EnterWorld(string character, int connection, string map)
        {
            string session = "session-" + character;

            _store.Rows[session] = new PersistedCharacter(
                new CharacterId(character), new AccountId("acc-" + character),
                new ServerId("srv-1"), character, (int)CharacterGender.Male, 5, 0, 100, 50,
                new DefinitionId("class.novice"), default, new DefinitionId(map),
                default, null, null, null, 1, null, 0, default, null, null, default);

            WorldSpawnResult spawned = _players.Spawn(connection,
                WorldAdmission.Admitted(new SessionId(session),
                    new AccountId("acc-" + character), new CharacterId(character),
                    new ServerId("srv-1"), new ChannelId("ch-1"),
                    new DefinitionId(map), new Revision(1), new Revision(1),
                    SessionState.EnteringWorld),
                new ResourceLimits(100, 50), new CombatTeam(1));

            Assert.That(spawned.IsSpawned, Is.True, spawned.Detail);
        }

        private void AddMonsters(int count, string map)
        {
            for (var i = 0; i < count; i++)
            {
                _monsterRuntime.AddSpawnPoint(new MonsterSpawnPoint(new DefinitionId(Grunt),
                    new CombatPosition(i % 12 * 3f, 0f, i / 12 * 3f), 2f, 1, 30f,
                    new DefinitionId(map)));
            }

            _monsterRuntime.PopulateAll();
        }

        private NetworkManager BuildManager(string name, bool listening, out GameObject host)
        {
            host = new GameObject(name);
            host.SetActive(false);

            LogAssert.Expect(LogType.Error, new Regex("SpawnablePrefabs is null"));

            // Every FishNet manager caches its statistics reference while initialising and
            // never looks again, so the counters have to be switched on before the object
            // is activated. Enabled after the fact, they exist and count nothing.
            EnableTraffic(host.AddComponent<StatisticsManager>());

            NetworkManager manager = host.AddComponent<NetworkManager>();

            manager.SpawnablePrefabs =
                UnityEditor.AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>(RegistryPath);

            typeof(NetworkManager)
                .GetField("_persistence", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(manager, NetworkManager.PersistenceType.AllowMultiple);

            var transport = host.AddComponent<Tugboat>();
            transport.SetPort(_port);

            if (listening) transport.SetServerBindAddress("127.0.0.1", IPAddressType.IPv4);
            else transport.SetClientAddress("127.0.0.1");

            return manager;
        }

        /// <summary>Turns FishNet's own byte counters on, the way its inspector would.</summary>
        private static void EnableTraffic(StatisticsManager statistics)
        {
            FieldInfo field = typeof(StatisticsManager).GetField("_networkTraffic",
                BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.That(field, Is.Not.Null, "FishNet no longer exposes network traffic");

            var traffic = field.GetValue(statistics) as NetworkTrafficStatistics;

            if (traffic == null)
            {
                traffic = new NetworkTrafficStatistics();

                field.SetValue(statistics, traffic);
            }

            Set(traffic, "_enableMode", NetworkTrafficStatistics.EnabledMode.Headless);
            Set(traffic, "_updateClient", true);
            Set(traffic, "_updateServer", true);

            // FishNet refuses to hand out statistics in a build -- or an editor whose
            // standalone subtarget is Server, which defines UNITY_SERVER -- unless this is
            // set. It is the same switch its own inspector shows.
            Set(statistics, "_runInRelease", true);
        }

        private static void Set(object target, string field, object value)
        {
            FieldInfo info = target.GetType().GetField(field,
                BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.That(info, Is.Not.Null, "FishNet renamed " + field);

            info.SetValue(target, value);
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

        private MonsterDefinition Monster()
        {
            var definition = Track(ScriptableObject.CreateInstance<MonsterDefinition>());

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + Grunt + "\"},\"_level\":5,\"_aggressionType\":2,"
                + "\"_experienceReward\":10,\"_attackRange\":2,\"_detectionRange\":12,"
                + "\"_leashRange\":40,\"_moveSpeed\":2,"
                + "\"_respawn\":{\"_respawnDelaySeconds\":30,\"_maxAliveInArea\":10}}",
                definition);

            typeof(MonsterDefinition)
                .GetField("_baseStats", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(definition,
                    new[] { new StatValue(new DefinitionId(MaxHp), 100f) });

            return definition;
        }

        private MapDefinition Map(string id)
        {
            var map = Track(ScriptableObject.CreateInstance<MapDefinition>());

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_movementRadius\":500}", map);

            return map;
        }

        private SpawnPointDefinition PlayerSpawn(string map, string id)
        {
            var spawn = Track(ScriptableObject.CreateInstance<SpawnPointDefinition>());

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_map\":{\"_value\":\"" + map
                + "\"},\"_spawnType\":" + (int)SpawnType.Player
                + ",\"_x\":0,\"_y\":0,\"_z\":0}", spawn);

            return spawn;
        }
    }
}

#endif
