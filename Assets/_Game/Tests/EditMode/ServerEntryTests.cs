using System;
using System.IO;
using System.Linq;
using System.Reflection;
using ChibiFantasy.Editor;
using ChibiFantasy.Server;
using FishNet.Managing;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Where each of the two programs starts.
    /// </summary>
    /// <remarks>
    /// <b>The defect this pins:</b> a dedicated server built from the project's shared scene
    /// list opens scene 0, which is Login -- a screen asking a machine with no display to
    /// type a password. It builds, it runs, it reports no errors, and it never becomes a
    /// server. Everything here exists so that cannot come back quietly.
    ///
    /// <b>Entry is a build-time fact.</b> Which scene is first in a build's own scene list
    /// is the whole mechanism, so these are assertions about the lists, and the PlayMode and
    /// real-process smokes are what prove the lists were told the truth.
    /// </remarks>
    [TestFixture]
    internal sealed class ServerEntryTests
    {
        private const string Login = "Assets/_Game/Scenes/Client/Login.unity";
        private const string World = "Assets/_Game/Scenes/World/World_Server.unity";

        /// <summary>The one prototype asset the shipped world legitimately reaches.</summary>
        private const string KnownPrototypeAnimator =
            "Assets/_Game/Prefabs/Prototype/Proto_Locomotion.controller";

        // ---- entry ---------------------------------------------------------------------------

        [Test]
        public void APlayersBuildStartsAtTheLoginScreen()
        {
            Assert.That(GameBuilder.ClientScenes, Is.Not.Empty);
            Assert.That(GameBuilder.ClientScenes[0], Is.EqualTo(Login));
            Assert.That(GameBuilder.ClientEntryScene, Is.EqualTo(Login));
        }

        [Test]
        public void ADedicatedServerStartsInTheWorld()
        {
            Assert.That(GameBuilder.ServerScenes, Is.Not.Empty);
            Assert.That(GameBuilder.ServerScenes[0], Is.EqualTo(World),
                "the server's first scene is what its process opens");
            Assert.That(GameBuilder.ServerEntryScene, Is.EqualTo(World));
        }

        [Test]
        public void ADedicatedServerShipsNoClientSceneAtAll()
        {
            // Not merely "Login is not first": a server that contains Login can be told to
            // load it, and a server that contains no client scene cannot.
            foreach (string scene in GameBuilder.ServerScenes)
            {
                Assert.That(scene.Contains("/Scenes/Client/"), Is.False, scene);
                Assert.That(scene.Contains("SampleScene"), Is.False, scene);
                Assert.That(scene.Contains("/Prototype/"), Is.False, scene);
            }

            Assert.That(GameBuilder.ServerScenes, Has.No.Member(Login));
        }

        [Test]
        public void TheTwoBuildsShareNoEntryScene()
        {
            Assert.That(GameBuilder.ClientScenes, Has.No.Member(World),
                "a player's build must not be able to open the server world");

            Assert.That(GameBuilder.ClientScenes[0],
                Is.Not.EqualTo(GameBuilder.ServerScenes[0]));
        }

        [Test]
        public void EveryNamedSceneActuallyExists()
        {
            foreach (string scene in GameBuilder.ClientScenes.Concat(GameBuilder.ServerScenes))
            {
                Assert.That(File.Exists(scene), Is.True, "no scene at " + scene);
            }
        }

        [Test]
        public void TheClientKeepsTheWholeFlowItNeedsToWalkThrough()
        {
            // Login is the entry; the rest have to be built in or SceneManager cannot reach
            // them, which would turn "press Login" into a silent no-op in a player build.
            foreach (string scene in new[]
            {
                "ServerSelect", "ChannelSelect", "CharacterSelect", "GameWorld",
            })
            {
                Assert.That(GameBuilder.ClientScenes.Any(s => s.Contains(scene)), Is.True,
                    "a player's build cannot reach " + scene);
            }
        }

        // ---- how the builds are configured ---------------------------------------------------------

        [Test]
        public void TheServerIsBuiltAsADedicatedServerAndTheClientIsNot()
        {
            // The subtarget is what defines UNITY_SERVER and strips the graphics a server
            // will never use. Asserted on the options a build actually runs with, so a
            // change that quietly shipped a headless client instead fails here.
            BuildPlayerOptions server = GameBuilder.ServerOptions(
                BuildTarget.StandaloneLinux64, "Builds/x/s");

            Assert.That(server.subtarget, Is.EqualTo((int)StandaloneBuildSubtarget.Server));
            Assert.That(server.scenes[0], Is.EqualTo(World));

            BuildPlayerOptions client = GameBuilder.ClientOptions(
                BuildTarget.StandaloneWindows64, "Builds/x/c");

            Assert.That(client.subtarget, Is.EqualTo((int)StandaloneBuildSubtarget.Player));
            Assert.That(client.scenes[0], Is.EqualTo(Login));

            Assert.That(server.subtarget, Is.Not.EqualTo(client.subtarget));
        }

        [Test]
        public void NoBuildRewritesTheProjectsSharedSceneList()
        {
            // A build that reordered EditorBuildSettings would leave whoever built last
            // deciding what the next person's Play button opens.
            string source = File.ReadAllText("Assets/_Game/Scripts/Editor/GameBuilder.cs");

            Assert.That(source.Contains("EditorBuildSettings.scenes ="), Is.False,
                "a build rewrites the project's scene list");

            Assert.That(source.Contains("scenes = ClientScenes")
                && source.Contains("scenes = ServerScenes"), Is.True,
                "builds must state their own scenes explicitly");
        }

        [Test]
        public void ServerEntryIsDecidedByTheBuildAndNotByBatchMode()
        {
            string source = File.ReadAllText("Assets/_Game/Scripts/Editor/GameBuilder.cs");

            // A client can be run headless too, so batch mode does not mean "server".
            Assert.That(source.Contains("Application.isBatchMode && "), Is.False);

            // And nothing anywhere decides which scene to open from batch mode.
            foreach (string path in RuntimeSources())
            {
                Assert.That(Code(path).Contains("isBatchMode"), Is.False,
                    path + " decides behaviour from batch mode");
            }
        }

        [Test]
        public void NothingAtRuntimeLooksUpASceneByBuildIndexOrAssetDatabase()
        {
            foreach (string path in RuntimeSources())
            {
                string source = Code(path);

                Assert.That(source.Contains("AssetDatabase"), Is.False,
                    path + " uses the asset database at runtime");

                Assert.That(source.Contains("LoadScene(0)")
                    || source.Contains("GetSceneByBuildIndex"), Is.False,
                    path + " names a scene by build index");
            }
        }

        // ---- the server side owns no client code -----------------------------------------------------

        [Test]
        public void TheServerAssemblyDoesNotReferenceTheClient()
        {
            string asmdef = File.ReadAllText(
                "Assets/_Game/Scripts/Server/ChibiFantasy.Server.asmdef");

            Assert.That(asmdef.Contains("ChibiFantasy.Client"), Is.False,
                "the server assembly references the client");
            Assert.That(asmdef.Contains("ChibiFantasy.UI"), Is.False,
                "the server assembly references the UI");

            Assembly server = typeof(WorldServerBootstrap).Assembly;

            foreach (AssemblyName referenced in server.GetReferencedAssemblies())
            {
                Assert.That(referenced.Name, Is.Not.EqualTo("ChibiFantasy.Client"));
                Assert.That(referenced.Name, Is.Not.EqualTo("ChibiFantasy.UI"));
            }
        }

        [Test]
        public void ThereIsStillExactlyOneAuthoritativeWorldTickSource()
        {
            Assembly server = typeof(WorldServerBootstrap).Assembly;

            string[] simulations = server.GetTypes()
                .Where(t => t.Name.EndsWith("Simulation"))
                .Select(t => t.FullName)
                .ToArray();

            Assert.That(simulations.Length, Is.EqualTo(1), string.Join(", ", simulations));

            string[] bootstraps = server.GetTypes()
                .Where(t => t.Name.Contains("ServerBootstrap"))
                .Select(t => t.FullName)
                .ToArray();

            Assert.That(bootstraps.Length, Is.EqualTo(1), string.Join(", ", bootstraps));
        }

        // ---- the scene the server opens ----------------------------------------------------------------

        [Test]
        public void TheServerSceneIsStructurallyWhatTheProcessNeeds()
        {
            Scene scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(World,
                UnityEditor.SceneManagement.OpenSceneMode.Additive);

            try
            {
                var managers = 0;
                var bootstraps = 0;
                var missing = 0;
                var activeRoots = 0;

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (root.activeSelf) activeRoots++;

                    managers += root.GetComponentsInChildren<NetworkManager>(true).Length;
                    bootstraps += root.GetComponentsInChildren<WorldServerBootstrap>(true).Length;

                    foreach (Component component in root.GetComponentsInChildren<Component>(true))
                    {
                        if (component == null) missing++;
                    }
                }

                Assert.That(managers, Is.EqualTo(1), "two managers means two sockets");
                Assert.That(bootstraps, Is.EqualTo(1), "two bootstraps means two worlds");
                Assert.That(missing, Is.Zero, "the server scene has missing scripts");

                Assert.That(activeRoots, Is.GreaterThan(0),
                    "every root is inactive, so nothing in this scene would ever wake");

                // The catalogue it composes from, and the guarantee that it starts itself.
                WorldServerBootstrap bootstrap = UnityEngine.Object
                    .FindObjectsByType<WorldServerBootstrap>(FindObjectsInactive.Include,
                        FindObjectsSortMode.None)
                    .Single(b => b.gameObject.scene == scene);

                Assert.That(bootstrap.gameObject.activeInHierarchy, Is.True,
                    "the bootstrap is on an inactive object and its Awake would never run");

                Assert.That(Field(bootstrap, "_content"), Is.Not.Null,
                    "the server scene names no content catalogue");

                Assert.That(Field(bootstrap, "_characterPrefab"), Is.Not.Null,
                    "the server scene names no character prefab");

                Assert.That((bool)Field(bootstrap, "_startOnAwake"), Is.True,
                    "the server would wait for somebody to click something");
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void TheServerSceneDependsOnNoTestOrValidationAsset()
        {
            Assert.That(File.Exists(KnownPrototypeAnimator), Is.True,
                "the allowance below names an asset that no longer exists");

            var allowed = new System.Collections.Generic.HashSet<string>(
                AssetDatabase.GetDependencies(KnownPrototypeAnimator, true))
            {
                KnownPrototypeAnimator,
            };

            foreach (string dependency in AssetDatabase.GetDependencies(World, true))
            {
                // This project's own assets. A package's internal folder layout is its
                // business -- FishNet ships its inspector icons under a path containing
                // "Editor" and nothing about that reaches the server's behaviour.
                if (!dependency.StartsWith("Assets/")) continue;

                Assert.That(dependency.Contains("/Tests/"), Is.False, dependency);
                Assert.That(dependency.Contains("/Validation/"), Is.False, dependency);
                Assert.That(dependency.Contains("/Editor/"), Is.False, dependency);
                // One prototype asset is reached on purpose and has been since 18.6: the
                // character prefab's animator controller, the only locomotion controller
                // that was ever validated, together with the clips it plays. Allowed as
                // that controller's own dependency closure rather than as a blanket
                // exemption, so any *other* prototype asset drifting into the shipped
                // server still fails here.
                if (allowed.Contains(dependency)) continue;

                Assert.That(dependency.Contains("/Prototype/"), Is.False, dependency);
            }
        }

        // ---- helpers ---------------------------------------------------------------------------------------

        /// <summary>The subtarget a build method actually passes, read from its source.</summary>
        /// <remarks>Read rather than restated, so changing the build method to ship a
        /// headless client instead of a dedicated server fails here.</remarks>
        private static int SubtargetOf(string method)
        {
            string source = File.ReadAllText("Assets/_Game/Scripts/Editor/GameBuilder.cs");

            int start = source.IndexOf(method + "(BuildTarget", StringComparison.Ordinal);

            Assert.That(start, Is.GreaterThan(-1), "no method " + method);

            const string Marker = "subtarget = (int)StandaloneBuildSubtarget.";

            int at = source.IndexOf(Marker, start, StringComparison.Ordinal);

            Assert.That(at, Is.GreaterThan(-1), method + " sets no subtarget");

            string named = source.Substring(at + Marker.Length).Split(',')[0].Trim();

            return (int)(StandaloneBuildSubtarget)Enum.Parse(
                typeof(StandaloneBuildSubtarget), named);
        }

        /// <summary>A file's code, with its comments removed.</summary>
        /// <remarks>These guards look for a call, and a file that documents "no
        /// AssetDatabase here" contains the word without doing it. Scanning prose would
        /// make the guard punish the explanation and reward silence.</remarks>
        private static string Code(string path)
        {
            var code = new System.Text.StringBuilder();

            foreach (string line in File.ReadAllLines(path))
            {
                string trimmed = line.TrimStart();

                if (trimmed.StartsWith("//") || trimmed.StartsWith("*")
                    || trimmed.StartsWith("/*"))
                {
                    continue;
                }

                int comment = line.IndexOf("//", StringComparison.Ordinal);

                code.AppendLine(comment >= 0 ? line.Substring(0, comment) : line);
            }

            return code.ToString();
        }

        private static string[] RuntimeSources()
        {
            return Directory.GetFiles("Assets/_Game/Scripts", "*.cs",
                    SearchOption.AllDirectories)
                .Where(p => !p.Replace('\\', '/').Contains("/Editor/"))
                .ToArray();
        }

        private static object Field(object target, string name)
        {
            return target.GetType()
                .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(target);
        }
    }
}
