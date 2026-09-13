using ChibiFantasy.Backend;
using ChibiFantasy.Client.UI;
using ChibiFantasy.Client.World;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using FishNet.Connection;
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

        [Tooltip("Translations. Left empty, every screen reads the English compiled in.")]
        [SerializeField] private ChibiFantasy.UI.LocalizationCatalogue _localization;

        [Tooltip("Approved monster models, keyed by definition id. A monster with no entry "
            + "falls back to the development placeholder.")]
        [SerializeField] private MonsterVisualCatalogue _monsterVisuals;

        [Tooltip("Which effect and sound answer which blow. Every hook is optional; with "
            + "nothing assigned combat still shows numbers, flashes and a built-in spark.")]
        [SerializeField] private ChibiFantasy.Client.Combat.CombatPresentationConfig _combatPresentation;

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

        /// <summary>
        /// The language every screen in this client reads from.
        /// </summary>
        /// <remarks>
        /// <b>One, for the whole client.</b> Composed here and handed down, exactly as the
        /// session and the content catalogue are -- not reached for by the screens. A second
        /// source would be a second language, and the two would disagree the moment somebody
        /// changed one.
        ///
        /// Never null once composition has run: a client with no translation files gets a
        /// service holding only the English compiled into <c>UiStrings</c>, which is a
        /// complete game.
        /// </remarks>
        public ChibiFantasy.UI.LocalizationService Language { get; private set; }

        private WorldHudScreen _hud;
        private SessionDirectory _sessions;
        private System.IDisposable _transportLifetime;
        private DefinitionRegistry<ItemDefinition> _items;
        private DefinitionRegistry<MapDefinition> _maps;
        private DefinitionRegistry<SpawnPointDefinition> _spawns;
        private DefinitionRegistry<NPCDefinition> _npcDefinitions;
        private DefinitionRegistry<QuestDefinition> _questDefinitions;
        private DefinitionRegistry<MonsterDefinition> _monsterDefinitions;

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

        /// <summary>
        /// Builds the one language service and puts the client into the player's language.
        /// </summary>
        /// <remarks>
        /// <b>Before anything draws.</b> Called first in composition, so the sign-in screen
        /// -- the first thing a player sees and the one they have to read before they can
        /// identify themselves -- is already in their language rather than flashing English
        /// and correcting itself.
        ///
        /// <b>The font comes with the language.</b> Thai needs a face TextMeshPro does not
        /// ship, and asking for it here rather than inside the picker means a player whose
        /// stored preference is Thai gets readable letters on the very first frame.
        /// </remarks>
        private void ComposeLanguage()
        {
            Language = _localization != null
                ? _localization.Build()
                : BuildEnglishOnlyLanguage();

            // Checked, not installed. The Thai face is a project asset listed in
            // TextMeshPro's settings; runtime code that wrote to shared font assets is what
            // wiped the Latin font's character table and froze the editor.
            LocalizationFonts.Verify();

            UseLanguage(LanguagePreference.Current, remember: false);

            Language.Changed -= OnLanguageChanged;
            Language.Changed += OnLanguageChanged;
        }

        /// <summary>Everything <c>UiStrings</c> ships, and nothing else.</summary>
        /// <remarks>What a build with no translation asset gets. Not an error: English is a
        /// complete game, and a missing content file must not be able to produce a client
        /// with no words in it.</remarks>
        private static ChibiFantasy.UI.LocalizationService BuildEnglishOnlyLanguage()
        {
            var service = new ChibiFantasy.UI.LocalizationService();

            service.Load(ChibiFantasy.UI.GameLanguage.English, ChibiFantasy.UI.UiStrings.English);

            return service;
        }

        /// <summary>
        /// Switches the whole client to a language, and optionally remembers the choice.
        /// </summary>
        /// <remarks>Remembering is the caller's decision because two things call this: the
        /// launch path, which is applying a preference rather than making one, and the
        /// picker, where the player really did choose.</remarks>
        public void UseLanguage(ChibiFantasy.UI.GameLanguage language, bool remember = true)
        {
            if (Language == null) return;

            // Nothing to install: switching language cannot change which fonts exist. A
            // missing face is reported once at composition and is a content fix.

            Language.Use(language);

            if (remember) LanguagePreference.Remember(language);

            // Use() only raises when the language actually changed, and applying a stored
            // preference of English on a client that is already English changes nothing --
            // so the screens are told directly rather than through the event.
            OnLanguageChanged();
        }

        /// <summary>
        /// Tells every screen currently in existence to rewrite itself.
        /// </summary>
        /// <remarks>
        /// <b>Found rather than remembered.</b> Screens come and go with scenes, and a list
        /// held here would be a list of destroyed objects one scene later. This runs once per
        /// language change -- an action a player takes perhaps twice in the life of an
        /// install -- so walking the scene is the cheap option, not the expensive one.
        /// </remarks>
        private void OnLanguageChanged()
        {
            foreach (SessionScreenBase screen in
                FindObjectsByType<SessionScreenBase>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None))
            {
                screen.Text = Language;
            }

            if (_hud != null) _hud.Text = Language;

            if (QuestList != null)
            {
                QuestList.Text = Language;

                if (QuestList.IsVisible) QuestList.Show(QuestList.Tab);
            }

            if (NpcDialogue != null) NpcDialogue.Text = Language;

            // The world's controllers already push their source down into the views they
            // own, so handing each of them the language reaches the tooltips, the tracker,
            // the map name and the loot pickups without naming any of those here.
            foreach (WorldUiController world in
                FindObjectsByType<WorldUiController>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None))
            {
                world.Text = Language;
            }

            foreach (QuestUiController quests in
                FindObjectsByType<QuestUiController>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None))
            {
                quests.Text = Language;
            }

            foreach (InventoryUiController bag in
                FindObjectsByType<InventoryUiController>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None))
            {
                bag.Text = Language;
            }

            foreach (InventoryScreen screen in
                FindObjectsByType<InventoryScreen>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None))
            {
                screen.Text = Language;
            }
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
        ///
        /// <b>Not while a world holds the character.</b> Releasing the session revokes the
        /// token, and the world server saves the character with that token -- so a client
        /// that released on its way out raced the save and usually won. The world's
        /// disconnect handler saves first and releases the session afterwards, in that
        /// order and for that reason; doing it from here as well threw away everything since
        /// the last periodic save, every single time anybody quit.
        ///
        /// The original defect is still covered: a client that never reached the world has
        /// no server to release its session, so this still does it.
        /// </remarks>
        private void ReleaseSessionOnExit()
        {
            if (_sessionReleased) return;

            _sessionReleased = true;

            if (IsInAWorldThatWillReleaseTheSession()) return;

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
        /// Whether a world server is holding this character and will release the session.
        /// </summary>
        /// <remarks>
        /// The world's disconnect handling saves the character and then hands the session
        /// back, in that order. While a connection to it exists, that is the release that
        /// must happen -- and it must happen after the save, which a release from here would
        /// prevent by revoking the token first.
        ///
        /// Asked of the connection rather than remembered as a flag, because a connection
        /// that has already dropped leaves nobody to save anything and this client should
        /// hand the session back itself.
        /// </remarks>
        private bool IsInAWorldThatWillReleaseTheSession()
        {
            return NetworkManager != null
                && NetworkManager.ClientManager != null
                && NetworkManager.ClientManager.Started;
        }

        /// <summary>
        /// Says so when a player's password would leave this machine unencrypted.
        /// </summary>
        /// <remarks>
        /// <b>Warned here, not refused.</b> The world server refuses the same situation
        /// because a server that will not start is fixed by the person who deployed it. A
        /// client belongs to a player who cannot fix anything and would only be locked out of
        /// a game somebody else misconfigured -- so this is loud in the log the developer
        /// reads, and does not punish the player for it.
        ///
        /// <b>What it is about.</b> The very first request a client makes carries a password.
        /// Over a plaintext connection to anywhere but this machine, so does anyone watching.
        /// </remarks>
        private void WarnIfTheAccountApiIsInTheClear()
        {
            var endpoint = new HttpEndpoint(_apiBaseAddress, _apiTimeoutSeconds);

            if (!endpoint.IsUnencryptedOverNetwork) return;

            Debug.LogError("[client] the account API is at " + endpoint + ", which is not "
                + "encrypted and is not this machine. What a player types to sign in would "
                + "be readable by anything on that network. Use https before this build "
                + "reaches anybody.", this);
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
            WarnIfTheAccountApiIsInTheClear();

            ComposeLanguage();

            var transport = new UnityWebRequestTransport(_apiBaseAddress, _apiTimeoutSeconds);

            _transportLifetime = transport;

            var api = new HttpAccountApi(transport);

            Api = api;

            var authority = new RemoteSessionAuthority(api);

            _sessions = new SessionDirectory();

            Session = gameObject.GetComponent<SessionUiController>();

            if (Session == null) Session = gameObject.AddComponent<SessionUiController>();

            Session.Bind(api, authority, _sessions, ReportedVersions, default);

            Session.Text = Language;

            _items = _content == null
                ? new DefinitionRegistry<ItemDefinition>()
                : _content.BuildItems();

            // The map and spawn registries the environment presenter resolves an
            // authoritative map id to a scene through. Built from the same catalogue as the
            // items, so a client resolves content locally and the wire carries only ids.
            _maps = _content == null
                ? new DefinitionRegistry<MapDefinition>()
                : _content.BuildMaps();

            _spawns = _content == null
                ? new DefinitionRegistry<SpawnPointDefinition>()
                : _content.BuildSpawnPoints();

            // Who is standing in the towns. The client resolves a name and a role tag from
            // this; what any of it is allowed to do is still the server's answer.
            _npcDefinitions = _content == null
                ? new DefinitionRegistry<NPCDefinition>()
                : _content.BuildNpcs();

            // What a quest is, and what its objectives point at. The wire carries a quest
            // id and a handful of counters; everything a player reads is resolved here.
            _questDefinitions = _content == null
                ? new DefinitionRegistry<QuestDefinition>()
                : _content.BuildQuests();

            _monsterDefinitions = _content == null
                ? new DefinitionRegistry<MonsterDefinition>()
                : _content.BuildMonsters();
        }

        // ---- scenes ----------------------------------------------------------------------

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            BindScene(scene);

            // A map scene arrives additively, long after the world was composed, and it is
            // the scene the townspeople are standing in. Adopting here rather than polling
            // means an NPC becomes clickable the moment it exists and never before.
            Npcs?.Adopt(scene);
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

            // Before the binder builds it, so the bag's three fixed captions are written
            // in the right language once rather than in English and then corrected.
            if (bag != null) bag.Text = Language;

            WorldNpcPresenter npcs = ComposeNpcs();

            binder.Compose(NetworkManager, hud, bag, _items, camera, npcs, Journal);

            ComposeEnvironment(binder);

            ComposeInteraction(hud, camera);
        }

        /// <summary>
        /// Gives the townspeople in whatever map is loaded a name and a click.
        /// </summary>
        /// <remarks>
        /// <b>On the network manager's object, like everything else that exists only while a
        /// world does.</b> Leaving the world takes it with them, so a presenter cannot
        /// outlive the connection whose player it was sending requests for.
        ///
        /// <b>It adopts rather than spawns.</b> Harbor Town's five NPCs are placed by hand in
        /// the scene; this finds the markers on them when the map finishes loading. Nothing
        /// here creates a townsperson, which is why re-entering a map cannot leave two.
        ///
        /// <b>Answers open a panel and nothing else.</b> The dialogue view is the service
        /// entry for this gate -- it proves the right NPC resolved to the right role. Each
        /// real service replaces its body later rather than opening a second door.
        /// </remarks>
        private WorldNpcPresenter ComposeNpcs()
        {
            GameObject host = NetworkManager.gameObject;

            WorldNpcPresenter npcs = host.GetComponent<WorldNpcPresenter>();

            if (npcs == null) npcs = host.AddComponent<WorldNpcPresenter>();

            npcs.Bind(_npcDefinitions);

            // The journal first, because the NPC marks read from it. One per world, on the
            // same object as everything else that exists only while a world does.
            Journal = host.GetComponent<QuestJournal>();

            if (Journal == null) Journal = host.AddComponent<QuestJournal>();

            Journal.Bind(_questDefinitions, _items, _monsterDefinitions, _npcDefinitions);

            npcs.UseJournal(Journal);

            NpcDialogue = FindAnyObjectByType<ChibiFantasy.UI.NpcDialogueView>(
                FindObjectsInactive.Include);

            if (NpcDialogue == null) NpcDialogue = BuildNpcDialogue();

            if (NpcDialogue != null)
            {
                // Before the visuals, so the buttons are built with their captions
                // already in the right language rather than written twice.
                NpcDialogue.Text = Language;

                NpcDialogue.EnsureVisuals();

                npcs.Authorised -= OnNpcAuthorised;
                npcs.Authorised += OnNpcAuthorised;
            }

            // After the dialogue exists, and handed it, so the order cannot be got wrong
            // again. It was: this ran first, subscribed to a null dialogue, and the Accept
            // button raised an event nobody was listening to -- a button that did nothing,
            // with no error to find.
            ComposeQuestJournalUi(NpcDialogue);

            Npcs = npcs;

            // Whatever is already loaded, plus whatever arrives later. A map scene finishes
            // loading after this runs on the very first entry and before it on a rejoin, so
            // both orders have to work.
            AdoptNpcsInLoadedScenes();

            return npcs;
        }

        /// <summary>
        /// Builds the dialogue panel when the scene did not author one.
        /// </summary>
        /// <remarks>
        /// <b>Built rather than authored, for now.</b> This is the service entry a later
        /// gate replaces with a real shop, storage and job screen, and authoring five
        /// panels into a scene that is about to lose them is work thrown away. It is parented
        /// to whatever canvas the world already has, so it inherits the scaling the rest of
        /// the HUD uses rather than introducing a second one.
        ///
        /// Null when there is no canvas at all, which is a world with no HUD -- and a world
        /// with no HUD has nowhere to put a dialogue box either.
        /// </remarks>
        private ChibiFantasy.UI.NpcDialogueView BuildNpcDialogue()
        {
            var canvas = FindAnyObjectByType<UnityEngine.Canvas>(FindObjectsInactive.Include);

            if (canvas == null) return null;

            var host = new GameObject("NPC Dialogue", typeof(RectTransform));

            host.transform.SetParent(canvas.transform, false);

            var rect = (RectTransform)host.transform;

            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 90f);

            // Sized for a quest offer -- greeting, objective and rewards -- rather than for
            // the one-line conversation this panel started as.
            rect.sizeDelta = new Vector2(460f, 280f);

            return host.AddComponent<ChibiFantasy.UI.NpcDialogueView>();
        }

        /// <summary>Takes the NPC markers in every loaded scene under management.</summary>
        public int AdoptNpcsInLoadedScenes()
        {
            if (Npcs == null) return 0;

            var found = 0;

            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                found += Npcs.Adopt(SceneManager.GetSceneAt(i));
            }

            return found;
        }

        /// <summary>The panel an authorised interaction opens. Null when the scene has none.</summary>
        public ChibiFantasy.UI.NpcDialogueView NpcDialogue { get; private set; }

        /// <summary>The townspeople presenter, once the world is composed.</summary>
        public WorldNpcPresenter Npcs { get; private set; }

        /// <summary>Draws monsters that have approved art. Null outside the world.</summary>
        public WorldMonsterPresenter MonsterPresenter { get; private set; }

        /// <summary>
        /// The approved monster models, as the scene wired them.
        /// </summary>
        /// <remarks>
        /// <b>Serialized, never looked up.</b> An earlier version of this fell back to
        /// <c>AssetDatabase.LoadAssetAtPath</c> behind a <c>UNITY_EDITOR</c> guard, so that a
        /// scene nobody had wired still drew its monsters. That is a convenience with a
        /// sting: it works in the editor, ships nothing to a player, and hides the missing
        /// reference until somebody runs a build and finds a world full of capsules.
        ///
        /// Two tests refuse it by name -- content is found through a catalogue a scene
        /// points at, never by a runtime path scan -- and they were right to.
        /// </remarks>
        public MonsterVisualCatalogue MonsterVisuals => _monsterVisuals;

        /// <summary>What blows are drawn with in the world. Null until the world is up.</summary>
        public CombatFeedback CombatFeedbackService { get; private set; }

        /// <summary>This player's quest log and what they could take.</summary>
        public QuestJournal Journal { get; private set; }

        /// <summary>The Ctrl+Q panel. Null when the world has no canvas.</summary>
        public ChibiFantasy.UI.QuestListView QuestList { get; private set; }

        /// <summary>What reads Ctrl+Q.</summary>
        public WorldQuestInput QuestInput { get; private set; }

        private void OnNpcAuthorised(ChibiFantasy.Network.NpcInteractionSnapshot answer)
        {
            if (NpcDialogue == null || Npcs == null) return;

            var id = new DefinitionId(answer.NpcId ?? string.Empty);

            NPCDefinition npc = null;

            if (_npcDefinitions != null) _npcDefinitions.TryGet(id, out npc);

            string name = npc == null ? id.ToString() : WorldNpcPresenter.FallbackName(npc);

            var role = (NpcRole)answer.Role;

            // A guide with something to hand out opens with the offer attached, which is the
            // one place a quest can actually be taken. Everything else is the plain
            // conversation Phase 19B.2 already opened.
            // Handing back comes first: a player walking up to a guide with a finished
            // quest came back to be paid, not to be offered another one.
            DefinitionId finished = role == NpcRole.Quest ? FirstFinishedQuestOf(npc) : default;

            if (finished.IsValid && Journal != null)
            {
                NpcDialogue.ShowQuestTurnIn(id, name,
                    WorldViewAdapter.BuildQuest(Journal.State, finished, Journal.ViewContext));

                return;
            }

            DefinitionId offer = role == NpcRole.Quest ? FirstTakeableQuestOf(npc) : default;

            if (offer.IsValid && Journal != null)
            {
                // The offer view, not the log view: a repeatable quest already run once
                // would otherwise be offered showing last run's finished counters.
                NpcDialogue.ShowQuestOffer(id, name,
                    WorldViewAdapter.BuildQuestOffer(offer, Journal.ViewContext));

                return;
            }

            NpcDialogue.Show(id, role, name);
        }

        /// <summary>
        /// The first quest this NPC offers that the player could take right now.
        /// </summary>
        /// <remarks>Asked of the journal, which asks <c>QuestService</c> -- so the offer a
        /// guide makes and the answer the server gives come from one set of rules. Invalid
        /// when the guide has nothing for this player, which is the ordinary state of a
        /// guide whose quest is already under way.</remarks>
        /// <summary>
        /// The first quest this NPC gave that the player has finished.
        /// </summary>
        /// <remarks>Read from the journal's own log rather than decided here, so the mark
        /// above their head and the conversation they open come from one answer. The server
        /// checks the claim again when the request arrives.</remarks>
        private DefinitionId FirstFinishedQuestOf(NPCDefinition npc)
        {
            if (npc == null || Journal == null) return default;

            DefinitionId[] offered = npc.Quests;

            for (var i = 0; i < offered.Length; i++)
            {
                if (Journal.State.StatusOf(offered[i]) == QuestStatus.ReadyToComplete)
                {
                    return offered[i];
                }
            }

            return default;
        }

        private DefinitionId FirstTakeableQuestOf(NPCDefinition npc)
        {
            if (npc == null || Journal == null) return default;

            if (Journal.MarkerFor(npc) != QuestMarker.Available) return default;

            DefinitionId[] offered = npc.Quests;

            for (var i = 0; i < offered.Length; i++)
            {
                for (var q = 0; q < Journal.Available.Count; q++)
                {
                    if (Journal.Available[q].QuestId == offered[i]) return offered[i];
                }
            }

            return default;
        }

        /// <summary>
        /// Builds the journal panel and the key that opens it.
        /// </summary>
        /// <remarks>
        /// <b>Viewing is free; taking is not.</b> The panel lists what is on offer wherever
        /// the player is standing, and its Accept button is never live there -- taking a
        /// quest means standing in front of the giver, which is the same question the NPC
        /// dialogue answers and the same one the server enforces. Ctrl+Q is therefore a way
        /// to read, never a way to collect every quest in the world from a bench.
        /// </remarks>
        private void ComposeQuestJournalUi(ChibiFantasy.UI.NpcDialogueView dialogue)
        {
            GameObject host = NetworkManager.gameObject;

            QuestInput = host.GetComponent<WorldQuestInput>();

            if (QuestInput == null) QuestInput = host.AddComponent<WorldQuestInput>();

            QuestInput.Compose();

            QuestList = FindAnyObjectByType<ChibiFantasy.UI.QuestListView>(
                FindObjectsInactive.Include);

            if (QuestList == null) QuestList = BuildQuestList();

            if (QuestList == null) return;

            QuestList.Text = Language;

            QuestList.EnsureVisuals();

            QuestList.GiverName = quest =>
            {
                DefinitionId giver = Journal == null ? default : Journal.GiverOf(quest);

                if (!giver.IsValid) return null;

                return _npcDefinitions != null
                    && _npcDefinitions.TryGet(giver, out NPCDefinition who)
                        ? WorldNpcPresenter.FallbackName(who)
                        : giver.ToString();
            };

            // The journal is a window, not a counter.
            QuestList.CanAcceptHere = _ => false;

            QuestInput.Toggled -= ToggleQuestList;
            QuestInput.Toggled += ToggleQuestList;

            if (Journal != null)
            {
                Journal.Changed -= RefreshQuestList;
                Journal.Changed += RefreshQuestList;
            }

            // Taken from the argument rather than the field: a caller that has not built
            // the dialogue yet cannot reach this line at all.
            if (dialogue != null)
            {
                dialogue.QuestAccepted -= OnQuestAccepted;
                dialogue.QuestAccepted += OnQuestAccepted;

                dialogue.QuestTurnedIn -= OnQuestTurnedIn;
                dialogue.QuestTurnedIn += OnQuestTurnedIn;
            }

            RefreshQuestList();
        }

        private void ToggleQuestList()
        {
            if (QuestList == null) return;

            RefreshQuestList();

            QuestList.Toggle();
        }

        private void RefreshQuestList()
        {
            if (QuestList == null || Journal == null) return;

            QuestList.Bind(Journal.Available, Journal.Active, Journal.Completed);
        }

        /// <summary>Hands a finished quest back to the guide and is paid.</summary>
        /// <remarks>The reward is the server's to grant: this asks, and the next quest log
        /// is the answer.</remarks>
        private void OnQuestTurnedIn(DefinitionId quest)
        {
            if (Journal == null || NpcDialogue == null) return;

            Journal.RequestTurnIn(quest, NpcDialogue.Npc);

            NpcDialogue.Close();
        }

        /// <summary>Takes a quest from the guide the player is standing in front of.</summary>
        private void OnQuestAccepted(DefinitionId quest)
        {
            if (Journal == null || NpcDialogue == null) return;

            Journal.RequestAccept(quest, NpcDialogue.Npc);

            // Closed straight away: the answer is the next quest log, and a panel left open
            // on a stale offer is the one that looks like the click did nothing.
            NpcDialogue.Close();
        }

        /// <summary>Builds the journal panel when the scene did not author one.</summary>
        private ChibiFantasy.UI.QuestListView BuildQuestList()
        {
            var canvas = FindAnyObjectByType<UnityEngine.Canvas>(FindObjectsInactive.Include);

            if (canvas == null) return null;

            var host = new GameObject("Quest List", typeof(RectTransform));

            host.transform.SetParent(canvas.transform, false);

            var rect = (RectTransform)host.transform;

            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(640f, 380f);

            return host.AddComponent<ChibiFantasy.UI.QuestListView>();
        }

        /// <summary>
        /// Makes the additive environment follow the map the server has the owner on.
        /// </summary>
        /// <remarks>
        /// <b>One loader, one presenter, on the world connection.</b> Both live on the
        /// network manager's object, beside the other things that exist only while a world
        /// does, so leaving the world takes them -- and the environment they brought up --
        /// with it. There is exactly one of each: the presenter drives the single
        /// <see cref="MapSceneLoader"/> and nothing else loads a map scene.
        ///
        /// <b>The authority is read, never chosen.</b> The presenter is handed a delegate
        /// that returns the owned character's replicated map, so the environment it shows is
        /// whatever the server put the player on. When the binder holds no character yet --
        /// before the owner spawns, or between a disconnect and a reconnect -- the delegate
        /// returns <see cref="DefinitionId.None"/> and the presenter waits.
        /// </remarks>
        private void ComposeEnvironment(WorldPresentationBinder binder)
        {
            GameObject host = NetworkManager.gameObject;

            MapSceneLoader loader = host.GetComponent<MapSceneLoader>();

            if (loader == null) loader = host.AddComponent<MapSceneLoader>();

            WorldEnvironmentPresenter presenter = host.GetComponent<WorldEnvironmentPresenter>();

            if (presenter == null) presenter = host.AddComponent<WorldEnvironmentPresenter>();

            presenter.Compose(loader, _maps, _spawns,
                () => binder != null && binder.Bound != null
                    ? binder.Bound.Map
                    : DefinitionId.None);

            // GameWorld's flat placeholder floor. Found by name because it is authored scene
            // content with no script of its own; a test pins the name to the scene file.
            presenter.UseFallbackGround(GameObject.Find(FallbackGroundName));

            // GameWorld's own sun, for the same reason as its own floor: it is there so a
            // world with no environment yet is not black, and it has to step aside when one
            // arrives. Left on, every environment was lit by two suns, and the second was
            // one the day and night cycle could not reach -- so arriving in the world looked
            // like daylight until the environment finished loading, whatever hour it was.
            presenter.UseFallbackLight(GameObject.Find(FallbackLightName));

            // And the question it waits on: has the world said what hour it is? Until it
            // has, bringing the environment up would show its authored daylight and then
            // correct it, which is exactly the flash this removes.
            presenter.UseHourKnown(() => World != null && World.LastTime.SecondsPerDay > 0f);
        }

        /// <summary>The name of GameWorld's placeholder floor, as authored in the scene.</summary>
        public const string FallbackGroundName = "World Ground";

        /// <summary>The name of GameWorld's stand-in sun, as authored in the scene.</summary>
        public const string FallbackLightName = "Directional Light";

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

            // Clicks route around obstacles over the map's baked ground -- the same data
            // the server walks the character on, so the route and the authority agree.
            Pointer.UsePathfinding(_maps);

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

            if (_hud != null) _hud.Text = Language;

            // The approved monster art. Production, not development: a monster with a
            // model in the catalogue is drawn by this, at any build setting.
            MonsterPresenter = host.GetComponent<WorldMonsterPresenter>();

            if (MonsterPresenter == null)
            {
                MonsterPresenter = host.AddComponent<WorldMonsterPresenter>();
            }

            // The selection is read through a function rather than handed the input object,
            // so the presentation can ask what the player has targeted and has no way to
            // tell it anything.
            MonsterPresenter.Compose(NetworkManager, MonsterVisuals, _monsterDefinitions,
                Language, () => Combat == null ? null : Combat.Target);

            // The numbers, sparks, flashes and sounds every blow is drawn with. One per
            // world, composed here so that both the monster presenter above and the
            // character presenters the network instantiates draw through the same pools.
            CombatFeedbackService = host.GetComponent<CombatFeedback>();

            if (CombatFeedbackService == null)
            {
                CombatFeedbackService = host.AddComponent<CombatFeedback>();
            }

            CombatFeedbackService.Compose(_combatPresentation);

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            var monsters = host.GetComponent<DevelopmentMonsterVisualizer>();

            if (monsters == null)
            {
                monsters = host.AddComponent<DevelopmentMonsterVisualizer>();
            }

            monsters.Compose(NetworkManager);

            // So the placeholder skips anything the presenter already draws properly.
            monsters.Presenter = MonsterPresenter;

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

        /// <summary>The character object this connection owns, or null before entering.</summary>
        private ChibiFantasy.Network.CharacterNetworkEntity OwnedCharacter()
        {
            if (NetworkManager == null || NetworkManager.ClientManager == null) return null;

            NetworkConnection connection = NetworkManager.ClientManager.Connection;

            if (connection == null || connection.Objects == null) return null;

            foreach (NetworkObject owned in connection.Objects)
            {
                if (owned == null) continue;

                if (owned.TryGetComponent(
                    out ChibiFantasy.Network.CharacterNetworkEntity character))
                {
                    return character;
                }
            }

            return null;
        }

        /// <summary>Keeps the target readout showing whatever the server says about it.</summary>
        private void Update()
        {
            if (_hud == null) return;

            // Whether the player is down is read from their own replicated health, so the
            // notice appears and disappears because the server said so and for no other
            // reason. The screen never decides it, and hiding it revives nobody.
            ChibiFantasy.Network.CharacterNetworkEntity me = OwnedCharacter();

            _hud.ShowDefeated(me != null && me.MaxHealth > 0 && !me.IsAlive);

            if (Combat == null) return;

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
