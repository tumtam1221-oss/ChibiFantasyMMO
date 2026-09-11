#if UNITY_EDITOR

using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using ChibiFantasy.Backend;
using ChibiFantasy.Client;
using ChibiFantasy.Client.UI;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ChibiFantasy.Tests.PlayMode
{
    /// <summary>
    /// Whether clicking the actual rows moves a player through the actual flow.
    /// </summary>
    /// <remarks>
    /// <b>The defect this exists for.</b> Every selection screen raises an event when the
    /// authority accepts what a player did -- <c>ServerSelectScreen.Selected</c>,
    /// <c>ChannelSelectScreen.Selected</c>, <c>CharacterSelectScreen.WorldAuthorised</c>,
    /// <c>LoginScreen.SignedIn</c> -- and not one of them had a subscriber in production.
    /// Clicking a server called the real authority, was accepted, and then nothing happened.
    /// The driver could not recover on its own either: it advances only when
    /// <c>RefreshIfChanged</c> reports a revision it has not seen, and every
    /// <c>Submit...</c> refreshes the controller before returning, so the one change that
    /// mattered was always already consumed.
    ///
    /// <b>Why the existing tests passed.</b> They call <c>driver.Evaluate()</c> by hand and
    /// the controller's <c>Submit...</c> methods directly, and they assign
    /// <c>login.Credentials</c> themselves. Every one of those is production wiring the
    /// tests supplied on production's behalf, so the missing wiring was invisible.
    ///
    /// <b>What this does instead.</b> It composes the shipped
    /// <see cref="ClientApplicationBootstrap"/> and lets it do the binding, then presses the
    /// actual <see cref="Button"/> objects the screens built. Nothing here calls a controller
    /// method, calls <c>Evaluate</c>, or names a scene. The session is moved by the real PHP
    /// authority over a real socket, or the test does not run at all.
    /// </remarks>
    [TestFixture]
    internal sealed class LiveClientNavigationTests
    {
        private const string BaseAddress = "http://127.0.0.1:8099";

        private IntegrationFixture _fixture;
        private readonly List<GameObject> _created = new List<GameObject>();

        private LoginScreen _login;
        private ServerSelectScreen _servers;
        private ChannelSelectScreen _channels;
        private CharacterSelectScreen _characters;
        private ClientFlowDriver _driver;
        private ClientApplicationBootstrap _root;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _fixture = IntegrationFixture.Load();

            if (!_fixture.IsAvailable)
            {
                Assert.Ignore("no live backend fixture: " + _fixture.Reason);
            }

            using (var probe = new UnityWebRequestTransport(BaseAddress, 5))
            {
                HttpExchange health = probe.Send("GET", "/api/health", null, null);

                if (!health.Reached)
                {
                    Assert.Ignore("no PHP server on " + BaseAddress + " ("
                        + health.FailureKind + ")");
                }
            }

            // The screens and the driver exist before the client root, which is the order a
            // scene gives them.
            _login = New<LoginScreen>();
            _servers = New<ServerSelectScreen>();
            _channels = New<ChannelSelectScreen>();
            _characters = New<CharacterSelectScreen>();

            _driver = New<ClientFlowDriver>();

            // Scenes are not loaded: this asserts where the flow decides to go, and loading
            // the world scene for real would need a dedicated server as well as PHP.
            _driver.LoadScenes = false;

            var host = new GameObject("Client Root");
            _created.Add(host);

            _root = host.AddComponent<ClientApplicationBootstrap>();

            yield return null;

            Assert.That(ClientApplicationBootstrap.Current, Is.Not.Null,
                "the client root did not compose");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // The account may not sign in again while this session is live, so it is handed
            // back before anything else -- otherwise every later test fails on a session
            // this one left behind.
            if (_root != null && _root.Api is HttpAccountApi api
                && !string.IsNullOrEmpty(api.SessionToken))
            {
                api.ReleaseSession(RequestId.New());
            }

            ClientApplicationBootstrap current = ClientApplicationBootstrap.Current;

            if (current != null) Object.DestroyImmediate(current.gameObject);

            foreach (GameObject created in _created)
            {
                if (created != null) Object.DestroyImmediate(created);
            }

            _created.Clear();

            yield return null;
        }

        /// <summary>
        /// The whole flow, by pressing the things a person presses.
        /// </summary>
        /// <remarks>Login, a server, a channel, a character. Each hop asserts that the
        /// authority moved the session and that the driver followed it, because the defect
        /// was precisely a session that moved while the client stayed put.</remarks>
        [UnityTest]
        public IEnumerator ClickingTheRealRowsWalksTheWholeFlowToTheWorld()
        {
            // ---- login ------------------------------------------------------------------
            _login.Fill(_fixture.LoginIdentifier, _fixture.Password);
            _login.Submit();

            yield return null;

            Assert.That(_root.Session.LastLoginResult.IsAccepted, Is.True,
                "live sign-in failed: " + _login.StatusMessage);

            Assert.That(_driver.CurrentScreen, Is.EqualTo(ClientScreen.ServerSelect),
                "a signed-in player was left on the login screen");

            ArriveOnScreen();

            // ---- a server, clicked ------------------------------------------------------
            Assert.That(Click(_servers, ServerLabel()), Is.True,
                "no clickable row for the server PHP listed");

            yield return null;

            // The server screen is two steps by design: a click highlights, Enter confirms.
            // Pressing only the row is what a player does first, and it must not yet have
            // asked the authority for anything.
            Assert.That(_root.Session.Flow.State, Is.EqualTo(SessionState.Authenticated),
                "highlighting a row selected a server without the player confirming it");

            Assert.That(_servers.HasHighlight, Is.True,
                "the row was pressed and nothing was highlighted");

            _servers.Confirm();

            yield return null;

            Assert.That(_root.Session.Flow.State,
                Is.EqualTo(SessionState.ServerSelected),
                "the server row was pressed and the authority never selected a server");

            Assert.That(_driver.CurrentScreen, Is.EqualTo(ClientScreen.ChannelSelect),
                "the server was selected and the client stayed on the server screen");

            ArriveOnScreen();

            // ---- a channel, clicked -----------------------------------------------------
            Assert.That(Click(_channels, ChannelLabel()), Is.True,
                "no clickable row for the channel PHP listed");

            yield return null;

            // The channel screen is two steps as well: highlight, then confirm.
            Assert.That(_channels.HasHighlight, Is.True,
                "the channel row was pressed and nothing was highlighted");

            _channels.Confirm();

            yield return null;

            Assert.That(_driver.CurrentScreen, Is.EqualTo(ClientScreen.CharacterSelect),
                "the channel was selected and the client stayed on the channel screen");

            ArriveOnScreen();

            // ---- a character: two steps, like the two screens before it ------------------
            //
            // Clicking a slot highlights it and fills the panel beside it; Enter World is
            // what asks the authority. The screen was rebuilt that way in 18.18B1 so that a
            // player sees which character they are about to take in, and this drives it the
            // way a player does rather than the way it used to work.
            Assert.That(Click(_characters, CharacterLabel()), Is.True,
                "no clickable row for the character PHP listed");

            yield return null;

            Assert.That(_characters.HasHighlight, Is.True,
                "the character row was pressed and nothing was highlighted");

            Assert.That(_root.Session.LastEnterWorldResult.IsAccepted, Is.False,
                "a highlight is not an entry: clicking a slot must not enter the world");

            _characters.Confirm();

            yield return null;

            Assert.That(_root.Session.LastEnterWorldResult.IsAccepted, Is.True,
                "the authority refused the world: " + _characters.StatusMessage);

            Assert.That(_driver.CurrentScreen, Is.EqualTo(ClientScreen.World),
                "the world was authorised and the client never went there");

            Assert.That(ClientFlowCoordinator.SceneFor(ClientScreen.World),
                Is.EqualTo(ClientScenes.World),
                "the world screen no longer maps to the world scene");
        }

        /// <summary>
        /// Binding twice must not subscribe twice.
        /// </summary>
        /// <remarks>The root binds every screen each time a scene loads. Were the handlers
        /// added without being removed, a scene opened beside another would advance the flow
        /// twice per click -- which reads as a screen being skipped. Asserted on the events'
        /// invocation lists, because a second identical subscriber is invisible from the
        /// outside until it does damage.</remarks>
        [UnityTest]
        public IEnumerator RebindingTheSceneDoesNotSubscribeTwice()
        {
            MethodInfo bind = typeof(ClientApplicationBootstrap).GetMethod("BindScene",
                BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.That(bind, Is.Not.Null, "the root no longer binds scenes");

            // Three times: once in Awake, and twice more here.
            bind.Invoke(_root, new object[] { SceneManager.GetActiveScene() });
            bind.Invoke(_root, new object[] { SceneManager.GetActiveScene() });

            yield return null;

            Assert.That(Subscribers(_login, "SignedIn"), Is.EqualTo(1),
                "the login screen would advance the flow more than once per sign-in");

            Assert.That(Subscribers(_servers, "Selected"), Is.EqualTo(1),
                "the server screen would advance the flow more than once per click");

            Assert.That(Subscribers(_channels, "Selected"), Is.EqualTo(1),
                "the channel screen would advance the flow more than once per click");

            Assert.That(Subscribers(_characters, "WorldAuthorised"), Is.EqualTo(1),
                "the character screen would advance the flow more than once per click");
        }

        /// <summary>
        /// No screen may move the client itself.
        /// </summary>
        /// <remarks>Where a client goes is decided from session state the authority owns, by
        /// the driver, in one place. A screen that loaded a scene would be navigation that
        /// had never asked the server anything -- which is exactly the shortcut that would
        /// have "fixed" this defect while breaking the thing the flow is for.</remarks>
        [Test]
        public void NoScreenNavigatesWithoutTheAuthority()
        {
            string[] screens =
            {
                "Assets/_Game/Scripts/Client/UI/LoginScreen.cs",
                "Assets/_Game/Scripts/Client/UI/ServerSelectScreen.cs",
                "Assets/_Game/Scripts/Client/UI/ChannelSelectScreen.cs",
                "Assets/_Game/Scripts/Client/UI/CharacterSelectScreen.cs",
                "Assets/_Game/Scripts/Client/UI/SessionScreenBase.cs",
            };

            for (var i = 0; i < screens.Length; i++)
            {
                string source = System.IO.File.ReadAllText(screens[i]);

                Assert.That(source.Contains("SceneManager"), Is.False,
                    screens[i] + " loads scenes itself, which is navigation that never "
                    + "asked the authority anything");
            }
        }

        // ---- helpers -----------------------------------------------------------------------

        /// <summary>
        /// Presses the row whose title carries <paramref name="identifier"/>.
        /// </summary>
        /// <remarks>The button's own <c>onClick</c>, which is what a pointer raises. Matched
        /// on the visible text so the test presses the row a person would press rather than
        /// whichever one happens to be first.</remarks>
        private static bool Click(SessionScreenBase screen, string identifier)
        {
            foreach (Button button in screen.GetComponentsInChildren<Button>(true))
            {
                if (!button.interactable || !button.isActiveAndEnabled) continue;

                var matched = false;

                foreach (TMP_Text label in button.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (label.text != null && label.text.Contains(identifier)) matched = true;
                }

                if (!matched) continue;

                button.onClick.Invoke();

                return true;
            }

            return false;
        }

        /// <summary>
        /// Stands in for the scene load this fixture deliberately does not perform.
        /// </summary>
        /// <remarks>
        /// Production reaches a screen by loading its scene, which raises <c>sceneLoaded</c>,
        /// which calls the root's <c>BindScene</c> -- and that is what fetches the list and
        /// builds the rows. With scene loading switched off, the callback has to be raised
        /// here or there would be no rows to press.
        ///
        /// <b>It is the production method that runs</b>, by reflection, exactly as the scene
        /// load would call it. Nothing here binds a screen, fetches a list or advances a
        /// flow itself -- and every assertion that the flow advanced is made <em>before</em>
        /// this is called, so a click that failed to advance cannot be rescued by it.
        /// </remarks>
        private void ArriveOnScreen()
        {
            typeof(ClientApplicationBootstrap)
                .GetMethod("BindScene", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(_root, new object[] { SceneManager.GetActiveScene() });
        }

        /// <summary>
        /// The text on the row standing for the fixture's server.
        /// </summary>
        /// <remarks>Read from what the controller received rather than assumed, because a
        /// row is labelled with a name and the fixture knows an id. Looking it up here is
        /// also the assertion that the authority returned that row at all.</remarks>
        private string ServerLabel()
        {
            foreach (ServerRowViewData row in _root.Session.Servers)
            {
                if (row.Server.Value == _fixture.ServerId) return row.NameKey.Key;
            }

            Assert.Fail("PHP listed no server matching the fixture");

            return null;
        }

        private string ChannelLabel()
        {
            foreach (ChannelRowViewData row in _root.Session.Channels)
            {
                if (row.Channel.Value == _fixture.ChannelId) return row.NameKey.Key;
            }

            Assert.Fail("PHP listed no channel matching the fixture");

            return null;
        }

        private string CharacterLabel()
        {
            foreach (CharacterRowViewData row in _root.Session.Characters)
            {
                if (row.Character.Value == _fixture.CharacterId) return row.Name;
            }

            Assert.Fail("PHP listed no character matching the fixture");

            return null;
        }

        /// <summary>How many handlers a field-like event currently holds.</summary>
        private static int Subscribers(object screen, string eventName)
        {
            FieldInfo field = screen.GetType().GetField(eventName,
                BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.That(field, Is.Not.Null, "no event '" + eventName + "'");

            var handler = field.GetValue(screen) as System.Delegate;

            return handler == null ? 0 : handler.GetInvocationList().Length;
        }

        private T New<T>() where T : MonoBehaviour
        {
            var host = new GameObject(typeof(T).Name);

            _created.Add(host);

            return host.AddComponent<T>();
        }
    }
}

#endif
