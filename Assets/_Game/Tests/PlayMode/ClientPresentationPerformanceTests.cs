#if UNITY_EDITOR

using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ChibiFantasy.Client.World;
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
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace ChibiFantasy.Tests.PlayMode
{
    /// <summary>
    /// What a client frame costs, with a real socket underneath it and the approved models
    /// on screen.
    /// </summary>
    /// <remarks>
    /// <b>Real sockets, real prefabs, real models.</b> The server is a production
    /// <c>CharacterReplicationService</c> and <c>MonsterReplicationService</c> over the
    /// committed prefab registry; the client is a second <c>NetworkManager</c> connected to
    /// it over loopback, and every character it draws was built by the production
    /// <c>CharacterVisualPresenter</c> from the approved catalogue. Nothing here fabricates
    /// a visual or a connection, because a benchmark against a fabricated one measures the
    /// fabrication.
    ///
    /// <b>The picture is rendered somewhere fixed.</b> A camera renders to a 1920x1080
    /// render texture every frame, so the rendering counters describe a stated resolution
    /// rather than whatever size the Game view happened to be. Before and after are
    /// therefore comparable, which is the only thing a baseline is for.
    ///
    /// <b>What it cannot say.</b> Monsters have no art in this project -- the network entity
    /// is the whole of a monster -- so their cost here is replication and object handling,
    /// never geometry. Any GPU figure below covers characters, the pet follower and the HUD,
    /// and says nothing about monsters that do not exist yet.
    ///
    /// <b>Logged, and asserted only where an assertion is not brittle.</b> Frame times in an
    /// Editor test runner sharing a machine with a compiler are evidence, not verdicts. The
    /// numbers are logged for the baseline document; the assertions are on counts and
    /// ceilings that a regression would break.
    /// </remarks>
    [TestFixture]
    internal sealed class ClientPresentationPerformanceTests
    {
        private const string RegistryPath = "Assets/DefaultPrefabObjects.asset";

        private const string CharacterPrefabPath =
            "Assets/_Game/Prefabs/Network/WorldEntity_Character.prefab";

        private const string MonsterPrefabPath =
            "Assets/_Game/Prefabs/Network/WorldEntity_Monster.prefab";

        private const string CataloguePath =
            "Assets/_Game/Prefabs/Presentation/CharacterVisualCatalogue.asset";

        private const string PetAssetPath =
            "Assets/_Game/Data/Production/Pets/pet_lumi_slime.asset";

        private const string EvolvedPetAssetPath =
            "Assets/_Game/Data/Production/Pets/pet_lumi_slime_evolved.asset";

        private const string CameraSettingsPath =
            "Assets/_Game/Prefabs/Prototype/ProtoCameraSettings.asset";

        private const string HomeMap = "map.home";
        private const string Grunt = "monster.client-grunt";
        private const string MaxHp = "stat.max_hp";

        /// <summary>Frames rendered before anything is believed.</summary>
        private const int Warmup = 60;

        /// <summary>Frames measured.</summary>
        private const int Frames = 180;

        private const int Width = 1920;
        private const int Height = 1080;

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

        /// <summary>One fixture's measured frame.</summary>
        private sealed class Measurement
        {
            public Measurement(string fixture, int characters, int monsters, int visuals,
                double mainThreadMs, double renderThreadMs, double gpuMs, long bytesPerFrame,
                long batches, long setPass, long triangles, long vertices, long shadowCasters,
                double replicationMs)
            {
                ReplicationMs = replicationMs;
                Fixture = fixture;
                Characters = characters;
                Monsters = monsters;
                Visuals = visuals;
                MainThreadMs = mainThreadMs;
                RenderThreadMs = renderThreadMs;
                GpuMs = gpuMs;
                BytesPerFrame = bytesPerFrame;
                Batches = batches;
                SetPass = setPass;
                Triangles = triangles;
                Vertices = vertices;
                ShadowCasters = shadowCasters;
            }

            public string Fixture { get; }

            public int Characters { get; }

            public int Monsters { get; }

            public int Visuals { get; }

            public double MainThreadMs { get; }

            public double RenderThreadMs { get; }

            public double GpuMs { get; }

            public long BytesPerFrame { get; }

            public long Batches { get; }

            public long SetPass { get; }

            public long Triangles { get; }

            public long Vertices { get; }

            public long ShadowCasters { get; }

            /// <summary>What preparing replication costs the server, per frame.</summary>
            public double ReplicationMs { get; }

            public override string ToString()
            {
                return "[client] " + Fixture
                    + " characters=" + Characters
                    + " monsters=" + Monsters
                    + " visuals=" + Visuals
                    + " mainThread=" + MainThreadMs.ToString("0.000") + "ms"
                    + " renderThread=" + RenderThreadMs.ToString("0.000") + "ms"
                    + " gpu=" + GpuMs.ToString("0.000") + "ms"
                    + " allocPerFrame=" + BytesPerFrame + "B"
                    + " batches=" + Batches
                    + " setPass=" + SetPass
                    + " tris=" + Triangles
                    + " verts=" + Vertices
                    + " shadowCasters=" + ShadowCasters
                    + " replicationPrep=" + ReplicationMs.ToString("0.000") + "ms"
                    + " res=" + Width + "x" + Height;
            }
        }

        private GameObject _serverObject;
        private GameObject _clientObject;
        private NetworkManager _server;
        private NetworkManager _client;

        private FakeStore _store;
        private WorldCharacterRegistry _players;
        private MonsterWorldRuntime _monsterRuntime;
        private CharacterReplicationService _characters;
        private MonsterReplicationService _monsters;
        private CharacterPetAuthority _petAuthority;
        private CharacterMovementAuthority _movement;
        private WorldSimulation _world;

        /// <summary>Whether the binder is composed, so its own cost can be isolated.</summary>
        private bool _withBinder = true;

        private GameObject _presentation;
        private GameObject _hudObject;
        private GameObject _bagObject;

        private Camera _camera;
        private RenderTexture _target;
        private GameObject _cameraObject;
        private GameObject _lightObject;

        /// <summary>
        /// The other players in the world, as network objects.
        /// </summary>
        /// <remarks>
        /// <b>Why they are spawned here rather than by the replication service.</b> A
        /// character's object is spawned <i>for</i> its owning connection, so twenty-five
        /// players would need twenty-five client <c>NetworkManager</c>s -- and every one of
        /// them, living in this same process, would build its own copy of all twenty-five
        /// models. Six hundred and twenty-five character visuals is not what a client draws,
        /// so measuring it would answer a question nobody asked.
        ///
        /// These are therefore the production prefab, spawned unowned, publishing through
        /// the production entity's own server methods. The presentation path under
        /// measurement -- <c>CharacterVisualPresenter</c>, the approved catalogue, the
        /// animator, the nameplate -- is the shipped one, which is what a visual fixture is
        /// for. What this fixture does not measure is the socket, and that is measured
        /// separately, with real clients, where the count is stated rather than simulated.
        /// </remarks>
        private readonly List<CharacterNetworkEntity> _crowd =
            new List<CharacterNetworkEntity>();

        private readonly List<Object> _created = new List<Object>();
        private readonly List<double> _replicationSamples = new List<double>();
        private readonly System.Diagnostics.Stopwatch _watch =
            new System.Diagnostics.Stopwatch();

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
            _withBinder = true;
            _port = NextPort();

            _server = BuildManager("PerfServer", true, out _serverObject);
            _serverObject.SetActive(true);

            _client = BuildManager("PerfClient", false, out _clientObject);
            _clientObject.SetActive(true);

            var maps = new DefinitionRegistry<MapDefinition>();
            maps.Register(Map());

            var spawns = new DefinitionRegistry<SpawnPointDefinition>();
            spawns.Register(PlayerSpawn());

            var pets = new DefinitionRegistry<PetDefinition>();
            pets.Register(UnityEditor.AssetDatabase.LoadAssetAtPath<PetDefinition>(PetAssetPath));
            pets.Register(UnityEditor.AssetDatabase
                .LoadAssetAtPath<PetDefinition>(EvolvedPetAssetPath));

            var monsterDefinitions = new DefinitionRegistry<MonsterDefinition>();
            monsterDefinitions.Register(Monster());

            _store = new FakeStore();
            _players = new WorldCharacterRegistry(_store, spawns, null, 30, null, pets);

            _petAuthority = new CharacterPetAuthority(_players, pets);

            _movement = new CharacterMovementAuthority(_players, _ => true, maps, 4f);

            _monsterRuntime = new MonsterWorldRuntime(_players, monsterDefinitions,
                new DefinitionId(MaxHp), new CombatTeam(2), maps);

            _characters = new CharacterReplicationService(_server, _players,
                Prefab(CharacterPrefabPath), null, _movement);

            _monsters = new MonsterReplicationService(_server, _monsterRuntime,
                Prefab(MonsterPrefabPath));

            _world = new WorldSimulation(_players, null, null, null, _movement, null,
                _monsterRuntime, null);

            BuildTheView();
        }

        [TearDown]
        public void TearDown()
        {
            _monsters?.DespawnAll();
            _characters?.DespawnAll();

            if (_client != null) _client.ClientManager.StopConnection();
            if (_server != null) _server.ServerManager.StopConnection(true);

            if (_camera != null) _camera.targetTexture = null;

            if (_target != null)
            {
                _target.Release();
                Object.DestroyImmediate(_target);
                _target = null;
            }

            if (_presentation != null) Object.DestroyImmediate(_presentation);
            if (_hudObject != null) Object.DestroyImmediate(_hudObject);
            if (_bagObject != null) Object.DestroyImmediate(_bagObject);

            if (_cameraObject != null) Object.DestroyImmediate(_cameraObject);
            if (_lightObject != null) Object.DestroyImmediate(_lightObject);
            if (_clientObject != null) Object.DestroyImmediate(_clientObject);
            if (_serverObject != null) Object.DestroyImmediate(_serverObject);

            foreach (Object created in _created)
            {
                if (created != null) Object.DestroyImmediate(created);
            }

            _created.Clear();
            _crowd.Clear();
        }

        // ---- the fixtures -------------------------------------------------------------------

        /// <summary>
        /// A connected client with an empty world.
        /// </summary>
        /// <remarks>The subtraction baseline. An Editor PlayMode frame allocates on its own
        /// account whatever the game does, so every figure below is only meaningful against
        /// this one.</remarks>
        [UnityTest]
        public IEnumerator ClientEmpty()
        {
            yield return Fill(characters: 0, monsters: 0, pets: 0);

            Measurement measured = null;

            yield return Measure("EMPTY", 0, 0, m => measured = m);

            Report(measured);
        }

        /// <summary>Monsters and nothing else: replication and object handling, no visuals.</summary>
        /// <remarks>Monsters have no art in this project, so this fixture separates what a
        /// client pays for merely holding replicated objects from what it pays for drawing
        /// characters.</remarks>
        [UnityTest]
        public IEnumerator ClientMonstersOnly()
        {
            yield return Fill(characters: 0, monsters: 150, pets: 0);

            Measurement measured = null;

            yield return Measure("MONSTERS", 0, 150, m => measured = m);

            Report(measured);
        }

        /// <summary>The same monsters, with no HUD composed.</summary>
        /// <remarks>The difference between this and the fixture above is what the shipped
        /// client presentation costs while it is looking for the character it belongs to.
        /// Measured rather than reasoned about, because "the HUD is cheap" is exactly the
        /// kind of thing that is true until it is not.</remarks>
        [UnityTest]
        public IEnumerator ClientMonstersOnlyWithoutTheHud()
        {
            yield return Fill(characters: 0, monsters: 150, pets: 0, presentation: false);

            Measurement measured = null;

            yield return Measure("MONSTERS-NOHUD", 0, 150, m => measured = m);

            Report(measured);
        }

        /// <summary>The same monsters and the same HUD, with nobody looking for an owner.</summary>
        /// <remarks>Isolates the binder from the screens it drives: the HUD is still here,
        /// still updating, and the only thing missing is the per-frame search for the
        /// character this client owns.</remarks>
        [UnityTest]
        public IEnumerator ClientMonstersOnlyWithoutTheBinder()
        {
            _withBinder = false;

            yield return Fill(characters: 0, monsters: 150, pets: 0);

            Measurement measured = null;

            yield return Measure("MONSTERS-NOBINDER", 0, 150, m => measured = m);

            Report(measured);
        }

        [UnityTest]
        public IEnumerator ClientSmall()
        {
            yield return Fill(characters: 1, monsters: 5, pets: 1);

            Measurement measured = null;

            yield return Measure("SMALL", 1, 5, m => measured = m);

            Report(measured);

            Assert.That(measured.Visuals, Is.EqualTo(1),
                "the approved model did not appear, so nothing visual was measured");
        }

        [UnityTest]
        public IEnumerator ClientMedium()
        {
            yield return Fill(characters: 10, monsters: 50, pets: 5);

            Measurement measured = null;

            yield return Measure("MEDIUM", 10, 50, m => measured = m);

            Report(measured);

            Assert.That(measured.Visuals, Is.EqualTo(10));
        }

        [UnityTest]
        public IEnumerator ClientStress()
        {
            yield return Fill(characters: 25, monsters: 150, pets: 12);

            Measurement measured = null;

            yield return Measure("STRESS", 25, 150, m => measured = m);

            Report(measured);

            Assert.That(measured.Visuals, Is.EqualTo(25),
                "twenty-five approved models did not all appear");
        }

        // ---- the measurement -----------------------------------------------------------------

        private IEnumerator Measure(string fixture, int characters, int monsters,
            System.Action<Measurement> report)
        {
            for (var i = 0; i < Warmup; i++)
            {
                Step();

                yield return null;
            }

            ProfilerRecorder main = ProfilerRecorder.StartNew(ProfilerCategory.Internal,
                "Main Thread", Frames + 8);

            ProfilerRecorder renderThread = ProfilerRecorder.StartNew(ProfilerCategory.Render,
                "CPU Render Thread Frame Time", Frames + 8);

            ProfilerRecorder gpu = ProfilerRecorder.StartNew(ProfilerCategory.Render,
                "GPU Frame Time", Frames + 8);

            ProfilerRecorder allocated = ProfilerRecorder.StartNew(ProfilerCategory.Memory,
                "GC Allocated In Frame", Frames + 8);

            ProfilerRecorder batches = ProfilerRecorder.StartNew(ProfilerCategory.Render,
                "Batches Count", Frames + 8);

            ProfilerRecorder setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render,
                "SetPass Calls Count", Frames + 8);

            ProfilerRecorder triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render,
                "Triangles Count", Frames + 8);

            ProfilerRecorder vertices = ProfilerRecorder.StartNew(ProfilerCategory.Render,
                "Vertices Count", Frames + 8);

            ProfilerRecorder shadows = ProfilerRecorder.StartNew(ProfilerCategory.Render,
                "Shadow Casters Count", Frames + 8);

            _replicationSamples.Clear();

            for (var i = 0; i < Frames; i++)
            {
                Step();

                yield return null;
            }

            var measured = new Measurement(fixture, characters, monsters, VisualCount(),
                Median(main) / 1e6d,
                Median(renderThread) / 1e6d,
                Median(gpu) / 1e6d,
                Average(allocated),
                Count(batches), Count(setPass), Count(triangles), Count(vertices),
                Count(shadows), MedianOf(_replicationSamples));

            main.Dispose();
            renderThread.Dispose();
            gpu.Dispose();
            allocated.Dispose();
            batches.Dispose();
            setPass.Dispose();
            triangles.Dispose();
            vertices.Dispose();
            shadows.Dispose();

            report(measured);
        }

        /// <summary>One frame of ordinary server work, so the client has something to apply.</summary>
        /// <remarks>Movement every frame is the point: a fixture where nothing moves measures
        /// a world at rest, which is the mistake the server soak already made once.</remarks>
        private void Step()
        {
            IReadOnlyList<LivingCharacter> all = _players.All();

            for (var i = 0; i < all.Count; i++)
            {
                if (all[i].Combatant == null) continue;

                float angle = Time.frameCount * 0.05f + i;

                all[i].Combatant.Position = new CombatPosition(
                    Mathf.Cos(angle) * 6f + i * 1.5f, 0f, Mathf.Sin(angle) * 6f);
            }

            for (var i = 0; i < _crowd.Count; i++)
            {
                if (_crowd[i] == null) continue;

                float angle = Time.frameCount * 0.05f + i;

                _crowd[i].ServerPublishState(
                    Mathf.Cos(angle) * 6f + (i + 1) * 1.5f, 0f, Mathf.Sin(angle) * 6f,
                    100, 100, 50, 50, 5, 0L);
            }

            _world.Tick(Time.deltaTime);

            // Timed on its own: preparing replication is the server-side half of what a
            // networked frame costs, and it is the half this project can change.
            _watch.Restart();

            _characters.Synchronise();
            _monsters.Synchronise();

            _watch.Stop();

            _replicationSamples.Add(_watch.Elapsed.TotalMilliseconds);
        }

        private static double MedianOf(List<double> samples)
        {
            if (samples.Count == 0) return 0d;

            var sorted = new List<double>(samples);

            sorted.Sort();

            return sorted[sorted.Count / 2];
        }

        private static double Median(ProfilerRecorder recorder)
        {
            if (!recorder.Valid || recorder.Count == 0) return 0d;

            var samples = new List<double>(recorder.Count);

            for (var i = 0; i < recorder.Count; i++)
            {
                samples.Add(recorder.GetSample(i).Value);
            }

            samples.Sort();

            return samples[samples.Count / 2];
        }

        /// <summary>The median of a counter that reports whole things rather than time.</summary>
        private static long Count(ProfilerRecorder recorder)
        {
            return (long)Median(recorder);
        }

        private static long Average(ProfilerRecorder recorder)
        {
            if (!recorder.Valid || recorder.Count == 0) return 0L;

            var total = 0L;

            for (var i = 0; i < recorder.Count; i++) total += recorder.GetSample(i).Value;

            return total / recorder.Count;
        }

        private static void Report(Measurement measured)
        {
            Assert.That(measured, Is.Not.Null, "nothing was measured");

            Debug.Log(measured.ToString());
        }

        /// <summary>How many approved models the client actually built.</summary>
        private int VisualCount()
        {
            var built = 0;

            foreach (KeyValuePair<int, NetworkObject> pair in
                _client.ClientManager.Objects.Spawned)
            {
                if (pair.Value == null) continue;

                var visual = pair.Value.GetComponent<CharacterVisualPresenter>();

                if (visual != null && visual.HasVisual) built++;
            }

            return built;
        }

        // ---- filling the world ---------------------------------------------------------------

        private IEnumerator Fill(int characters, int monsters, int pets,
            bool presentation = true)
        {
            Assert.That(_server.ServerManager.StartConnection(), Is.True);

            yield return Until(() => _server.ServerManager.Started);

            Assert.That(_client.ClientManager.StartConnection(), Is.True);

            yield return Until(() => _client.ClientManager.Started
                && _server.ServerManager.Clients.Count >= 1);

            int connection = FirstConnection();

            // The presentation a real client runs -- the binder, the HUD, the bag and the
            // camera director -- composed the way GameWorld composes it. A client fixture
            // without the HUD would be measuring half a client.
            if (presentation) BuildPresentation(_withBinder);

            // The local player: the whole production path, from the registry through the
            // replication service to an owned object on a real socket.
            if (characters > 0)
            {
                EnterWorld("char-0", connection, CharacterGender.Male, "Player 0",
                    pets > 0
                        ? new[]
                        {
                            new PersistedPet(new InstanceId("pet-0"),
                                new DefinitionId("pet.lumi_slime"), 1, 0, 0),
                        }
                        : null);

                if (pets > 0) _petAuthority.Activate(connection, new InstanceId("pet-0"));
            }

            // Everybody else in view.
            for (var i = 1; i < characters; i++)
            {
                bool female = i % 2 == 1;

                CharacterNetworkEntity other = SpawnBystander(i, female);

                _crowd.Add(other);
            }

            for (var i = 0; i < monsters; i++)
            {
                _monsterRuntime.AddSpawnPoint(new MonsterSpawnPoint(new DefinitionId(Grunt),
                    new CombatPosition(i % 12 * 3f, 0f, i / 12 * 3f), 2f, 1, 30f,
                    new DefinitionId(HomeMap)));
            }

            _monsterRuntime.PopulateAll();

            _characters.Synchronise();
            _monsters.Synchronise();

            // The client needs frames to receive spawns and build models from them.
            yield return Until(() => VisualCount() >= characters, 900);
        }

        /// <summary>One more player standing in the world, drawn exactly as a player is.</summary>
        private CharacterNetworkEntity SpawnBystander(int index, bool female)
        {
            NetworkObject instance = Object.Instantiate(Prefab(CharacterPrefabPath));

            _server.ServerManager.Spawn(instance);

            var entity = instance.GetComponent<CharacterNetworkEntity>();

            entity.ServerPublishIdentity(new CharacterId("char-" + index),
                new DefinitionId(HomeMap), 100,
                (int)(female ? CharacterGender.Female : CharacterGender.Male),
                "Player " + index);

            entity.ServerPublishState(index * 1.5f, 0f, 0f, 100, 100, 50, 50, 5, 0L);

            return entity;
        }

        /// <summary>Stands up the shipped client presentation for the measuring client.</summary>
        private void BuildPresentation(bool withBinder)
        {
            var host = new GameObject("Client Presentation");

            _presentation = host;

            var director = host.AddComponent<WorldCameraDirector>();

            var settings = UnityEditor.AssetDatabase
                .LoadAssetAtPath<ChibiFantasy.Client.Prototype.ProtoCameraSettings>(
                    CameraSettingsPath);

            director.Compose(settings);

            var hud = new GameObject("Client HUD")
                .AddComponent<ChibiFantasy.Client.UI.WorldHudScreen>();

            var bag = new GameObject("Client Bag")
                .AddComponent<ChibiFantasy.Client.UI.InventoryScreen>();

            _hudObject = hud.gameObject;
            _bagObject = bag.gameObject;

            if (!withBinder) return;

            var binder = host.AddComponent<ChibiFantasy.Client.UI.WorldPresentationBinder>();

            binder.Compose(_client, hud, bag, new DefinitionRegistry<ItemDefinition>(),
                director);
        }

        private int FirstConnection()
        {
            foreach (KeyValuePair<int, NetworkConnection> pair in _server.ServerManager.Clients)
            {
                return pair.Key;
            }

            return -1;
        }

        private void EnterWorld(string character, int connection, CharacterGender gender,
            string name, PersistedPet[] pets)
        {
            string session = "session-" + character;

            _store.Rows[session] = new PersistedCharacter(
                new CharacterId(character), new AccountId("acc-" + character),
                new ServerId("srv-1"), name, (int)gender, 5, 0, 100, 50,
                new DefinitionId("class.novice"), default, new DefinitionId(HomeMap),
                default, null, null, null, 1, null, 0, default, null, pets, default);

            WorldSpawnResult spawned = _players.Spawn(connection,
                WorldAdmission.Admitted(new SessionId(session),
                    new AccountId("acc-" + character), new CharacterId(character),
                    new ServerId("srv-1"), new ChannelId("ch-1"),
                    new DefinitionId(HomeMap), new Revision(1), new Revision(1),
                    SessionState.EnteringWorld),
                new ResourceLimits(100, 50), new CombatTeam(1));

            Assert.That(spawned.IsSpawned, Is.True, spawned.Detail);
        }

        // ---- the picture ----------------------------------------------------------------------

        /// <summary>
        /// A camera drawing the crowd into a fixed-size target, and one light.
        /// </summary>
        /// <remarks>A render texture rather than the Game view, so "1920x1080" in the
        /// baseline is a fact about the measurement rather than about somebody's window.
        /// </remarks>
        private void BuildTheView()
        {
            _target = new RenderTexture(Width, Height, 24)
            {
                name = "Client Perf Target",
            };

            _target.Create();

            _cameraObject = new GameObject("Perf Camera");
            _cameraObject.transform.position = new Vector3(20f, 18f, -22f);
            _cameraObject.transform.rotation = Quaternion.Euler(28f, -20f, 0f);

            _camera = _cameraObject.AddComponent<Camera>();
            _camera.targetTexture = _target;
            _camera.farClipPlane = 400f;

            _lightObject = new GameObject("Perf Light");
            _lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            Light light = _lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;
        }

        // ---- harness ---------------------------------------------------------------------------

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
                .GetField("_baseStats", System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic)
                .SetValue(definition,
                    new[] { new StatValue(new DefinitionId(MaxHp), 100f) });

            return definition;
        }

        private MapDefinition Map()
        {
            var map = Track(ScriptableObject.CreateInstance<MapDefinition>());

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + HomeMap + "\"},\"_movementRadius\":500}", map);

            return map;
        }

        private SpawnPointDefinition PlayerSpawn()
        {
            var spawn = Track(ScriptableObject.CreateInstance<SpawnPointDefinition>());

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"spawn.home\"},\"_map\":{\"_value\":\"" + HomeMap
                + "\"},\"_spawnType\":" + (int)SpawnType.Player
                + ",\"_x\":0,\"_y\":0,\"_z\":0}", spawn);

            return spawn;
        }
    }
}

#endif
