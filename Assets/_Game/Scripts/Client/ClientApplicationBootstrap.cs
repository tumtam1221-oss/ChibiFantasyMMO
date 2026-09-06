using ChibiFantasy.Backend;
using ChibiFantasy.Client.UI;
using ChibiFantasy.Client.World;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using FishNet.Managing;
using FishNet.Object;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ChibiFantasy.Client
{
    /// <summary>
    /// The one place a client is assembled.
    /// </summary>
    /// <remarks>
    /// <b>The gap this closes.</b> Every part of the client flow existed and was tested --
    /// the login screen, the session controller, the HTTP account API, the flow driver, the
    /// world bootstrap, the presentation binder -- and nothing in production ever put them
    /// together. <c>SessionUiController.Bind</c> and <c>WorldPresentationBinder.Compose</c>
    /// were called only by tests, and the world scene contained no networking at all. A
    /// player could type a password into a screen that had nowhere to send it.
    ///
    /// <b>One root, one lifetime.</b> It is built in the first scene and survives every
    /// scene load, because the session it holds is the thing the player spends the whole
    /// session inside. A second one destroys itself rather than composing a second API, a
    /// second session or a second world.
    ///
    /// <b>It composes; it decides nothing.</b> Which screen the player belongs on is
    /// <c>ClientFlowCoordinator</c>'s, whether a login succeeds is the account API's, and
    /// what happens in the world is the server's. This hands each of them to the other and
    /// then keeps out of the way.
    ///
    /// <b>Where the world lives is deployment, not code.</b> The API address and the world
    /// address are serialized fields with local defaults, which is what makes a development
    /// machine playable without making a shipped client unable to point somewhere else.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ClientApplicationBootstrap : MonoBehaviour
    {
        [Header("Account API")]
        [Tooltip("Where the account API answers. Deployment configuration, never a secret.")]
        [SerializeField] private string _apiBaseAddress = "http://127.0.0.1:8099";

        [SerializeField] private int _apiTimeoutSeconds = 10;

        [Header("World server")]
        [Tooltip("Where the world server listens. Deployment configuration.")]
        [SerializeField] private string _worldAddress = "127.0.0.1";

        [SerializeField] private ushort _worldPort = 7770;

        [Header("Versions this build reports")]
        [SerializeField] private string _clientVersion = "1.0.0";

        [SerializeField] private string _protocolVersion = "1.0.0";

        [SerializeField] private string _contentVersion = "1.0.0";

        [Header("Composition")]
        [Tooltip("The shared World_NetworkManager prefab. The client half of it is used.")]
        [SerializeField] private GameObject _networkManagerPrefab;

        [Tooltip("Authored content, so the bag can name the items the server sends.")]
        [SerializeField] private WorldContentCatalogue _content;

        /// <summary>The one instance, or null before the first scene has loaded.</summary>
        public static ClientApplicationBootstrap Current { get; private set; }

        /// <summary>The session every screen submits through.</summary>
        public SessionUiController Session { get; private set; }

        /// <summary>The account API this client speaks to. One, for the whole session.</summary>
        public IAccountApi Api { get; private set; }

        /// <summary>The world connection, once the world scene has been reached.</summary>
        public WorldClientBootstrap World { get; private set; }

        /// <summary>The network manager this client is using, or null before the world.</summary>
        public NetworkManager NetworkManager { get; private set; }

        /// <summary>How many times a world connection has been composed. For tests.</summary>
        public int WorldCompositions { get; private set; }

        /// <summary>How a person picks a monster and asks to hit it.</summary>
        public WorldCombatInput Combat { get; private set; }

        /// <summary>How a person picks up what a monster left.</summary>
        public WorldLootInput Loot { get; private set; }

        /// <summary>
        /// Lets this client keep its own network manager in a process that already runs one.
        /// </summary>
        /// <remarks>
        /// <b>Off, because a client process has exactly one.</b> FishNet destroys a second
        /// <c>NetworkManager</c> by default, and for a shipped client that is the right
        /// answer: a second one would mean a second connection nobody asked for.
        ///
        /// It is switchable because one process legitimately runs both halves -- the editor
        /// with a world open beside a client, and every fixture that proves this composition
        /// works. Without it, the client's manager quietly destroys itself the moment a world
        /// exists in the same process, which looks exactly like a client that will not
        /// connect.
        /// </remarks>
        public bool AllowMultipleNetworkManagers { get; set; }

        /// <summary>
        /// The session this client presents to the world.
        /// </summary>
        /// <remarks>
        /// <b>Normally the one the account API issued.</b> Left unset, the token is whatever
        /// the login produced, which is the only way a player ever reaches the world: the
        /// server refuses a join with no token before it asks anybody anything.
        ///
        /// Settable because the world and the account API are separate services, and
        /// something that already holds a session -- a launcher resuming one, a fixture
        /// exercising the world without the account flow -- should not have to log in again
        /// to say which session it is.
        /// </remarks>
        public SessionToken WorldToken { get; set; }

        private WorldHudScreen _hud;
        private SessionDirectory _sessions;
        private System.IDisposable _transportLifetime;
        private DefinitionRegistry<ItemDefinition> _items;

        private void Awake()
        {
            if (Current != null && Current != this)
            {
                // A second root would mean a second session and a second world. The first
                // one through the door owns the client.
                Destroy(gameObject);

                return;
            }

            Current = this;

            DontDestroyOnLoad(gameObject);

            Compose();

            SceneManager.sceneLoaded += OnSceneLoaded;

            // The scene this root was built in is already loaded, so nothing will announce
            // it: bind what is already here.
            BindScene(SceneManager.GetActiveScene());
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;

            SceneManager.sceneLoaded -= OnSceneLoaded;

            _transportLifetime?.Dispose();
        }

        /// <summary>
        /// Builds the account stack and the session the screens submit through.
        /// </summary>
        /// <remarks>The existing pieces, in the order they depend on each other: a transport
        /// that knows an address, an API that speaks the account protocol over it, an
        /// authority that remembers what the API answered, and a controller that turns all
        /// of it into what a screen draws.</remarks>
        private void Compose()
        {
            var transport = new UnityWebRequestTransport(_apiBaseAddress, _apiTimeoutSeconds);

            _transportLifetime = transport;

            var api = new HttpAccountApi(transport);

            Api = api;

            var authority = new RemoteSessionAuthority(api);

            _sessions = new SessionDirectory();

            Session = gameObject.GetComponent<SessionUiController>();

            if (Session == null) Session = gameObject.AddComponent<SessionUiController>();

            var versions = new VersionSet(Parse(_clientVersion), Parse(_protocolVersion),
                Parse(_contentVersion));

            Session.Bind(api, authority, _sessions, versions, default);

            _items = _content == null
                ? new DefinitionRegistry<ItemDefinition>()
                : _content.BuildItems();
        }

        // ---- scenes ----------------------------------------------------------------------

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            BindScene(scene);
        }

        /// <summary>
        /// Hands the session to whatever the newly loaded scene brought with it.
        /// </summary>
        /// <remarks>The screens do not look for this root; it finds them. That keeps every
        /// screen exactly as testable as it was -- something hands it a controller -- and
        /// means a scene added later needs no knowledge of composition.</remarks>
        private void BindScene(Scene scene)
        {
            if (!scene.IsValid()) return;

            foreach (SessionScreenBase screen in
                FindObjectsByType<SessionScreenBase>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None))
            {
                screen.Bind(Session);
            }

            foreach (ClientFlowDriver driver in
                FindObjectsByType<ClientFlowDriver>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None))
            {
                driver.Bind(Session);
            }

            if (scene.name == ClientScenes.World) ComposeWorld();
        }

        // ---- the world -------------------------------------------------------------------

        /// <summary>
        /// Points this client at a world server.
        /// </summary>
        /// <remarks>Deployment configuration, the same seam <c>WorldClientBootstrap</c>
        /// offers: a launcher, a command line or a test decides where the world is, and the
        /// serialized values are only the local default.</remarks>
        public void UseWorldEndpoint(string address, ushort port)
        {
            if (!string.IsNullOrEmpty(address)) _worldAddress = address;

            if (port != 0) _worldPort = port;
        }

        /// <summary>
        /// Connects to the world and points the presentation at it.
        /// </summary>
        /// <remarks>
        /// <b>The shared prefab, with the server half removed.</b> One prefab carries both
        /// bootstraps because one project ships both programs; a client that kept the server
        /// bootstrap would compose an entire authoritative world -- content, authorities,
        /// an HTTP connection to the backend -- inside a process that must never own one. It
        /// is removed before the object wakes, not disabled afterwards.
        ///
        /// <b>The token is the session the player already has.</b> It comes from the account
        /// API that answered the login; the world server resolves it through the same
        /// authority that issued it, which is what makes the FishNet connection an
        /// authenticated one rather than a name typed into a message.
        /// </remarks>
        public void ComposeWorld()
        {
            if (World != null && NetworkManager != null) return;

            if (_networkManagerPrefab == null)
            {
                Debug.LogError("[client] no network manager prefab: the world cannot be "
                    + "reached from this client");

                return;
            }

            GameObject instance = Instantiate(_networkManagerPrefab);

            instance.name = "World Network (client)";

            instance.SetActive(false);

            RemoveServerBootstrap(instance);

            if (AllowMultipleNetworkManagers) AllowMultiple(instance);

            NetworkManager = instance.GetComponent<NetworkManager>();

            World = instance.GetComponent<WorldClientBootstrap>();

            if (NetworkManager == null || World == null)
            {
                Debug.LogError("[client] the network prefab has no client bootstrap");

                Destroy(instance);

                return;
            }

            World.UseEndpoint(_worldAddress, _worldPort);
            World.UseVersions(_clientVersion, _protocolVersion, _contentVersion);

            World.Token = WorldToken.IsPresent
                ? WorldToken
                : new SessionToken(Api is HttpAccountApi http ? http.SessionToken : null);

            instance.SetActive(true);

            DontDestroyOnLoad(instance);

            WorldCompositions++;

            ComposePresentation();

            World.Connect();
        }

        /// <summary>
        /// Tells this manager not to destroy itself beside another one.
        /// </summary>
        /// <remarks>Set before the object wakes, because FishNet decides during
        /// initialization and there is no second chance. Reflection because the value is a
        /// serialized field FishNet keeps to itself; the alternative is a second prefab that
        /// differs from the shipped one, which is worse.</remarks>
        private static void AllowMultiple(GameObject instance)
        {
            var manager = instance.GetComponent<NetworkManager>();

            if (manager == null) return;

            typeof(NetworkManager)
                .GetField("_persistence", System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance)
                ?.SetValue(manager, NetworkManager.PersistenceType.AllowMultiple);
        }

        /// <summary>
        /// Takes the server half off a client's copy of the shared prefab.
        /// </summary>
        /// <remarks>
        /// <b>By name, because the client assembly does not reference the server one.</b>
        /// That boundary is deliberate and enforced elsewhere; a client that could name
        /// <c>WorldServerBootstrap</c> could also compose one. Matching the type's name is
        /// the smaller price, and a rename breaks the test that pins this rather than
        /// silently shipping a client that builds a world.
        /// </remarks>
        private static void RemoveServerBootstrap(GameObject instance)
        {
            MonoBehaviour[] components = instance.GetComponents<MonoBehaviour>();

            for (var i = 0; i < components.Length; i++)
            {
                if (components[i] == null) continue;

                if (components[i].GetType().Name != "WorldServerBootstrap") continue;

                DestroyImmediate(components[i]);

                return;
            }
        }

        /// <summary>
        /// Reads a version the way the world server reads it.
        /// </summary>
        /// <remarks>Unreadable parts are zero rather than an exception: a version that
        /// cannot be read is a floor, and the authority refuses it. The same rule the
        /// authenticator applies on the other side of the wire.</remarks>
        private static VersionNumber Parse(string value)
        {
            if (string.IsNullOrEmpty(value)) return default;

            string[] parts = value.Split('.');

            return new VersionNumber(Part(parts, 0), Part(parts, 1), Part(parts, 2));
        }

        private static int Part(string[] parts, int index)
        {
            if (parts == null || index >= parts.Length) return 0;

            return int.TryParse(parts[index], out int value) && value >= 0 ? value : 0;
        }

        /// <summary>Points the world screens at the connection, the way GameWorld intends.</summary>
        private void ComposePresentation()
        {
            var binder = FindAnyObjectByType<WorldPresentationBinder>(
                FindObjectsInactive.Include);

            if (binder == null) return;

            var hud = FindAnyObjectByType<WorldHudScreen>(FindObjectsInactive.Include);
            var bag = FindAnyObjectByType<InventoryScreen>(FindObjectsInactive.Include);
            var camera = FindAnyObjectByType<WorldCameraDirector>(FindObjectsInactive.Include);

            binder.Compose(NetworkManager, hud, bag, _items, camera);

            ComposeInteraction(hud, camera);
        }

        /// <summary>
        /// The interactions a person needs: choosing a monster, hitting it, taking what it left.
        /// </summary>
        /// <remarks>
        /// <b>Composed here rather than authored into the scene.</b> They exist only while a
        /// world connection does, and they need it: an input with no connection to send
        /// through is a component waiting to throw. Built on the connection's own object, so
        /// leaving the world takes them with it.
        ///
        /// <b>The development placeholder is part of this and only this.</b> Monsters have no
        /// art; without something to click, targeting is a control with nothing to point at.
        /// It is compiled out of a production build.
        /// </remarks>
        private void ComposeInteraction(WorldHudScreen hud, WorldCameraDirector camera)
        {
            GameObject host = NetworkManager.gameObject;

            Combat = host.GetComponent<WorldCombatInput>();

            if (Combat == null) Combat = host.AddComponent<WorldCombatInput>();

            Combat.Compose(NetworkManager, camera == null ? null : camera.Camera);

            Loot = host.GetComponent<WorldLootInput>();

            if (Loot == null) Loot = host.AddComponent<WorldLootInput>();

            Loot.Compose(NetworkManager);

            _hud = hud;

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            var monsters = host.GetComponent<DevelopmentMonsterVisualizer>();

            if (monsters == null)
            {
                monsters = host.AddComponent<DevelopmentMonsterVisualizer>();
            }

            monsters.Compose(NetworkManager);
#endif
        }

        /// <summary>Keeps the target readout showing whatever the server says about it.</summary>
        private void Update()
        {
            if (_hud == null || Combat == null) return;

            ChibiFantasy.Network.MonsterNetworkEntity target = Combat.Target;

            if (target == null)
            {
                _hud.ClearTarget();

                return;
            }

            // The monster's own replicated values. Nothing here computes a health bar the
            // server would disagree with.
            _hud.ShowTarget(target.Definition.Value, target.Health, target.MaxHealth);
        }

        /// <summary>
        /// Drops the world connection, for a player going back to a menu.
        /// </summary>
        /// <remarks>The session survives: leaving the world is not logging out. What goes is
        /// the socket and the objects it brought, so a reconnection starts from nothing
        /// rather than from a half-remembered world.</remarks>
        public void LeaveWorld()
        {
            if (World != null) World.Disconnect();

            if (NetworkManager != null) Destroy(NetworkManager.gameObject);

            World = null;
            NetworkManager = null;
            Combat = null;
            Loot = null;
            _hud = null;
        }
    }
}
