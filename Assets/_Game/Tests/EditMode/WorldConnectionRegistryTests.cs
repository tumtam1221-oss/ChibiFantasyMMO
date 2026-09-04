using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Network;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Who is connected, what that means for their character, and what happens when two
    /// connections want the same one.
    /// </summary>
    /// <remarks>
    /// The registry is pure, so every one of these rules is decided without a socket. That
    /// matters more here than anywhere else in the phase: "a stale connection cannot control
    /// a character" and "a disconnect observed twice does nothing" are exactly the
    /// properties that are impossible to test reliably against a real transport, because
    /// reproducing the race is the hard part.
    /// </remarks>
    [TestFixture]
    internal sealed class WorldConnectionRegistryTests
    {
        private WorldConnectionRegistry _registry;

        [SetUp]
        public void SetUp()
        {
            _registry = new WorldConnectionRegistry();
        }

        private static WorldAdmission Admission(string session, string account, string character,
            string server = "srv-1", string channel = "ch-1")
        {
            return WorldAdmission.Admitted(
                new SessionId(session),
                new AccountId(account),
                new CharacterId(character),
                new ServerId(server),
                new ChannelId(channel),
                new DefinitionId("map.town"),
                new Revision(1),
                new Revision(1),
                SessionState.EnteringWorld);
        }

        // ---- registering -----------------------------------------------------------------

        [Test]
        public void AnAdmittedConnectionIsRegisteredAsConnectingRatherThanPresent()
        {
            ConnectionOutcome outcome =
                _registry.Register(1, Admission("s1", "a1", "c1"), out int displaced);

            Assert.That(outcome, Is.EqualTo(ConnectionOutcome.Registered));
            Assert.That(displaced, Is.EqualTo(-1));

            // Admitted is not arrived. A connection that dies while loading must never have
            // read as present.
            Assert.That(_registry.PresenceOf(new CharacterId("c1")),
                Is.EqualTo(WorldPresence.Connecting));
        }

        [Test]
        public void ARefusedAdmissionRegistersNothing()
        {
            ConnectionOutcome outcome = _registry.Register(1,
                WorldAdmission.Refused(SessionRejection.SessionExpired), out _);

            Assert.That(outcome, Is.EqualTo(ConnectionOutcome.Refused));
            Assert.That(_registry.Count, Is.Zero);
            Assert.That(_registry.CanAct(1), Is.False);
        }

        [Test]
        public void ANegativeConnectionIdIsRefused()
        {
            Assert.That(_registry.Register(-1, Admission("s1", "a1", "c1"), out _),
                Is.EqualTo(ConnectionOutcome.Refused));
        }

        [Test]
        public void RegisteringTheSameConnectionAndSessionTwiceIsRecognisedAsARetry()
        {
            _registry.Register(1, Admission("s1", "a1", "c1"), out _);

            Assert.That(_registry.Register(1, Admission("s1", "a1", "c1"), out _),
                Is.EqualTo(ConnectionOutcome.AlreadyRegistered));
            Assert.That(_registry.Count, Is.EqualTo(1));
        }

        [Test]
        public void OneConnectionCannotBecomeASecondSession()
        {
            _registry.Register(1, Admission("s1", "a1", "c1"), out _);

            Assert.That(_registry.Register(1, Admission("s2", "a2", "c2"), out _),
                Is.EqualTo(ConnectionOutcome.Refused),
                "a socket that already has an identity cannot acquire another");
        }

        // ---- two connections, one character -------------------------------------------------

        [Test]
        public void ADifferentSessionCannotTakeACharacterSomebodyIsHolding()
        {
            _registry.Register(1, Admission("s1", "a1", "c1"), out _);

            ConnectionOutcome outcome =
                _registry.Register(2, Admission("s2", "a2", "c1"), out _);

            Assert.That(outcome, Is.EqualTo(ConnectionOutcome.Refused),
                "two authoritative copies of one character is the corruption to prevent");
            Assert.That(_registry.CanAct(1), Is.True, "the holder is untouched");
            Assert.That(_registry.CanAct(2), Is.False);
        }

        [Test]
        public void TheSameSessionReconnectingDisplacesItsOwnOlderConnection()
        {
            _registry.Register(1, Admission("s1", "a1", "c1"), out _);

            ConnectionOutcome outcome =
                _registry.Register(2, Admission("s1", "a1", "c1"), out int displaced);

            Assert.That(outcome, Is.EqualTo(ConnectionOutcome.Replaced));
            Assert.That(displaced, Is.EqualTo(1), "the caller is told which socket to close");

            // Refusing instead would lock a player out of their own character every time
            // their network dropped without a clean close, which is the common case.
            Assert.That(_registry.CanAct(2), Is.True);
            Assert.That(_registry.Count, Is.EqualTo(1));
        }

        [Test]
        public void ADisplacedConnectionIsRememberedAsStaleRatherThanForgotten()
        {
            _registry.Register(1, Admission("s1", "a1", "c1"), out _);
            _registry.Register(2, Admission("s1", "a1", "c1"), out _);

            Assert.That(_registry.IsStale(1), Is.True);
            Assert.That(_registry.CanAct(1), Is.False,
                "the last packet of a dead socket must not move a live character");
        }

        [Test]
        public void AStaleConnectionsSessionStillResolvesToItsReplacement()
        {
            _registry.Register(1, Admission("s1", "a1", "c1"), out _);
            _registry.Register(2, Admission("s1", "a1", "c1"), out _);

            Assert.That(_registry.TryGetBySession(new SessionId("s1"),
                out WorldConnectionRegistry.Entry entry), Is.True);
            Assert.That(entry.ConnectionId, Is.EqualTo(2),
                "forgetting the old socket must not unregister the new one");
        }

        [Test]
        public void ACharacterHeldByAReplacedConnectionIsStillPresent()
        {
            _registry.Register(1, Admission("s1", "a1", "c1"), out _);
            _registry.MarkInWorld(1);
            _registry.Register(2, Admission("s1", "a1", "c1"), out _);

            // A reconnection resets presence to Connecting: the new socket has not arrived
            // yet, and claiming otherwise would report a player as playing before they are.
            Assert.That(_registry.PresenceOf(new CharacterId("c1")),
                Is.EqualTo(WorldPresence.Connecting));
        }

        // ---- presence -----------------------------------------------------------------------

        [Test]
        public void AnUnknownCharacterIsOffline()
        {
            Assert.That(_registry.PresenceOf(new CharacterId("nobody")),
                Is.EqualTo(WorldPresence.Offline));
        }

        [Test]
        public void AnInvalidCharacterIsOfflineRatherThanAnError()
        {
            Assert.That(_registry.PresenceOf(default), Is.EqualTo(WorldPresence.Offline));
        }

        [Test]
        public void ArrivingMovesPresenceFromConnectingToInWorld()
        {
            _registry.Register(1, Admission("s1", "a1", "c1"), out _);

            Assert.That(_registry.MarkInWorld(1), Is.True);
            Assert.That(_registry.PresenceOf(new CharacterId("c1")),
                Is.EqualTo(WorldPresence.InWorld));
        }

        [Test]
        public void ArrivingTwiceChangesNothingTheSecondTime()
        {
            _registry.Register(1, Admission("s1", "a1", "c1"), out _);
            _registry.MarkInWorld(1);

            Assert.That(_registry.MarkInWorld(1), Is.False);
            Assert.That(_registry.PresenceOf(new CharacterId("c1")),
                Is.EqualTo(WorldPresence.InWorld));
        }

        [Test]
        public void AnUnknownConnectionCannotArrive()
        {
            Assert.That(_registry.MarkInWorld(99), Is.False);
        }

        [Test]
        public void DisconnectingReturnsACharacterToOffline()
        {
            _registry.Register(1, Admission("s1", "a1", "c1"), out _);
            _registry.MarkInWorld(1);

            Assert.That(_registry.Unregister(1, out _), Is.True);
            Assert.That(_registry.PresenceOf(new CharacterId("c1")),
                Is.EqualTo(WorldPresence.Offline));
        }

        // ---- disconnecting -------------------------------------------------------------------

        [Test]
        public void UnregisteringReportsWhoLeft()
        {
            _registry.Register(7, Admission("s1", "a1", "c1", "srv-9", "ch-9"), out _);

            Assert.That(_registry.Unregister(7, out WorldConnectionRegistry.Entry entry), Is.True);
            Assert.That(entry.Session.Value, Is.EqualTo("s1"));
            Assert.That(entry.Account.Value, Is.EqualTo("a1"));
            Assert.That(entry.Server.Value, Is.EqualTo("srv-9"));
            Assert.That(entry.Channel.Value, Is.EqualTo("ch-9"));
        }

        [Test]
        public void UnregisteringTwiceDoesNothingTheSecondTime()
        {
            _registry.Register(1, Admission("s1", "a1", "c1"), out _);

            Assert.That(_registry.Unregister(1, out _), Is.True);
            Assert.That(_registry.Unregister(1, out _), Is.False,
                "a callback, a timeout and a shutdown can all fire for one socket");
            Assert.That(_registry.Count, Is.Zero);
        }

        [Test]
        public void UnregisteringAConnectionThatNeverExistedIsHarmless()
        {
            Assert.That(_registry.Unregister(42, out _), Is.False);
        }

        [Test]
        public void RemovingAStaleConnectionDoesNotDisturbItsReplacement()
        {
            _registry.Register(1, Admission("s1", "a1", "c1"), out _);
            _registry.Register(2, Admission("s1", "a1", "c1"), out _);

            // The old socket's disconnect arrives after the reconnection, as it always does.
            _registry.Unregister(1, out _);

            Assert.That(_registry.CanAct(2), Is.True,
                "the player who just reconnected must not be dropped by the old socket dying");
            Assert.That(_registry.PresenceOf(new CharacterId("c1")),
                Is.EqualTo(WorldPresence.Connecting));
        }

        [Test]
        public void AConnectionIdReusedByTheTransportIsNoLongerStale()
        {
            _registry.Register(1, Admission("s1", "a1", "c1"), out _);
            _registry.Register(2, Admission("s1", "a1", "c1"), out _);
            _registry.Unregister(1, out _);

            // Transports reuse client ids. A new player on id 1 must not inherit a stale
            // mark from somebody else's dead socket.
            _registry.Register(1, Admission("s9", "a9", "c9"), out _);

            Assert.That(_registry.IsStale(1), Is.False);
            Assert.That(_registry.CanAct(1), Is.True);
        }

        // ---- shutdown --------------------------------------------------------------------------

        [Test]
        public void ClearingReportsEveryConnectionSoNoneIsLeftStranded()
        {
            _registry.Register(1, Admission("s1", "a1", "c1"), out _);
            _registry.Register(2, Admission("s2", "a2", "c2"), out _);
            _registry.Register(3, Admission("s3", "a3", "c3"), out _);

            System.Collections.Generic.IReadOnlyList<WorldConnectionRegistry.Entry> cleared =
                _registry.Clear();

            Assert.That(cleared.Count, Is.EqualTo(3),
                "a server that stops without releasing strands every player in it");
            Assert.That(_registry.Count, Is.Zero);
            Assert.That(_registry.PresenceOf(new CharacterId("c2")),
                Is.EqualTo(WorldPresence.Offline));
        }

        [Test]
        public void ClearingAnEmptyRegistryIsHarmless()
        {
            Assert.That(_registry.Clear(), Is.Empty);
        }

        [Test]
        public void ManyConnectAndDisconnectCyclesLeaveNothingBehind()
        {
            // The leak check of rule 16.20: nothing accumulates across a churn of
            // connections, including the stale set.
            for (int i = 0; i < 500; i++)
            {
                var admission = Admission("s" + i, "a" + i, "c" + i);

                _registry.Register(i, admission, out _);
                _registry.MarkInWorld(i);
                _registry.Unregister(i, out _);
            }

            Assert.That(_registry.Count, Is.Zero);
            Assert.That(_registry.Stale, Is.Empty);
            Assert.That(_registry.All(), Is.Empty);
        }

        [Test]
        public void RepeatedReconnectionOfOneSessionLeavesExactlyOneLiveConnection()
        {
            for (int i = 1; i <= 200; i++)
            {
                _registry.Register(i, Admission("s1", "a1", "c1"), out int displaced);

                if (displaced >= 0) _registry.Unregister(displaced, out _);
            }

            Assert.That(_registry.Count, Is.EqualTo(1));
            Assert.That(_registry.Stale, Is.Empty, "stale marks must not accumulate");
            Assert.That(_registry.CanAct(200), Is.True);
        }
    }
}
