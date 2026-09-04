using ChibiFantasy.Backend;
using ChibiFantasy.Client;
using ChibiFantasy.Client.UI;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The production screens, driven as a player drives them, against the real API.
    /// </summary>
    /// <remarks>
    /// <b>What this proves that nothing else can.</b> The screen suite drives the panels
    /// against a fixture authority; the live backend suite drives the transport against real
    /// PHP. Neither can show that a player typing a password into the real login screen ends
    /// up authorised by the real server -- the seam between them is exactly where a flow
    /// breaks, and it is the seam a fixture cannot occupy.
    ///
    /// <b>Everything below the screens is real.</b> Real HTTP, real PHP, real MySQL, real
    /// password verification, real session issuance, real account-scoped SQL. The screens are
    /// the production components with their canvases built. Nothing is stubbed and no
    /// identifier is hard-coded: every id comes from the fixture the backend itself wrote.
    ///
    /// <b>How to run it.</b> Two commands, then the suite:
    /// <code>
    ///   php backend/bin/integration-fixture.php
    ///   php -S 127.0.0.1:8099 -t backend/public          (with DB_DATABASE set)
    /// </code>
    /// Without them every test here <b>skips with a reason</b> rather than failing. A run
    /// that reports these as ignored has not proven the flow.
    /// </remarks>
    [TestFixture]
    internal sealed class LiveClientFlowIntegrationTests
    {
        private const string BaseAddress = "http://127.0.0.1:8099";

        private IntegrationFixture _fixture;
        private UnityWebRequestTransport _transport;
        private HttpAccountApi _api;
        private RemoteSessionAuthority _authority;
        private SessionUiController _controller;
        private SessionConfiguration _configuration;

        private readonly System.Collections.Generic.List<Object> _created =
            new System.Collections.Generic.List<Object>();

        [SetUp]
        public void SetUp()
        {
            _fixture = IntegrationFixture.Load();

            if (!_fixture.IsAvailable)
            {
                Assert.Ignore("no live backend fixture: " + _fixture.Reason);
            }

            _transport = new UnityWebRequestTransport(BaseAddress, 15);
            _api = new HttpAccountApi(_transport);

            HttpExchange health = _transport.Send("GET", "/api/health", null, null);

            if (!health.IsSuccess)
            {
                Assert.Ignore("no PHP server on " + BaseAddress + " (" + health.FailureKind
                    + ") -- start it with: php -S 127.0.0.1:8099 -t backend/public");
            }

            _configuration = NewConfiguration();

            var host = new GameObject("Live Session");
            _created.Add(host);

            _controller = host.AddComponent<SessionUiController>();

            // The production pairing: the HTTP transport for the calls, and the authority
            // that answers the domain from what that transport was told.
            _authority = new RemoteSessionAuthority(_api);

            _controller.Bind(_api, _authority, new SessionDirectory(), Versions, Required,
                _configuration);
        }

        [TearDown]
        public void TearDown()
        {
            if (_api != null && !string.IsNullOrEmpty(_api.SessionToken))
            {
                _api.ReleaseSession(RequestId.New());
            }

            _transport?.Dispose();

            foreach (Object created in _created)
            {
                if (created != null) Object.DestroyImmediate(created);
            }

            _created.Clear();
        }

        // ---- the flow, screen by screen ----------------------------------------------------

        [Test]
        public void TheRealLoginScreenSignsInAgainstTheRealBackend()
        {
            LoginScreen login = NewScreen<LoginScreen>();

            var advanced = false;

            login.Versions = Versions;
            login.Credentials = SendCredentials;
            login.SignedIn += () => advanced = true;
            login.Bind(_controller);

            login.Fill(_fixture.LoginIdentifier, _fixture.Password);
            login.Submit();

            Assert.That(advanced, Is.True, login.StatusMessage);
            Assert.That(_controller.Flow.State, Is.EqualTo(SessionState.Authenticated));
            Assert.That(ClientFlowCoordinator.ScreenFor(_controller.Flow.State),
                Is.EqualTo(ClientScreen.ServerSelect));

            // The token the server issued exists on the transport, and nowhere else.
            Assert.That(_api.SessionToken, Is.Not.Empty);
            Assert.That(login.StatusMessage, Does.Not.Contain(_api.SessionToken));
        }

        [Test]
        public void AWrongPasswordIsRefusedByPhpAndTheScreenSaysSoWithoutSayingWhy()
        {
            LoginScreen login = NewScreen<LoginScreen>();

            var advanced = false;

            login.Versions = Versions;
            login.Credentials = SendCredentials;
            login.SignedIn += () => advanced = true;
            login.Bind(_controller);

            login.Fill(_fixture.LoginIdentifier, _fixture.Password + "-wrong");
            login.Submit();

            Assert.That(advanced, Is.False, "a refused login must not reach a server list");
            Assert.That(_controller.Flow.State, Is.EqualTo(SessionState.Unauthenticated));

            // What the player is told names no account, no SQL, no host and no exception.
            Assert.That(login.StatusMessage, Is.Not.Empty);
            Assert.That(login.StatusMessage.ToLowerInvariant(),
                Does.Not.Contain(_fixture.LoginIdentifier.ToLowerInvariant()));
            Assert.That(login.StatusMessage, Does.Not.Contain("SQL"));
            Assert.That(login.StatusMessage, Does.Not.Contain("127.0.0.1"));
            Assert.That(login.StatusMessage, Does.Not.Contain("Exception"));
            Assert.That(login.StatusMessage, Does.Not.Contain("backend"));
        }

        [Test]
        public void AnUnknownAccountIsRefusedTheSameWayAsAWrongPassword()
        {
            string wrongPassword = Refuse(_fixture.LoginIdentifier,
                _fixture.Password + "-wrong");

            string unknownAccount = Refuse("nobody-" + RequestId.New().Value, "whatever");

            Assert.That(unknownAccount, Is.EqualTo(wrongPassword),
                "telling them apart tells an attacker which logins are real");
        }

        [Test]
        public void TheServerAndChannelScreensListWhatPhpReturned()
        {
            SignIn();

            ServerSelectScreen servers = NewScreen<ServerSelectScreen>();
            servers.Bind(_controller);

            Assert.That(_controller.Servers, Is.Not.Empty,
                "the list came from the database, not from a fixture in this file");

            var pickedServer = false;
            servers.Selected += () => pickedServer = true;

            Invoke(servers, "Pick", new ServerId(_fixture.ServerId));

            Assert.That(pickedServer, Is.True, servers.StatusMessage);
            Assert.That(_controller.Flow.State, Is.EqualTo(SessionState.ServerSelected));

            ChannelSelectScreen channels = NewScreen<ChannelSelectScreen>();
            channels.Bind(_controller);

            Assert.That(_controller.Channels, Is.Not.Empty);

            var pickedChannel = false;
            channels.Selected += () => pickedChannel = true;

            Invoke(channels, "Pick", new ChannelId(_fixture.ChannelId));

            Assert.That(pickedChannel, Is.True, channels.StatusMessage);
            Assert.That(_controller.Flow.State, Is.EqualTo(SessionState.ChannelSelected));
        }

        [Test]
        public void TheCharacterScreenEntersTheWorldThroughTheRealAuthority()
        {
            SignInAndReachChannel();

            CharacterSelectScreen characters = NewScreen<CharacterSelectScreen>();

            EnterWorldResult authorised = default;
            characters.WorldAuthorised += result => authorised = result;

            characters.Bind(_controller);

            Assert.That(_controller.Characters, Is.Not.Empty,
                "the account's own characters, scoped by SQL");

            Invoke(characters, "Pick", new CharacterId(_fixture.CharacterId));

            Assert.That(authorised.IsAccepted, Is.True, characters.StatusMessage);
            Assert.That(_controller.Flow.State, Is.EqualTo(SessionState.EnteringWorld));
            Assert.That(ClientFlowCoordinator.ScreenFor(_controller.Flow.State),
                Is.EqualTo(ClientScreen.World),
                "and only now does the flow point at the world scene");
        }

        [Test]
        public void ACharacterThisAccountDoesNotOwnIsRefusedByTheAuthorityNotByTheScreen()
        {
            SignInAndReachChannel();

            CharacterSelectScreen characters = NewScreen<CharacterSelectScreen>();

            var authorised = false;
            characters.WorldAuthorised += _ => authorised = true;

            characters.Bind(_controller);

            // Asked for directly rather than through a row, the way a tampered client would.
            Invoke(characters, "Pick", new CharacterId("not-a-real-character"));

            Assert.That(authorised, Is.False);
            Assert.That(_controller.Flow.State, Is.EqualTo(SessionState.ChannelSelected),
                "ownership is the server's answer and the screen respects it");
            Assert.That(characters.StatusMessage, Is.Not.Empty);
        }

        [Test]
        public void TheDriverFollowsTheRealSessionAllTheWayToTheWorld()
        {
            var host = new GameObject("Live Driver");
            _created.Add(host);

            var driver = host.AddComponent<ClientFlowDriver>();
            driver.LoadScenes = false;

            var visited = new System.Collections.Generic.List<ClientScreen>();
            driver.ScreenChanged += screen => visited.Add(screen);

            driver.Bind(_controller);

            SignIn();
            driver.Evaluate();

            _controller.SubmitSelectServer(new ServerId(_fixture.ServerId), RequestId.New());
            driver.Evaluate();

            _controller.SubmitSelectChannel(new ChannelId(_fixture.ChannelId), RequestId.New());
            driver.Evaluate();

            _controller.SubmitSelectCharacter(new CharacterId(_fixture.CharacterId),
                RequestId.New());
            driver.Evaluate();

            Assert.That(_controller.SubmitEnterWorld(RequestId.New()).IsAccepted, Is.True);
            driver.Evaluate();

            Assert.That(visited, Is.EqualTo(new[]
            {
                ClientScreen.Login,
                ClientScreen.ServerSelect,
                ClientScreen.ChannelSelect,
                ClientScreen.CharacterSelect,
                ClientScreen.World,
            }), "every screen was reached because the real server moved the session");
        }

        // ---- the authority reports rather than decides -----------------------------------------

        [Test]
        public void TheAuthorityKnowsOnlyWhatTheServerSentIt()
        {
            // Before anything is fetched, everything is unknown -- and unknown is not
            // permission.
            var fresh = new RemoteSessionAuthority(_api);

            Assert.That(fresh.StatusOf(new AccountId(_fixture.AccountId)),
                Is.EqualTo(AccountStatus.Unknown));
            Assert.That(fresh.TryGetServer(new ServerId(_fixture.ServerId), out ServerInfo _),
                Is.False);
            Assert.That(fresh.IsUnderMaintenance(), Is.False);

            SignIn();

            Assert.That(_authority.StatusOf(new AccountId(_fixture.AccountId)),
                Is.EqualTo(AccountStatus.Active), "the status PHP reported");
            Assert.That(_authority.StatusOf(new AccountId("somebody-else")),
                Is.EqualTo(AccountStatus.Unknown),
                "a client knows the status of exactly one account");

            Assert.That(_authority.TryGetServer(new ServerId(_fixture.ServerId),
                out ServerInfo server), Is.True);
            Assert.That(server.Server.Value, Is.EqualTo(_fixture.ServerId));

            Assert.That(_authority.TryGetServer(new ServerId("srv-nowhere"), out ServerInfo _),
                Is.False, "a server the authority never listed is simply absent");
        }

        [Test]
        public void OwnershipIsAskedOverTheWireRatherThanReadFromTheListAlreadyFetched()
        {
            SignInAndReachChannel();

            var account = new AccountId(_fixture.AccountId);

            Assert.That(_authority.OwnsCharacter(account,
                new CharacterId(_fixture.CharacterId)), Is.True);

            Assert.That(_authority.OwnsCharacter(account,
                new CharacterId("not-a-real-character")), Is.False);
        }

        // ---- helpers -----------------------------------------------------------------------------

        private static VersionSet Versions => new VersionSet(new VersionNumber(1, 0, 0),
            new VersionNumber(1, 0, 0), new VersionNumber(1, 0, 0));

        private static VersionRequirement Required => new VersionRequirement(
            new VersionNumber(1, 0, 0), new VersionNumber(1, 0, 0),
            new VersionNumber(1, 0, 0), new VersionNumber(1, 0, 0),
            new VersionNumber(1, 0, 0));

        /// <summary>
        /// Hands the typed credentials to the transport that will verify them.
        /// </summary>
        /// <remarks>This is the production wiring the screen expects: the screen names no
        /// transport, and the credential goes to the concrete implementation rather than
        /// through <c>IAccountApi</c>, which has nowhere to put a password.</remarks>
        private void SendCredentials(string account, string password)
        {
            _api.PendingLoginIdentifier = account;
            _api.PendingPassword = password;
        }

        private LoginScreen SignIn()
        {
            LoginScreen login = NewScreen<LoginScreen>();

            login.Versions = Versions;
            login.Credentials = SendCredentials;
            login.Bind(_controller);
            login.Fill(_fixture.LoginIdentifier, _fixture.Password);
            login.Submit();

            Assert.That(_controller.Flow.State, Is.EqualTo(SessionState.Authenticated),
                "live sign-in failed: " + login.StatusMessage);

            return login;
        }

        private void SignInAndReachChannel()
        {
            SignIn();

            Assert.That(_controller.SubmitSelectServer(new ServerId(_fixture.ServerId),
                RequestId.New()).IsAccepted, Is.True);

            Assert.That(_controller.SubmitSelectChannel(new ChannelId(_fixture.ChannelId),
                RequestId.New()).IsAccepted, Is.True);
        }

        /// <summary>Attempts a login that should fail and returns what the player is told.</summary>
        private string Refuse(string account, string password)
        {
            LoginScreen login = NewScreen<LoginScreen>();

            login.Versions = Versions;
            login.Credentials = SendCredentials;
            login.Bind(_controller);
            login.Fill(account, password);
            login.Submit();

            Assert.That(_controller.Flow.State, Is.EqualTo(SessionState.Unauthenticated));

            return login.StatusMessage;
        }

        private T NewScreen<T>() where T : SessionScreenBase
        {
            var host = new GameObject(typeof(T).Name);
            _created.Add(host);

            return host.AddComponent<T>();
        }

        private SessionConfiguration NewConfiguration()
        {
            var definition = ScriptableObject.CreateInstance<SessionConfiguration>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"config.session\"},\"_maxCharacterSlots\":5,"
                + "\"_sessionLifetimeSeconds\":0,\"_maxLoginAttempts\":0,"
                + "\"_loginAttemptWindowSeconds\":60,\"_maxEnterWorldAttempts\":0,"
                + "\"_allowConcurrentSessions\":false}", definition);

            _created.Add(definition);

            return definition;
        }

        /// <summary>Calls a screen's private row handler, which is what its button calls.</summary>
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
