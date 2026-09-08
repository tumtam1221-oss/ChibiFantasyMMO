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

        /// <summary>What reads the mouse in the world. Null outside it.</summary>
        public WorldPointerInput Pointer { get; private set; }

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

        private void OnApplicationQuit()
        {
            // Fires before teardown on a normal quit, which is the reliable moment to hand
            // the session back.
            ReleaseSessionOnExit();
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;

            SceneManager.sceneLoaded -= OnSceneLoaded;

            // Covers the paths OnApplicationQuit does not: leaving Play Mode in the editor,
            // or this root being destroyed directly.
            ReleaseSessionOnExit();

            _transportLifetime?.Dispose();
        }

        private bool _sessionReleased;

        /// <summary>
        /// Hands the live session back to the server as the client shuts down.
        /// </summary>
        /// <remarks>
        /// Closing the client used to strand its session: nothing released it, so the
        /// account's next sign-in was refused with <c>session_already_active</c> until the
        /// row expired, and the login screen -- which maps every failure to the same
        /// message -- reported only that it could not sign in. The account API sends
        /// synchronously to a known address, so a release issued here reaches the server
        /// before the process is gone. A client on its way out can do nothing useful with a
        /// failure, so one is swallowed; the guard makes the two shutdown hooks idempotent.
        /// </remarks>
        private void ReleaseSessionOnExit()
        {
            if (_sessionReleased) return;

            _sessionReleased = true;

            if (Api is HttpAccountApi http && !string.IsNullOrEmpty(http.SessionToken))
            {
                try
                {
                    http.ReleaseSession(RequestId.New());
                }
                catch
                {
                    // A process that is exiting cannot act on a release failure.
                }
            }
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

            Session.Bind(api, authority, _sessions, ReportedVersions, default);

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
        /// Carries what a player typed to the API that verifies it.
        /// </summary>
        /// <remarks>
        /// <b>The defect this closes.</b> <see cref="LoginScreen"/> raises
        /// <see cref="LoginScreen.Credentials"/> with the login and password a person typed,
        /// and <see cref="HttpAccountApi"/> exposes somewhere to put them, because
        /// <see cref="LoginRequest"/> deliberately has no room for a password. Nothing in
        /// production connected the two. Every manual sign-in therefore posted a null login
        /// to the API, which answered <c>400 invalid_login_identifier</c> -- reported to the
        /// player as "Could not reach the server", though the server had answered perfectly.
        ///
        /// <b>The credential is not stored here.</b> It is handed straight to the API, which
        /// clears both fields in a <c>finally</c> as soon as the request has been built. This
        /// object keeps no copy, and nothing on this path logs either value.
        ///
        /// <b>Versions for the same reason.</b> The screen documents its versions as supplied
        /// rather than invented, and nothing supplied them, so every login reported an empty
        /// build. This is the one place that knows what this build claims to be.
        /// </remarks>
        /// <summary>
        /// Connects a screen's "the authority accepted this" event to the flow driver.
        /// </summary>
        /// <remarks>
        /// <b>The defect this closes, on every screen at once.</b> Each screen raises an
        /// event when the authority accepted what a player did --
        /// <see cref="ServerSelectScreen.Selected"/>,
        /// <see cref="ChannelSelectScreen.Selected"/>,
        /// <see cref="CharacterSelectScreen.WorldAuthorised"/>,
        /// <see cref="LoginScreen.SignedIn"/> -- and not one of them had a subscriber in
        /// production. Clicking a server called the authority, was accepted, and then
        /// nothing happened, because nothing was listening.
        ///
        /// <b>Why the driver could not recover on its own.</b> It advances from its
        /// <c>Update</c> only when <c>RefreshIfChanged</c> reports a revision it has not
        /// seen, and every <c>Submit...</c> on the controller refreshes itself before
        /// returning. The one revision change that matters was therefore always already
        /// consumed. That is true of login, server, channel and character alike -- so the
        /// flow could never advance past any screen without this.
        ///
        /// <b>Every screen, deliberately.</b> Wiring only the one that was reported would
        /// have left the identical defect two clicks further on.
        /// </remarks>
        private void BindAdvance(SessionScreenBase screen)
        {
            // Subtracted before added, so re-binding -- a scene loaded beside this one, a
            // second sceneLoaded for the same screen -- cannot advance the flow twice.
            switch (screen)
            {
                case LoginScreen login:
                    BindLogin(login);

                    break;

                case ServerSelectScreen servers:
                    servers.Selected -= Advance;
                    servers.Selected += Advance;

                    // Signing out moves the session back to Unauthenticated, which is a
                    // flow change like any other. Without this the client hands the session
                    // back and then sits on a screen it can no longer act on -- every button
                    // alive, every request refused, and no way forward.
                    servers.SignedOut -= Advance;
                    servers.SignedOut += Advance;

                    break;

                case ChannelSelectScreen channels:
                    channels.Selected -= Advance;
                    channels.Selected += Advance;

                    channels.WentBack -= Advance;
                    channels.WentBack += Advance;

                    break;

                case CharacterSelectScreen characters:
                    characters.WorldAuthorised -= AdvanceToWorld;
                    characters.WorldAuthorised += AdvanceToWorld;

                    characters.WentBack -= Advance;
                    characters.WentBack += Advance;

                    break;
            }
        }

        /// <summary>The world's entry carries a result; the decision does not need it.</summary>
        /// <remarks>A named method rather than a lambda, so that subtracting it before
        /// adding it actually removes the previous subscription. Where the client goes is
        /// still read from the session state by the driver, not from this payload.</remarks>
        private void AdvanceToWorld(EnterWorldResult entry)
        {
            Advance();
        }

        private void BindLogin(LoginScreen login)
        {
            login.Versions = ReportedVersions;

            login.SignedIn -= Advance;
            login.SignedIn += Advance;

            if (!(Api is HttpAccountApi http)) return;

            login.Credentials = (identifier, password) =>
            {
                http.PendingLoginIdentifier = identifier;
                http.PendingPassword = password;
            };
        }

        /// <summary>
        /// Moves the client on once the authority has accepted a sign-in.
        /// </summary>
        /// <remarks>
        /// <b>The defect this closes.</b> <see cref="ClientFlowDriver"/> advances from its
        /// <c>Update</c>, but only when <c>RefreshIfChanged</c> reports a revision it has not
        /// seen -- and <c>SubmitLogin</c> refreshes the controller itself before returning,
        /// so the one transition that matters was always already consumed by the time the
        /// driver looked. A correct sign-in therefore left the player on the login screen
        /// with no error and nothing happening.
        ///
        /// <see cref="LoginScreen.SignedIn"/> exists for exactly this and had no subscriber.
        /// The driver is asked to evaluate; it decides where to go, from the session state,
        /// as it always did. Nothing here names a scene or a screen.
        /// </remarks>
        private void Advance()
        {
            foreach (ClientFlowDriver driver in
                FindObjectsByType<ClientFlowDriver>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None))
            {
                driver.Evaluate();
            }
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

                BindAdvance(screen);
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

            // The result is read rather than dropped. StartConnection only reports whether
            // the attempt began -- whether it arrives is answered later, on the state
            // callback the bootstrap now raises -- but an attempt that will not even start
            // is worth saying out loud rather than leaving as an empty world.
            if (!World.Connect())
            {
                Debug.LogError("[client] could not start a connection to the world server at "
                    + _worldAddress + ":" + _worldPort, this);
            }
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
        /// What this build reports about itself.
        /// </summary>
        /// <remarks>Built from the serialized fields rather than stored, so the value the
        /// session controller was bound with and the value a screen puts on a login request
        /// cannot drift apart.</remarks>
        private VersionSet ReportedVersions => new VersionSet(Parse(_clientVersion),
            Parse(_protocolVersion), Parse(_contentVersion));

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

            // The one thing that reads a click. Composed after combat and loot because it
            // drives both of them, and never duplicated: a second one would mean two
            // opinions about where the character is walking.
            Pointer = host.GetComponent<WorldPointerInput>();

            if (Pointer == null) Pointer = host.AddComponent<WorldPointerInput>();

            Pointer.Compose(NetworkManager, camera == null ? null : camera.Camera, Combat,
                Loot);

            // Orbiting now needs the right button held. Without this the camera would spin
            // whenever the player moved the mouse to point at something, which is exactly
            // the gesture the left button now uses.
            if (camera != null && camera.Rig != null)
            {
                var look = FindAnyObjectByType<Prototype.ProtoPlayerInput>(
                    FindObjectsInactive.Include);

                if (look != null)
                {
                    look.RequireHoldToLook = true;

                    camera.Rig.SetInput(look);
                }
            }

            _hud = hud;

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            var monsters = host.GetComponent<DevelopmentMonsterVisualizer>();

            if (monsters == null)
            {
                monsters = host.AddComponent<DevelopmentMonsterVisualizer>();
            }

            monsters.Compose(NetworkManager);

            var piles = host.GetComponent<DevelopmentLootVisualizer>();

            if (piles == null) piles = host.AddComponent<DevelopmentLootVisualizer>();

            piles.Compose(Loot);

            // Nothing in this project draws a floor yet, and a click needs something to
            // land on. Composed only when the world really has no ground of its own.
            var ground = host.GetComponent<DevelopmentGroundPlane>();

            if (ground == null) ground = host.AddComponent<DevelopmentGroundPlane>();

            ground.Compose();
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
