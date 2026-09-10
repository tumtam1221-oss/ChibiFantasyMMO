using System;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Network;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;

namespace ChibiFantasy.Client
{
    /// <summary>
    /// The client half of the production world bootstrap.
    /// </summary>
    /// <remarks>
    /// <b>It asks and it listens. It decides nothing.</b> This connects, presents the
    /// session token the account API issued, and reports what the server said. Every
    /// identity in the response -- account, character, server, channel -- is the server's
    /// answer, held here only so a screen can show it. Nothing on this component is written
    /// back into gameplay state, and there is no method that could.
    ///
    /// <b>The same architecture as the server, deliberately.</b> Requirement F of this gate
    /// is that a client uses the production networking configuration rather than a parallel
    /// one, so this sits on the same prefab as <c>WorldServerBootstrap</c>, beside the same
    /// <see cref="NetworkManager"/> and the same transport. What differs is which of the two
    /// starts.
    ///
    /// <b>The Phase 16 handshake is preserved exactly.</b> The join message carries the
    /// token and the three version numbers, because that is what
    /// <c>WorldAuthenticator</c> reads. Changing the shape here would silently break a
    /// handshake that has a real socket test behind it.
    ///
    /// <b>Nothing here is logged.</b> The join request carries a session token; there is no
    /// logging call in this file, which is the only reliable way to keep one out of a
    /// player's log file.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class WorldClientBootstrap : MonoBehaviour
    {
        [Header("Connect")]
        [Tooltip("Address of the world server. Supplied by deployment, never invented.")]
        [SerializeField] private string _address = "127.0.0.1";

        [SerializeField] private ushort _port = 7770;

        [Tooltip("Connect as soon as this component wakes. Off on the shared prefab; the "
            + "client role scene turns it on.")]
        [SerializeField] private bool _connectOnAwake;

        [Header("Version")]
        [Tooltip("What this build reports. The authority decides whether it is acceptable.")]
        [SerializeField] private string _clientVersion = "1.0.0";

        [SerializeField] private string _protocolVersion = "1.0.0";

        [SerializeField] private string _contentVersion = "1.0.0";

        private NetworkManager _networkManager;
        private bool _registered;

        /// <summary>
        /// The token this client will present, set by the login flow before connecting.
        /// </summary>
        /// <remarks>
        /// Write-only from outside. A token that could be read back off a component is a
        /// token a UI can accidentally display and a bug report can accidentally contain --
        /// which is exactly what Phase 16 spent effort preventing.
        /// </remarks>
        public SessionToken Token { private get; set; }

        /// <summary>What the server said about the join, or a refusal.</summary>
        public WorldJoinResponseMessage LastResponse { get; private set; }

        /// <summary>Where the server placed the character, if it did.</summary>
        public WorldSpawnMessage LastSpawn { get; private set; }

        public bool IsConnected =>
            _networkManager != null && _networkManager.ClientManager.Started;

        /// <summary>Raised when the server answers the join request.</summary>
        public event Action<WorldJoinResponseMessage> OnJoinAnswered;

        /// <summary>Raised when the server says where the character stands.</summary>
        public event Action<WorldSpawnMessage> OnSpawnReceived;

        /// <summary>The last weather this client was told about, as <c>WorldWeather</c>.</summary>
        public int LastWeather { get; private set; }

        /// <summary>Raised when the server says the sky turned.</summary>
        public event Action<int> OnWeatherReceived;

        /// <summary>The last time the world reported, or a default before it has said anything.</summary>
        public WorldTimeMessage LastTime { get; private set; }

        /// <summary>Raised when the world repeats what time it is.</summary>
        public event Action<WorldTimeMessage> OnTimeReceived;

        /// <summary>Command-line names and environment variables this client accepts.</summary>
        public const string AddressOption = "world-address";
        public const string AddressVariable = "CHIBI_WORLD_ADDRESS";
        public const string PortOption = "world-port";
        public const string PortVariable = "CHIBI_WORLD_PORT";

        /// <summary>
        /// Lets the launch say which world server to reach.
        /// </summary>
        /// <remarks>
        /// <b>The same build, pointed anywhere.</b> A test client, a staging world and a live
        /// one differ by an address, and baking that into the build made each of them a
        /// separate build of identical code.
        ///
        /// <b>An address, never a credential.</b> Where the world is, is not a secret -- a
        /// player's own client necessarily knows it. Nothing that proves who somebody is
        /// arrives this way, and nothing here is logged.
        /// </remarks>
        private void ApplyLaunchOptions()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            Func<string, string> environment = Environment.GetEnvironmentVariable;

            _address = LaunchOptions.Resolve(AddressOption, AddressVariable,
                arguments, environment, _address);

            _port = LaunchOptions.ResolvePort(PortOption, PortVariable,
                arguments, environment, _port);
        }

