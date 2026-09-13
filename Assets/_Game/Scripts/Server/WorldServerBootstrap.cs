using System;
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

        [Tooltip("Let clients ask for a sky. Has no effect on a release build, whatever it "
            + "is set to -- see RegisterWeatherCommands.")]
        [SerializeField] private bool _allowWeatherCommands = true;

        [Tooltip("How often the world repeats what time it is, in seconds. Zero never repeats "
            + "it, which leaves clients to drift.")]
        [SerializeField] private float _timeBroadcastSeconds = 60f;

        [Tooltip("Start listening as soon as this component wakes.")]
        [SerializeField] private bool _startOnAwake = true;

        [Header("World content")]
        [Tooltip("The world's authored content. Absent means session-only, no simulation.")]
        [SerializeField] private WorldContentCatalogue _content;

        [Tooltip("How far a basic attack reaches, centre to centre, in metres. An unarmed "
            + "fist reaches about a quarter of a metre; a metre allows for the target's own "
            + "body, a lunging slime and a client that stopped a step short. It used to be "
            + "2.5, which let a character punch from well outside arm's length.")]
        [SerializeField] private float _meleeReachMetres = 1.0f;

        [Tooltip("The team monsters fight on. Players are team one.")]
        [SerializeField] private int _monsterTeam = 2;

        [Tooltip("The networked character object the world spawns per player.")]
        [SerializeField] private FishNet.Object.NetworkObject _characterPrefab;

        /// <summary>
        /// The networked object a monster is drawn from.
        /// </summary>
        /// <remarks>
        /// <b>Without this, monsters exist and nobody is told.</b> They spawn, chase, fight,
        /// die and pay out on the server exactly as they always did -- and no client ever
        /// receives one, because nothing was replicating them. That was the shipped state
        /// until 18.18B measured a client's world and found it empty of everything except
        /// other players.
        ///
        /// Optional in the same sense the character prefab is not: a world with no monster
        /// prefab replicates no monsters and still runs, which is the right behaviour for a
        /// scene that has not been wired rather than a crash. The shipped scene wires it,
        /// and a test says so.
        /// </remarks>
        [Tooltip("The networked monster object the world spawns per living monster.")]
        [SerializeField] private FishNet.Object.NetworkObject _monsterPrefab;

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

        /// <summary>What stands a fallen character back up in town.</summary>
        public CharacterReviveAuthority ReviveAuthority { get; private set; }

        /// <summary>What makes a monster's swing actually land on somebody.</summary>
        public MonsterAttackAuthority MonsterAttacks { get; private set; }

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

            ApplyLaunchOptions();

            if (!AuthorityAddressIsAcceptable()) return;

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
            IMonsterSpawnConfigurationSource spawnConfiguration = null,
            IPartyStateStore parties = null,
            IMonsterRewardOutbox rewardOutbox = null,
            IWorldClockStore clockStore = null,
            IWorldReclaim reclaim = null)
        {
            Registry = new WorldConnectionRegistry();

            if (authority == null)
            {
                // Composed on the transport's own side of the line: this file names no
                // transport, no URL scheme and no HTTP type. One connection serves the
                // session authority, the character store and the monster configuration.
                authority = BackendAuthority.WorldServicesOverHttp(_apiBaseAddress,
                    _apiTimeoutSeconds, out ICharacterStateStore store,
                    out IMonsterSpawnConfigurationSource nests, out _authorityLifetime,
                    out IPartyStateStore partyStore, out IMonsterRewardOutbox outbox,
                    out IWorldClockStore clocks, out IWorldReclaim stranded);

                characters = characters ?? store;
                spawnConfiguration = spawnConfiguration ?? nests;
                parties = parties ?? partyStore;
                rewardOutbox = rewardOutbox ?? outbox;
                clockStore = clockStore ?? clocks;
                reclaim = reclaim ?? stranded;
            }

            ClockStore = clockStore;
            Reclaim = reclaim;

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

            ComposeWorld(characters, spawnConfiguration, parties, rewardOutbox);
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
            IMonsterSpawnConfigurationSource spawnConfiguration,
            IPartyStateStore parties,
            IMonsterRewardOutbox rewardOutbox)
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
            NpcAuthority = null;
            QuestAuthority = null;
            Parties = null;
            PartyStore = null;
            RewardOutbox = null;
            Cards = null;
            Pets = null;
            PetAuthority = null;
            MonsterReplication = null;

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

            // The pets this world can resolve, and the effects their buffs may name.
            // Both are content: a pet row naming something absent is refused rather than
            // substituted, which is what the registry needs them for.
            DefinitionRegistry<PetDefinition> pets = _content.BuildPets();

            var players = new WorldCharacterRegistry(characters, _spawnPoints, items,
                devilFruits: fruits, pets: pets, statusEffects: effects);

            Pets = pets;

            // Which pet a character has out, decided by the server. Phase 12's PetService
            // owns every rule; this is the seam a connection reaches it through.
            PetAuthority = new CharacterPetAuthority(players, pets, items, effects);

            // The maps go in, and they are not optional in practice.
            //
            // Without them MonsterWorldRuntime.DefinitionOf returns null for every map, and
            // the code that stands a freshly spawned monster on the ground is skipped in
            // silence -- so every monster sat at the flat y the database row happens to
            // carry. On Harbor Town's terrain that buried a 0.28 m slime up to 0.15 m into
            // the hillside, and nothing anywhere said why.
            var monsters = new MonsterWorldRuntime(players, _content.BuildMonsters(),
                _content.MaxHealthStat, new CombatTeam(_monsterTeam), maps);

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

            // The parties this world is running. Phase 13 decides what a party is; this
            // world just keeps them, so a defeat can ask who was in one.
            Parties = new WorldPartyRegistry(parties);

            PartyStore = parties;

            var rewards = new MonsterRewardAuthority(monsters, players, curve, loot, items,
                _content.BuildDropTables(), _rolls ?? new SystemRandomSource(),
                _quantities as IRandomRangeSource ?? new SystemRandomSource(),
                _lootLifetimeSeconds, _lootPersonalWindowSeconds,
                Parties, _rewardRangeMetres, rewardOutbox, pets, _petExperienceShare,

                // A kill advances a quest. Hung off the one call that claims a defeat, so
                // one dead slime is one point of progress however many times it was hit on
                // the way down.
                //
                // Resolved when it fires rather than captured now: the quest authority is
                // composed a few lines below this, and nothing can be killed before either
                // exists.
                (killer, monster) => QuestAuthority?.ReportKill(killer, monster));

            RewardOutbox = rewardOutbox;

            // The pile registry tells the reward authority what leaves it, so a defeat that
            // is still being finished knows which of its items are already carried.
            loot?.Observe(rewards);

            // Paced from the content's attack-speed stat, so AGI, a weapon's own speed and
            // a haste all reach the interval through the one calculator -- and a client
            // asking faster than its own figure allows is refused as NotReady, never served.
            var combat = new ServerCombatPipeline(commands, monsters, rewards,
                BasicAttackRules.Melee(_content.AttackStat, _content.DefenceStat,
                        MinimumDamage, _meleeReachMetres)
                    .WithAttackSpeed(_content.AttackSpeedStat),
                default, skills, skillRules, effects, fruits);

            // A command handled between ticks settles the world immediately, so a second
            // command in the same frame is never resolved against state the first one
            // invalidated. The lambda closes over the simulation assembled just below.
            // Declared before the handler so the handler can publish through it, and
            // assigned just below. The replication service takes the handler, so one of the
            // two has to be named before it exists; a closure is what unties the knot.
            CharacterReplicationService replication = null;

            var requests = new CharacterCombatRequestHandler(combat,
                () => Simulation?.Settle(),
                (connectionId, facing) => replication?.PublishAttack(connectionId, facing));

            replication = new CharacterReplicationService(_networkManager, players,
                _characterPrefab, requests, movement);

            // The figure every client paces and animates from is the one the pipeline
            // above refuses swings against: same stat, same calculator.
            replication.UseAttackSpeedStat(_content.AttackSpeedStat);

            var status = new CharacterStatusAuthority(players, effects, replication);

            replication.UseStatus(status);

            // Cards join the same resolver every other equipment modifier goes through,
            // and the same inventory authority every other item action goes through.
            // Without the registry a socketed piece would silently grant nothing.
            DefinitionRegistry<CardDefinition> cards = _content.BuildCards();

            Cards = cards;

            replication.UsePets(PetAuthority);

            // Kept, because the world loop also has to publish bags that changed for a
            // reason other than a request -- loot taken off the ground, and arriving at all.
            InventoryAuthority = new CharacterInventoryAuthority(players, _ => true,
                items, replication, fruits, effects, skills, maps, _spawnPoints, cards);

            replication.UseInventory(InventoryAuthority);

            // How a player asks for what a boss left behind. The registry above already
            // decides every rule; this is the identity a client can name and the distance
            // they must walk to name it.
            LootAuthority = new CharacterLootAuthority(players, loot, replication,
                _lootReachMetres);

            replication.UseLoot(LootAuthority);

            // Who a player may talk to, and about what. Every rule was already decided by
            // NpcInteractionService; this gives it the identity of whoever is asking and a
            // way to answer exactly them. The registries come from the same catalogue the
            // rest of the world is built from, so an NPC a client can see is one the server
            // can name -- an NPC only the scene knows about would refuse every interaction.
            NpcAuthority = new CharacterNpcAuthority(players,
                new NpcInteractionService.Context(_content.BuildNpcs(), _spawnPoints,
                    _content.BuildShops(), _content.BuildQuests()),
                replication);

            replication.UseNpcs(NpcAuthority);

            // Taking a quest, handing it in, and every kill that advances one. The rules
            // were already QuestService's; this gives them the asker's identity, the giver
            // they must be standing next to, and somewhere to send the answer.
            QuestAuthority = new CharacterQuestAuthority(players, _content.BuildQuests(),
                new NpcInteractionService.Context(_content.BuildNpcs(), _spawnPoints,
                    _content.BuildShops(), _content.BuildQuests()),
                items, replication, curve);

            // The date the daily quests turn over on, learned from the backend every time a
            // character loads. Without this the world would have to ask its own machine what
            // day it is, and dailies would reset at whatever midnight that machine believes
            // in rather than the one their completion times were stamped in.
            // Gameplay may not read the engine, so the clock is handed its source here --
            // an elapsed duration, which is all it needs to count forward to the midnight
            // the database picked.
            QuestAuthority.Days.ElapsedSeconds = () => Time.realtimeSinceStartupAsDouble;

            players.DayObserved = QuestAuthority.SyncDay;

            replication.UseQuests(QuestAuthority,
                (entity, character) =>
                    entity.ServerPublishQuestLog(QuestAuthority.SnapshotFor(character)));

            // What lets a player who lost a fight get up again. Without it a character
            // reduced to zero health stays there permanently -- current health is persisted,
            // so signing out and back in restores them to exactly the zero they left.
            ReviveAuthority = new CharacterReviveAuthority(players, _spawnPoints,
                _content.ReviveHealthFraction);

            replication.UseRevive(ReviveAuthority);



            var stat = new CharacterStatAuthority(players, _content.Formulas, stats, effects,
                new EquipmentModifierResolver.Context(items, cards: cards),
                _content.MaxHealthStat, _content.MaxManaStat, fruits, skills);

            // What tells clients about monsters. Composed from the same runtime the
            // simulation already ticks, so there is one monster world and one shadow of it.
            MonsterReplication = _monsterPrefab == null
                ? null
                : new MonsterReplicationService(_networkManager, monsters, _monsterPrefab);

            // What turns a monster's decision to swing into damage. Without it the AI still
            // decides, on schedule and correctly, and nothing ever happens to the player it
            // decided to hit -- which is how this world ran until now.
            MonsterAttacks = new MonsterAttackAuthority(monsters, _content.AttackStat,
                _content.DefenceStat, MinimumDamage);

            Simulation = new WorldSimulation(players, replication, status, stat, movement,
                combat, monsters, loot, MonsterReplication, rewards, MonsterAttacks,
                LootAuthority, InventoryAuthority, clock: RestoreClock());

            // The sky tells everybody at once. Subscribed here rather than polled in the
            // tick so a change costs one broadcast at the moment it happens and nothing at
            // all the rest of the time.
            Simulation.Weather.Changed += BroadcastWeather;

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

        /// <summary>
        /// Reads a map's monster nests, once.
        /// </summary>
        /// <remarks>
        /// <b>The defect this closes.</b> <see cref="MonsterConfigurationLoader"/> was
        /// composed by <see cref="ComposeWorld"/> and never asked for anything: nothing in
        /// production called <c>Load</c>. A dedicated server therefore ran a world with no
        /// nests at all -- a player arrived, stood in an empty map, and there was nothing to
        /// fight, no loot to drop and no experience to earn. The in-process tests never saw
        /// it because they register spawn points directly against the registry.
        ///
        /// <b>On arrival, not at boot.</b> A world server does not know which maps it will
        /// serve until somebody is admitted to one, and reading every map's nests up front
        /// would be work for maps nobody is standing on.
        ///
        /// <b>Once per map.</b> Re-reading on every arrival would re-seed nests under the
        /// players already fighting there.
        /// </remarks>
        private void LoadNests(DefinitionId map)
        {
            LoadNestsCore(map);
        }

        /// <summary>
        /// Tells every connected client the sky has turned.
        /// </summary>
        /// <remarks>
        /// <b>To everyone, not per map.</b> The weather is a property of the world in this
        /// build, so a per-map broadcast would draw a distinction the simulation does not yet
        /// make. When maps get their own skies, this is the one place that has to learn it.
        ///
        /// <b>Guarded rather than assumed.</b> The director keeps ticking while a server is
        /// shutting down, and a broadcast into a stopped ServerManager is an exception in a
        /// log that tells nobody anything useful.
        /// </remarks>
        private void BroadcastWeather(WorldWeather weather)
        {
            if (_networkManager == null || !_networkManager.ServerManager.Started) return;

            _networkManager.ServerManager.Broadcast(new WorldWeatherMessage
            {
                Weather = (int)weather,
            });
        }

        /// <summary>
        /// Opens the door a client can ask for weather through, on builds that may have one.
        /// </summary>
        /// <remarks>
        /// <b>Two locks, and only one of them can be picked.</b> A serialized switch says
        /// whether this server wants the door at all, and <see cref="Debug.isDebugBuild"/>
        /// says whether this build is allowed one. The second is decided when the server is
        /// compiled, so no scene edit, configuration file or connecting client can turn it on
        /// in a release build -- which matters, because the message has no sender check beyond
        /// this: anyone who can reach the port could otherwise make it snow on everybody.
        ///
        /// <b>Not registered at all when refused.</b> Registering it and then ignoring the
        /// message would leave a handler on a production server whose only protection is an
        /// if-statement inside it.
        /// </remarks>
        private void RegisterWeatherCommands()
        {
            if (!WeatherCommandsAllowed || _networkManager == null) return;

            _networkManager.ServerManager.RegisterBroadcast<WorldWeatherCommandMessage>(
                OnWeatherCommand);

            Debug.Log("[world] weather commands are ENABLED on this server (development build)");
        }

        /// <summary>Whether this server will take weather requests from clients.</summary>
        private bool WeatherCommandsAllowed => _allowWeatherCommands && Debug.isDebugBuild;

        /// <summary>
        /// A client asked for a sky.
        /// </summary>
        /// <remarks>The change is not answered to the sender: it is handed to the director,
        /// which announces it to everyone through the same event an ordinary turn of the
        /// weather uses. That is what keeps one player's festival from being a private one.
        /// </remarks>
        private void OnWeatherCommand(NetworkConnection connection,
            WorldWeatherCommandMessage message, Channel channel)
        {
            if (Simulation == null) return;

            if (message.Automatic)
            {
                Simulation.Weather.Release();

                Debug.Log("[world] weather released; the sky rolls on its own again");

                return;
            }

            var weather = (WorldWeather)message.Weather;

            if (!System.Enum.IsDefined(typeof(WorldWeather), weather)) return;

            Simulation.Weather.Hold(weather);

            Debug.Log("[world] weather held at " + weather);
        }

        private void LoadNestsCore(DefinitionId map)
        {
            if (MonsterConfiguration == null || !map.IsValid) return;

            if (!_loadedNests.Add(map.Value)) return;

            int nests = MonsterConfiguration.Load(map);

            Debug.Log("[world] monster nests for " + map.Value + ": " + nests
                + (MonsterConfiguration.LastReadSucceeded
                    ? string.Empty
                    : " (the configuration source could not be read)"));
        }

        /// <summary>Maps whose nests have already been read.</summary>
        private readonly HashSet<string> _loadedNests = new HashSet<string>();

        /// <summary>Records why the world will not start, and says so once.</summary>
        private void Refuse(string fault)
        {
            _contentFaults.Add(fault);

            Debug.LogError("[world] " + fault);
        }

        /// <summary>
        /// What replicates monsters to clients, or null when no monster prefab is wired.
        /// </summary>
        public MonsterReplicationService MonsterReplication { get; private set; }

        /// <summary>The live characters this world holds, or null when unready.</summary>
        public WorldCharacterRegistry Characters { get; private set; }

        /// <summary>Who answers for what characters are carrying. Null when unready.</summary>
        public CharacterInventoryAuthority InventoryAuthority { get; private set; }

        /// <summary>What is lying on the ground in this world. Null when unready.</summary>
        public MonsterLootRegistry Loot { get; private set; }

        /// <summary>Who has been paid for which defeat. Null when unready.</summary>
        public MonsterRewardAuthority Rewards { get; private set; }

        /// <summary>Where a pickup request lands. Null when unready.</summary>
        public CharacterLootAuthority LootAuthority { get; private set; }

        /// <summary>Who decides whether a player may talk to an NPC. Null before compose.</summary>
        public CharacterNpcAuthority NpcAuthority { get; private set; }

        /// <summary>Who decides what happens to a quest. Null before compose.</summary>
        public CharacterQuestAuthority QuestAuthority { get; private set; }

        /// <summary>The parties this world is running. Null when unready.</summary>
        public WorldPartyRegistry Parties { get; private set; }

        /// <summary>Where parties are kept between sessions. Null in a world with none.</summary>
        public IPartyStateStore PartyStore { get; private set; }

        /// <summary>Where this world writes a defeat down before it pays it.</summary>
        public IMonsterRewardOutbox RewardOutbox { get; private set; }

        /// <summary>The cards this world can resolve when a socketed piece is worn.</summary>
        public DefinitionRegistry<CardDefinition> Cards { get; private set; }

        /// <summary>The pets this world can resolve.</summary>
        public DefinitionRegistry<PetDefinition> Pets { get; private set; }

        /// <summary>Where a request to put a pet out is decided.</summary>
        public CharacterPetAuthority PetAuthority { get; private set; }

        [Tooltip("How near a party member must be to share a kill, in metres. "
            + "Zero shares with the whole map.")]
        [SerializeField] private float _rewardRangeMetres = 40f;

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
        [SerializeField] private float _lootLifetimeSeconds = 60f;

        [Tooltip("How long the killer alone may take their drops. Zero disables the window.")]
        [SerializeField] private float _lootPersonalWindowSeconds = 30f;

        /// <summary>
        /// The share of a character's own monster experience their active pet earns.
        /// </summary>
        /// <remarks>
        /// <b>One number for the world, and no pet knows about it.</b> Every pet earns the
        /// same fraction of what its owner was awarded, so a new pet needs no configuration
        /// and no code -- and a party's split reaches its pets already split, rather than
        /// each pet earning a share of the undivided total.
        ///
        /// Provisional: no earlier phase authored a pet experience rule, so this is the
        /// smallest generic policy rather than a design somebody signed off. Twenty-five per
        /// cent, floored to whole experience.
        /// </remarks>
        [Tooltip("Fraction of a character's monster experience their active pet earns.")]
        [Range(0f, 1f)]
        [SerializeField] private float _petExperienceShare = 0.25f;

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

            float calendar = MeasureRealSeconds();

            Simulation.Tick(Time.deltaTime, calendar);

            BroadcastTimeOnSchedule(calendar);

            SaveClockOnSchedule(calendar);
        }

        /// <summary>
        /// Writes the calendar down every so often, as well as on the way out.
        /// </summary>
        /// <remarks>Saving only at shutdown would lose the whole session to a crash, a power
        /// cut or a kill -- which is how most servers actually stop.</remarks>
        private void SaveClockOnSchedule(float deltaSeconds)
        {
            if (_clockSaveSeconds <= 0f) return;

            _sinceClockSaved += deltaSeconds;

            if (_sinceClockSaved < _clockSaveSeconds) return;

            _sinceClockSaved = 0f;

            SaveClock();
        }

        /// <summary>
        /// How much real time has passed since the last tick, whatever the engine reported.
        /// </summary>
        /// <remarks>
        /// <b>A stopwatch, not the frame delta.</b> Unity clamps the delta it reports to the
        /// project's Maximum Allowed Timestep -- a third of a second here -- so a stall longer
        /// than that is simply not counted. For movement that clamp is protective. For the
        /// calendar it is a leak: every hitch loses world time permanently, and a server left
        /// up for days would drift a long way from the hour it claims a day takes.
        ///
        /// <b>Monotonic on purpose.</b> A stopwatch cannot be moved by an operator setting the
        /// machine's clock or by daylight saving, both of which would otherwise jump or
        /// reverse the world's calendar.
        ///
        /// <b>The first tick reports nothing.</b> There is no previous reading to subtract, so
        /// it starts the measurement rather than guessing at one.
        /// </remarks>
        private float MeasureRealSeconds()
        {
            if (!_realTime.IsRunning)
            {
                _realTime.Start();
                _lastRealSeconds = 0.0;

                return 0f;
            }

            double now = _realTime.Elapsed.TotalSeconds;
            double elapsed = now - _lastRealSeconds;

            _lastRealSeconds = now;

            // A stall long enough to matter is real time that genuinely passed, so it is not
            // clamped -- but a wildly large step is more likely a suspended process than a
            // world that should leap forward, so it is capped at a minute.
            if (elapsed < 0.0) return 0f;

            return (float)System.Math.Min(elapsed, 60.0);
        }

        private readonly System.Diagnostics.Stopwatch _realTime = new System.Diagnostics.Stopwatch();
        private double _lastRealSeconds;

        private float _sinceTimeBroadcast;

        /// <summary>Command-line names and environment variables this server accepts.</summary>
        /// <remarks>Named here rather than spelled out at each call so a deployment file and
        /// this code cannot drift apart in a way nobody notices until a server starts as the
        /// wrong channel.</remarks>
        public const string ServerOption = "server";
        public const string ServerVariable = "CHIBI_SERVER_ID";
        public const string ChannelOption = "channel";
        public const string ChannelVariable = "CHIBI_CHANNEL_ID";
        public const string PortOption = "port";
        public const string PortVariable = "CHIBI_PORT";
        public const string ApiOption = "api";
        public const string ApiVariable = "CHIBI_API_BASE_ADDRESS";

        /// <summary>
        /// Lets the launch decide which world this process is, and where the authority lives.
        /// </summary>
        /// <remarks>
        /// <b>This is what makes one build serve ten channels.</b> Identity, port and the
        /// account authority's address used to exist only in the scene, so every channel was
        /// its own build and moving the API meant rebuilding the world.
        ///
        /// <b>Announced, because a server that started as the wrong channel looks fine.</b>
        /// It listens, it admits players, it saves its calendar -- under somebody else's
        /// name. The one cheap defence is saying out loud which world this process became.
        /// None of these four values is a secret; the key that is one is never printed.
        /// </remarks>
        private void ApplyLaunchOptions()
        {
            string[] arguments = System.Environment.GetCommandLineArgs();
            Func<string, string> environment = System.Environment.GetEnvironmentVariable;

            _serverId = LaunchOptions.Resolve(ServerOption, ServerVariable,
                arguments, environment, _serverId);

            _channelId = LaunchOptions.Resolve(ChannelOption, ChannelVariable,
                arguments, environment, _channelId);

            _apiBaseAddress = LaunchOptions.Resolve(ApiOption, ApiVariable,
                arguments, environment, _apiBaseAddress);

            _port = LaunchOptions.ResolvePort(PortOption, PortVariable,
                arguments, environment, _port);

            Debug.Log("[world] this process is server='" + _serverId
                + "' channel='" + _channelId + "' port=" + _port
                + " authority=" + (string.IsNullOrEmpty(_apiBaseAddress)
                    ? "<unconfigured>"
                    : _apiBaseAddress));
        }

        public const string InsecureOption = "allow-insecure-api";
        public const string InsecureVariable = "CHIBI_ALLOW_INSECURE_API";

        /// <summary>
        /// Refuses to start when the account authority would be reached in the clear.
        /// </summary>
        /// <remarks>
        /// <b>What is actually at stake.</b> Every request from this server to the authority
        /// carries the thing that proves which player it is acting for. On one machine that
        /// traffic never reaches a network card. Between two machines it is on a wire, and
        /// anyone who can watch that wire can act as any player on this server. That is not a
        /// hardening opportunity; it is the whole of a player's identity in plaintext.
        ///
        /// <b>Refused rather than warned.</b> A warning at startup is a line in a log nobody
        /// reads until afterwards, and "afterwards" here means after somebody's account was
        /// taken. A server that will not start gets fixed in the same minute.
        ///
        /// <b>With a door, because private networks are real.</b> A deployment whose two
        /// machines share a link nobody else can reach may legitimately want plaintext, and a
        /// rule with no way out gets worked around in worse ways -- usually by putting the
        /// whole thing back on one machine. Saying so explicitly is the price.
        ///
        /// <b>Loopback is exempt by address, not by build type.</b> Tying this to
        /// <c>Debug.isDebugBuild</c> would let a development build be deployed to two machines
        /// and quietly do the unsafe thing.
        /// </remarks>
        private bool AuthorityAddressIsAcceptable()
        {
            var endpoint = new HttpEndpoint(_apiBaseAddress, _apiTimeoutSeconds);

            if (!endpoint.IsUnencryptedOverNetwork) return true;

            bool allowed = !string.IsNullOrEmpty(LaunchOptions.Resolve(
                InsecureOption, InsecureVariable,
                System.Environment.GetCommandLineArgs(),
                System.Environment.GetEnvironmentVariable, null));

            if (allowed)
            {
                Debug.LogWarning("[world] talking to the account authority at "
                    + endpoint + " WITHOUT encryption, because " + InsecureVariable
                    + " was set. Everything this server sends it, including what proves who "
                    + "a player is, is readable by anything on that network.");

                return true;
            }

            Debug.LogError("[world] refusing to start: the account authority is at "
                + endpoint + ", which is not encrypted and is not this machine. What this "
                + "server sends it would identify players to anyone watching the network. "
                + "Use https, or set " + InsecureVariable + "=1 if that link really is "
                + "private.");

            return false;
        }

        /// <summary>Where this world's calendar is written down. Null when nowhere.</summary>
        public IWorldClockStore ClockStore { get; private set; }

        /// <summary>Who hands back what the last process left holding. Null when nobody.</summary>
        public IWorldReclaim Reclaim { get; private set; }

        /// <summary>What the last shutdown left stranded, as this process found it.</summary>
        /// <remarks>Kept so a test can read the outcome without a log, and so an operator
        /// can be told once at startup rather than discovering it from a player who cannot
        /// get in.</remarks>
        public WorldReclaimResult Reclaimed { get; private set; }

        /// <summary>Whether this world will be remembered across a restart.</summary>
        public bool CanSaveCalendar
        {
            get
            {
                if (ClockStore == null || !Server.IsValid || !Channel.IsValid) return false;

                var http = ClockStore as HttpWorldClockStore;

                return http == null || http.CanSave;
            }
        }

        /// <summary>
        /// The clock this world should open with.
        /// </summary>
        /// <remarks>
        /// <b>A saved calendar is resumed; anything else starts the authored day.</b> No
        /// store, an unreachable one, a world that has never been saved and a stored row that
        /// is not a usable measurement all mean the same thing here, and none of them stops a
        /// server from opening. A world that refused to start because the database was
        /// briefly away would be a far worse failure than one that opened at breakfast.
        ///
        /// <b>A changed day length is not resumed.</b> The elapsed total was counted against
        /// the rate it was saved with; reading it back under a different one would move the
        /// world by weeks. When they disagree the authored day is started instead, and the
        /// operator is told, because silently relocating the world in time is the kind of
        /// thing that gets blamed on anything but the setting that caused it.
        /// </remarks>
        private WorldClock RestoreClock()
        {
            double perDay = WorldClock.DefaultSecondsPerDay;

            if (ClockStore == null) return new WorldClock(perDay);

            WorldClockState? saved = ClockStore.Load(Server, Channel);

            if (saved == null) return new WorldClock(perDay);

            WorldClockState state = saved.Value;

            if (System.Math.Abs(state.SecondsPerDay - perDay) > 0.001)
            {
                Debug.LogWarning("[world] saved calendar was measured against a "
                    + state.SecondsPerDay + "s day but this server runs a " + perDay
                    + "s day; starting the authored day rather than relocating the world");

                return new WorldClock(perDay);
            }

            Debug.Log("[world] calendar resumed at day "
                + (long)(state.ElapsedSeconds / perDay));

            return WorldClock.Restore(perDay, state.ElapsedSeconds);
        }

        /// <summary>
        /// Writes the calendar down, if this deployment gave the server a way to.
        /// </summary>
        /// <remarks>Failure is reported once and then ignored: a world that could not save
        /// loses the last few minutes on the next restart, which is not worth interrupting a
        /// running server over.</remarks>
        private void SaveClock()
        {
            if (ClockStore == null || Simulation == null) return;

            bool saved = ClockStore.Save(Server, Channel, new WorldClockState(
                Simulation.Clock.ElapsedSeconds, Simulation.Clock.SecondsPerDay));

            if (saved || _warnedClockUnsaved) return;

            _warnedClockUnsaved = true;

            Debug.LogWarning("[world] the calendar could not be saved; this world will "
                + "restart at the authored hour. " + WhyTheCalendarCannotBeSaved());
        }

        /// <summary>
        /// The most likely reason a save failed, named rather than guessed at.
        /// </summary>
        /// <remarks>
        /// <b>Written after chasing the wrong one.</b> The first version of this warning only
        /// ever asked whether the key was set, so an unconfigured world id -- which is what it
        /// actually was -- sent the reader hunting through environment variables. A diagnostic
        /// that names one cause when there are three is worse than one that names none.
        /// </remarks>
        private string WhyTheCalendarCannotBeSaved()
        {
            if (!Server.IsValid || !Channel.IsValid)
            {
                return "This server has no server id or channel id configured, so its world "
                    + "has no name to be saved under. Set them on WorldServerBootstrap.";
            }

            var http = ClockStore as HttpWorldClockStore;

            if (http != null && !http.CanSave)
            {
                return "No deployment key: set " + HttpWorldClockStore.KeyVariable
                    + " in this server's environment.";
            }

            return "The account authority refused or could not be reached.";
        }

        private bool _warnedClockUnsaved;
        private float _sinceClockSaved;

        [Tooltip("How often the world's calendar is written down, in seconds. Zero never "
            + "writes it, and the world restarts at the authored hour.")]
        [SerializeField] private float _clockSaveSeconds = 120f;

        /// <summary>
        /// Repeats the world's time to everyone, every so often.
        /// </summary>
        /// <remarks>
        /// <b>Not every tick.</b> The clock is arithmetic a client can run itself, so this is
        /// a correction rather than a feed -- sending it sixty times a second would spend
        /// bandwidth to replace a calculation that was already right.
        ///
        /// <b>Not once, either.</b> That was the previous behaviour, and independent clocks
        /// drift: whatever a client's frame rate does to its own counting is permanent until
        /// it logs in again.
        /// </remarks>
        private void BroadcastTimeOnSchedule(float deltaSeconds)
        {
            if (_timeBroadcastSeconds <= 0f) return;
            if (_networkManager == null || !_networkManager.ServerManager.Started) return;

            _sinceTimeBroadcast += deltaSeconds;

            if (_sinceTimeBroadcast < _timeBroadcastSeconds) return;

            _sinceTimeBroadcast = 0f;

            _networkManager.ServerManager.Broadcast(new WorldTimeMessage
            {
                TimeOfDay = Simulation.Clock.TimeOfDay,
                SecondsPerDay = (float)Simulation.Clock.SecondsPerDay,
            });
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

            // Before the socket, deliberately. This process has nobody in it yet, so
            // anything the authority still believes is inside this world belongs to the
            // process that died -- and that is only true for as long as nobody can connect.
            ReclaimWhatTheLastProcessLeft();

            IsListening = _networkManager.ServerManager.StartConnection(_port);

            RegisterWeatherCommands();

            Announce();

            // Where this server will go to find out who a connecting player is. An address
            // is not a secret and carries no credential, and a server pointed at the wrong
            // one refuses every player with no clue as to why -- which is what happened.
            Debug.Log("[world] account authority at "
                + (string.IsNullOrEmpty(_apiBaseAddress) ? "<unconfigured>" : _apiBaseAddress));

            return IsListening;
        }

        /// <summary>
        /// Hands back the players the last process died holding.
        /// </summary>
        /// <remarks>
        /// <b>The bug this closes.</b> <see cref="StopServer"/> releases everyone, and
        /// nothing released anyone when a server was killed, crashed or lost power. The
        /// character stayed marked as being in a world that no longer existed, and because
        /// nothing in the system ever revisited that mark, the player was refused at the
        /// door forever -- not until a timeout, not until the session expired, but until
        /// somebody edited the database by hand. Which, for weeks, was me.
        ///
        /// <b>Why it is safe to do this and unsafe to do it anywhere else.</b> A server
        /// that has not opened its socket has no players. That makes "everyone this world
        /// still holds is a ghost" a fact rather than an inference, and it is a fact for
        /// exactly one instant -- the one before <see cref="StartServer"/> starts
        /// listening. A sweeper running on a timer would have to guess instead, and a wrong
        /// guess throws live players out of the world.
        ///
        /// <b>It never stops a server starting.</b> An authority that cannot be reached, a
        /// key that was never configured, a refusal: all of them leave the world as
        /// stranded as it already was, and all of them are better than a world that will
        /// not open. The one thing this must not do is fail quietly, so the reason is said
        /// out loud when nothing can be handed back.
        /// </remarks>
        private void ReclaimWhatTheLastProcessLeft()
        {
            Reclaimed = WorldReclaimResult.Nothing;

            if (Reclaim == null || !Server.IsValid || !Channel.IsValid) return;

            if (!Reclaim.CanReclaim)
            {
                // Said once, rather than discovered later from a player who cannot log in.
                Debug.LogWarning("[world] stranded players CANNOT be handed back: "
                    + HttpWorldClockStore.KeyVariable + " is not set for this process."
                    + " A crash will lock every player in this world out of their character.");

                return;
            }

            Reclaimed = Reclaim.ReleaseStranded(Server, Channel);

            if (!Reclaimed.FoundAnything) return;

            // Only worth a line when it found something: a non-zero count is also the only
            // evidence an operator gets that the last shutdown was not clean.
            Debug.Log("[world] handed back " + Reclaimed.Sessions + " session(s) and "
                + Reclaimed.Characters + " character(s) stranded by the last process in "
                + _serverId + "/" + _channelId + ". The previous shutdown was not clean.");
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

            // Said at startup rather than discovered after the first save interval: an
            // operator who learns at minute two that the world will not be remembered has
            // already lost the two minutes.
            Debug.Log("[world] calendar: " + (CanSaveCalendar
                ? "will be saved for " + _serverId + "/" + _channelId
                : "WILL NOT BE SAVED. " + WhyTheCalendarCannotBeSaved()));
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
            // Last chance to write the calendar down while the world still exists.
            SaveClock();

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

            // The map this player is arriving on now has somebody to fight. Nests are
            // runtime configuration and are read once per map, the first time anybody
            // stands on it.
            LoadNests(spawn.Map);

            _networkManager.ServerManager.Broadcast(connection, new WorldSpawnMessage
            {
                CharacterId = outcome.Admission.Character.Value,
                MapId = spawn.Map.Value,
                SpawnPointId = spawn.Id.Value,
                X = spawn.X,
                Y = spawn.Y,
                Z = spawn.Z,
                CharacterRevision = outcome.Admission.CharacterRevision.Value,

                // The sky this player is arriving under. Read from the simulation's clock so
                // there is one time of day in the world and the client is told it rather
                // than starting its own day from zero.
                TimeOfDay = Simulation != null ? Simulation.Clock.TimeOfDay : 0f,
                SecondsPerDay = Simulation != null
                    ? (float)Simulation.Clock.SecondsPerDay
                    : (float)WorldClock.DefaultSecondsPerDay,

                // And the weather, so somebody arriving mid-downpour arrives wet rather
                // than waiting for the next turn to find out it is raining.
                Weather = Simulation != null ? (int)Simulation.Weather.Weather : 0,
            }, requireAuthenticated: false);

            // Connecting becomes Ready, and the authority's session becomes Active.
            // The hour, immediately, to this connection alone.
            //
            // The arrival message above already carries it, and relying on that turned out
            // to be relying on one message arriving somewhere specific. The repeat that
            // follows is every sixty seconds, which is a long time to stand in a world
            // showing the wrong sky and then have it change while you watch. This costs two
            // floats once per player and removes the window entirely.
            _networkManager.ServerManager.Broadcast(connection, new WorldTimeMessage
            {
                TimeOfDay = Simulation != null ? Simulation.Clock.TimeOfDay : 0f,
                SecondsPerDay = Simulation != null
                    ? (float)Simulation.Clock.SecondsPerDay
                    : (float)WorldClock.DefaultSecondsPerDay,
            }, requireAuthenticated: false);

            // And what the sky is doing, to this connection alone.
            //
            // WorldWeatherMessage is otherwise only sent when the weather turns, which is
            // exactly right for everyone already here and useless to somebody who has just
            // walked in during a downpour: they would stand in the sun until it next
            // changed. Arriving in weather means arriving in it, not a minute later.
            _networkManager.ServerManager.Broadcast(connection, new WorldWeatherMessage
            {
                Weather = Simulation != null ? (int)Simulation.Weather.Weather : 0,
            }, requireAuthenticated: false);

            Coordinator.ConfirmArrival(connection.ClientId);

            // Nothing else to send about the sky: the arrival message above already carried
            // both the time of day and the weather.

            // And into the simulation, which computes their stats before anything is
            // published -- so the first state a client receives is already correct.
            if (Simulation != null)
            {
                WorldSpawnResult admitted = Simulation.Admit(connection.ClientId,
                    outcome.Admission, PlayerTeam);

                // Their party, if they have one. Read when a member actually arrives
                // rather than at world boot, and only when this world is not already
                // running it -- six members reconnecting at once share one party object.
                if (admitted.IsSpawned && Parties != null && PartyStore != null)
                {
                    Parties.Restore(outcome.Admission.Session,
                        outcome.Admission.Character, PartyStore);
                }

                // And whatever this world still owed when it last stopped. Read here
                // because a pending reward is scoped to a server and channel, and the
                // session is what tells the backend which -- at world boot there is none.
                if (admitted.IsSpawned && Rewards != null && RewardOutbox != null)
                {
                    Rewards.RecoverPending();
                }

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
