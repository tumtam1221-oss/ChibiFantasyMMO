using ChibiFantasy.Backend;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The half of the authority seam that speaks HTTP: what it sends, what it believes,
    /// and what it refuses to believe.
    /// </summary>
    /// <remarks>
    /// The property that matters most is negative and easy to lose: <b>no value on an
    /// admission ever came from the claim</b>. A version of this class that echoed the
    /// client's account id back would pass every happy-path test and be completely broken,
    /// so several tests below deliberately send claims that disagree, and one sends a claim
    /// full of lies and checks that the admission describes somebody else entirely.
    /// </remarks>
    [TestFixture]
    internal sealed class HttpWorldSessionAuthorityTests
    {
        private ScriptedHttpTransport _transport;
        private HttpWorldSessionAuthority _authority;

        [SetUp]
        public void SetUp()
        {
            _transport = new ScriptedHttpTransport();
            _authority = new HttpWorldSessionAuthority(_transport);
        }

        /// <summary>What the API returns for a session that has been authorised.</summary>
        private const string AuthorisedSession =
            "{\"session_id\":\"s1\",\"account_id\":\"a1\",\"state\":5,\"revision\":4,"
            + "\"server_id\":\"srv-1\",\"channel_id\":\"ch-1\",\"character_id\":\"c1\","
            + "\"map_id\":\"map.town\",\"level\":12,\"character_revision\":7}";

        private void ArrangeSession(string json = AuthorisedSession)
        {
            _transport.EnqueueOk("GET", "/api/session", json);
        }

        private static WorldJoinClaim Claim(string token = "tok-1", string account = null,
            string character = null, string server = null, string channel = null)
        {
            return new WorldJoinClaim(
                new SessionToken(token),
                default,
                account == null ? default : new AccountId(account),
                character == null ? default : new CharacterId(character),
                server == null ? default : new ServerId(server),
                channel == null ? default : new ChannelId(channel));
        }

        // ---- resolving ------------------------------------------------------------------

        [Test]
        public void AdmittingResolvesTheTokenAgainstTheAuthority()
        {
            ArrangeSession();

            WorldAdmission admission = _authority.Admit(Claim());

            Assert.That(admission.IsAdmitted, Is.True);
            Assert.That(_transport.Calls, Does.Contain("GET /api/session"));
            Assert.That(_transport.LastBearerToken, Is.EqualTo("tok-1"),
                "the token is presented as a bearer, not put in a body");
        }

        [Test]
        public void EveryIdentityOnTheAdmissionComesFromTheAuthorityAndNotTheClaim()
        {
            ArrangeSession();

            // A claim full of lies. Every one of them disagrees with the session, so this
            // must be refused -- but the point is that no lie could ever have become a
            // value, because the admission is built from the response alone.
            WorldAdmission lying = _authority.Admit(Claim(account: "a-other"));

            Assert.That(lying.IsAdmitted, Is.False);

            SetUp();
            ArrangeSession();

            WorldAdmission honest = _authority.Admit(Claim());

            Assert.That(honest.Account.Value, Is.EqualTo("a1"));
            Assert.That(honest.Character.Value, Is.EqualTo("c1"));
            Assert.That(honest.Server.Value, Is.EqualTo("srv-1"));
            Assert.That(honest.Channel.Value, Is.EqualTo("ch-1"));
            Assert.That(honest.Map.Value, Is.EqualTo("map.town"));
            Assert.That(honest.CharacterRevision.Value, Is.EqualTo(7));
        }

        [Test]
        public void AClaimThatStatesNothingIsAdmittedOnItsTokenAlone()
        {
            ArrangeSession();

            // Delete every claim and the outcome is identical, which is the proof that
            // claims are compared rather than read.
            WorldAdmission admission = _authority.Admit(Claim());

            Assert.That(admission.IsAdmitted, Is.True);
            Assert.That(admission.Account.Value, Is.EqualTo("a1"));
        }

        // ---- the five spoofs -------------------------------------------------------------

        [Test]
        public void AccountSpoofIsRefused()
        {
            ArrangeSession();

            Assert.That(_authority.Admit(Claim(account: "someone-else")).Reason,
                Is.EqualTo(SessionRejection.SessionInvalid));
        }

        [Test]
        public void CharacterSpoofIsRefused()
        {
            ArrangeSession();

            Assert.That(_authority.Admit(Claim(character: "c-theirs")).Reason,
                Is.EqualTo(SessionRejection.CharacterNotOwned));
        }

        [Test]
        public void ServerSpoofIsRefused()
        {
            ArrangeSession();

            Assert.That(_authority.Admit(Claim(server: "srv-9")).Reason,
                Is.EqualTo(SessionRejection.UnknownServer));
        }

        [Test]
        public void ChannelSpoofIsRefused()
        {
            ArrangeSession();

            Assert.That(_authority.Admit(Claim(channel: "ch-9")).Reason,
                Is.EqualTo(SessionRejection.ChannelServerMismatch));
        }

        // ---- what the authority refuses ----------------------------------------------------

        [TestCase("session_expired", SessionRejection.SessionExpired)]
        [TestCase("session_revoked", SessionRejection.SessionRevoked)]
        [TestCase("session_invalid", SessionRejection.SessionInvalid)]
        [TestCase("missing_token", SessionRejection.MissingContext)]
        [TestCase("account_banned", SessionRejection.AccountUnavailable)]
        [TestCase("account_disabled", SessionRejection.AccountUnavailable)]
        [TestCase("rate_limited", SessionRejection.RateLimited)]
        public void TheApisOwnCodeDecidesTheReason(string code, SessionRejection expected)
        {
            _transport.Enqueue("GET", "/api/session",
                HttpExchange.Responded(401, "{\"code\":\"" + code + "\"}"));

            Assert.That(_authority.Admit(Claim()).Reason, Is.EqualTo(expected),
                "the status says the category; the code says which refusal it was");
        }

        [Test]
        public void AnUnreadableErrorBodyFallsBackToTheStatus()
        {
            _transport.Enqueue("GET", "/api/session",
                HttpExchange.Responded(403, "<html>gateway</html>"));

            Assert.That(_authority.Admit(Claim()).Reason,
                Is.EqualTo(SessionRejection.SessionInvalid));
        }

        [Test]
        public void AnUnreachableAuthorityRefusesRatherThanAdmits()
        {
            _transport.Enqueue("GET", "/api/session",
                HttpExchange.Unreachable("connection refused"));

            WorldAdmission admission = _authority.Admit(Claim());

            Assert.That(admission.IsAdmitted, Is.False,
                "a server that admitted on an unanswered question would let anybody in "
                + "whenever the account service was down");
        }

        [Test]
        public void AClaimWithNoTokenNeverReachesTheNetwork()
        {
            WorldAdmission admission = _authority.Admit(Claim(token: string.Empty));

            Assert.That(admission.Reason, Is.EqualTo(SessionRejection.MissingContext));
            Assert.That(_transport.Calls, Is.Empty);
        }

        [Test]
        public void AMalformedSuccessBodyIsRefusedRatherThanAdmittedAsNobody()
        {
            _transport.EnqueueOk("GET", "/api/session", "not json at all");

            Assert.That(_authority.Admit(Claim()).Reason,
                Is.EqualTo(SessionRejection.SessionInvalid));
        }

        // ---- the flow has to have been followed ----------------------------------------------

        [TestCase(1)] // Authenticated
        [TestCase(2)] // ServerSelected
        [TestCase(3)] // ChannelSelected
        [TestCase(4)] // CharacterSelected
        public void ASessionThatSkippedTheSelectionFlowIsRefused(int state)
        {
            _transport.EnqueueOk("GET", "/api/session",
                "{\"session_id\":\"s1\",\"account_id\":\"a1\",\"character_id\":\"c1\","
                + "\"server_id\":\"srv-1\",\"channel_id\":\"ch-1\",\"state\":" + state + "}");

            Assert.That(_authority.Admit(Claim()).Reason,
                Is.EqualTo(SessionRejection.InvalidTransition),
                "only a session the authority already authorised may reach the world");
        }

        [Test]
        public void AnAlreadyActiveSessionIsAdmittedSoAReconnectWorks()
        {
            _transport.EnqueueOk("GET", "/api/session",
                "{\"session_id\":\"s1\",\"account_id\":\"a1\",\"character_id\":\"c1\","
                + "\"server_id\":\"srv-1\",\"channel_id\":\"ch-1\",\"state\":6}");

            Assert.That(_authority.Admit(Claim()).IsAdmitted, Is.True);
        }

        [Test]
        public void ASessionWithNoCharacterSelectedIsRefused()
        {
            _transport.EnqueueOk("GET", "/api/session",
                "{\"session_id\":\"s1\",\"account_id\":\"a1\",\"character_id\":\"\","
                + "\"state\":5}");

            Assert.That(_authority.Admit(Claim()).Reason,
                Is.EqualTo(SessionRejection.UnknownCharacter));
        }

        // ---- arriving and leaving -------------------------------------------------------------

        [Test]
        public void ConfirmingArrivalPresentsTheTokenTheAdmissionResolved()
        {
            ArrangeSession();
            _transport.EnqueueOk("POST", "/api/session/world-ready", "{\"state\":6}");

            WorldAdmission admission = _authority.Admit(Claim());

            Assert.That(_authority.ConfirmArrival(admission.Session), Is.True);
            Assert.That(_transport.LastBearerToken, Is.EqualTo("tok-1"),
                "the world server carries session ids; the token that proves them stays here");
        }

        [Test]
        public void ConfirmingArrivalForASessionThisAuthorityNeverAdmittedDoesNothing()
        {
            Assert.That(_authority.ConfirmArrival(new SessionId("s-unknown")), Is.False);
            Assert.That(_transport.Calls, Is.Empty,
                "no token means no right to act, so there is nothing to send");
        }

        [Test]
        public void ReleasingPresentsTheTokenAndThenForgetsIt()
        {
            ArrangeSession();
            _transport.EnqueueOk("POST", "/api/session/release", "{\"session_ended\":true}");

            WorldAdmission admission = _authority.Admit(Claim());

            Assert.That(_authority.Release(admission.Session), Is.True);

            // The token is gone. Holding one for a finished session is only a way to leak it.
            Assert.That(_authority.Release(admission.Session), Is.False);
        }

        [Test]
        public void ReleasingAnUnknownSessionIsHarmless()
        {
            Assert.That(_authority.Release(new SessionId("s-unknown")), Is.False);
            Assert.That(_authority.Release(default), Is.False);
        }

        [Test]
        public void NothingSentEverPutsTheTokenInABody()
        {
            ArrangeSession();
            _transport.EnqueueOk("POST", "/api/session/world-ready", "{}");

            WorldAdmission admission = _authority.Admit(Claim(token: "SECRET-TOKEN"));
            _authority.ConfirmArrival(admission.Session);

            Assert.That(_transport.LastBody ?? string.Empty, Does.Not.Contain("SECRET-TOKEN"),
                "a body is logged far more often than a header");
        }
    }
}