        private void Awake()
        {
            _networkManager = GetComponent<NetworkManager>();

            if (_networkManager == null)
            {
                Debug.LogError("[world] no NetworkManager beside WorldClientBootstrap");

                return;
            }

            ApplyLaunchOptions();

            Register();

            if (_connectOnAwake) Connect();
        }

        /// <summary>
        /// Subscribes to the two broadcasts the server sends back.
        /// </summary>
        /// <remarks>Registered once and before connecting, because a response can arrive on
        /// the same frame the join is sent and a handler attached afterwards would miss
        /// it.</remarks>
        private void Register()
        {
            if (_registered) return;

            _networkManager.ClientManager.RegisterBroadcast<WorldJoinResponseMessage>(OnJoinResponse);
            _networkManager.ClientManager.RegisterBroadcast<WorldSpawnMessage>(OnSpawn);
            _networkManager.ClientManager.RegisterBroadcast<WorldWeatherMessage>(OnWeather);
            _networkManager.ClientManager.RegisterBroadcast<WorldTimeMessage>(OnTime);
            _networkManager.ClientManager.OnClientConnectionState += OnConnectionState;

            _registered = true;
        }

        /// <summary>Opens the connection.</summary>
        public bool Connect()
        {
            if (_networkManager == null) return false;

            if (_networkManager.ClientManager.Started) return true;

            return _networkManager.ClientManager.StartConnection(_address, _port);
        }

        public void Disconnect()
        {
            if (_networkManager == null) return;

            _networkManager.ClientManager.StopConnection();
        }

        /// <summary>Points this client at a server. Deployment configuration, not a literal.</summary>
        public void UseEndpoint(string address, ushort port)
        {
            if (!string.IsNullOrEmpty(address)) _address = address;

            if (port != 0) _port = port;
        }

        /// <summary>What this build reports about itself.</summary>
        /// <remarks>Supplied rather than computed: a launcher fills these in after patching,
        /// which is why nothing here invents a version. The authority decides whether they
        /// are acceptable -- see Phase 14's VersionPolicy.</remarks>
        public void UseVersions(string client, string protocol, string content)
        {
            if (!string.IsNullOrEmpty(client)) _clientVersion = client;
            if (!string.IsNullOrEmpty(protocol)) _protocolVersion = protocol;
            if (!string.IsNullOrEmpty(content)) _contentVersion = content;
        }

        /// <summary>
        /// What the socket last did.
        /// </summary>
        /// <remarks>Recorded because it used to be discarded. Every state except Started was
        /// dropped on the floor, so a client that could not reach the world server loaded the
        /// world scene, drew the sky, and waited forever with nothing in it and nothing said
        /// -- which is exactly how an empty GameWorld reached a manual test.</remarks>
        public LocalConnectionState ConnectionState { get; private set; }
            = LocalConnectionState.Stopped;

        /// <summary>
        /// Whether the socket stopped without ever reaching the world.
        /// </summary>
        /// <remarks>True after a failed connect, false again once one succeeds. This is the
        /// difference between "the world is empty because nothing spawned yet" and "the world
        /// is empty because this client is not connected to anything", which a player and a
        /// test both need to be able to tell apart.</remarks>
        public bool ConnectionFailed { get; private set; }

        /// <summary>Raised on every change of socket state.</summary>
        public event System.Action<LocalConnectionState> ConnectionChanged;

