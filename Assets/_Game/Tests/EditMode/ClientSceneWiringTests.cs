using System.Reflection;
using ChibiFantasy.Client;
using ChibiFantasy.Client.UI;
using ChibiFantasy.Client.World;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// What each client scene must carry for a person to get through it.
    /// </summary>
    /// <remarks>
    /// <b>The defect this pins.</b> Every screen worked and none of them was ever given a
    /// session, because no scene contained anything that composed one. The world scene had a
    /// camera, a HUD and a binder, and no networking of any kind. Both were invisible to
    /// every existing test, because every existing test composed what it needed itself.
    ///
    /// <b>Assertions about the committed scenes.</b> These open the scene assets and look,
    /// so a scene saved without its root fails here rather than in somebody's hands.
    /// </remarks>
    [TestFixture]
    internal sealed class ClientSceneWiringTests
    {
        private const string Login = "Assets/_Game/Scenes/Client/Login.unity";
        private const string World = "Assets/_Game/Scenes/Client/GameWorld.unity";

        [Test]
        public void TheLoginSceneCarriesTheOneClientRoot()
        {
            Scene scene = EditorSceneManager.OpenScene(Login, OpenSceneMode.Single);

            ClientApplicationBootstrap[] roots =
                Object.FindObjectsByType<ClientApplicationBootstrap>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);

            Assert.That(roots.Length, Is.EqualTo(1),
                "the first client scene must compose exactly one client; found "
                + roots.Length);

            var flags = BindingFlags.NonPublic | BindingFlags.Instance;

            object prefab = typeof(ClientApplicationBootstrap)
                .GetField("_networkManagerPrefab", flags).GetValue(roots[0]);

            Assert.That(prefab, Is.Not.Null,
                "the client root has no network prefab, so the world cannot be reached");

            object content = typeof(ClientApplicationBootstrap)
                .GetField("_content", flags).GetValue(roots[0]);

            Assert.That(content, Is.Not.Null,
                "the client root has no content, so the bag cannot name what it holds");

            Assert.That(scene.isLoaded, Is.True);
        }

        [Test]
        public void TheLoginSceneStillCarriesItsScreenAndDriver()
        {
            EditorSceneManager.OpenScene(Login, OpenSceneMode.Single);

            Assert.That(Object.FindAnyObjectByType<LoginScreen>(FindObjectsInactive.Include),
                Is.Not.Null, "no login screen for a person to type into");

            Assert.That(Object.FindAnyObjectByType<ClientFlowDriver>(
                FindObjectsInactive.Include), Is.Not.Null,
                "nothing moves this client to the next screen");
        }

        [Test]
        public void TheWorldSceneCarriesEverythingTheRootWillComposeAgainst()
        {
            EditorSceneManager.OpenScene(World, OpenSceneMode.Single);

            Assert.That(Object.FindAnyObjectByType<WorldPresentationBinder>(
                FindObjectsInactive.Include), Is.Not.Null,
                "nothing binds the world screens to the character this client owns");

            Assert.That(Object.FindAnyObjectByType<WorldHudScreen>(
                FindObjectsInactive.Include), Is.Not.Null, "no HUD");

            Assert.That(Object.FindAnyObjectByType<InventoryScreen>(
                FindObjectsInactive.Include), Is.Not.Null, "no bag");

            Assert.That(Object.FindAnyObjectByType<WorldCameraDirector>(
                FindObjectsInactive.Include), Is.Not.Null, "no camera to follow anybody");
        }

        /// <summary>
        /// The world scene must not contain a network manager of its own.
        /// </summary>
        /// <remarks>The client's connection is composed at runtime by the root, from the
        /// shared prefab, with the server half removed. One authored into the scene would be
        /// a second manager the moment a player arrives -- and, being the shared prefab,
        /// would carry a world server into a client.</remarks>
        [Test]
        public void TheWorldSceneComposesNoNetworkManagerOfItsOwn()
        {
            EditorSceneManager.OpenScene(World, OpenSceneMode.Single);

            Assert.That(Object.FindAnyObjectByType<FishNet.Managing.NetworkManager>(
                FindObjectsInactive.Include), Is.Null,
                "the world scene authored its own network manager");
        }

        /// <summary>
        /// The driver must never ask for the scene it is already in.
        /// </summary>
        /// <remarks>
        /// <b>The defect this pins, which froze an editor.</b> Loading the active scene
        /// again destroys the driver and builds another one whose record of where it is
        /// starts empty; it decides the same thing and loads the same scene, forever. It was
        /// unreachable while nothing bound a session to a driver, and became reachable the
        /// moment the client was composed -- the first evaluation on the login screen asks
        /// for the login screen.
        ///
        /// The check is "already loaded" rather than "is active", because a scene loaded
        /// beside another -- a fixture opening the login screen additively -- would
        /// otherwise be loaded again, unloading everything it was opened beside.
        ///
        /// Asserted on the source because the behaviour cannot be exercised safely: a test
        /// that let it load scenes for real would be the loop.
        /// </remarks>
        [Test]
        public void TheFlowDriverNeverReloadsTheSceneItIsAlreadyIn()
        {
            string source = System.IO.File.ReadAllText(
                "Assets/_Game/Scripts/Client/UI/ClientFlowDriver.cs");

            Assert.That(source.Contains("!AlreadyThere(scene)"), Is.True,
                "the flow driver no longer checks whether it is already there before "
                + "loading a scene");

            Assert.That(source.Contains("gameObject.scene.name == scene"), Is.True,
                "the driver no longer recognises the scene it lives in");
        }

        /// <summary>
        /// Every client scene must be able to draw itself.
        /// </summary>
        /// <remarks>
        /// <b>The defect this pins.</b> The four screens before the world contained no
        /// camera at all. Their canvases are built at runtime and are screen-space overlays,
        /// which reads like it should not need one -- but a scene with no camera gives the
        /// render pipeline nothing to render, so pressing Play showed "Display 1 -- No
        /// cameras rendering" over an invisible login form. A person could not sign in.
        ///
        /// <b>Why no test caught it.</b> Every screen test drives the screens in code and
        /// asserts what they hold; none of them ever asked whether the scene would put
        /// anything on a display. Existence of components is not the same question as
        /// whether a frame appears.
        ///
        /// <b>What is asserted.</b> One enabled camera, on an active object, pointed at the
        /// display a player is looking at. One, because two cameras drawing the same screen
        /// is the other way to make a menu unreadable.
        /// </remarks>
        [TestCase(Login)]
        [TestCase("Assets/_Game/Scenes/Client/ServerSelect.unity")]
        [TestCase("Assets/_Game/Scenes/Client/ChannelSelect.unity")]
        [TestCase("Assets/_Game/Scenes/Client/CharacterSelect.unity")]
        [TestCase(World)]
        public void EveryClientSceneRendersToTheFirstDisplay(string scenePath)
        {
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            Camera[] cameras = Object.FindObjectsByType<Camera>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            var rendering = 0;

            foreach (Camera camera in cameras)
            {
                if (!camera.enabled) continue;
                if (!camera.gameObject.activeInHierarchy) continue;
                if (camera.targetDisplay != 0) continue;
                if (camera.targetTexture != null) continue;

                rendering++;
            }

            Assert.That(rendering, Is.EqualTo(1),
                scenePath + " has " + rendering + " cameras rendering to display 1; a "
                + "player sees \"No cameras rendering\" unless there is exactly one");
        }

        /// <summary>A scene a player looks at should also be a scene they can hear.</summary>
        /// <remarks>Unity warns about a scene with no audio listener, and about a scene with
        /// two. Both are the kind of thing nobody notices until a build is in somebody's
        /// hands.</remarks>
        [TestCase(Login)]
        [TestCase("Assets/_Game/Scenes/Client/ServerSelect.unity")]
        [TestCase("Assets/_Game/Scenes/Client/ChannelSelect.unity")]
        [TestCase("Assets/_Game/Scenes/Client/CharacterSelect.unity")]
        [TestCase(World)]
        public void EveryClientSceneHasExactlyOneAudioListener(string scenePath)
        {
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            AudioListener[] listeners = Object.FindObjectsByType<AudioListener>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            Assert.That(listeners.Length, Is.EqualTo(1),
                scenePath + " has " + listeners.Length + " audio listeners");
        }

        [Test]
        public void EveryClientSceneIsInTheBuild()
        {
            string[] wanted =
            {
                Login,
                "Assets/_Game/Scenes/Client/ServerSelect.unity",
                "Assets/_Game/Scenes/Client/ChannelSelect.unity",
                "Assets/_Game/Scenes/Client/CharacterSelect.unity",
                World,
            };

            for (var i = 0; i < wanted.Length; i++)
            {
                var found = false;

                foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
                {
                    if (scene.path != wanted[i] || !scene.enabled) continue;

                    found = true;

                    break;
                }

                Assert.That(found, Is.True, wanted[i] + " is not an enabled build scene, so "
                    + "a built client cannot reach it");
            }
        }
    }
}
