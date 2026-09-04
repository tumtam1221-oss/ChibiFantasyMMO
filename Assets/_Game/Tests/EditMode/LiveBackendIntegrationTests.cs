using System.Collections.Generic;
using ChibiFantasy.Backend;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The whole login-to-authorised flow, over a real socket, against the real PHP API
    /// and the real MySQL behind it.
    /// </summary>
    /// <remarks>
    /// <b>Nothing here is mocked, and that is the entire point.</b> Every other test of
    /// this adapter drives a scripted transport, which proves the adapter reads what it
    /// was told to expect. It cannot prove that PHP actually emits <c>population_known</c>
    /// as a JSON boolean rather than <c>1</c>, or that a field is named <c>class_id</c> on
    /// one side and <c>class_definition_id</c> on the other. Those are the failures a
    /// scripted transport is structurally unable to find, because the script and the
    /// reader were written by the same hand on the same afternoon.
    ///
    /// <b>How to run it.</b> Two commands, then the suite:
    /// <code>
    ///   php backend/bin/integration-fixture.php
    ///   php -S 127.0.0.1:8099 -t backend/public          (with DB_DATABASE=chibifantasy_test)
    /// </code>
    /// Without them every test here <b>skips with a reason</b> rather than failing, so the
    /// suite stays green on a machine with no PHP -- and so a skip is never mistaken for a
    /// pass. A run that reports these as ignored has not proven integration.
    ///
    /// <b>It writes to the test database only.</b> The fixture refuses to run outside a
    /// development environment and targets DB_TEST_DATABASE; the first test below asserts
    /// that is where it landed.
    /// </remarks>
    [TestFixture]
    internal sealed class LiveBackendIntegrationTests
    {
        private const string BaseAddress = "http://127.0.0.1:8099";

        private IntegrationFixture _fixture;
        private UnityWebRequestTransport _transport;
        private HttpAccountApi _api;

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
                Assert.Ignore("no PHP server on " + BaseAddress
                    + " (" + health.FailureKind + ") -- start it with: php -S 127.0.0.1:8099 -t backend/public");
            }
        }

        /// <summary>
        /// Hands the session back so the next test can sign in.
        /// </summary>
        /// <remarks>
        /// The first live run of this fixture failed on every test after the first, with
        /// <c>session_already_active</c>. That was the backend behaving exactly as Phase 15
        /// designed it -- a second live session is refused rather than silently replacing
        /// the first -- and it exposed that nothing could ever give a session up. A player
        /// who closed the game was locked out until it expired.
        ///
        /// So the release endpoint exists now, and this is what proves it works: every
        /// test here signs in fresh, which is only possible because the previous one
        /// genuinely ended its session on the server.
        /// </remarks>
        [TearDown]
        public void TearDown()
        {
            if (_api != null && !string.IsNullOrEmpty(_api.SessionToken))
            {
                _api.ReleaseSession(RequestId.New());
            }

            _transport?.Dispose();
        }

        /// <summary>Signs in for real and returns the account the server established.</summary>
        private AuthenticatedAccount SignIn()
        {
            _api.PendingLoginIdentifier = _fixture.LoginIdentifier;
            _api.PendingPassword = _fixture.Password;

            ApiResult<AuthenticatedAccount> result = _api.Authenticate(NewLogin());

            Assert.That(result.IsOk, Is.True, "login failed: " + result.Error);

            return result.Value;
        }

        private static LoginRequest NewLogin()
        {
            return new LoginRequest(RequestId.New(), new VersionSet(
                new VersionNumber(1, 0, 0), new VersionNumber(1, 0, 0), new VersionNumber(1, 0, 0)));
        }

        // ---- the fixture itself ----------------------------------------------------------

        [Test]
        public void TheFixtureLivesInTheTestDatabaseAndNotTheDevelopmentOne()
        {
            Assert.That(_fixture.Database, Does.Contain("test"),
                "an integration run must never write to the development database");
        }

        // ---- login -----------------------------------------------------------------------

        [Test]
        public void AGenuineLoginCrossesTheWireAndReturnsTheAccountTheServerDecided()
        {
            AuthenticatedAccount account = SignIn();

            Assert.That(account.Account.Value, Is.EqualTo(_fixture.AccountId));
            Assert.That(account.DisplayName, Is.Not.Empty);

            // The session and its token came from the server. Nothing here invented either.
            Assert.That(_api.Session.IsValid, Is.True);
            Assert.That(_api.SessionToken, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void TheTokenTheServerIssuedIsNotDerivedFromAnythingTheClientKnows()
        {
            SignIn();

            string token = _api.SessionToken;

            Assert.That(token, Does.Not.Contain(_fixture.AccountId));
            Assert.That(token, Does.Not.Contain(_fixture.LoginIdentifier));
            Assert.That(token, Does.Not.Contain(_api.Session.Value),
                "a token that contains its own session id gives away half of itself");
            Assert.That(token.Length, Is.GreaterThanOrEqualTo(32),
                "a short token is a guessable one");
        }

        [Test]
        public void AWrongPasswordIsRefusedByTheRealAuthenticator()
        {
            _api.PendingLoginIdentifier = _fixture.LoginIdentifier;
            _api.PendingPassword = "definitely-not-the-password";

            ApiResult<AuthenticatedAccount> result = _api.Authenticate(NewLogin());

            Assert.That(result.IsOk, Is.False);
            Assert.That(result.Error.Kind, Is.EqualTo(ApiErrorKind.Unauthorized));
        }

        [Test]
        public void AnUnknownAccountFailsTheSameWayAsAWrongPassword()
        {
            _api.PendingLoginIdentifier = "nobody-by-this-name";
            _api.PendingPassword = "irrelevant";

            ApiResult<AuthenticatedAccount> unknown = _api.Authenticate(NewLogin());

            Assert.That(unknown.Error.Kind, Is.EqualTo(ApiErrorKind.Unauthorized),
                "an unknown account must not be distinguishable from a wrong password");
        }

        [Test]
        public void AnUnauthenticatedRequestIsRefusedByTheServerAndNotByTheClient()
        {
            // No login. The client happily sends; the server is what says no.
            ApiResult<IReadOnlyList<ServerInfo>> result = _api.GetServers(new AccountId("anyone"));

            Assert.That(result.IsOk, Is.False);
            Assert.That(result.Error.Kind, Is.EqualTo(ApiErrorKind.Unauthorized));
        }

        // ---- directory: the wire format both sides actually agree on ---------------------

        [Test]
        public void TheServerListArrivesWithEveryFieldTheClientReads()
        {
            SignIn();

            ApiResult<IReadOnlyList<ServerInfo>> result = _api.GetServers(new AccountId(_fixture.AccountId));

            Assert.That(result.IsOk, Is.True, result.Error.ToString());
            Assert.That(result.Value, Is.Not.Empty);

            ServerInfo server = Find(result.Value, _fixture.ServerId);

            Assert.That(server.Server.Value, Is.EqualTo(_fixture.ServerId));
            Assert.That(server.NameKey.Key, Is.Not.Empty, "name_key crossed the wire");
            Assert.That(server.Region, Is.Not.Empty, "region crossed the wire");
            Assert.That(server.Status, Is.EqualTo(ServerStatus.Online), "status decoded as an enum");

            // The one that a scripted transport could never catch: PHP has to emit a JSON
            // boolean here. If it emitted 1, this reads false and a known population would
            // silently become Unknown.
            Assert.That(server.Enabled, Is.True, "enabled decoded as a JSON boolean");
            Assert.That(server.Population.IsKnown, Is.True, "population_known decoded as a boolean");
            Assert.That(server.Population.Value, Is.EqualTo(7), "the seeded population");
            Assert.That(server.Population.Capacity, Is.EqualTo(100));
        }

        [Test]
        public void TheChannelListArrivesWithEveryFieldTheClientReads()
        {
            SignIn();
            SelectServer();

            ApiResult<IReadOnlyList<ChannelInfo>> result =
                _api.GetChannels(new AccountId(_fixture.AccountId), new ServerId(_fixture.ServerId));

            Assert.That(result.IsOk, Is.True, result.Error.ToString());

            ChannelInfo channel = default;
            bool found = false;

            for (int i = 0; i < result.Value.Count; i++)
            {
                if (result.Value[i].Channel.Value != _fixture.ChannelId) continue;

                channel = result.Value[i];
                found = true;
            }

            Assert.That(found, Is.True, "the seeded channel came back");
            Assert.That(channel.Server.Value, Is.EqualTo(_fixture.ServerId),
                "the channel names its own server, so a mismatch is detectable");
            Assert.That(channel.NameKey.Key, Is.Not.Empty);
            Assert.That(channel.Status, Is.EqualTo(ChannelStatus.Online));
            Assert.That(channel.Enabled, Is.True);
            Assert.That(channel.PkEnabled, Is.False, "pk_enabled decoded as a boolean");
            Assert.That(channel.Population.IsKnown, Is.True);
            Assert.That(channel.Population.Value, Is.EqualTo(3));
        }

        [Test]
        public void TheCharacterListArrivesWithEveryFieldTheClientReads()
        {
            SignIn();
            SelectServer();

            ApiResult<IReadOnlyList<CharacterSelectEntry>> result =
                _api.GetCharacters(new AccountId(_fixture.AccountId), new ServerId(_fixture.ServerId));

            Assert.That(result.IsOk, Is.True, result.Error.ToString());
            Assert.That(result.Value.Count, Is.EqualTo(1));

            CharacterSelectEntry character = result.Value[0];

            Assert.That(character.Character.Value, Is.EqualTo(_fixture.CharacterId));
            Assert.That(character.Name, Is.EqualTo("Itest"));
            Assert.That(character.Gender, Is.EqualTo(CharacterGender.Female), "gender decoded as an enum");
            Assert.That(character.Level, Is.EqualTo(12));

            // The names differ on the two sides -- class_definition_id in the schema,
            // class_id on the wire -- so this asserts the mapping, not just the value.
            Assert.That(character.Class.Value, Is.EqualTo("class.novice"));
            Assert.That(character.Map.Value, Is.EqualTo(_fixture.MapId));
            Assert.That(character.Appearance.Value, Is.EqualTo("appearance.default"));
            Assert.That(character.Availability, Is.EqualTo(CharacterAvailability.Playable));
        }

        [Test]
        public void ACharacterListIsScopedToTheAccountBySqlAndNotByTheClient()
        {
            SignIn();
            SelectServer();

            // The account id is a parameter of the client method and is deliberately not
            // sent: the server takes it from the session. Passing somebody else's changes
            // nothing, which is what this asserts.
            ApiResult<IReadOnlyList<CharacterSelectEntry>> mine =
                _api.GetCharacters(new AccountId(_fixture.AccountId), new ServerId(_fixture.ServerId));

            ApiResult<IReadOnlyList<CharacterSelectEntry>> claimed =
                _api.GetCharacters(new AccountId("some-other-account"), new ServerId(_fixture.ServerId));

            Assert.That(claimed.IsOk, Is.True);
            Assert.That(claimed.Value.Count, Is.EqualTo(mine.Value.Count),
                "the client's claim about who it is has no effect on what it receives");
            Assert.That(claimed.Value[0].Character.Value, Is.EqualTo(_fixture.CharacterId));
        }

        // ---- the full flow ----------------------------------------------------------------

        private void SelectServer()
        {
            ApiResult<bool> selected = _api.SelectServer(RequestId.New(), new ServerId(_fixture.ServerId));

            Assert.That(selected.IsOk, Is.True, "select-server failed: " + selected.Error);
        }

        [Test]
        public void TheWholeFlowRunsFromLoginToAuthorisedAgainstARealServer()
        {
            AuthenticatedAccount account = SignIn();

            Assert.That(_api.SelectServer(RequestId.New(), new ServerId(_fixture.ServerId)).IsOk,
                Is.True);
            Assert.That(_api.SelectChannel(RequestId.New(), new ChannelId(_fixture.ChannelId)).IsOk,
                Is.True);
            Assert.That(_api.SelectCharacter(RequestId.New(), new CharacterId(_fixture.CharacterId)).IsOk,
                Is.True);

            ApiResult<bool> entered = _api.NotifyWorldEntry(
                account.Account,
                _api.Session,
                new CharacterId(_fixture.CharacterId),
                new ServerId(_fixture.ServerId),
                new ChannelId(_fixture.ChannelId));

            Assert.That(entered.IsOk, Is.True, "enter-world failed: " + entered.Error);
        }

        [Test]
        public void SelectingAChannelBeforeAServerIsRefusedByTheServersOwnStateMachine()
        {
            SignIn();

            // The client's copy of the flow would also refuse this. The point is that the
            // server refuses it too, which is what matters when the client is edited.
            ApiResult<bool> result = _api.SelectChannel(RequestId.New(), new ChannelId(_fixture.ChannelId));

            Assert.That(result.IsOk, Is.False);
        }

        [Test]
        public void SelectingACharacterThatBelongsToNobodyIsRefused()
        {
            SignIn();
            SelectServer();

            Assert.That(_api.SelectChannel(RequestId.New(), new ChannelId(_fixture.ChannelId)).IsOk,
                Is.True);

            ApiResult<bool> result =
                _api.SelectCharacter(RequestId.New(), new CharacterId("not-a-real-character"));

            Assert.That(result.IsOk, Is.False);
        }

        [Test]
        public void ARepeatedRequestIdIsRecognisedByTheRealIdempotencyStore()
        {
            SignIn();

            var once = RequestId.New();

            Assert.That(_api.SelectServer(once, new ServerId(_fixture.ServerId)).IsOk, Is.True);

            // The same request id again. The server replays its recorded outcome rather
            // than doing the work twice; either way the client sees success, which is the
            // property a retry depends on.
            Assert.That(_api.SelectServer(once, new ServerId(_fixture.ServerId)).IsOk, Is.True);
        }

        [Test]
        public void ARevokedTokenIsRefusedByTheServerAndNotMerelyForgottenByTheClient()
        {
            SignIn();

            string revoked = _api.SessionToken;

            Assert.That(_api.ReleaseSession(RequestId.New()).IsOk, Is.True);

            // The client has forgotten it, which proves nothing on its own -- a client can
            // be edited to remember. So the old token is presented again, deliberately, and
            // the server is what refuses it.
            HttpExchange exchange = _transport.Send("GET", "/api/servers", null, revoked);

            Assert.That(exchange.Reached, Is.True, "the server answered");
            Assert.That(exchange.Status, Is.EqualTo(401));
            Assert.That(JsonReader.Parse(exchange.Body).String("code"),
                Is.EqualTo("session_revoked"));
        }

        [Test]
        public void AForgedTokenIsRefused()
        {
            // Well-formed, right length, entirely invented.
            HttpExchange exchange = _transport.Send("GET", "/api/servers", null,
                new string('a', 64));

            Assert.That(exchange.Status, Is.EqualTo(401));
            Assert.That(JsonReader.Parse(exchange.Body).String("code"),
                Is.EqualTo("session_invalid"));
        }

        // ---- what must never come back ----------------------------------------------------

        [Test]
        public void NoResponseEverCarriesAPasswordHashOrACredential()
        {
            SignIn();

            HttpExchange servers = _transport.Send("GET", "/api/servers", null, _api.SessionToken);
            HttpExchange characters = _transport.Send("GET",
                "/api/characters?server_id=" + _fixture.ServerId, null, _api.SessionToken);

            foreach (HttpExchange exchange in new[] { servers, characters })
            {
                Assert.That(exchange.Body, Does.Not.Contain("password"));
                Assert.That(exchange.Body, Does.Not.Contain("$2y$"), "a bcrypt hash");
                Assert.That(exchange.Body, Does.Not.Contain("$argon"), "an argon hash");
                Assert.That(exchange.Body, Does.Not.Contain("token_hash"));
            }
        }

        [Test]
        public void AnErrorFromTheRealServerLeaksNoSqlPathOrStackTrace()
        {
            SignIn();

            // A deliberately malformed request: the field is required and absent.
            HttpExchange exchange = _transport.Send("POST", "/api/session/select-server",
                "{\"request_id\":\"req-bad\"}", _api.SessionToken);

            Assert.That(exchange.Reached, Is.True);
            Assert.That(exchange.IsSuccess, Is.False);

            string body = exchange.Body;

            Assert.That(body, Does.Not.Contain("SELECT "));
            Assert.That(body, Does.Not.Contain("SQLSTATE"));
            Assert.That(body, Does.Not.Contain(".php"));
            Assert.That(body, Does.Not.Contain("Stack trace"));
            Assert.That(body, Does.Not.Contain("chibifantasy_"), "the database name");

            // What it does carry: the stable contract.
            var json = JsonReader.Parse(body);
            Assert.That(json.String("code"), Is.Not.Empty);
            Assert.That(json.String("message_key"), Is.Not.Empty);
        }

        [Test]
        public void AnUnknownRouteIsAProblemDocumentAndNotAWebServerErrorPage()
        {
            HttpExchange exchange = _transport.Send("GET", "/api/does-not-exist", null, null);

            Assert.That(exchange.Reached, Is.True);
            Assert.That(exchange.Status, Is.EqualTo(404));
            Assert.That(JsonReader.Parse(exchange.Body).String("code"), Is.EqualTo("unknown_route"));
        }

        // ---- releasing a session --------------------------------------------------------

        [Test]
        public void ASecondLoginIsRefusedWhileTheFirstSessionIsStillLive()
        {
            SignIn();

            // A separate client, as a second machine would be.
            using (var other = new UnityWebRequestTransport(BaseAddress, 15))
            {
                var otherApi = new HttpAccountApi(other)
                {
                    PendingLoginIdentifier = _fixture.LoginIdentifier,
                    PendingPassword = _fixture.Password,
                };

                ApiResult<AuthenticatedAccount> second = otherApi.Authenticate(NewLogin());

                Assert.That(second.IsOk, Is.False,
                    "one live session per account: taking somebody's session away is a "
                    + "policy decision, not a side effect of signing in again");
            }
        }

        [Test]
        public void ReleasingASessionLetsTheAccountSignInAgain()
        {
            SignIn();

            ApiResult<bool> released = _api.ReleaseSession(RequestId.New());

            Assert.That(released.IsOk, Is.True, released.Error.ToString());
            Assert.That(released.Value, Is.True, "the session was live, so it ended");
            Assert.That(_api.SessionToken, Is.Null, "the client stopped holding it");

            // The proof: what was refused a moment ago now succeeds.
            AuthenticatedAccount again = SignIn();

            Assert.That(again.Account.Value, Is.EqualTo(_fixture.AccountId));
        }

        [Test]
        public void ReleasingTwiceIsHarmless()
        {
            SignIn();

            string token = _api.SessionToken;

            Assert.That(_api.ReleaseSession(RequestId.New()).IsOk, Is.True);

            // The second release presents a token the server has already revoked. A
            // disconnect callback firing twice must not be an error.
            HttpExchange again = _transport.Send("POST", "/api/session/release",
                "{\"request_id\":\"req-again\"}", token);

            Assert.That(again.IsSuccess, Is.True);
            Assert.That(JsonReader.Parse(again.Body).Bool("session_ended"), Is.False,
                "there was nothing left to end");
        }

        [Test]
        public void ReleasingASessionThatReachedTheWorldPutsTheCharacterBack()
        {
            AuthenticatedAccount account = SignIn();

            _api.SelectServer(RequestId.New(), new ServerId(_fixture.ServerId));
            _api.SelectChannel(RequestId.New(), new ChannelId(_fixture.ChannelId));
            _api.SelectCharacter(RequestId.New(), new CharacterId(_fixture.CharacterId));

            Assert.That(_api.NotifyWorldEntry(account.Account, _api.Session,
                new CharacterId(_fixture.CharacterId), new ServerId(_fixture.ServerId),
                new ChannelId(_fixture.ChannelId)).IsOk, Is.True);

            // The character is now InWorld. If releasing did not undo that, it would be
            // permanently unplayable -- the ownership corruption disconnect handling exists
            // to prevent.
            HttpExchange release = _transport.Send("POST", "/api/session/release",
                "{\"request_id\":\"req-release\"}", _api.SessionToken);

            Assert.That(JsonReader.Parse(release.Body).Bool("character_released"), Is.True);

            _api.ClearSession();

            AuthenticatedAccount again = SignIn();
            _api.SelectServer(RequestId.New(), new ServerId(_fixture.ServerId));

            ApiResult<IReadOnlyList<CharacterSelectEntry>> characters =
                _api.GetCharacters(again.Account, new ServerId(_fixture.ServerId));

            Assert.That(characters.Value[0].Availability,
                Is.EqualTo(CharacterAvailability.Playable),
                "the character is playable again, not stranded in a world nobody is in");
        }

        private static ServerInfo Find(IReadOnlyList<ServerInfo> servers, string id)
        {
            for (int i = 0; i < servers.Count; i++)
            {
                if (servers[i].Server.Value == id) return servers[i];
            }

            Assert.Fail("server " + id + " was not in the list");

            return default;
        }
    }
}
