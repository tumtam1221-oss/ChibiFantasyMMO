using System;
using ChibiFantasy.Contracts;
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

        private void Awake()
        {
            _networkManager = GetComponent<NetworkManager>();

            if (_networkManager == null)
            {
                Debug.LogError("[world] no NetworkManager beside WorldClientBootstrap");

                return;
            }

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

        private void OnConnectionState(ClientConnectionStateArgs args)
        {
            if (args.ConnectionState != LocalConnectionState.Started) return;

            SendJoinRequest();
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

            OnSpawnReceived?.Invoke(message);
        }

        private void OnDestroy()
        {
            if (_networkManager == null || !_registered) return;

            _networkManager.ClientManager.OnClientConnectionState -= OnConnectionState;
        }
    }
}
