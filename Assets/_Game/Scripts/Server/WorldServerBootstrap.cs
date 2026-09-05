using System.Collections.Generic;
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

        [Header("World content")]
        [Tooltip("The world's authored content. Absent means session-only, no simulation.")]
        [SerializeField] private WorldContentCatalogue _content;

        [Tooltip("How far a basic attack reaches, in metres.")]
        [SerializeField] private float _meleeReachMetres = 2.5f;

        [Tooltip("The team monsters fight on. Players are team one.")]
        [SerializeField] private int _monsterTeam = 2;

        [Tooltip("The networked character object the world spawns per player.")]
        [SerializeField] private FishNet.Object.NetworkObject _characterPrefab;

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

        /// <summary>
        /// Whether a character may be admitted into a world that can hold them.
        /// </summary>
        /// <remarks>
        /// <b>Listening and ready are different things.</b> A socket is open long before a
        /// world exists, and a player admitted into a world with no content is a player who
        /// is disconnected a moment later for having nowhere to stand. One flag rather than a
        /// state machine, because two states is not a state machine.
        /// </remarks>
        public bool IsWorldReady { get; private set; }

        /// <summary>What content validation refused, if it did. For an operator's log.</summary>
        /// <remarks>Content faults only -- ids, missing formulas, unplaceable spawns. No
        /// address, no credential and no token ever reaches this list.</remarks>
        public IReadOnlyList<string> ContentFaults => _contentFaults;

        /// <summary>Loads monster nests from the backend. Null on a world with no source.</summary>
        public MonsterConfigurationLoader MonsterConfiguration { get; private set; }

        private readonly List<string> _contentFaults = new List<string>();

        /// <summary>Floor under a resolved blow, shared by basic attacks and skills.</summary>
        /// <remarks>One value rather than two literals: whether a hopeless attack chips for
        /// one is a single balance decision, and a spell and a sword should not be able to
        /// disagree about it by accident.</remarks>
        private const int MinimumDamage = 1;

        public ServerId Server => new ServerId(_serverId);

        public ChannelId Channel => new ChannelId(_channelId);

        /// <summary>The team players fight on. Monsters are the other one.</summary>
        private static CombatTeam PlayerTeam => new CombatTeam(1);

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
            VersionRequirement required = default,
            ICharacterStateStore characters = null,
            IMonsterSpawnConfigurationSource spawnConfiguration = null)
        {
            Registry = new WorldConnectionRegistry();

            if (authority == null)
            {
                // Composed on the transport's own side of the line: this file names no
                // transport, no URL scheme and no HTTP type. One connection serves the
                // session authority, the character store and the monster configuration.
                authority = BackendAuthority.WorldServicesOverHttp(_apiBaseAddress,
                    _apiTimeoutSeconds, out ICharacterStateStore store,
                    out IMonsterSpawnConfigurationSource nests, out _authorityLifetime);

                characters = characters ?? store;
                spawnConfiguration = spawnConfiguration ?? nests;
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

            ComposeWorld(characters, spawnConfiguration);
        }

        /// <summary>
        /// Builds the world this process simulates, from authored content.
        /// </summary>
        /// <remarks>
        /// <b>The order is the point.</b> Content is validated before a registry is built, a
        /// registry before an authority, every authority before the simulation, and the
        /// simulation before <see cref="IsWorldReady"/> is set -- which is what admission
        /// waits on. A player therefore cannot arrive in a world that is still assembling.
        ///
        /// <b>Refusing is a real outcome.</b> Content that does not validate leaves the world
        /// unready and the reasons in <see cref="ContentFaults"/>. The socket may still be
        /// listening -- this process is a session authority too -- but nobody is admitted
        /// into a world that cannot hold them, and nothing invents a fallback definition to
        /// paper over it.
        ///
        /// <b>Every number comes from the catalogue.</b> No definition id appears below;
        /// which stat is the health ceiling and how fast a character walks are content.
        /// </remarks>
        private void ComposeWorld(ICharacterStateStore characters,
            IMonsterSpawnConfigurationSource spawnConfiguration)
        {
            IsWorldReady = false;
            _contentFaults.Clear();

            // Nothing of the previous world survives a recomposition. Leaving a simulation
            // standing behind a world that was refused is worse than having none: it would
            // keep ticking, keep saving characters, and answer to nobody.
            Simulation = null;
            Characters = null;
            MonsterConfiguration = null;
            Loot = null;
            Rewards = null;
            LootAuthority = null;

            // A session-only process: it admits, places and releases, and simulates nothing.
            // Legitimate, and not a fault.
            if (_content == null) return;

            if (!_content.Validate(_contentFaults))
            {
                Debug.LogError("[world] content refused; the world will not start. "
                    + string.Join("; ", _contentFaults));

                return;
            }

            if (characters == null)
            {
                Refuse("no character store: a world cannot load anybody");

                return;
            }

            if (_characterPrefab == null)
            {
                Refuse("no character prefab: an admitted player would have no object");

                return;
            }

            DefinitionRegistry<StatDefinition> stats = _content.BuildStats();
            DefinitionRegistry<MapDefinition> maps = _content.BuildMaps();
            DefinitionRegistry<ItemDefinition> items = _content.BuildItems();
            DefinitionRegistry<StatusEffectDefinition> effects = _content.BuildStatusEffects();
            DefinitionRegistry<SkillDefinition> skills = _content.BuildSkills();

            _spawnPoints = _content.BuildSpawnPoints();

            DefinitionRegistry<DevilFruitDefinition> fruits = _content.BuildDevilFruits();

            var players = new WorldCharacterRegistry(characters, _spawnPoints, items,
                devilFruits: fruits);

            var monsters = new MonsterWorldRuntime(players, _content.BuildMonsters(),
                _content.MaxHealthStat, new CombatTeam(_monsterTeam));

            var movement = new CharacterMovementAuthority(players, _ => true, maps,
                _content.WalkMetresPerSecond);

            var commands = new CombatCommandAuthority(players, _ => true, monsters);

            // What resists a skill, named from content. Physical and magic differ here by
            // which stat answers them and nowhere else -- the arithmetic below this line is
            // the same subtraction a basic attack uses, and a skill that authors no damage
            // type is resisted by armour exactly as it always was.
            var skillRules = new SkillExecutionRules(_content.DefenceStat,
                _content.MagicDefenceStat, MinimumDamage);

            // What a defeated monster leaves on the ground, and who is allowed to take it.
            // Both existed since Phase 17.15 and the shipped world never composed them, so
            // until now nothing production could drop anything at all.
            var loot = new MonsterLootRegistry(players, items);

            DefinitionRegistry<CharacterProgressionDefinition> progressions =
                _content.BuildProgressions();

            CharacterProgressionDefinition curve = progressions.All.Count > 0
                ? progressions.All[0]
                : null;

            var rewards = new MonsterRewardAuthority(monsters, players, curve, loot, items,
                _content.BuildDropTables(), _rolls ?? new SystemRandomSource(),
                _quantities as IRandomRangeSource ?? new SystemRandomSource(),
                _lootLifetimeSeconds, _lootPersonalWindowSeconds);

            var combat = new ServerCombatPipeline(commands, monsters, rewards,
                BasicAttackRules.Melee(_content.AttackStat, _content.DefenceStat,
                    MinimumDamage, _meleeReachMetres),
                default, skills, skillRules, effects, fruits);

            // A command handled between ticks settles the world immediately, so a second
            // command in the same frame is never resolved against state the first one
            // invalidated. The lambda closes over the simulation assembled just below.
            var requests = new CharacterCombatRequestHandler(combat,
                () => Simulation?.Settle());

            var replication = new CharacterReplicationService(_networkManager, players,
                _characterPrefab, requests, movement);

            var status = new CharacterStatusAuthority(players, effects, replication);

            replication.UseStatus(status);

            replication.UseInventory(new CharacterInventoryAuthority(players, _ => true,
                items, replication, fruits, effects, skills, maps, _spawnPoints));

            // How a player asks for what a boss left behind. The registry above already
            // decides every rule; this is the identity a client can name and the distance
            // they must walk to name it.
            LootAuthority = new CharacterLootAuthority(players, loot, replication,
                _lootReachMetres);

            replication.UseLoot(LootAuthority);

            var stat = new CharacterStatAuthority(players, _content.Formulas, stats, effects,
                new EquipmentModifierResolver.Context(items),
                _content.MaxHealthStat, _content.MaxManaStat, fruits, skills);

            Simulation = new WorldSimulation(players, replication, status, stat, movement,
                combat, monsters, loot);

            Loot = loot;
            Rewards = rewards;

            Characters = players;
            Replication = replication;

            if (spawnConfiguration != null)
            {
                // Monster nests are runtime configuration and live in the database; the
                // monsters themselves are authored content. That split is preserved.
                MonsterConfiguration = new MonsterConfigurationLoader(spawnConfiguration,
                    monsters, maps);
            }

            IsWorldReady = true;
        }

        /// <summary>Records why the world will not start, and says so once.</summary>
        private void Refuse(string fault)
        {
            _contentFaults.Add(fault);

            Debug.LogError("[world] " + fault);
        }

        /// <summary>The live characters this world holds, or null when unready.</summary>
        public WorldCharacterRegistry Characters { get; private set; }

        /// <summary>What is lying on the ground in this world. Null when unready.</summary>
        public MonsterLootRegistry Loot { get; private set; }

        /// <summary>Who has been paid for which defeat. Null when unready.</summary>
        public MonsterRewardAuthority Rewards { get; private set; }

        /// <summary>Where a pickup request lands. Null when unready.</summary>
        public CharacterLootAuthority LootAuthority { get; private set; }

        [Tooltip("How close a character must be to take a pile, in metres.")]
        [SerializeField] private float _lootReachMetres = 4f;

        /// <summary>
        /// The roll a rare drop is decided by.
        /// </summary>
        /// <remarks>
        /// <b>A seam, not a switch.</b> A test replaces the source of randomness so a
        /// one-in-ten-million drop can be observed; it never replaces the one-in-ten-million.
        /// The authored chance stays exactly what content says it is, which is the only
        /// reason a test proving the rare path can be believed.
        /// </remarks>
        public void UseRandom(IRandomResultSource rolls, IRandomRangeSource quantities = null)
        {
            _rolls = rolls;
            _quantities = quantities;
        }

        private IRandomResultSource _rolls;
        private IRandomRangeSource _quantities;

        [Tooltip("How long a dropped pile lasts. Zero means it never expires on its own.")]
        [SerializeField] private float _lootLifetimeSeconds = 300f;

        [Tooltip("How long the killer alone may take their drops. Zero disables the window.")]
        [SerializeField] private float _lootPersonalWindowSeconds = 30f;

        /// <summary>What spawns and publishes character objects, or null when unready.</summary>
        public CharacterReplicationService Replication { get; private set; }

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

            Announce();

            return IsListening;
        }

        /// <summary>
        /// Says what this process is, once, when it starts listening.
        /// </summary>
        /// <remarks>
        /// <b>An operator reading a log needs to know four things:</b> which scene the
        /// process actually opened, whether the content was accepted, whether a world
        /// exists, and whether anything is listening. Without this line all four can only be
        /// guessed at from the absence of errors, and "no errors" is also what a server that
        /// booted the wrong scene entirely looks like.
        ///
        /// <b>Nothing sensitive is in it.</b> Scene, port, readiness and content counts --
        /// no address, no credential, no token, and content faults are ids, which is what
        /// the person fixing them needs.
        /// </remarks>
        private void Announce()
        {
            string where = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

            string state = _content == null
                ? "session-only (no world content)"
                : IsWorldReady
                    ? "world ready"
                    : "world NOT ready: " + string.Join("; ", _contentFaults);

            Debug.Log("[world] scene=" + where
                + " listening=" + IsListening
                + " port=" + _port
                + " " + state
                + " characters=" + (Characters == null ? 0 : Characters.Count));
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
            // A world that is still assembling, or whose content was refused, has nowhere to
            // put anybody. Better to turn a player away at the door than to admit them into
            // a world that cannot hold them.
            if (_content != null && !IsWorldReady)
            {
                Debug.LogWarning("[world] refusing entry: the world is not ready");

                connection.Disconnect(immediately: false);

                return;
            }

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

            // And into the simulation, which computes their stats before anything is
            // published -- so the first state a client receives is already correct.
            if (Simulation != null)
            {
                WorldSpawnResult admitted = Simulation.Admit(connection.ClientId,
                    outcome.Admission, PlayerTeam);

                if (!admitted.IsSpawned)
                {
                    Debug.LogWarning("[world] could not place " + outcome.Admission.Character
                        + ": " + admitted.Reason);
                }
            }
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

            // The world first, so the character is saved and forgotten before the session
            // that owns it is handed back.
            Simulation?.Release(connection.ClientId);

            Coordinator?.Leave(connection.ClientId);
        }
    }
}
