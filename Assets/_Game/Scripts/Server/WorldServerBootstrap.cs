using ChibiFantasy.Backend;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Network;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;

namespace ChibiFantasy.Server
{
    /// <summary>
    /// Starts the dedicated world server and wires the authority behind it.
    /// </summary>
    /// <remarks>
    /// <b>The smallest bootstrap that is actually authoritative.</b> It starts a listen
    /// socket, installs the authenticator, resolves spawns from authored definitions and
    /// releases sessions on shutdown. It does not replicate a world, synchronise inventory
    /// or move a monster -- those are later phases, and pretending to do them here would be
    /// the fake handoff the brief forbids.
    ///
    /// <b>Its identity is configuration, not a constant.</b> The server and channel it
    /// serves are fields, and the address of the account API is a field. There is no
    /// hard-coded id anywhere in this file, which is rule 6, and no credential either --
    /// the only secret this process handles is a player's own token, and it holds that
    /// only for as long as the player is connected.
    ///
    /// <b>Composition happens here and only here.</b> This is the one place that knows both
    /// that an HTTP transport exists and that a world exists. Everything either side of it
    /// is written against an interface, which is why the coordinator can be tested without
    /// a socket and the authority without a world.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class WorldServerBootstrap : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("The server this process serves. Supplied by deployment, never invented.")]
        [SerializeField] private string _serverId;

        [Tooltip("The channel this process serves.")]
        [SerializeField] private string _channelId;

        [Header("Account authority")]
        [Tooltip("Base address of the PHP API. No credential: the API is asked, not trusted.")]
        [SerializeField] private string _apiBaseAddress = "http://127.0.0.1:8080";

        [SerializeField] private int _apiTimeoutSeconds = 10;

        [Header("Listen")]
        [SerializeField] private ushort _port = 7770;

        [Tooltip("Start listening as soon as this component wakes.")]
        [SerializeField] private bool _startOnAwake = true;

        [Header("Content")]
        [Tooltip("Spawn points, so arrivals resolve from authored data rather than coordinates.")]
        [SerializeField] private DefinitionRegistry<SpawnPointDefinition> _spawnPoints;

        private NetworkManager _networkManager;
        private WorldAuthenticator _authenticator;
        private UnityWebRequestTransport _transport;

        /// <summary>The connection registry, exposed for diagnostics and tests.</summary>
        public WorldConnectionRegistry Registry { get; private set; }

        public WorldEntryCoordinator Coordinator { get; private set; }

        public ServerId Server => new ServerId(_serverId);

        public ChannelId Channel => new ChannelId(_channelId);

        public bool IsListening { get; private set; }

        private void Awake()
        {
            _networkManager = GetComponent<NetworkManager>();

            if (_networkManager == null)
            {
                Debug.LogError("[world] no NetworkManager beside WorldServerBootstrap");

                return;
            }

            Compose();

            if (_startOnAwake) StartServer();
        }

        /// <summary>
        /// Builds the object graph.
        /// </summary>
        /// <remarks>Separated from <see cref="Awake"/> so a test or an editor harness can
        /// compose the same graph with a different authority and never open a socket.</remarks>
        public void Compose(IWorldSessionAuthority authority = null,
            VersionRequirement required = default)
        {
            Registry = new WorldConnectionRegistry();

            if (authority == null)
            {
                _transport = new UnityWebRequestTransport(_apiBaseAddress, _apiTimeoutSeconds);
                authority = new HttpWorldSessionAuthority(_transport);
            }

            Coordinator = new WorldEntryCoordinator(authority, Registry, required);

            _authenticator = GetComponent<WorldAuthenticator>();

            if (_authenticator == null)
            {
                _authenticator = gameObject.AddComponent<WorldAuthenticator>();
            }

            _authenticator.UseCoordinator(Coordinator);
            _authenticator.OnAdmitted += OnAdmitted;

            _networkManager.ServerManager.SetAuthenticator(_authenticator);
            _networkManager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
        }

        public bool StartServer()
        {
            if (IsListening) return true;

            IsListening = _networkManager.ServerManager.StartConnection(_port);

            return IsListening;
        }

        /// <summary>
        /// Stops listening and hands every session back.
        /// </summary>
        /// <remarks>
        /// <b>The release is the important half.</b> Without it, everyone who was playing is
        /// locked out of their own account until their session expires, and every character
        /// stays marked InWorld in a world that no longer exists. A server that stops
        /// without releasing corrupts nothing in the database, but it strands every player
        /// in it -- which is worse than it sounds, because nothing will ever fix it but time.
        /// </remarks>
        public int StopServer()
        {
            int released = Coordinator?.ReleaseAll() ?? 0;

            if (IsListening)
            {
                _networkManager.ServerManager.StopConnection(sendDisconnectMessage: true);
                IsListening = false;
            }

            return released;
        }

        private void OnDestroy()
        {
            if (_authenticator != null) _authenticator.OnAdmitted -= OnAdmitted;

            if (_networkManager != null)
            {
                _networkManager.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
            }

            StopServer();

            _transport?.Dispose();
        }

        private void OnApplicationQuit()
        {
            StopServer();
        }

        /// <summary>
        /// An admitted connection: resolve where it stands and tell it.
        /// </summary>
        /// <remarks>
        /// <b>The client is told; it does not decide.</b> The map comes from the character's
        /// own row and the spawn from the authored definition, so a client that wanted to
        /// appear somewhere else has no message in which to say so.
        ///
        /// A character whose map has no player spawn is disconnected rather than placed at
        /// the origin. Inventing a position would put a player inside terrain and call it
        /// success.
        /// </remarks>
        private void OnAdmitted(NetworkConnection connection, WorldJoinOutcome outcome)
        {
            SpawnPointDefinition spawn = Coordinator.ResolveSpawn(outcome.Admission, _spawnPoints);

            if (spawn == null)
            {
                Debug.LogWarning("[world] no player spawn on map " + outcome.Admission.Map
                    + "; refusing entry rather than inventing a position");

                connection.Disconnect(immediately: false);

                return;
            }

            _networkManager.ServerManager.Broadcast(connection, new WorldSpawnMessage
            {
                CharacterId = outcome.Admission.Character.Value,
                MapId = spawn.Map.Value,
                SpawnPointId = spawn.Id.Value,
                X = spawn.X,
                Y = spawn.Y,
                Z = spawn.Z,
                CharacterRevision = outcome.Admission.CharacterRevision.Value,
            });

            // Connecting becomes Ready, and the authority's session becomes Active.
            Coordinator.ConfirmArrival(connection.ClientId);
        }

        /// <summary>
        /// A connection appearing or going away.
        /// </summary>
        /// <remarks>Only the stopped case is acted on. FishNet can report a stop more than
        /// once for one socket, and the coordinator's release is idempotent precisely
        /// because of that.</remarks>
        private void OnRemoteConnectionState(NetworkConnection connection,
            RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState != RemoteConnectionState.Stopped) return;

            Coordinator?.Leave(connection.ClientId);
        }
    }
}
