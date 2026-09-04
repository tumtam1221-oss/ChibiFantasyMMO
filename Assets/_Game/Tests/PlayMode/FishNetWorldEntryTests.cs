using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Network;
using ChibiFantasy.Server;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Transporting;
using FishNet.Transporting.Tugboat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ChibiFantasy.Tests.PlayMode
{
    /// <summary>
    /// A real FishNet server and a real FishNet client, over a real loopback socket.
    /// </summary>
    /// <remarks>
    /// <b>Why this exists separately from everything else.</b> The coordinator's rules are
    /// proven exhaustively in EditMode against a pure registry, and the HTTP boundary is
    /// proven against live PHP. Neither proves that the FishNet wiring is correct -- that
    /// the authenticator is actually installed, that the join broadcast is actually
    /// accepted before authentication, that a refusal actually disconnects, and that a
    /// disconnect actually reaches the coordinator.
    ///
    /// Those are wiring facts, and the only way to establish them is to open a socket.
    /// So this does: two <see cref="NetworkManager"/> instances in one process, two Tugboat
    /// transports, and a loopback connection between them.
    ///
    /// <b>The authority is a fake here, deliberately.</b> This test is about FishNet, not
    /// about PHP. Mixing the two would mean a failure could be either, and neither test
    /// would be diagnostic.
    ///
    /// <b>Ports are chosen per run</b> so a rerun does not collide with a socket the OS has
    /// not finished releasing.
    /// </remarks>
    [TestFixture]
    internal sealed class FishNetWorldEntryTests
    {
        private const string Token = "tok-playmode";

        /// <summary>An authority that admits one token and records what it was told.</summary>
        private sealed class FakeAuthority : IWorldSessionAuthority
        {
            public readonly List<string> Arrived = new List<string>();
            public readonly List<string> Released = new List<string>();

            public bool Admits = true;

            public WorldAdmission Admit(WorldJoinClaim claim)
            {
                if (!Admits || claim.Token.Value != Token)
                {
                    return WorldAdmission.Refused(SessionRejection.SessionExpired);
                }

                return WorldAdmission.Admitted(
                    new SessionId("s-playmode"),
                    new AccountId("a-playmode"),
                    new CharacterId("c-playmode"),
                    new ServerId("srv-playmode"),
                    new ChannelId("ch-playmode"),
                    new DefinitionId("map.town"),
                    new Revision(1),
                    new Revision(1),
                    SessionState.EnteringWorld);
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

        private GameObject _serverObject;
        private GameObject _clientObject;
        private NetworkManager _server;
        private NetworkManager _client;
        private WorldAuthenticator _authenticator;
        private WorldEntryCoordinator _coordinator;
        private FakeAuthority _authority;
        private ushort _port;

        private static ushort NextPort()
        {
            // High ephemeral range, randomised per run so a rerun does not land on a
            // socket the OS is still holding in TIME_WAIT.
            return (ushort)Random.Range(24000, 30000);
        }

        /// <summary>
        /// Builds a NetworkManager whose Awake finds everything it needs.
        /// </summary>
        /// <remarks>
        /// Two details, both learned the hard way on the first run of this fixture.
        ///
        /// <b>The object starts inactive.</b> <c>AddComponent</c> runs Awake immediately on
        /// an active object, so a NetworkManager added before its transport wakes up with no
        /// transport and leaves its managers null. Building the whole object while inactive
        /// and activating once means Awake sees a finished object.
        ///
        /// <b>SpawnablePrefabs is assigned explicitly.</b> FishNet auto-populates it from the
        /// project in a normal scene, and cannot in the throwaway scene a PlayMode test runs
        /// in -- it logs an error and stops initialising. An empty DefaultPrefabObjects is
        /// exactly right here: this phase spawns no networked prefabs, and an empty
        /// collection says so honestly rather than pulling in the project's.
        /// </remarks>
        private static NetworkManager BuildManager(string name, ushort port, bool listening,
            out GameObject host, out Tugboat transport)
        {
            host = new GameObject(name);
            host.SetActive(false);

            // FishNet's OnValidate runs inside AddComponent and logs an error because the
            // field it wants cannot be auto-populated in a throwaway test scene. It is
            // expected by name rather than suppressed wholesale: an unexpected error in
            // these tests must still fail them.
            LogAssert.Expect(LogType.Error, new Regex("SpawnablePrefabs is null"));

            NetworkManager manager = host.AddComponent<NetworkManager>();
            manager.SpawnablePrefabs = ScriptableObject.CreateInstance<DefaultPrefabObjects>();

            // A NetworkManager destroys itself if another already exists, because a game has
            // exactly one. A test that needs a server and a client in one process needs two,
            // and the switch that allows it is a private serialized field with no setter --
            // so it is set by reflection, here, and nowhere near production code.
            //
            // Without this the second manager silently destroys itself and every reference
            // to it becomes null, which is precisely how the first run of this fixture
            // failed.
            typeof(NetworkManager)
                .GetField("_persistence", System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance)
                ?.SetValue(manager, NetworkManager.PersistenceType.AllowMultiple);

            transport = host.AddComponent<Tugboat>();
            transport.SetPort(port);

            if (listening) transport.SetServerBindAddress("127.0.0.1", IPAddressType.IPv4);
            else transport.SetClientAddress("127.0.0.1");

            return manager;
        }

        [SetUp]
        public void SetUp()
        {
            _port = NextPort();
            _authority = new FakeAuthority();

            _server = BuildManager("PlayModeServer", _port, listening: true,
                out _serverObject, out _);

            _authenticator = _serverObject.AddComponent<WorldAuthenticator>();

            _coordinator = new WorldEntryCoordinator(_authority, new WorldConnectionRegistry(),
                default);

            _authenticator.UseCoordinator(_coordinator);

            // Activated only now: Awake runs once, with the transport and the authenticator
            // both already on the object.
            _serverObject.SetActive(true);

            _server.ServerManager.SetAuthenticator(_authenticator);

            _client = BuildManager("PlayModeClient", _port, listening: false,
                out _clientObject, out _);

            _clientObject.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            if (_client != null) _client.ClientManager.StopConnection();
            if (_server != null) _server.ServerManager.StopConnection(true);

            if (_clientObject != null) Object.DestroyImmediate(_clientObject);
            if (_serverObject != null) Object.DestroyImmediate(_serverObject);
        }

        /// <summary>Pumps frames until a condition holds, or gives up.</summary>
        /// <remarks>A fixed frame count rather than a wall-clock wait: a test machine under
        /// load should get more time, not fail sooner.</remarks>
        private static IEnumerator Until(System.Func<bool> condition, int frames = 300)
        {
            for (int i = 0; i < frames && !condition(); i++) yield return null;
        }

        private IEnumerator StartServerAndClient()
        {
            Assert.That(_server.ServerManager.StartConnection(), Is.True, "server did not start");

            yield return Until(() => _server.ServerManager.Started);

            Assert.That(_server.ServerManager.Started, Is.True, "server never reported started");

            Assert.That(_client.ClientManager.StartConnection(), Is.True, "client did not start");

            yield return Until(() => _client.ClientManager.Started);

            Assert.That(_client.ClientManager.Started, Is.True, "client never connected");
        }

        private WorldJoinRequestMessage JoinMessage(string token = Token)
        {
            return new WorldJoinRequestMessage
            {
                Token = token,
                ClientVersion = "1.0.0",
                ProtocolVersion = "1.0.0",
                ContentVersion = "1.0.0",
            };
        }

        // ---- the connection itself ----------------------------------------------------------

        [UnityTest]
        public IEnumerator AClientConnectsToTheServerOverARealSocket()
        {
            yield return StartServerAndClient();

            yield return Until(() => _server.ServerManager.Clients.Count == 1);

            Assert.That(_server.ServerManager.Clients.Count, Is.EqualTo(1),
                "the server observed a real remote connection");
        }

        [UnityTest]
        public IEnumerator AValidJoinIsAdmittedAndReachesTheCoordinator()
        {
            yield return StartServerAndClient();

            WorldJoinResponseMessage response = default;
            bool received = false;

            _client.ClientManager.RegisterBroadcast<WorldJoinResponseMessage>((message, channel) =>
            {
                response = message;
                received = true;
            });

            _client.ClientManager.Broadcast(JoinMessage());

            yield return Until(() => received);

            Assert.That(received, Is.True, "no join response arrived");
            Assert.That(response.Admitted, Is.True);

            // The identities came from the authority, across a real wire.
            Assert.That(response.AccountId, Is.EqualTo("a-playmode"));
            Assert.That(response.CharacterId, Is.EqualTo("c-playmode"));
            Assert.That(response.EntryState, Is.EqualTo((int)WorldEntryState.Connecting));

            Assert.That(_coordinator.Registry.Count, Is.EqualTo(1),
                "the connection is registered on the server side");
        }

        [UnityTest]
        public IEnumerator AJoinWithNoTokenIsRefusedAndRegistersNothing()
        {
            yield return StartServerAndClient();

            WorldJoinResponseMessage response = default;
            bool received = false;

            _client.ClientManager.RegisterBroadcast<WorldJoinResponseMessage>((message, channel) =>
            {
                response = message;
                received = true;
            });

            _client.ClientManager.Broadcast(JoinMessage(token: string.Empty));

            yield return Until(() => received);

            Assert.That(received, Is.True, "a refusal must be answered, not dropped silently");
            Assert.That(response.Admitted, Is.False);
            Assert.That(response.Rejection, Is.EqualTo((int)SessionRejection.MissingContext));
            Assert.That(_coordinator.Registry.Count, Is.Zero,
                "a rejected connection must not spawn a character");
        }

        [UnityTest]
        public IEnumerator AnExpiredSessionIsRefusedWithItsOwnReason()
        {
            _authority.Admits = false;

            yield return StartServerAndClient();

            WorldJoinResponseMessage response = default;
            bool received = false;

            _client.ClientManager.RegisterBroadcast<WorldJoinResponseMessage>((message, channel) =>
            {
                response = message;
                received = true;
            });

            _client.ClientManager.Broadcast(JoinMessage());

            yield return Until(() => received);

            Assert.That(response.Admitted, Is.False);
            Assert.That(response.Rejection, Is.EqualTo((int)SessionRejection.SessionExpired),
                "a player is owed the actual reason");
        }

        [UnityTest]
        public IEnumerator AnUnauthenticatedConnectionIsFailedAfterARefusal()
        {
            _authority.Admits = false;

            yield return StartServerAndClient();

            _client.ClientManager.Broadcast(JoinMessage());

            // FishNet fails the connection when the authenticator reports false.
            yield return Until(() => _server.ServerManager.Clients.Count == 0);

            Assert.That(_server.ServerManager.Clients.Count, Is.Zero,
                "a refused connection is not left sitting on the server");
        }

        // ---- disconnecting -------------------------------------------------------------------

        [UnityTest]
        public IEnumerator AClientDisconnectingReleasesItsSession()
        {
            yield return StartServerAndClient();

            bool received = false;

            _client.ClientManager.RegisterBroadcast<WorldJoinResponseMessage>((m, c) => received = true);
            _client.ClientManager.Broadcast(JoinMessage());

            yield return Until(() => received);

            Assert.That(_coordinator.Registry.Count, Is.EqualTo(1));

            // The bootstrap normally wires this; here the test plays that part, because the
            // point is that a real FishNet disconnect reaches the coordinator at all.
            int connectionId = -1;

            foreach (WorldConnectionRegistry.Entry entry in _coordinator.Registry.All())
            {
                connectionId = entry.ConnectionId;
            }

            _client.ClientManager.StopConnection();

            yield return Until(() => _server.ServerManager.Clients.Count == 0);

            _coordinator.Leave(connectionId);

            Assert.That(_authority.Released, Is.EqualTo(new[] { "s-playmode" }),
                "a player who closes the game must get their session back");
            Assert.That(_coordinator.Registry.Count, Is.Zero);
        }

        [UnityTest]
        public IEnumerator RepeatedConnectAndDisconnectLeavesNothingBehind()
        {
            yield return StartServerAndClient();

            for (int i = 0; i < 5; i++)
            {
                bool received = false;

                _client.ClientManager.RegisterBroadcast<WorldJoinResponseMessage>(
                    (m, c) => received = true);

                _client.ClientManager.Broadcast(JoinMessage());

                yield return Until(() => received);

                foreach (WorldConnectionRegistry.Entry entry in _coordinator.Registry.All())
                {
                    _coordinator.Leave(entry.ConnectionId);
                }

                _client.ClientManager.StopConnection();

                yield return Until(() => _server.ServerManager.Clients.Count == 0);

                _client.ClientManager.StartConnection();

                yield return Until(() => _client.ClientManager.Started);
            }

            Assert.That(_coordinator.Registry.Count, Is.LessThanOrEqualTo(1),
                "connections must not accumulate across reconnection cycles");
            Assert.That(_coordinator.Registry.Stale, Is.Empty);
        }

        // ---- shutdown ------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator StoppingTheServerReleasesEverySessionItHeld()
        {
            yield return StartServerAndClient();

            bool received = false;

            _client.ClientManager.RegisterBroadcast<WorldJoinResponseMessage>((m, c) => received = true);
            _client.ClientManager.Broadcast(JoinMessage());

            yield return Until(() => received);

            int released = _coordinator.ReleaseAll();

            Assert.That(released, Is.EqualTo(1),
                "a server that stops without releasing strands every player in it");
            Assert.That(_coordinator.Registry.Count, Is.Zero);
        }
    }
}
