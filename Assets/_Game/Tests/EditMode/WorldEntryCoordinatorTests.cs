using System.Collections.Generic;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Network;
using ChibiFantasy.Server;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The world door: every spoof, every race, and every way a connection can end.
    /// </summary>
    /// <remarks>
    /// <b>The authority is a fake, and the fake is the point.</b> What is under test is not
    /// whether PHP resolves a token correctly -- that is proven over a real socket in
    /// <see cref="LiveBackendIntegrationTests"/>. It is what the world server does with the
    /// answer, including all the answers a live server would only give under conditions that
    /// are miserable to arrange: an expired session, a banned account, a channel that filled
    /// between selection and entry, two connections racing for one character.
    ///
    /// A fake authority makes those ordinary. That is the division of labour: the real
    /// boundary is proven once, and the behaviour behind it is proven exhaustively.
    /// </remarks>
    [TestFixture]
    internal sealed class WorldEntryCoordinatorTests
    {
        /// <summary>An authority that answers whatever a test needs it to.</summary>
        private sealed class FakeAuthority : IWorldSessionAuthority
        {
            private readonly Dictionary<string, WorldAdmission> _byToken =
                new Dictionary<string, WorldAdmission>();

            public SessionRejection RefuseWith = SessionRejection.SessionInvalid;

            public readonly List<string> Arrived = new List<string>();
            public readonly List<string> Released = new List<string>();

            public int ReleaseCalls => Released.Count;

            /// <summary>Records what this token resolves to, as the database would.</summary>
            public FakeAuthority Holds(string token, WorldAdmission admission)
            {
                _byToken[token] = admission;

                return this;
            }

            public WorldAdmission Admit(WorldJoinClaim claim)
            {
                if (!claim.HasToken) return WorldAdmission.Refused(SessionRejection.MissingContext);

                if (!_byToken.TryGetValue(claim.Token.Value, out WorldAdmission admission))
                {
                    return WorldAdmission.Refused(RefuseWith);
                }

                if (!admission.IsAdmitted) return admission;

                // The real implementation compares claims against the resolved session.
                // Mirrored here so the coordinator is exercised against the same behaviour.
                if (claim.ClaimedAccount.IsValid && claim.ClaimedAccount != admission.Account)
                {
                    return WorldAdmission.Refused(SessionRejection.SessionInvalid);
                }

                if (claim.ClaimedCharacter.IsValid && claim.ClaimedCharacter != admission.Character)
                {
                    return WorldAdmission.Refused(SessionRejection.CharacterNotOwned);
                }

                if (claim.ClaimedServer.IsValid && claim.ClaimedServer != admission.Server)
                {
                    return WorldAdmission.Refused(SessionRejection.UnknownServer);
                }

                if (claim.ClaimedChannel.IsValid && claim.ClaimedChannel != admission.Channel)
                {
                    return WorldAdmission.Refused(SessionRejection.ChannelServerMismatch);
                }

                return admission;
            }

            public bool ConfirmArrival(SessionId session)
            {
                Arrived.Add(session.Value);

                return true;
            }

            public bool Release(SessionId session)
            {
                Released.Add(session.Value);

                return true;
            }
        }

        private FakeAuthority _authority;
        private WorldConnectionRegistry _registry;
        private WorldEntryCoordinator _coordinator;

        /// <summary>1.0.0 everywhere, and the protocol is exact.</summary>
        private static readonly VersionRequirement Required = new VersionRequirement(
            new VersionNumber(1, 0, 0), new VersionNumber(1, 0, 0), new VersionNumber(1, 0, 0));

        private static VersionSet Current => new VersionSet(
            new VersionNumber(1, 0, 0), new VersionNumber(1, 0, 0), new VersionNumber(1, 0, 0));

        /// <summary>What an unset or unreadable version set looks like: 0.0.0 throughout.</summary>
        /// <remarks>Spelled out rather than passed as <c>default</c>, because the parameter
        /// is a nullable and <c>default</c> there means "use the normal one" -- which is how
        /// the first version of this test passed while proving nothing.</remarks>
        private static VersionSet Unset => new VersionSet(default, default, default);

        [SetUp]
        public void SetUp()
        {
            _authority = new FakeAuthority();
            _registry = new WorldConnectionRegistry();
            _coordinator = new WorldEntryCoordinator(_authority, _registry, Required);
        }

        private static WorldAdmission Admitted(string session, string account, string character,
            string server = "srv-1", string channel = "ch-1", string map = "map.town")
        {
            return WorldAdmission.Admitted(
                new SessionId(session), new AccountId(account), new CharacterId(character),
                new ServerId(server), new ChannelId(channel), new DefinitionId(map),
                new Revision(1), new Revision(1), SessionState.EnteringWorld);
        }

        private static WorldJoinClaim Claim(string token, string account = null,
            string character = null, string server = null, string channel = null,
            VersionSet? versions = null)
        {
            return new WorldJoinClaim(
                new SessionToken(token),
                versions ?? Current,
                account == null ? default : new AccountId(account),
                character == null ? default : new CharacterId(character),
                server == null ? default : new ServerId(server),
                channel == null ? default : new ChannelId(channel));
        }

        private void ArrangePlayer(string token = "tok-1", string session = "s1",
            string account = "a1", string character = "c1")
        {
            _authority.Holds(token, Admitted(session, account, character));
        }

        // ---- admitting ---------------------------------------------------------------------

        [Test]
        public void AValidTokenJoinsAndIsRecordedAsConnecting()
        {
            ArrangePlayer();

            WorldJoinOutcome outcome = _coordinator.Join(1, Claim("tok-1"));

            Assert.That(outcome.IsAccepted, Is.True);
            Assert.That(outcome.EntryState, Is.EqualTo(WorldEntryState.Connecting),
                "Phase 14 stopped at Authorised; this is the next state, not Ready");
            Assert.That(outcome.Admission.Account.Value, Is.EqualTo("a1"));
            Assert.That(_coordinator.PresenceOf(new CharacterId("c1")),
                Is.EqualTo(WorldPresence.Connecting));
        }

        [Test]
        public void TheIdentitiesComeFromTheAuthorityAndNotFromTheClient()
        {
            ArrangePlayer();

            // The client claims nothing at all. It is still admitted as exactly who its
            // token says, because no claim was ever a source of a value.
            WorldJoinOutcome outcome = _coordinator.Join(1, Claim("tok-1"));

            Assert.That(outcome.Admission.Account.Value, Is.EqualTo("a1"));
            Assert.That(outcome.Admission.Character.Value, Is.EqualTo("c1"));
            Assert.That(outcome.Admission.Server.Value, Is.EqualTo("srv-1"));
            Assert.That(outcome.Admission.Channel.Value, Is.EqualTo("ch-1"));
        }

        [Test]
        public void AConnectionWithNoTokenIsRefusedWithoutAskingTheAuthority()
        {
            WorldJoinOutcome outcome = _coordinator.Join(1, Claim(string.Empty));

            Assert.That(outcome.IsAccepted, Is.False);
            Assert.That(outcome.Reason, Is.EqualTo(SessionRejection.MissingContext));
            Assert.That(_registry.Count, Is.Zero, "a refused connection spawns nothing");
        }

        [Test]
        public void ACoordinatorWithNoAuthorityRefusesEverythingRatherThanGuessing()
        {
            var blind = new WorldEntryCoordinator(null, _registry, Required);

            Assert.That(blind.Join(1, Claim("tok-1")).IsAccepted, Is.False,
                "a server that cannot ask must not admit");
        }

        // ---- 16.16 A-E: the five spoofs ------------------------------------------------------

        [Test]
        public void AccountSpoof_ClaimingAnotherAccountIsRefused()
        {
            ArrangePlayer();

            WorldJoinOutcome outcome = _coordinator.Join(1, Claim("tok-1", account: "someone-else"));

            Assert.That(outcome.IsAccepted, Is.False);
            Assert.That(outcome.Reason, Is.EqualTo(SessionRejection.SessionInvalid));
            Assert.That(_registry.Count, Is.Zero);
        }

        [Test]
        public void CharacterSpoof_ClaimingAnotherAccountsCharacterIsRefused()
        {
            ArrangePlayer();

            WorldJoinOutcome outcome = _coordinator.Join(1, Claim("tok-1", character: "c-theirs"));

            Assert.That(outcome.IsAccepted, Is.False);
            Assert.That(outcome.Reason, Is.EqualTo(SessionRejection.CharacterNotOwned));
        }

        [Test]
        public void OwnerSpoof_TheOwnerIsProjectedFromTheAccountAndCannotBeSuppliedAtAll()
        {
            ArrangePlayer();

            WorldJoinOutcome outcome = _coordinator.Join(1, Claim("tok-1"));

            // There is no owner field on the claim or the message. Ownership is the
            // account's, projected the way Phase 08 defined -- so a foreign OwnerId is
            // unrepresentable rather than refused.
            Assert.That(outcome.Admission.Account.Value, Is.EqualTo("a1"));
            Assert.That(typeof(WorldJoinClaim).GetProperty("ClaimedOwner"), Is.Null,
                "there must be nowhere to put a forged owner");
        }

        [Test]
        public void ServerSpoof_ClaimingAnotherServerIsRefused()
        {
            ArrangePlayer();

            Assert.That(_coordinator.Join(1, Claim("tok-1", server: "srv-other")).Reason,
                Is.EqualTo(SessionRejection.UnknownServer));
        }

        [Test]
        public void ChannelSpoof_ClaimingAnotherChannelIsRefused()
        {
            ArrangePlayer();

            Assert.That(_coordinator.Join(1, Claim("tok-1", channel: "ch-other")).Reason,
                Is.EqualTo(SessionRejection.ChannelServerMismatch));
        }

        // ---- 16.16 F-K: what the authority refuses -------------------------------------------

        [TestCase(SessionRejection.SessionExpired)]
        [TestCase(SessionRejection.SessionRevoked)]
        [TestCase(SessionRejection.SessionInvalid)]
        [TestCase(SessionRejection.AccountUnavailable)]
        [TestCase(SessionRejection.ServerMaintenance)]
        [TestCase(SessionRejection.ServerFull)]
        [TestCase(SessionRejection.ChannelFull)]
        [TestCase(SessionRejection.ServerUnavailable)]
        [TestCase(SessionRejection.RateLimited)]
        public void EveryAuthorityRefusalReachesTheClientAsItsOwnReason(SessionRejection reason)
        {
            _authority.Holds("tok-1", WorldAdmission.Refused(reason));

            WorldJoinOutcome outcome = _coordinator.Join(1, Claim("tok-1"));

            Assert.That(outcome.IsAccepted, Is.False);
            Assert.That(outcome.Reason, Is.EqualTo(reason),
                "a player is owed the actual reason, not a generic refusal");
            Assert.That(_registry.Count, Is.Zero, "nothing is spawned for any of them");
        }

        [Test]
        public void AnUnknownTokenIsRefused()
        {
            Assert.That(_coordinator.Join(1, Claim("never-issued")).IsAccepted, Is.False);
        }

        [Test]
        public void AnAdmittedSessionWithNoCharacterIsRefused()
        {
            _authority.Holds("tok-1", WorldAdmission.Admitted(
                new SessionId("s1"), new AccountId("a1"), default,
                new ServerId("srv-1"), new ChannelId("ch-1"), new DefinitionId("map.town"),
                new Revision(1), new Revision(1), SessionState.EnteringWorld));

            Assert.That(_coordinator.Join(1, Claim("tok-1")).Reason,
                Is.EqualTo(SessionRejection.UnknownCharacter));
        }

        // ---- 16.16 H / 16.12: versions ---------------------------------------------------------

        [Test]
        public void WrongProtocol_IsRefusedBeforeTheAuthorityIsEvenAsked()
        {
            ArrangePlayer();

            var stale = new VersionSet(new VersionNumber(1, 0, 0), new VersionNumber(2, 0, 0),
                new VersionNumber(1, 0, 0));

            WorldJoinOutcome outcome = _coordinator.Join(1, Claim("tok-1", versions: stale));

            Assert.That(outcome.Reason, Is.EqualTo(SessionRejection.VersionMismatch),
                "a client that cannot be spoken to cannot be told anything useful about "
                + "its session, so the protocol is checked first");
        }

        [Test]
        public void AnOutdatedClientBelowTheFloorIsRefused()
        {
            ArrangePlayer();

            var old = new VersionSet(new VersionNumber(0, 9, 0), new VersionNumber(1, 0, 0),
                new VersionNumber(1, 0, 0));

            Assert.That(_coordinator.Join(1, Claim("tok-1", versions: old)).Reason,
                Is.EqualTo(SessionRejection.VersionMismatch));
        }

        [Test]
        public void AMissingVersionIsRefusedRatherThanGivenTheBenefitOfTheDoubt()
        {
            ArrangePlayer();

            Assert.That(_coordinator.Join(1, Claim("tok-1", versions: Unset)).Reason,
                Is.EqualTo(SessionRejection.VersionMismatch));
        }

        [Test]
        public void TheServerNeverInventsAClientVersion()
        {
            ArrangePlayer();

            // With no requirement configured, anything is playable -- but the version still
            // came from the client and was still evaluated. Nothing here substitutes one.
            var permissive = new WorldEntryCoordinator(_authority, new WorldConnectionRegistry());

            Assert.That(permissive.Join(1, Claim("tok-1", versions: Unset)).IsAccepted, Is.True);
        }

        // ---- 16.16 L, M / 16.17: duplicate and stale connections ---------------------------------

        [Test]
        public void DuplicateConnection_TheSameSessionReplacesItsOwnOlderSocket()
        {
            ArrangePlayer();

            _coordinator.Join(1, Claim("tok-1"));

            WorldJoinOutcome second = _coordinator.Join(2, Claim("tok-1"));

            Assert.That(second.IsAccepted, Is.True);
            Assert.That(second.Connection, Is.EqualTo(ConnectionOutcome.Replaced));
            Assert.That(second.DisplacedConnectionId, Is.EqualTo(1),
                "the caller is told exactly which socket to close");
        }

        [Test]
        public void StaleConnection_ADisplacedSocketCannotActOnTheCharacter()
        {
            ArrangePlayer();

            _coordinator.Join(1, Claim("tok-1"));
            _coordinator.Join(2, Claim("tok-1"));

            Assert.That(_coordinator.CanAct(1), Is.False);
            Assert.That(_coordinator.CanAct(2), Is.True);
        }

        [Test]
        public void StaleConnection_ADisplacedSocketCannotConfirmArrivalForTheLiveOne()
        {
            ArrangePlayer();

            _coordinator.Join(1, Claim("tok-1"));
            _coordinator.Join(2, Claim("tok-1"));

            Assert.That(_coordinator.ConfirmArrival(1), Is.False);
            Assert.That(_authority.Arrived, Is.Empty);
        }

        [Test]
        public void TwoSessionsRacingForOneCharacterProduceExactlyOneWinner()
        {
            _authority.Holds("tok-a", Admitted("s1", "a1", "shared"));
            _authority.Holds("tok-b", Admitted("s2", "a2", "shared"));

            WorldJoinOutcome first = _coordinator.Join(1, Claim("tok-a"));
            WorldJoinOutcome second = _coordinator.Join(2, Claim("tok-b"));

            Assert.That(first.IsAccepted, Is.True);
            Assert.That(second.IsAccepted, Is.False);
            Assert.That(second.Reason, Is.EqualTo(SessionRejection.AlreadyInWorld));
            Assert.That(_registry.Count, Is.EqualTo(1),
                "there is never more than one authoritative copy of a character");
        }

        [Test]
        public void OneAccountsTwoCharactersMayNotBothBeInTheWorld()
        {
            // Same account, two sessions, two different characters. The session collision
            // is what stops it, not the character one.
            _authority.Holds("tok-a", Admitted("s1", "a1", "c1"));
            _authority.Holds("tok-b", Admitted("s1", "a1", "c2"));

            _coordinator.Join(1, Claim("tok-a"));
            WorldJoinOutcome second = _coordinator.Join(2, Claim("tok-b"));

            Assert.That(second.Connection, Is.EqualTo(ConnectionOutcome.Replaced),
                "one session is one presence, whichever character it names");
            Assert.That(_registry.Count, Is.EqualTo(1));
        }

        // ---- arriving --------------------------------------------------------------------------

        [Test]
        public void ConfirmingArrivalMovesTheSessionToActiveAndTheCharacterToInWorld()
        {
            ArrangePlayer();
            _coordinator.Join(1, Claim("tok-1"));

            Assert.That(_coordinator.ConfirmArrival(1), Is.True);
            Assert.That(_authority.Arrived, Is.EqualTo(new[] { "s1" }),
                "the authority is told, so the session leaves EnteringWorld");
            Assert.That(_coordinator.PresenceOf(new CharacterId("c1")),
                Is.EqualTo(WorldPresence.InWorld));
        }

        [Test]
        public void ConfirmingArrivalTwiceTellsTheAuthorityOnlyOnce()
        {
            ArrangePlayer();
            _coordinator.Join(1, Claim("tok-1"));
            _coordinator.ConfirmArrival(1);

            Assert.That(_coordinator.ConfirmArrival(1), Is.False);
            Assert.That(_authority.Arrived.Count, Is.EqualTo(1));
        }

        [Test]
        public void AConnectionThatNeverJoinedCannotConfirmArrival()
        {
            Assert.That(_coordinator.ConfirmArrival(99), Is.False);
            Assert.That(_authority.Arrived, Is.Empty);
        }

        [Test]
        public void AConnectionThatDiesWhileLoadingNeverReadsAsPresent()
        {
            ArrangePlayer();
            _coordinator.Join(1, Claim("tok-1"));

            // Admitted, never arrived. The session stays in EnteringWorld, which is the
            // correct record of a handoff that did not complete.
            _coordinator.Leave(1);

            Assert.That(_authority.Arrived, Is.Empty);
            Assert.That(_coordinator.PresenceOf(new CharacterId("c1")),
                Is.EqualTo(WorldPresence.Offline));
        }

        // ---- 16.10: leaving ----------------------------------------------------------------------

        [Test]
        public void LeavingReleasesTheSessionSoTheAccountCanSignInAgain()
        {
            ArrangePlayer();
            _coordinator.Join(1, Claim("tok-1"));
            _coordinator.ConfirmArrival(1);

            Assert.That(_coordinator.Leave(1), Is.True);
            Assert.That(_authority.Released, Is.EqualTo(new[] { "s1" }));
        }

        [Test]
        public void LeavingTwiceReleasesOnlyOnce()
        {
            ArrangePlayer();
            _coordinator.Join(1, Claim("tok-1"));

            _coordinator.Leave(1);

            Assert.That(_coordinator.Leave(1), Is.False);
            Assert.That(_authority.ReleaseCalls, Is.EqualTo(1),
                "a callback, a timeout and a shutdown can all fire for one socket");
        }

        [Test]
        public void AStaleConnectionLeavingDoesNotReleaseTheSessionItsReplacementIsUsing()
        {
            ArrangePlayer();

            _coordinator.Join(1, Claim("tok-1"));
            _coordinator.Join(2, Claim("tok-1"));

            // The old socket's disconnect always arrives after the reconnection.
            _coordinator.Leave(1);

            Assert.That(_authority.ReleaseCalls, Is.Zero,
                "releasing here would disconnect the player who just reconnected");
            Assert.That(_coordinator.CanAct(2), Is.True);
        }

        [Test]
        public void ADisconnectRacingAWorldEntryDoesNotStrandTheCharacter()
        {
            ArrangePlayer();

            _coordinator.Join(1, Claim("tok-1"));

            // The socket dies between admission and arrival -- the worst moment, because
            // the authority has already marked the character InWorld.
            Assert.That(_coordinator.Leave(1), Is.True);
            Assert.That(_authority.Released, Is.EqualTo(new[] { "s1" }),
                "the character must be handed back or it is unplayable forever");

            // And arriving afterwards does nothing: there is no connection left to arrive.
            Assert.That(_coordinator.ConfirmArrival(1), Is.False);
        }

        [Test]
        public void LeavingAConnectionThatNeverJoinedReleasesNothing()
        {
            Assert.That(_coordinator.Leave(42), Is.False);
            Assert.That(_authority.ReleaseCalls, Is.Zero);
        }

        // ---- 16.11: reconnecting ------------------------------------------------------------------

        [Test]
        public void AValidSessionMayReconnectAfterACleanDisconnect()
        {
            ArrangePlayer();

            _coordinator.Join(1, Claim("tok-1"));
            _coordinator.Leave(1);

            WorldJoinOutcome again = _coordinator.Join(2, Claim("tok-1"));

            Assert.That(again.IsAccepted, Is.True);
            Assert.That(again.Connection, Is.EqualTo(ConnectionOutcome.Registered),
                "nothing was left to displace");
        }

        [Test]
        public void AnExpiredSessionMayNotReconnect()
        {
            ArrangePlayer();
            _coordinator.Join(1, Claim("tok-1"));
            _coordinator.Leave(1);

            // The session ended when it was released; the authority now refuses the token.
            _authority.Holds("tok-1", WorldAdmission.Refused(SessionRejection.SessionExpired));

            Assert.That(_coordinator.Join(2, Claim("tok-1")).Reason,
                Is.EqualTo(SessionRejection.SessionExpired));
        }

        [Test]
        public void ReconnectingRevalidatesOwnershipRatherThanTrustingTheEarlierAdmission()
        {
            ArrangePlayer();
            _coordinator.Join(1, Claim("tok-1"));
            _coordinator.Leave(1);

            // The character was deleted, or transferred, while they were away.
            _authority.Holds("tok-1", WorldAdmission.Refused(SessionRejection.CharacterNotOwned));

            Assert.That(_coordinator.Join(2, Claim("tok-1")).IsAccepted, Is.False,
                "an earlier admission proves nothing about now");
        }

        [Test]
        public void ReconnectingNeverProducesTwoWorldCharacters()
        {
            ArrangePlayer();

            for (int i = 1; i <= 20; i++)
            {
                _coordinator.Join(i, Claim("tok-1"));
                _coordinator.ConfirmArrival(i);
            }

            Assert.That(_registry.Count, Is.EqualTo(1));
            Assert.That(_coordinator.PresenceOf(new CharacterId("c1")),
                Is.EqualTo(WorldPresence.InWorld));
        }

        // ---- shutdown --------------------------------------------------------------------------

        [Test]
        public void StoppingTheServerReleasesEverySessionItWasHolding()
        {
            _authority.Holds("t1", Admitted("s1", "a1", "c1"));
            _authority.Holds("t2", Admitted("s2", "a2", "c2"));
            _authority.Holds("t3", Admitted("s3", "a3", "c3"));

            _coordinator.Join(1, Claim("t1"));
            _coordinator.Join(2, Claim("t2"));
            _coordinator.Join(3, Claim("t3"));

            Assert.That(_coordinator.ReleaseAll(), Is.EqualTo(3));
            Assert.That(_authority.Released, Is.EquivalentTo(new[] { "s1", "s2", "s3" }));
            Assert.That(_registry.Count, Is.Zero);
        }

        [Test]
        public void StoppingTwiceReleasesNothingTheSecondTime()
        {
            ArrangePlayer();
            _coordinator.Join(1, Claim("tok-1"));

            _coordinator.ReleaseAll();

            Assert.That(_coordinator.ReleaseAll(), Is.Zero);
            Assert.That(_authority.ReleaseCalls, Is.EqualTo(1));
        }

        // ---- 16.20: nothing accumulates -------------------------------------------------------------

        [Test]
        public void ManyConnectAndDisconnectCyclesLeakNothing()
        {
            for (int i = 0; i < 300; i++)
            {
                string token = "tok-" + i;
                _authority.Holds(token, Admitted("s" + i, "a" + i, "c" + i));

                _coordinator.Join(i, Claim(token));
                _coordinator.ConfirmArrival(i);
                _coordinator.Leave(i);
            }

            Assert.That(_registry.Count, Is.Zero);
            Assert.That(_registry.Stale, Is.Empty);
            Assert.That(_authority.ReleaseCalls, Is.EqualTo(300),
                "every cycle released exactly once");
        }
    }
}
