using System.Collections.Generic;
using ChibiFantasy.Backend;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// How a wire outcome becomes something the layers above can act on.
    /// </summary>
    /// <remarks>
    /// The seam's whole purpose is that "the server refused you" and "there was no server"
    /// arrive as different facts. These tests drive every outcome a transport can produce
    /// through <see cref="HttpAccountApi"/> and check which of the two it became -- because a
    /// mapping that collapsed them would show a banned player a connection error, and a
    /// player behind a dead router an accusation.
    /// </remarks>
    [TestFixture]
    internal sealed class TransportFailureMappingTests
    {
        private ScriptedHttpTransport _transport;
        private HttpAccountApi _api;

        [SetUp]
        public void SetUp()
        {
            _transport = new ScriptedHttpTransport();
            _api = new HttpAccountApi(_transport);
        }

        private ApiResult<IReadOnlyList<ServerInfo>> SendServers(HttpExchange exchange)
        {
            _transport.Enqueue("GET", "/api/servers", exchange);

            return _api.GetServers(new AccountId("acc-a"));
        }

        // ---- no reply ------------------------------------------------------------------

        [Test]
        public void AnUnreachableHostIsUnreachable()
        {
            ApiResult<IReadOnlyList<ServerInfo>> result =
                SendServers(HttpExchange.Unreachable("connection refused"));

            Assert.That(result.Error.Kind, Is.EqualTo(ApiErrorKind.Unreachable));
            Assert.That(result.Error.IsTransient, Is.True);
        }

        [Test]
        public void ATimeoutIsATimeoutAndNotAnUnreachableHost()
        {
            ApiResult<IReadOnlyList<ServerInfo>> result =
                SendServers(HttpExchange.TimedOut("no reply within 10s"));

            Assert.That(result.Error.Kind, Is.EqualTo(ApiErrorKind.Timeout));
            Assert.That(result.Error.IsTransient, Is.True, "waiting longer might work");
        }

        [Test]
        public void ACancellationIsNeitherAFailureOfTheServerNorWorthRetrying()
        {
            ApiResult<IReadOnlyList<ServerInfo>> result =
                SendServers(HttpExchange.Cancelled("player pressed back"));

            Assert.That(result.Error.Kind, Is.EqualTo(ApiErrorKind.Cancelled));
            Assert.That(result.Error.IsTransient, Is.False,
                "nobody should auto-retry something the caller abandoned");
        }

        // ---- a reply, with a status ------------------------------------------------------

        [TestCase(400, ApiErrorKind.BadRequest)]
        [TestCase(401, ApiErrorKind.Unauthorized)]
        [TestCase(403, ApiErrorKind.Unauthorized)]
        [TestCase(404, ApiErrorKind.BadRequest)]
        [TestCase(409, ApiErrorKind.BadRequest)]
        [TestCase(429, ApiErrorKind.RateLimited)]
        [TestCase(500, ApiErrorKind.ServerError)]
        [TestCase(503, ApiErrorKind.ServerError)]
        public void AStatusBecomesTheFailureKindThatDescribesIt(int status, ApiErrorKind expected)
        {
            ApiResult<IReadOnlyList<ServerInfo>> result =
                SendServers(HttpExchange.Responded(status, "{\"code\":\"whatever\"}"));

            Assert.That(result.IsOk, Is.False);
            Assert.That(result.Error.Kind, Is.EqualTo(expected));
        }

        [Test]
        public void OnlyServerSideStatusesAreWorthRetrying()
        {
            Assert.That(SendServers(HttpExchange.Responded(503, "{}")).Error.IsTransient, Is.True);

            SetUp();

            Assert.That(SendServers(HttpExchange.Responded(401, "{}")).Error.IsTransient, Is.False);
        }

        // ---- a reply that cannot be read --------------------------------------------------

        [Test]
        public void AMalformedSuccessBodyYieldsNoDataRatherThanAnException()
        {
            // 200 with rubbish. The reader is total, so this must produce an empty list and
            // not throw out of the middle of a network callback.
            ApiResult<IReadOnlyList<ServerInfo>> result =
                SendServers(HttpExchange.Responded(200, "not json at all {{{"));

            Assert.That(result.IsOk, Is.True);
            Assert.That(result.Value, Is.Empty);
        }

        [Test]
        public void AnEmptyBodyOnASuccessYieldsNoDataRatherThanAnException()
        {
            ApiResult<IReadOnlyList<ServerInfo>> result =
                SendServers(HttpExchange.Responded(200, string.Empty));

            Assert.That(result.IsOk, Is.True);
            Assert.That(result.Value, Is.Empty);
        }

        [Test]
        public void AMalformedErrorBodyStillProducesTheCorrectFailureKind()
        {
            // The status is what classifies the failure. A body that cannot be read costs
            // the diagnostic code, not the classification.
            ApiResult<IReadOnlyList<ServerInfo>> result =
                SendServers(HttpExchange.Responded(401, "<html>gateway error</html>"));

            Assert.That(result.Error.Kind, Is.EqualTo(ApiErrorKind.Unauthorized));
        }

        // ---- a reply carrying a domain refusal -----------------------------------------

        [Test]
        public void ADomainRefusalIsAReplyAndNotATransportFailure()
        {
            // The authority answered, and its answer was "no". That is a successful exchange
            // carrying a refusal, which is the distinction the two layers exist to keep.
            var exchange = HttpExchange.Responded(403,
                "{\"code\":\"account_banned\",\"message_key\":\"error.account.banned\"}");

            Assert.That(exchange.Reached, Is.True);
            Assert.That(exchange.FailureKind, Is.EqualTo(TransportFailureKind.None));

            ApiResult<IReadOnlyList<ServerInfo>> result = SendServers(exchange);

            Assert.That(result.Error.Kind, Is.EqualTo(ApiErrorKind.Unauthorized));
            Assert.That(result.Error.IsTransient, Is.False, "retrying will not unban anybody");
        }

        // ---- request identity ------------------------------------------------------------

        [Test]
        public void TheRequestIdTheCallerSuppliedIsTheOneThatGoesOnTheWire()
        {
            var request = new RequestId("req-fixed-1234");

            _transport.EnqueueOk("POST", "/api/session/select-server", "{\"state\":2}");
            _api.SelectServer(request, new ServerId("srv-a"));

            Assert.That(_transport.LastBody, Does.Contain("req-fixed-1234"),
                "an invented request id would defeat idempotency: a retry would look new");
        }

        [Test]
        public void ARetryWithTheSameRequestIdSendsTheSameRequestId()
        {
            var request = new RequestId("req-fixed-1234");

            _transport.EnqueueOk("POST", "/api/session/select-server", "{\"state\":2}");
            _transport.EnqueueOk("POST", "/api/session/select-server",
                "{\"state\":2,\"replayed\":true}");

            _api.SelectServer(request, new ServerId("srv-a"));
            string first = _transport.LastBody;

            _api.SelectServer(request, new ServerId("srv-a"));

            Assert.That(_transport.LastBody, Is.EqualTo(first));
        }

        // ---- the bearer ------------------------------------------------------------------

        [Test]
        public void NoBearerIsSentOnTheOneCallThatHasNoSessionYet()
        {
            _transport.EnqueueOk("POST", "/api/auth/login",
                "{\"account_id\":\"a\",\"session_id\":\"s\",\"token\":\"t\"}");

            _api.PendingLoginIdentifier = "player";
            _api.PendingPassword = "hunter2";
            _api.Authenticate(new LoginRequest(RequestId.New(), default));

            Assert.That(_transport.LastBearerToken, Is.Null,
                "there is no session to present on the request that creates one");
        }

        [Test]
        public void TheBearerIsPresentedOnEveryCallThatFollowsALogin()
        {
            _transport.EnqueueOk("POST", "/api/auth/login",
                "{\"account_id\":\"a\",\"session_id\":\"s\",\"token\":\"tok-xyz\"}");
            _transport.EnqueueOk("GET", "/api/servers", "{\"servers\":[]}");

            _api.PendingLoginIdentifier = "player";
            _api.PendingPassword = "hunter2";
            _api.Authenticate(new LoginRequest(RequestId.New(), default));
            _api.GetServers(new AccountId("a"));

            Assert.That(_transport.LastBearerToken, Is.EqualTo("tok-xyz"));
        }

        [Test]
        public void ClearingTheSessionStopsTheBearerBeingSent()
        {
            _transport.EnqueueOk("POST", "/api/auth/login",
                "{\"account_id\":\"a\",\"session_id\":\"s\",\"token\":\"tok-xyz\"}");
            _transport.EnqueueOk("GET", "/api/servers", "{\"servers\":[]}");

            _api.PendingLoginIdentifier = "player";
            _api.PendingPassword = "hunter2";
            _api.Authenticate(new LoginRequest(RequestId.New(), default));
            _api.ClearSession();
            _api.GetServers(new AccountId("a"));

            Assert.That(_transport.LastBearerToken, Is.Null);
        }
    }
}
