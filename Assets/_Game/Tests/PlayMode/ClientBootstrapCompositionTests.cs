#if UNITY_EDITOR

using System.Collections;
using ChibiFantasy.Client;
using ChibiFantasy.Client.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ChibiFantasy.Tests.PlayMode
{
    /// <summary>
    /// Whether the client is actually assembled, and assembled once.
    /// </summary>
    /// <remarks>
    /// <b>The defect this pins.</b> Every screen in this project was built to be handed a
    /// <c>SessionUiController</c>, and until 18.18B1 nothing in production handed them one.
    /// The login screen rendered, the button worked, and the click had nowhere to go. These
    /// assert the composition rather than the screens, because the screens were never the
    /// problem.
    ///
    /// <b>PlayMode, because composition happens in Awake.</b> A root that composed correctly
    /// only when a test called a method by hand would prove nothing about a player pressing
    /// Play.
    /// </remarks>
    [TestFixture]
    internal sealed class ClientBootstrapCompositionTests
    {
        private GameObject _rootObject;
        private GameObject _screenObject;
        private GameObject _driverObject;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_rootObject != null) Object.DestroyImmediate(_rootObject);
            if (_screenObject != null) Object.DestroyImmediate(_screenObject);
            if (_driverObject != null) Object.DestroyImmediate(_driverObject);

            yield return null;
        }

        [UnityTest]
        public IEnumerator TheRootComposesASessionTheScreensCanSubmitThrough()
        {
            // The screens exist before the root, which is the order a scene gives them.
            _screenObject = new GameObject("Login");

            LoginScreen screen = _screenObject.AddComponent<LoginScreen>();

            _driverObject = new GameObject("Flow");

            ClientFlowDriver driver = _driverObject.AddComponent<ClientFlowDriver>();

            driver.LoadScenes = false;

            _rootObject = new GameObject("Client Root");
            _rootObject.AddComponent<ClientApplicationBootstrap>();

            yield return null;

            ClientApplicationBootstrap root = ClientApplicationBootstrap.Current;

            Assert.That(root, Is.Not.Null, "no client root composed itself");
            Assert.That(root.Session, Is.Not.Null, "the root composed no session controller");
            Assert.That(root.Api, Is.Not.Null, "the root composed no account API");

            // What was missing in production: the screens are handed the controller.
            Assert.That(screen.Session, Is.SameAs(root.Session),
                "the login screen was never given a session to submit through");

            Assert.That(driver.CurrentScreen, Is.EqualTo(ClientScreen.Login),
                "the flow driver was never told where the session stands");
        }

        [UnityTest]
        public IEnumerator ASecondRootDestroysItselfRatherThanComposingASecondClient()
        {
            _rootObject = new GameObject("Client Root");
            _rootObject.AddComponent<ClientApplicationBootstrap>();

            yield return null;

            ClientApplicationBootstrap first = ClientApplicationBootstrap.Current;

            Assert.That(first, Is.Not.Null);

            var second = new GameObject("Second Root");

            second.AddComponent<ClientApplicationBootstrap>();

            yield return null;

            Assert.That(ClientApplicationBootstrap.Current, Is.SameAs(first),
                "a second root took over the client");

            Assert.That(second == null, Is.True,
                "a second root survived, which means a second session and a second world");
        }

        [UnityTest]
        public IEnumerator TheRootSurvivesTheSceneItWasBuiltIn()
        {
            _rootObject = new GameObject("Client Root");
            _rootObject.AddComponent<ClientApplicationBootstrap>();

            yield return null;

            Assert.That(_rootObject.scene.name, Is.EqualTo("DontDestroyOnLoad"),
                "the root would be destroyed by the first scene change, taking the "
                + "session with it");
        }
    }
}

#endif
