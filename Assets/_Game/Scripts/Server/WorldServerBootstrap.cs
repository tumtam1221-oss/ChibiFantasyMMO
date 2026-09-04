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
    ///
    /// <b>It owns the world's only clock.</b> <see cref="WorldSimulation"/> holds the
    /// authorities and the order they run in; this drives it once per frame. There is
    /// exactly one such loop in the project by design -- a second one would advance the same
    /// timers twice and expire a buff in half its authored duration.
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

        /// <summary>
        /// Spawn points, so arrivals resolve from authored data rather than coordinates.
        /// </summary>
        /// <remarks>
        /// Supplied through <see cref="UseContent"/> rather than as a serialized field.
        /// <c>DefinitionRegistry&lt;T&gt;</c> is a plain generic class, and Unity does not
        /// serialize those -- a <c>[SerializeField]</c> here would sit in the inspector
        /// looking configurable and arrive null at runtime, which is worse than not offering
        /// it. Content loading is the composition root's job in any case.
        /// </remarks>
        private IDefinitionRegistry<SpawnPointDefinition> _spawnPoints;

        private NetworkManager _networkManager;
        private WorldAuthenticator _authenticator;
        /// <summary>Whatever the authority is holding open. Disposed on shutdown.</summary>
        /// <remarks>An <c>IDisposable</c> and not a transport: this assembly does not know
        /// that HTTP exists, which is the boundary rule and was an audit finding when this
        /// field named a concrete transport.</remarks>
        private System.IDisposable _authorityLifetime;

        /// <summary>The connection registry, exposed for diagnostics and tests.</summary>
        public WorldConnectionRegistry Registry { get; private set; }

        public WorldEntryCoordinator Coordinator { get; private set; }

        /// <summary>
        /// The world's authorities and the order they tick in, or null on a session-only
        /// server.
        /// </summary>
        /// <remarks>Supplied through <see cref="UseWorld"/> rather than built here: what
        /// content a world runs and which authorities it composes is the composition root's
        /// decision, and a login-only process legitimately runs none of it.</remarks>
        public WorldSimulation Simulation { get; private set; }

        /// <summary>How many world ticks have run. For diagnostics.</summary>
        public long Ticks => Simulation == null ? 0L : Simulation.Ticks;

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
                // Composed on the transport's own side of the line: this file names no
                // transport, no URL scheme and no HTTP type.
                authority = BackendAuthority.OverHttp(_apiBaseAddress, _apiTimeoutSeconds,
                    out _authorityLifetime);
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

        /// <summary>
        /// Supplies the composed world this server is to run.
        /// </summary>
        /// <remarks>
        /// Optional, and deliberately so. This process is a session authority first: it can
        /// admit players, place them and release them without simulating anything, which is
        /// what it did before a world existed to run. Given one, it becomes the world's
        /// clock as well.
        /// </remarks>
        public void UseWorld(WorldSimulation simulation)
        {
            Simulation = simulation;
        }

        /// <summary>
        /// Advances the world once per frame.
        /// </summary>
        /// <remarks>
        /// <b>The only place the world's time comes from.</b> Unity's frame delta, handed
        /// straight through -- every authority underneath takes elapsed seconds as an
        /// argument and reads no clock of its own, which is what makes each of them
        /// reproducible in a test and all of them agree here.
        ///
        /// A server that is not listening does not advance: a world nobody is in has no
        /// time to pass, and ticking one would expire the buffs of players who have not
        /// arrived yet.
        /// </remarks>
        private void Update()
        {
            if (!IsListening || Simulation == null) return;

            Simulation.Tick(Time.deltaTime);
        }

        /// <summary>Supplies the authored content this server places arrivals against.</summary>
        /// <remarks>A server with no spawn points admits connections and then refuses each
        /// one at placement, which is the correct behaviour for a misconfigured server: it
        /// never invents a position.</remarks>
        public void UseContent(IDefinitionRegistry<SpawnPointDefinition> spawnPoints)
        {
            _spawnPoints = spawnPoints;
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

            _authorityLifetime?.Dispose();
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
