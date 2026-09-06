#if UNITY_EDITOR

using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ChibiFantasy.Client.World;
using ChibiFantasy.Data;
using ChibiFantasy.Network;
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
    /// A real client, on a real socket, against a real dedicated server, for ten minutes.
    /// </summary>
    /// <remarks>
    /// <b>What has to be running.</b> A built development dedicated server started with
    /// <c>-soak</c>, listening on the port below. This fixture connects to it, joins, draws
    /// what it is sent through the shipped presentation, and watches its own process for ten
    /// minutes. With nothing listening it reports that and stops rather than inventing a
    /// server of its own -- a soak against a server this fixture built would be a soak
    /// against this fixture.
    ///
    /// <b>Why it is a test.</b> It needs frames, a camera, the production prefabs and the
    /// production presentation; PlayMode is where those exist together in this project. The
    /// client is therefore an editor development client, which is stated in the baseline
    /// document rather than dressed up as a shipped player.
    ///
    /// <b>What it watches.</b> Managed allocation traffic, the managed heap, the process's
    /// own memory, how many network objects it holds, how many renderers and materials
    /// exist, and whether anything was logged as an error. A leak in receiving, drawing or
    /// forgetting the world shows up in one of those.
    /// </remarks>
    [TestFixture]
    [Explicit("Needs a development dedicated server running with -soak. Ten minutes.")]
    internal sealed class ClientSoakTests
    {
        private const string RegistryPath = "Assets/DefaultPrefabObjects.asset";

        private const string CameraSettingsPath =
            "Assets/_Game/Prefabs/Prototype/ProtoCameraSettings.asset";

        private const ushort Port = 7770;
        private const string Address = "127.0.0.1";

        /// <summary>Minutes of soaking.</summary>
        private const float Minutes = 10f;

        /// <summary>Seconds between samples.</summary>
        private const float Sample = 30f;

        private GameObject _clientObject;
        private NetworkManager _client;
        private GameObject _presentation;
        private GameObject _hudObject;
        private GameObject _bagObject;
        private GameObject _cameraObject;
        private GameObject _lightObject;
        private Camera _camera;
        private RenderTexture _target;

        private int _errors;
        private long _sequence;

        private readonly List<Object> _created = new List<Object>();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Application.logMessageReceived -= OnLog;

            if (_client != null) _client.ClientManager.StopConnection();

            yield return null;

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

            foreach (Object created in _created)
            {
                if (created != null) Object.DestroyImmediate(created);
            }

            _created.Clear();
        }

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator TenMinutesConnectedToADedicatedServer()
        {
            Application.logMessageReceived += OnLog;

            BuildClient();
            BuildView();
            BuildPresentation();

            Assert.That(_client.ClientManager.StartConnection(Address, Port), Is.True,
                "the client would not start");

            yield return Until(() => _client.ClientManager.Started, 600);

            if (!_client.ClientManager.Started)
            {
                Assert.Ignore("no development dedicated server is listening on " + Address
                    + ":" + Port + "; start one with -soak before running this soak");
            }

            // The join a shipped client sends, with the claims a soak server does not check.
            _client.ClientManager.Broadcast(new WorldJoinRequestMessage
            {
                Token = "soak-client",
                ClientVersion = "1.0.0",
                ProtocolVersion = "1.0.0",
                ContentVersion = "1.0.0",
            });

            yield return Until(() => Owned() != null, 900);

            Assert.That(Owned(), Is.Not.Null,
                "the server never gave this client a character to draw");

            ProfilerRecorder allocated = ProfilerRecorder.StartNew(ProfilerCategory.Memory,
                "GC Allocated In Frame", 240);

            float finish = Time.realtimeSinceStartup + Minutes * 60f;
            float next = Time.realtimeSinceStartup;

            var samples = 0;
            long firstHeap = 0;
            long lastHeap = 0;
            int firstObjects = 0;
            int lastObjects = 0;

            while (Time.realtimeSinceStartup < finish)
            {
                Drive();

                if (Time.realtimeSinceStartup >= next)
                {
                    long heap = System.GC.GetTotalMemory(false);
                    int objects = Spawned();

                    if (samples == 0)
                    {
                        firstHeap = heap;
                        firstObjects = objects;
                    }

                    lastHeap = heap;
                    lastObjects = objects;

                    samples++;

                    Debug.Log("[client-soak] t="
                        + Mathf.RoundToInt(Time.realtimeSinceStartup) + "s"
                        + " connected=" + _client.ClientManager.Started
                        + " objects=" + objects
                        + " visuals=" + Visuals()
                        + " renderers=" + Object.FindObjectsByType<Renderer>(
                            FindObjectsSortMode.None).Length
                        + " animators=" + Object.FindObjectsByType<Animator>(
                            FindObjectsSortMode.None).Length
                        + " allocPerFrame=" + Average(allocated) + "B"
                        + " managedHeapMB=" + heap / (1024 * 1024)
                        + " privateMB=" + System.Diagnostics.Process
                            .GetCurrentProcess().PrivateMemorySize64 / (1024 * 1024)
                        + " errors=" + _errors);

                    next = Time.realtimeSinceStartup + Sample;
                }

                yield return null;
            }

            allocated.Dispose();

            Debug.Log("[client-soak] finished samples=" + samples
                + " heapFirstMB=" + firstHeap / (1024 * 1024)
                + " heapLastMB=" + lastHeap / (1024 * 1024)
                + " objectsFirst=" + firstObjects
                + " objectsLast=" + lastObjects
                + " errors=" + _errors);

            Assert.That(_client.ClientManager.Started, Is.True,
                "the client was disconnected during the soak");

            Assert.That(_errors, Is.Zero, "the client logged errors during the soak");

            // Objects come and go as monsters die and respawn; what must not happen is the
            // count climbing without bound.
            Assert.That(lastObjects, Is.LessThanOrEqualTo(firstObjects * 3 + 32),
                "the client accumulated network objects: " + firstObjects + " to "
                    + lastObjects);
        }

        // ---- driving ----------------------------------------------------------------------

        /// <summary>Asks the server to walk, so the soak is not a client standing still.</summary>
        private void Drive()
        {
            CharacterNetworkEntity owned = Owned();

            if (owned == null) return;

            float angle = Time.frameCount * 0.03f;

            owned.RequestMove(Mathf.Cos(angle), Mathf.Sin(angle), ++_sequence);
        }

        private CharacterNetworkEntity Owned()
        {
            if (_client == null || !_client.ClientManager.Started) return null;

            foreach (NetworkObject owned in _client.ClientManager.Connection.Objects)
            {
                if (owned == null) continue;

                if (owned.TryGetComponent(out CharacterNetworkEntity entity)) return entity;
            }

            return null;
        }

        private int Spawned()
        {
            var counted = 0;

            foreach (KeyValuePair<int, NetworkObject> pair in
                _client.ClientManager.Objects.Spawned)
            {
                if (pair.Value != null) counted++;
            }

            return counted;
        }

        private int Visuals()
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

        private static long Average(ProfilerRecorder recorder)
        {
            if (!recorder.Valid || recorder.Count == 0) return 0L;

            var total = 0L;

            for (var i = 0; i < recorder.Count; i++) total += recorder.GetSample(i).Value;

            return total / recorder.Count;
        }

        private void OnLog(string message, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception
                && type != LogType.Assert)
            {
                return;
            }

            // The fixture builds its NetworkManager before handing it a prefab registry,
            // which FishNet says so about once. It is this fixture's own noise, expected by
            // LogAssert, and counting it would mean the soak could never report zero.
            if (message != null && message.Contains("SpawnablePrefabs is null")) return;

            _errors++;
        }

        // ---- composition ---------------------------------------------------------------------

        private void BuildClient()
        {
            _clientObject = new GameObject("SoakClient");
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
            transport.SetPort(Port);
            transport.SetClientAddress(Address);

            _clientObject.SetActive(true);
        }

        private void BuildPresentation()
        {
            _presentation = new GameObject("Soak Presentation");

            var director = _presentation.AddComponent<WorldCameraDirector>();

            director.Compose(UnityEditor.AssetDatabase
                .LoadAssetAtPath<ChibiFantasy.Client.Prototype.ProtoCameraSettings>(
                    CameraSettingsPath));

            var hud = new GameObject("Soak HUD")
                .AddComponent<ChibiFantasy.Client.UI.WorldHudScreen>();

            var bag = new GameObject("Soak Bag")
                .AddComponent<ChibiFantasy.Client.UI.InventoryScreen>();

            _hudObject = hud.gameObject;
            _bagObject = bag.gameObject;

            var binder = _presentation
                .AddComponent<ChibiFantasy.Client.UI.WorldPresentationBinder>();

            binder.Compose(_client, hud, bag, new DefinitionRegistry<ItemDefinition>(),
                director);
        }

        private void BuildView()
        {
            _target = new RenderTexture(1920, 1080, 24) { name = "Soak Target" };
            _target.Create();

            _cameraObject = new GameObject("Soak Camera");
            _cameraObject.transform.position = new Vector3(12f, 14f, -18f);
            _cameraObject.transform.rotation = Quaternion.Euler(30f, -20f, 0f);

            _camera = _cameraObject.AddComponent<Camera>();
            _camera.targetTexture = _target;
            _camera.farClipPlane = 400f;

            _lightObject = new GameObject("Soak Light");
            _lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            Light light = _lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;
        }

        private static IEnumerator Until(System.Func<bool> condition, int frames = 400)
        {
            for (int i = 0; i < frames && !condition(); i++) yield return null;
        }
    }
}

#endif
