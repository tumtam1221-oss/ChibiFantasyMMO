using System.Collections.Generic;
using ChibiFantasy.Backend;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The HTTP adapter: wire format, failure mapping, and what it refuses to keep.
    /// </summary>
    /// <remarks>
    /// Driven through a scripted transport rather than a socket, so the tests are
    /// deterministic and run in EditMode. What is being checked is the adapter's own
    /// behaviour -- how it builds a request, how it reads a reply, and what it does with a
    /// token -- which is exactly the part a live server would not exercise any better.
    /// </remarks>
    [TestFixture]
    internal sealed class HttpAccountApiTests
    {
        private ScriptedHttpTransport _transport;
        private HttpAccountApi _api;

        [SetUp]
        public void SetUp()
        {
            _transport = new ScriptedHttpTransport();
            _api = new HttpAccountApi(_transport);
        }

        private LoginRequest NewLogin()
        {
            return new LoginRequest(RequestId.New(), new VersionSet(
                new VersionNumber(1, 0, 0), new VersionNumber(1, 0, 0), new VersionNumber(1, 0, 0)));
        }

        private void ArrangeLogin(string accountId = "acc-a", string token = "tok-abc")
        {
            _transport.EnqueueOk("POST", "/api/auth/login",
                "{\"session_id\":\"sess-1\",\"token\":\"" + token + "\",\"account_id\":\""
                + accountId + "\",\"display_name\":\"Ayla\",\"replayed\":false}");
        }

        // ---- login -----------------------------------------------------------------

        [Test]
        public void A_successful_login_yields_the_account_and_keeps_the_token()
        {
            ArrangeLogin();

            _api.PendingLoginIdentifier = "ayla@test";
            _api.PendingPassword = "a-password";

            ApiResult<AuthenticatedAccount> result = _api.Authenticate(NewLogin());

            Assert.That(result.IsOk, Is.True);
            Assert.That(result.Value.Account, Is.EqualTo(new AccountId("acc-a")));
            Assert.That(result.Value.DisplayName, Is.EqualTo("Ayla"));
            Assert.That(_api.Session, Is.EqualTo(new SessionId("sess-1")));
        }

        [Test]
        public void The_password_is_sent_once_and_never_retained()
        {
            ArrangeLogin();

            _api.PendingLoginIdentifier = "ayla@test";
            _api.PendingPassword = "a-password";

            _api.Authenticate(NewLogin());

            Assert.That(_transport.LastBody, Does.Contain("a-password"),
                "it must reach the one request that needs it");

            // And it is gone afterwards: a second call cannot resend it.
            _transport.EnqueueOk("POST", "/api/auth/login", "{\"account_id\":\"acc-a\"}");
            _api.Authenticate(NewLogin());

            Assert.That(_transport.LastBody, Does.Not.Contain("a-password"),
                "the password does not outlive its call");
        }

        [Test]
        public void The_password_is_cleared_even_when_the_transport_fails()
        {
            _transport.Enqueue("POST", "/api/auth/login", HttpExchange.Unreachable("no route"));

            _api.PendingLoginIdentifier = "ayla@test";
            _api.PendingPassword = "a-password";

            _api.Authenticate(NewLogin());

            _transport.EnqueueOk("POST", "/api/auth/login", "{\"account_id\":\"acc-a\"}");
            _api.Authenticate(NewLogin());

            Assert.That(_transport.LastBody, Does.Not.Contain("a-password"));
        }

        [Test]
        public void A_login_sends_no_bearer_token()
        {
            ArrangeLogin();

            _api.PendingLoginIdentifier = "ayla@test";
            _api.PendingPassword = "a-password";
            _api.Authenticate(NewLogin());

            Assert.That(_transport.LastBearerToken, Is.Null,
                "there is no session to present yet");
        }

        [Test]
        public void Later_calls_present_the_bearer_token()
        {
            ArrangeLogin(token: "tok-xyz");

            _api.PendingLoginIdentifier = "ayla@test";
            _api.PendingPassword = "a-password";
            _api.Authenticate(NewLogin());

            _transport.EnqueueOk("GET", "/api/servers", "{\"servers\":[]}");
            _api.GetServers(new AccountId("acc-a"));

            Assert.That(_transport.LastBearerToken, Is.EqualTo("tok-xyz"));
        }

        [Test]
        public void Clearing_the_session_forgets_the_token()
        {
            ArrangeLogin(token: "tok-xyz");

            _api.PendingLoginIdentifier = "ayla@test";
            _api.PendingPassword = "a-password";
            _api.Authenticate(NewLogin());

            _api.ClearSession();

            Assert.That(_api.SessionToken, Is.Null);
            Assert.That(_api.Session.IsValid, Is.False);
        }

        // ---- failure mapping ---------------------------------------------------------

        [Test]
        public void A_transport_failure_is_not_a_domain_answer()
        {
            _transport.Enqueue("POST", "/api/auth/login",
                HttpExchange.Unreachable("connection refused"));

            ApiResult<AuthenticatedAccount> result = _api.Authenticate(NewLogin());

            Assert.That(result.IsOk, Is.False);
            Assert.That(result.Error.Kind, Is.EqualTo(ApiErrorKind.Unreachable));
            Assert.That(result.Error.IsTransient, Is.True, "worth retrying");
        }

        [Test]
        public void Http_statuses_map_onto_transport_neutral_kinds()
        {
            var expected = new Dictionary<int, ApiErrorKind>
            {
                { 400, ApiErrorKind.BadRequest },
                { 401, ApiErrorKind.Unauthorized },
                { 403, ApiErrorKind.Unauthorized },
                { 404, ApiErrorKind.BadRequest },
                { 409, ApiErrorKind.BadRequest },
                { 429, ApiErrorKind.RateLimited },
                { 500, ApiErrorKind.ServerError },
                { 503, ApiErrorKind.ServerError },
            };

            foreach (KeyValuePair<int, ApiErrorKind> pair in expected)
            {
                var transport = new ScriptedHttpTransport();
                transport.Enqueue("POST", "/api/auth/login",
                    HttpExchange.Responded(pair.Key, "{\"code\":\"x\"}"));

                var api = new HttpAccountApi(transport);

                Assert.That(api.Authenticate(NewLogin()).Error.Kind, Is.EqualTo(pair.Value),
                    "status " + pair.Key);
            }
        }

        [Test]
        public void A_server_error_is_transient_and_a_client_error_is_not()
        {
            Assert.That(new ApiError(ApiErrorKind.ServerError).IsTransient, Is.True);
            Assert.That(new ApiError(ApiErrorKind.Unauthorized).IsTransient, Is.False);
            Assert.That(new ApiError(ApiErrorKind.BadRequest).IsTransient, Is.False);
        }

        // ---- reading lists -----------------------------------------------------------

        [Test]
        public void Servers_are_read_from_the_response()
        {
            _transport.EnqueueOk("GET", "/api/servers",
                "{\"servers\":[{\"server_id\":\"srv-1\",\"name_key\":\"srv.one\","
                + "\"region\":\"eu\",\"status\":1,\"enabled\":true,\"capacity\":1000,"
                + "\"population\":120,\"population_known\":true,\"revision\":3}]}");

            ApiResult<IReadOnlyList<ServerInfo>> result = _api.GetServers(new AccountId("acc-a"));

            Assert.That(result.IsOk, Is.True);
            Assert.That(result.Value.Count, Is.EqualTo(1));

            ServerInfo server = result.Value[0];

            Assert.That(server.Server, Is.EqualTo(new ServerId("srv-1")));
            Assert.That(server.Region, Is.EqualTo("eu"));
            Assert.That(server.Status, Is.EqualTo(ServerStatus.Online));
            Assert.That(server.Population.IsKnown, Is.True);
            Assert.That(server.Population.Value, Is.EqualTo(120));
            Assert.That(server.Revision, Is.EqualTo(new Revision(3)));
        }

        [Test]
        public void An_unknown_population_stays_unknown_rather_than_becoming_zero()
        {
            _transport.EnqueueOk("GET", "/api/servers",
                "{\"servers\":[{\"server_id\":\"srv-1\",\"population\":null,"
                + "\"population_known\":false,\"capacity\":500,\"status\":1,\"enabled\":true}]}");

            PopulationReading population = _api.GetServers(default).Value[0].Population;

            Assert.That(population.IsKnown, Is.False);
            Assert.That(population.IsFull, Is.False, "absent information is not a barrier");
            Assert.That(population.Capacity, Is.EqualTo(500));
        }

        [Test]
        public void Channels_carry_the_pk_flag_the_server_reported()
        {
            _transport.EnqueueOk("GET", "/api/channels?server_id=srv-1",
                "{\"channels\":[{\"channel_id\":\"ch-1\",\"server_id\":\"srv-1\","
                + "\"name_key\":\"ch.one\",\"status\":1,\"enabled\":true,\"pk_enabled\":false,"
                + "\"population_known\":false,\"capacity\":200,\"revision\":0},"
                + "{\"channel_id\":\"ch-2\",\"server_id\":\"srv-1\",\"name_key\":\"ch.two\","
                + "\"status\":1,\"enabled\":true,\"pk_enabled\":true,"
                + "\"population_known\":false,\"capacity\":200,\"revision\":0}]}");

            IReadOnlyList<ChannelInfo> channels =
                _api.GetChannels(default, new ServerId("srv-1")).Value;

            Assert.That(channels.Count, Is.EqualTo(2));
            Assert.That(channels[0].PkEnabled, Is.False);
            Assert.That(channels[1].PkEnabled, Is.True,
                "two channels of one server differ, so nothing is deriving it");
        }

        [Test]
        public void Characters_are_read_as_summaries()
        {
            _transport.EnqueueOk("GET", "/api/characters?server_id=srv-1",
                "{\"characters\":[{\"character_id\":\"char-1\",\"name\":\"Ayla\",\"gender\":2,"
                + "\"level\":25,\"class_id\":\"class.novice\",\"job_id\":\"\","
                + "\"map_id\":\"map.town\",\"appearance_id\":\"\",\"availability\":1,"
                + "\"revision\":7}]}");

            CharacterSelectEntry entry =
                _api.GetCharacters(default, new ServerId("srv-1")).Value[0];

            Assert.That(entry.Character, Is.EqualTo(new CharacterId("char-1")));
            Assert.That(entry.Name, Is.EqualTo("Ayla"));
            Assert.That(entry.Gender, Is.EqualTo(CharacterGender.Female));
            Assert.That(entry.Level, Is.EqualTo(25));
            Assert.That(entry.Map, Is.EqualTo(new DefinitionId("map.town")));
            Assert.That(entry.IsPlayable, Is.True);
            Assert.That(entry.Revision, Is.EqualTo(new Revision(7)));
        }

        [Test]
        public void The_server_id_is_escaped_into_the_query()
        {
            _transport.EnqueueOk("GET", "/api/channels?server_id=a%20b%2Fc", "{\"channels\":[]}");

            _api.GetChannels(default, new ServerId("a b/c"));

            Assert.That(_transport.Calls[0], Is.EqualTo("GET /api/channels?server_id=a%20b%2Fc"));
        }

        // ---- json robustness ----------------------------------------------------------

        [Test]
        public void A_malformed_response_yields_empty_values_rather_than_throwing()
        {
            _transport.EnqueueOk("GET", "/api/servers", "not json at all {{{");

            ApiResult<IReadOnlyList<ServerInfo>> result = _api.GetServers(default);

            Assert.That(result.IsOk, Is.True);
            Assert.That(result.Value.Count, Is.EqualTo(0),
                "nothing parsed, and nothing escaped from a network callback");
        }

        [Test]
        public void A_truncated_response_does_not_throw()
        {
            _transport.EnqueueOk("GET", "/api/characters?server_id=srv-1",
                "{\"characters\":[{\"character_id\":\"char-1\",\"name\":");

            Assert.That(() => _api.GetCharacters(default, new ServerId("srv-1")),
                Throws.Nothing);
        }

        [Test]
        public void A_value_containing_json_punctuation_survives_the_round_trip()
        {
            string awkward = "Ayla \"the\\Brave\", {rank: 1}";

            string body = new JsonWriter().Add("name", awkward).ToJson();

            Assert.That(JsonReader.Parse(body).String("name"), Is.EqualTo(awkward));
        }

        [Test]
        public void A_key_inside_a_nested_object_is_not_mistaken_for_a_top_level_one()
        {
            var reader = JsonReader.Parse(
                "{\"outer\":\"right\",\"nested\":{\"outer\":\"wrong\"}}");

            Assert.That(reader.String("outer"), Is.EqualTo("right"));
        }

        [Test]
        public void A_key_like_string_inside_a_value_is_not_matched()
        {
            var reader = JsonReader.Parse("{\"name\":\"\\\"level\\\": 99\",\"level\":1}");

            Assert.That(reader.Int("level"), Is.EqualTo(1),
                "the level inside the name must not win");
        }

        [Test]
        public void A_comma_inside_a_name_does_not_split_an_array_element()
        {
            var reader = JsonReader.Parse(
                "{\"characters\":[{\"name\":\"Ayla, the Brave\",\"level\":3}]}");

            IReadOnlyList<JsonReader> rows = reader.Array("characters");

            Assert.That(rows.Count, Is.EqualTo(1));
            Assert.That(rows[0].String("name"), Is.EqualTo("Ayla, the Brave"));
            Assert.That(rows[0].Int("level"), Is.EqualTo(3));
        }

        [Test]
        public void Missing_keys_read_as_defaults()
        {
            var reader = JsonReader.Parse("{\"present\":1}");

            Assert.That(reader.String("absent"), Is.Empty);
            Assert.That(reader.Int("absent"), Is.EqualTo(0));
            Assert.That(reader.Bool("absent"), Is.False);
            Assert.That(reader.Array("absent").Count, Is.EqualTo(0));
        }

        [Test]
        public void An_explicit_null_is_distinguishable_from_a_missing_key()
        {
            var reader = JsonReader.Parse("{\"population\":null}");

            Assert.That(reader.IsNull("population"), Is.True);
            Assert.That(reader.IsNull("absent"), Is.False);
        }

        [Test]
        public void Negative_numbers_are_read()
        {
            Assert.That(JsonReader.Parse("{\"delta\":-250}").Int("delta"), Is.EqualTo(-250));
        }

        // ---- architecture --------------------------------------------------------------

        [Test]
        public void The_adapter_holds_no_credential_field()
        {
            System.Reflection.FieldInfo[] fields = typeof(HttpAccountApi).GetFields(
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public);

            foreach (System.Reflection.FieldInfo field in fields)
            {
                string name = field.Name.ToLowerInvariant();

                Assert.That(name.Contains("password") && !name.Contains("pending"), Is.False,
                    field.Name + " would outlive the request that needs it");
                Assert.That(name.Contains("secret"), Is.False, field.Name);
                Assert.That(name.Contains("hash"), Is.False, field.Name);
            }
        }

        [Test]
        public void No_url_or_host_is_written_into_the_adapter()
        {
            foreach (string code in CodeLines(
                "Assets/_Game/Scripts/Backend/HttpAccountApi.cs"))
            {
                Assert.That(code, Does.Not.Contain("http://"),
                    "the base address belongs to the transport");
                Assert.That(code, Does.Not.Contain("https://"));
                Assert.That(code, Does.Not.Contain("localhost"));
                Assert.That(code, Does.Not.Contain("127.0.0.1"));
            }
        }

        [Test]
        public void No_transport_type_leaks_above_the_backend_assembly()
        {
            string[] directories =
            {
                "Assets/_Game/Scripts/Gameplay",
                "Assets/_Game/Scripts/UI",
                "Assets/_Game/Scripts/Contracts",
                "Assets/_Game/Scripts/Data",
            };

            foreach (string directory in directories)
            {
                foreach (string file in System.IO.Directory.GetFiles(directory, "*.cs",
                    System.IO.SearchOption.AllDirectories))
                {
                    foreach (string code in CodeLines(file))
                    {
                        Assert.That(code, Does.Not.Contain("UnityWebRequest"), file);
                        Assert.That(code, Does.Not.Contain("IHttpTransport"), file);
                        Assert.That(code, Does.Not.Contain("HttpAccountApi"), file);
                        Assert.That(code, Does.Not.Contain("System.Net"), file);
                    }
                }
            }
        }

        [Test]
        public void No_sql_or_database_vocabulary_appears_anywhere_in_unity()
        {
            string[] directories =
            {
                "Assets/_Game/Scripts/Gameplay",
                "Assets/_Game/Scripts/UI",
                "Assets/_Game/Scripts/Backend",
                "Assets/_Game/Scripts/Contracts",
            };

            foreach (string directory in directories)
            {
                foreach (string file in System.IO.Directory.GetFiles(directory, "*.cs",
                    System.IO.SearchOption.AllDirectories))
                {
                    foreach (string code in CodeLines(file))
                    {
                        Assert.That(code, Does.Not.Contain("SELECT "), file);
                        Assert.That(code, Does.Not.Contain("INSERT INTO"), file);
                        Assert.That(code, Does.Not.Contain("MySql"), file);
                        Assert.That(code, Does.Not.Contain("PDO"), file);
                        Assert.That(code, Does.Not.Contain("connectionString"), file);
                        Assert.That(code, Does.Not.Contain("DB_PASSWORD"), file);
                    }
                }
            }
        }

        [Test]
        public void There_is_one_account_api_implementation_in_the_project()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts", "*.cs",
                System.IO.SearchOption.AllDirectories);

            int implementations = 0;

            foreach (string file in files)
            {
                string source = System.IO.File.ReadAllText(file);

                if (source.Contains(": IAccountApi")) implementations++;

                Assert.That(source, Does.Not.Contain("class LoginApi2"), file);
                Assert.That(source, Does.Not.Contain("class SessionApi2"), file);
                Assert.That(source, Does.Not.Contain("class AuthenticationApi2"), file);
            }

            Assert.That(implementations, Is.EqualTo(1),
                "one HTTP implementation; the test double lives in the test assembly");
        }

        private static IEnumerable<string> CodeLines(string file)
        {
            string[] lines = System.IO.File.ReadAllLines(file);

            for (int i = 0; i < lines.Length; i++)
            {
                string code = lines[i].TrimStart();
                if (code.StartsWith("//") || code.StartsWith("*")) continue;

                yield return code;
            }
        }
    }
}