        private void OnConnectionState(ClientConnectionStateArgs args)
        {
            ConnectionState = args.ConnectionState;

            if (args.ConnectionState == LocalConnectionState.Started)
            {
                ConnectionFailed = false;

                ConnectionChanged?.Invoke(args.ConnectionState);

                SendJoinRequest();

                return;
            }

            if (args.ConnectionState == LocalConnectionState.Stopped)
            {
                ConnectionFailed = true;

                // Loud, because the alternative is a world that renders correctly and is
                // simply empty. There is nothing else on screen to say why.
                Debug.LogError("[client] not connected to the world server at " + _address
                    + ":" + _port + " -- the world will be empty until it is reachable", this);
            }

            ConnectionChanged?.Invoke(args.ConnectionState);
        }

        /// <summary>
        /// Presents the token, immediately after connecting.
        /// </summary>
        /// <remarks>
        /// The claims are sent because Phase 16's authenticator compares them against what
        /// the authority says and refuses a disagreement. They are not how the server
        /// decides who this is -- that comes from resolving the token -- so a client that
        /// edited them changes nothing except making its own request contradict itself.
        /// </remarks>
        public void SendJoinRequest()
        {
            if (_networkManager == null || !_networkManager.ClientManager.Started) return;

            _networkManager.ClientManager.Broadcast(new WorldJoinRequestMessage
            {
                Token = Token.Value,
                ClientVersion = _clientVersion,
                ProtocolVersion = _protocolVersion,
                ContentVersion = _contentVersion,
            });
        }

        /// <summary>
        /// Asks the server to hold the sky at one weather.
        /// </summary>
        /// <remarks>
        /// <b>Nothing is applied locally.</b> This sends a request and returns; the sky only
        /// changes when the server says so, through the same broadcast every other client
        /// gets. A version that also set the local weather would show the sender a sky nobody
        /// else was standing under whenever the server refused -- which a release server
        /// always does.
        /// </remarks>
        public void RequestWeather(int weather)
        {
            Send(new WorldWeatherCommandMessage { Weather = weather, Automatic = false });
        }

        /// <summary>Asks the server to stop holding the sky and let it roll again.</summary>
        public void RequestAutomaticWeather()
        {
            Send(new WorldWeatherCommandMessage { Automatic = true });
        }

        private void Send(WorldWeatherCommandMessage message)
        {
            if (_networkManager == null || !_networkManager.ClientManager.Started) return;

            _networkManager.ClientManager.Broadcast(message);
        }

        private void OnJoinResponse(WorldJoinResponseMessage message, Channel channel)
        {
            LastResponse = message;

            OnJoinAnswered?.Invoke(message);
        }

        private void OnSpawn(WorldSpawnMessage message, Channel channel)
        {
            // Recorded for presentation. The server decided this position; nothing here
            // writes it back or argues with it.
            LastSpawn = message;

            LastWeather = message.Weather;

            OnSpawnReceived?.Invoke(message);
        }

        /// <summary>
        /// The weather turned while this client was connected.
        /// </summary>
        /// <remarks>Kept as well as raised, so a presenter that loads after the message
        /// arrives -- an environment scene streaming in mid-storm -- can still catch up
        /// rather than staying dry until the next change.</remarks>
        private void OnWeather(WorldWeatherMessage message, Channel channel)
        {
            LastWeather = message.Weather;

            OnWeatherReceived?.Invoke(message.Weather);
        }

        /// <summary>
        /// The world said what time it is.
        /// </summary>
        /// <remarks>Kept as well as raised, for the same reason the weather is: a presenter
        /// that loads between two of these can read the last one rather than run on a stale
        /// clock until the next minute comes round.</remarks>
        private void OnTime(WorldTimeMessage message, Channel channel)
        {
            LastTime = message;

            OnTimeReceived?.Invoke(message);
        }

        private void OnDestroy()
        {
            if (_networkManager == null || !_registered) return;

            _networkManager.ClientManager.OnClientConnectionState -= OnConnectionState;
        }
    }
}
