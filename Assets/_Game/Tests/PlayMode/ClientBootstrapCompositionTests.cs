#if UNITY_EDITOR

using System.Collections;
using ChibiFantasy.Backend;
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

        /// <summary>
        /// The login screen must be handed somewhere to put what a player typed.
        /// </summary>
        /// <remarks>
        /// <b>The defect this pins.</b> <c>LoginScreen</c> raises <c>Credentials</c> with the
        /// login and password, because <c>LoginRequest</c> deliberately carries no password;
        /// <c>HttpAccountApi</c> exposes <c>PendingLoginIdentifier</c> and
        /// <c>PendingPassword</c> to receive them. Nothing in production connected the two,
        /// so every manual sign-in posted a null login, the API answered
        /// <c>400 invalid_login_identifier</c>, and the player was told "Could not reach the
        /// server" about a server that had replied correctly.
        ///
        /// <b>Why no test caught it.</b> Every login test either drove the screen against a
        /// scripted API or set the pending fields itself. Nothing asserted that the shipped
        /// composition root performs the handoff.
        ///
        /// <b>What is asserted.</b> Not that a delegate is non-null -- that the value a
        /// player types actually arrives at the API that will send it. The identifier is read
        /// back through the property's non-public getter; the password is only asserted to
        /// have arrived, and is never printed.
        /// </remarks>
        [UnityTest]
        public IEnumerator WhatAPlayerTypesReachesTheApiThatVerifiesIt()
        {
            _screenObject = new GameObject("Login");

            LoginScreen screen = _screenObject.AddComponent<LoginScreen>();

            _rootObject = new GameObject("Client Root");
            _rootObject.AddComponent<ClientApplicationBootstrap>();

            yield return null;

            ClientApplicationBootstrap root = ClientApplicationBootstrap.Current;

            Assert.That(root, Is.Not.Null);

            Assert.That(screen.Credentials, Is.Not.Null,
                "nothing subscribed to the login screen, so the password a player types is "
                + "discarded and the API is sent a null login");

            // The build must also say what it is: the screen documents its versions as
            // supplied, and nothing supplied them.
            Assert.That(screen.Versions.Client.ToString(), Is.Not.EqualTo("0.0.0"),
                "the login screen reports an empty client version");

            var api = root.Api as HttpAccountApi;

            Assert.That(api, Is.Not.Null, "the root composed no HTTP account API");

            screen.Credentials("a-player", "not-a-real-password");

            Assert.That(Read(api, "PendingLoginIdentifier"), Is.EqualTo("a-player"),
                "the login a player typed never reached the API");

            Assert.That(Read(api, "PendingPassword"), Is.Not.Null.And.Not.Empty,
                "the password a player typed never reached the API");
        }

        /// <summary>Reads a property whose getter is deliberately not public.</summary>
        /// <remarks><c>PendingPassword</c> and <c>PendingLoginIdentifier</c> are write-only
        /// on purpose, so that nothing above the API can read a credential back out. A test
        /// proving the handoff has to go around that, and nothing here prints what it
        /// reads.</remarks>
        private static string Read(HttpAccountApi api, string property)
        {
            return (string)typeof(HttpAccountApi)
                .GetProperty(property,
                    System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Instance)
                .GetGetMethod(nonPublic: true)
                .Invoke(api, null);
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
