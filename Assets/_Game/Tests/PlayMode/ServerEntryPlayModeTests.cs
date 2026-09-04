// Editor-only for the same reason the other shipped-scene fixtures are: it loads the
// committed production scenes, because the point is to prove what the built process opens.
#if UNITY_EDITOR

using System.Collections;
using System.Linq;
using ChibiFantasy.Data;
using ChibiFantasy.Editor;
using ChibiFantasy.Server;
using FishNet.Managing;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ChibiFantasy.Tests.PlayMode
{
    /// <summary>
    /// What actually happens when the scene a dedicated server opens is opened.
    /// </summary>
    /// <remarks>
    /// <b>The build tells this fixture which scene to load.</b> Nothing here names
    /// World_Server as a literal: it loads <see cref="GameBuilder.ServerEntryScene"/>, so
    /// pointing the server build at a different scene makes these tests load that one and
    /// fail on it, rather than passing while the shipped process boots somewhere else.
    ///
    /// <b>This does not replace the real-process smoke.</b> A scene loading correctly in the
    /// editor is necessary and not sufficient; the built executable is what a deployment
    /// runs, and it is launched separately.
    /// </remarks>
    [TestFixture]
    internal sealed class ServerEntryPlayModeTests
    {
        private Scene _scene;
        private WorldServerBootstrap _bootstrap;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_bootstrap != null)
            {
                _bootstrap.StopServer();
                Object.DestroyImmediate(_bootstrap.gameObject);
            }

            if (_scene.IsValid() && _scene.isLoaded)
            {
                yield return SceneManager.UnloadSceneAsync(_scene);
            }
        }

        // ---- A: the entry contract ---------------------------------------------------------

        [UnityTest]
        public IEnumerator TheSceneADedicatedServerOpensIsTheWorldAndNotTheLoginScreen()
        {
            Assert.That(GameBuilder.ServerEntryScene, Is.EqualTo(GameBuilder.ServerScenes[0]),
                "the entry constant and the built scene list disagree");

            Assert.That(GameBuilder.ServerScenes[0],
                Is.Not.EqualTo(GameBuilder.ClientScenes[0]));

            yield return LoadServerEntry();

            Assert.That(_scene.name, Is.EqualTo("World_Server"));
            Assert.That(_scene.name, Is.Not.EqualTo("Login"));

            // And what it opened is a server, not a screen.
            Assert.That(_bootstrap, Is.Not.Null,
                "the server's entry scene contains no world bootstrap");

            Assert.That(Object.FindObjectsByType<NetworkManager>(
                FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator TheServersEntrySceneCarriesNoClientFlowAtAll()
        {
            yield return LoadServerEntry();

            // A dedicated server has nobody to show a login form to. Checked by name so no
            // reference to the Client assembly is needed to assert its absence.
            foreach (GameObject root in _scene.GetRootGameObjects())
            {
                foreach (Component component in root.GetComponentsInChildren<Component>(true))
                {
                    Assert.That(component, Is.Not.Null, "missing script in the entry scene");

                    string type = component.GetType().Name;

                    Assert.That(type.Contains("FlowDriver") || type.Contains("Screen")
                        || type.Contains("LoginScreen"), Is.False,
                        "the server entry scene runs client flow: " + type);
                }
            }
        }

        // ---- B: it becomes a server by itself ------------------------------------------------

        [UnityTest]
        public IEnumerator OpeningThatSceneComposesAReadyWorldAndListensWithNobodyClickingAnything()
        {
            yield return LoadServerEntry();

            // Nothing below calls Compose or StartServer. Awake did both.
            Assert.That(_bootstrap.ContentFaults, Is.Empty,
                "shipped content refused: " + string.Join("; ", _bootstrap.ContentFaults));

            Assert.That(_bootstrap.IsWorldReady, Is.True, "no world");
            Assert.That(_bootstrap.Simulation, Is.Not.Null);
            Assert.That(_bootstrap.Characters, Is.Not.Null);
            Assert.That(_bootstrap.IsListening, Is.True, "nothing is listening");

            long ticks = _bootstrap.Ticks;

            for (var i = 0; i < 5; i++) yield return null;

            Assert.That(_bootstrap.Ticks, Is.GreaterThan(ticks), "the world is not running");
        }

        // ---- C: bad content stops rather than limps -----------------------------------------------

        [UnityTest]
        public IEnumerator AServerWhoseContentIsRefusedStaysUnreadyAndAdmitsNobody()
        {
            yield return LoadServerEntry();

            var empty = ScriptableObject.CreateInstance<WorldContentCatalogue>();

            try
            {
                typeof(WorldServerBootstrap)
                    .GetField("_content", System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.Instance)
                    .SetValue(_bootstrap, empty);

                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                    "content refused"));

                _bootstrap.Compose();

                Assert.That(_bootstrap.IsWorldReady, Is.False);
                Assert.That(_bootstrap.Simulation, Is.Null, "a refused world kept running");
                Assert.That(_bootstrap.Characters, Is.Null);

                // An operator gets a list of what to fix, and no credential in it.
                Assert.That(_bootstrap.ContentFaults, Is.Not.Empty);

                foreach (string fault in _bootstrap.ContentFaults)
                {
                    foreach (string secret in new[]
                    {
                        "password", "secret", "token", "bearer", "mysql", "://",
                    })
                    {
                        Assert.That(fault.ToLowerInvariant().Contains(secret), Is.False,
                            "a content fault leaks '" + secret + "': " + fault);
                    }
                }

                // It did not fall back to a client scene, and it did not invent content.
                Assert.That(SceneManager.GetActiveScene().name, Is.Not.EqualTo("Login"));
            }
            finally
            {
                Object.DestroyImmediate(empty);
            }
        }

        // ---- D: the client still starts where a player expects ------------------------------------

        [UnityTest]
        public IEnumerator TheClientEntrySceneIsStillTheLoginScreenAndRunsNoServer()
        {
            Assert.That(GameBuilder.ClientEntryScene, Is.EqualTo(GameBuilder.ClientScenes[0]));

            _scene = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                GameBuilder.ClientEntryScene, new LoadSceneParameters(LoadSceneMode.Additive));

            yield return Until(() => _scene.isLoaded);
            yield return null;

            Assert.That(_scene.name, Is.EqualTo("Login"));

            // No world server wakes up in a player's build.
            WorldServerBootstrap[] servers = Object.FindObjectsByType<WorldServerBootstrap>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            Assert.That(servers, Is.Empty, "a client entry scene started a world server");

            foreach (GameObject root in _scene.GetRootGameObjects())
            {
                foreach (Component component in root.GetComponentsInChildren<Component>(true))
                {
                    Assert.That(component, Is.Not.Null,
                        "missing script in the client entry scene");
                }
            }

            // And the flow a player walks is present and reachable.
            Assert.That(_scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<Component>(true))
                .Any(c => c != null && c.GetType().Name.Contains("Flow")), Is.True,
                "the login scene drives no client flow");
        }

        // ---- E: stopping and starting again --------------------------------------------------------

        [UnityTest]
        public IEnumerator StoppingAndRestartingTheListenerBuildsNoSecondWorld()
        {
            yield return LoadServerEntry();

            WorldSimulation world = _bootstrap.Simulation;
            object characters = _bootstrap.Characters;

            _bootstrap.StopServer();

            Assert.That(_bootstrap.IsListening, Is.False);

            long stopped = _bootstrap.Ticks;

            for (var i = 0; i < 5; i++) yield return null;

            Assert.That(_bootstrap.Ticks, Is.EqualTo(stopped),
                "a stopped server kept simulating");

            Assert.That(_bootstrap.StartServer(), Is.True);

            for (var i = 0; i < 5; i++) yield return null;

            Assert.That(_bootstrap.Simulation, Is.SameAs(world),
                "the restart abandoned everyone in the previous world");
            Assert.That(_bootstrap.Characters, Is.SameAs(characters));
            Assert.That(_bootstrap.Ticks, Is.GreaterThan(stopped));

            // Still exactly one of each thing that could tick or listen.
            Assert.That(Object.FindObjectsByType<WorldServerBootstrap>(
                FindObjectsInactive.Include, FindObjectsSortMode.None).Length, Is.EqualTo(1));

            Assert.That(Object.FindObjectsByType<NetworkManager>(
                FindObjectsInactive.Include, FindObjectsSortMode.None).Length, Is.EqualTo(1));

            // And the scene was never reloaded to hide a duplicate.
            Assert.That(_scene.name, Is.EqualTo("World_Server"));
        }

        // ---- harness ------------------------------------------------------------------------------------

        /// <summary>Loads whatever scene the dedicated-server build starts in.</summary>
        private IEnumerator LoadServerEntry()
        {
            _scene = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                GameBuilder.ServerEntryScene, new LoadSceneParameters(LoadSceneMode.Additive));

            yield return Until(() => _scene.isLoaded);

            // Awake, and FishNet moving the manager into DontDestroyOnLoad.
            yield return null;

            WorldServerBootstrap[] found = Object.FindObjectsByType<WorldServerBootstrap>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            Assert.That(found.Length, Is.EqualTo(1),
                "the server entry scene must contribute exactly one bootstrap");

            _bootstrap = found[0];

            yield return null;
        }

        private static IEnumerator Until(System.Func<bool> condition, int frames = 400)
        {
            for (int i = 0; i < frames && !condition(); i++) yield return null;
        }
    }
}

#endif
