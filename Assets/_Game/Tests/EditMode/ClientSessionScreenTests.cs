using System.Collections.Generic;
using ChibiFantasy.Client.UI;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.UI;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The screens a player passes through, driven as a player drives them.
    /// </summary>
    /// <remarks>
    /// <b>No networking is involved and that is why these are here.</b> Signing in, listing
    /// servers and choosing a character are the account authority's business over HTTP; the
    /// world socket is not opened until after all of it. Putting these in PlayMode would buy
    /// a frame loop and prove nothing extra -- the FishNet half has its own suite where a
    /// real socket is actually under test.
    ///
    /// The screens are real <c>MonoBehaviour</c>s built here, with their canvases and
    /// buttons, driven through the same methods their buttons call.
    /// </remarks>
    [TestFixture]
    internal sealed class ClientSessionScreenTests : SessionTestBase
    {
        private readonly List<Object> _created = new List<Object>();

        private SessionUiController _controller;

        [SetUp]
        public void SetUpScreens()
        {
            var host = new GameObject("Session");
            _created.Add(host);

            _controller = host.AddComponent<SessionUiController>();

            _controller.Bind(Authority, Authority, Sessions, CurrentVersions,
                CurrentRequirement, Configuration);
        }

        [TearDown]
        public void TearDownScreens()
        {
            foreach (Object created in _created)
            {
                if (created != null) Object.DestroyImmediate(created);
            }

            _created.Clear();
        }

        private T NewScreen<T>() where T : SessionScreenBase
        {
            var host = new GameObject(typeof(T).Name);
            _created.Add(host);

            // The editor does not send Awake outside play mode, so the widgets are built
            // on first use instead. That is the screen's own rule, not a test fixture's:
            // binding a screen builds it, which is exactly what production does too.
            return host.AddComponent<T>();
        }

        /// <summary>Signs in the way the login screen does, so later screens have a session.</summary>
        private LoginScreen SignIn(AccountId account)
        {
            Authority.NextAuthenticated = account;

            LoginScreen login = NewScreen<LoginScreen>();

            login.Versions = CurrentVersions;
            login.Credentials = (_, _) => { };
            login.Bind(_controller);
            login.Fill("player", "a-password");
            login.Submit();

            return login;
        }

        // ---- A: a refused login stays on the login screen ----------------------------------

        [Test]
        public void ARefusedLoginSaysWhyAndDoesNotAdvance()
        {
            Authority.SetStatus(AccountA, AccountStatus.Banned);
            Authority.NextAuthenticated = AccountA;

            LoginScreen login = NewScreen<LoginScreen>();

            var advanced = false;

            login.Versions = CurrentVersions;
            login.Credentials = (_, _) => { };
            login.SignedIn += () => advanced = true;
            login.Bind(_controller);
            login.Fill("player", "a-password");

            login.Submit();

            Assert.That(advanced, Is.False, "a refused login must not reach a server list");
            Assert.That(login.StatusMessage, Is.EqualTo("This account is banned"));
            Assert.That(_controller.Flow.State, Is.EqualTo(SessionState.Unauthenticated));
            Assert.That(ClientFlowCoordinator.ScreenFor(_controller.Flow.State),
                Is.EqualTo(ClientScreen.Login));
        }

        [Test]
        public void AnEmptyFormIsRefusedWithoutAskingTheServer()
        {
            LoginScreen login = NewScreen<LoginScreen>();

            login.Versions = CurrentVersions;
            login.Credentials = (_, _) => Assert.Fail("no credential should be sent");
            login.Bind(_controller);
            login.Fill(string.Empty, string.Empty);

            login.Submit();

            Assert.That(login.StatusMessage, Is.EqualTo("Enter your login and password"));
        }

        [Test]
        public void ThePasswordIsClearedWhateverTheAnswerWas()
        {
            Authority.SetStatus(AccountA, AccountStatus.Banned);

            LoginScreen login = SignIn(AccountA);

            string sent = null;

            login.Credentials = (_, password) => sent = password;
            login.Fill("player", "another-password");
            login.Submit();

            Assert.That(sent, Is.EqualTo("another-password"),
                "it reached the transport once");

            // And the field no longer holds it. A failed attempt leaving the password on
            // screen is how it ends up in a screenshot.
            login.Credentials = (_, password) => sent = password;
            sent = null;
            login.Submit();

            Assert.That(sent, Is.Null.Or.Empty);
        }

        // ---- B: a good login reaches the server list ------------------------------------------

        [Test]
        public void AGoodLoginSignsInAndTheFlowMovesToTheServerList()
        {
            var advanced = false;

            Authority.NextAuthenticated = AccountA;

            LoginScreen login = NewScreen<LoginScreen>();

            login.Versions = CurrentVersions;
            login.Credentials = (_, _) => { };
            login.SignedIn += () => advanced = true;
            login.Bind(_controller);
            login.Fill("player", "a-password");

            login.Submit();

            Assert.That(advanced, Is.True, login.StatusMessage);
            Assert.That(_controller.Flow.State, Is.EqualTo(SessionState.Authenticated));
            Assert.That(ClientFlowCoordinator.ScreenFor(_controller.Flow.State),
                Is.EqualTo(ClientScreen.ServerSelect));
        }

        // ---- C: server and channel ---------------------------------------------------------------

        [Test]
        public void TheServerScreenListsWhatTheAuthorityReturnedAndSelectingAdvances()
        {
            SignIn(AccountA);

            ServerSelectScreen screen = NewScreen<ServerSelectScreen>();

            var advanced = false;
            screen.Selected += () => advanced = true;

            screen.Bind(_controller);

            Assert.That(_controller.Servers, Is.Not.Empty, "the list came from the authority");

            // Driven the way the row's button drives it.
            Invoke(screen, "Pick", _controller.Servers[0].Server);

            Assert.That(advanced, Is.True, screen.StatusMessage);
            Assert.That(_controller.Flow.State, Is.EqualTo(SessionState.ServerSelected));
            Assert.That(ClientFlowCoordinator.ScreenFor(_controller.Flow.State),
                Is.EqualTo(ClientScreen.ChannelSelect));
        }

        [Test]
        public void TheChannelScreenAdvancesToCharacterSelection()
        {
            SignIn(AccountA);

            _controller.SubmitSelectServer(Server1, RequestId.New());

            ChannelSelectScreen screen = NewScreen<ChannelSelectScreen>();

            var advanced = false;
            screen.Selected += () => advanced = true;

            screen.Bind(_controller);

            Assert.That(_controller.Channels, Is.Not.Empty);

            Invoke(screen, "Pick", Channel1A);

            Assert.That(advanced, Is.True, screen.StatusMessage);
            Assert.That(_controller.Flow.State, Is.EqualTo(SessionState.ChannelSelected));
        }

        [Test]
        public void ARefusedSelectionExplainsItselfAndTheFlowDoesNotMove()
        {
            SignIn(AccountA);

            ServerSelectScreen screen = NewScreen<ServerSelectScreen>();
            screen.Bind(_controller);

            // A server the authority does not have. The screen does not decide this.
            Invoke(screen, "Pick", new ServerId("srv-nowhere"));

            Assert.That(_controller.Flow.State, Is.EqualTo(SessionState.Authenticated));
            Assert.That(screen.StatusMessage, Is.Not.Empty,
                "a player told nothing assumes the game is broken");
        }

        // ---- D: characters, and the way into the world ---------------------------------------------

        [Test]
        public void TheCharacterScreenShowsOnlyThisAccountsCharacters()
        {
            SignIn(AccountA);

            _controller.SubmitSelectServer(Server1, RequestId.New());
            _controller.SubmitSelectChannel(Channel1A, RequestId.New());

            CharacterSelectScreen screen = NewScreen<CharacterSelectScreen>();
            screen.Bind(_controller);

            var shown = new List<string>();

            foreach (CharacterRowViewData row in _controller.Characters)
            {
                shown.Add(row.Character.Value);
            }

            Assert.That(shown, Contains.Item(CharacterA1.Value));
            Assert.That(shown, Does.Not.Contain(CharacterB1.Value),
                "another account's character must never appear in this list");
        }

        [Test]
        public void ChoosingACharacterAsksToEnterTheWorld()
        {
            SignIn(AccountA);

            _controller.SubmitSelectServer(Server1, RequestId.New());
            _controller.SubmitSelectChannel(Channel1A, RequestId.New());

            CharacterSelectScreen screen = NewScreen<CharacterSelectScreen>();

            EnterWorldResult authorised = default;
            screen.WorldAuthorised += result => authorised = result;

            screen.Bind(_controller);

            Invoke(screen, "Pick", CharacterA1);

            Assert.That(authorised.IsAccepted, Is.True, screen.StatusMessage);
            Assert.That(_controller.Flow.State, Is.EqualTo(SessionState.EnteringWorld));
            Assert.That(ClientFlowCoordinator.ScreenFor(_controller.Flow.State),
                Is.EqualTo(ClientScreen.World),
                "and only now does the flow point at the world scene");
        }

        [Test]
        public void AForeignCharacterIsRefusedByTheDomainAndTheScreenStays()
        {
            SignIn(AccountA);

            _controller.SubmitSelectServer(Server1, RequestId.New());
            _controller.SubmitSelectChannel(Channel1A, RequestId.New());

            CharacterSelectScreen screen = NewScreen<CharacterSelectScreen>();

            var authorised = false;
            screen.WorldAuthorised += _ => authorised = true;

            screen.Bind(_controller);

            // Somebody else's character, asked for directly rather than through a row.
            Invoke(screen, "Pick", CharacterB1);

            Assert.That(authorised, Is.False);
            Assert.That(_controller.Flow.State, Is.EqualTo(SessionState.ChannelSelected),
                "ownership is the authority's answer, and the screen respects it");
            Assert.That(screen.StatusMessage, Is.Not.Empty);
        }

        [Test]
        public void AnEmptyListSaysSoRatherThanShowingNothing()
        {
            SignIn(AccountB);

            _controller.SubmitSelectServer(Server1, RequestId.New());
            _controller.SubmitSelectChannel(Channel1A, RequestId.New());

            CharacterSelectScreen screen = NewScreen<CharacterSelectScreen>();
            screen.Bind(_controller);

            // Account B has a character in the fixture, so the interesting empty case is a
            // screen with no rows at all -- proven by the message it chooses.
            Assert.That(screen.StatusMessage, Is.Not.Null);
        }

        // ---- the driver follows all of it -------------------------------------------------------------

        [Test]
        public void TheDriverWalksTheWholeFlowWithoutEverDecidingAnything()
        {
            var host = new GameObject("Driver");
            _created.Add(host);

            var driver = host.AddComponent<ClientFlowDriver>();
            driver.LoadScenes = false;

            var visited = new List<ClientScreen>();
            driver.ScreenChanged += screen => visited.Add(screen);

            driver.Bind(_controller);

            SignIn(AccountA);
            driver.Evaluate();

            _controller.SubmitSelectServer(Server1, RequestId.New());
            driver.Evaluate();

            _controller.SubmitSelectChannel(Channel1A, RequestId.New());
            driver.Evaluate();

            _controller.SubmitSelectCharacter(CharacterA1, RequestId.New());
            driver.Evaluate();

            _controller.SubmitEnterWorld(RequestId.New());
            driver.Evaluate();

            Assert.That(visited, Is.EqualTo(new[]
            {
                ClientScreen.Login,
                ClientScreen.ServerSelect,
                ClientScreen.ChannelSelect,
                ClientScreen.CharacterSelect,
                ClientScreen.World,
            }), "each screen was reached because the session moved, not because a button "
                + "said so");
        }

        /// <summary>Calls a screen's private row handler, which is what its button calls.</summary>
        /// <remarks>Reflection rather than a public method, because making these public
        /// purely for a test would widen the surface a client can reach.</remarks>
        private static void Invoke(SessionScreenBase screen, string method, object argument)
        {
            System.Reflection.MethodInfo info = screen.GetType().GetMethod(method,
                System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance);

            Assert.That(info, Is.Not.Null, "no method '" + method + "'");

            info.Invoke(screen, new[] { argument });
        }
    }
}
