// Development only. A production dedicated build compiles none of this file, so the
// harness cannot be started in one however the process is launched.
#if DEVELOPMENT_BUILD || UNITY_EDITOR

using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Gameplay;
using UnityEngine;

namespace ChibiFantasy.Server
{
    /// <summary>
    /// Drives a dedicated server process through ordinary play, so that a soak measures a
    /// world that is doing something.
    /// </summary>
    /// <remarks>
    /// <b>A source of input, not a second simulation.</b> Every character it admits goes
    /// through <see cref="WorldSimulation.Admit"/>, every swing through the shipped combat
    /// pipeline, every reward is decided by <see cref="MonsterRewardAuthority"/> and every
    /// pet action goes through <see cref="CharacterPetAuthority"/>. Nothing here grants
    /// experience, completes a reward, applies a buff or writes progression: it presses the
    /// same buttons a player would and lets the world answer. The clock is the bootstrap's
    /// own Update, unchanged.
    ///
    /// <b>The actors are authoritative server-runtime characters, not connected clients.</b>
    /// There are no sockets, no NetworkConnections and no replication for them. What is
    /// being soaked is the simulation, its rewards and its persistence, not the transport --
    /// socket coverage is the FishNet suite's job. A soak report must therefore never call
    /// these "connected players".
    ///
    /// <b>Off unless asked for, twice over.</b> The file compiles only into development
    /// builds and the editor, and even then nothing happens without <c>-soak</c> on the
    /// command line. A server started without the flag is byte-for-byte the server it was.
    ///
    /// <b>Persistence is in memory.</b> A soak that wrote to the real backend would spend
    /// its ten minutes proving MySQL works, which the live suites already do. The world is
    /// rebuilt through the shipped <see cref="WorldServerBootstrap.Compose"/> with in-memory
    /// stores -- the same substitution the shipped-scene tests make, and the only thing
    /// substituted.
    /// </remarks>
    public sealed class WorldSoakHarness : MonoBehaviour
    {
        private const string Flag = "-soak";
        private const string CharactersFlag = "-soakCharacters=";
        private const string MonstersFlag = "-soakMonsters=";
        private const string MinutesFlag = "-soakMinutes=";
        private const string SampleFlag = "-soakSampleSeconds=";
        private const string MapFlag = "-soakMap=";
        private const string MonsterFlag = "-soakMonster=";
        private const string PetFlag = "-soakPet=";
        private const string EvolvedPetFlag = "-soakEvolvedPet=";
        private const string ClassFlag = "-soakClass=";

        // Names for things this process makes up, because nothing issues them to it: no
        // account database, no session service, no pet the player already owns. They are
        // prefixes rather than identities -- every actual id is generated per run.
        private const string Prefix = "soak-";
        private const string SessionPrefix = "session-";
        private const string AccountPrefix = "acc-";
        private const string ServerPrefix = "srv-soak";
        private const string ChannelPrefix = "ch-soak";
        private const string PetPrefix = "soakpet-";

        /// <summary>Starts the harness when the process was launched with <c>-soak</c>.</summary>
        /// <remarks>A hook rather than a component on the shipped scene, so the production
        /// scene asset carries nothing about this and a run without the flag never
        /// constructs it.</remarks>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartIfAsked()
        {
            if (!HasFlag(Flag)) return;

            var host = new GameObject("World Soak Harness");

            DontDestroyOnLoad(host);

            host.AddComponent<WorldSoakHarness>();
        }

        /// <summary>Storage that keeps what it is given, for the life of the process.</summary>
        private sealed class MemoryCharacterStore : ICharacterStateStore
        {
            private readonly Dictionary<string, PersistedCharacter> _rows =
                new Dictionary<string, PersistedCharacter>();

            public CharacterPersistenceResult Load(SessionId session)
            {
                return _rows.TryGetValue(session.Value, out PersistedCharacter row)
                    ? CharacterPersistenceResult.Loaded(row)
                    : CharacterPersistenceResult.Failed(CharacterPersistenceFailure.NotOwned);
            }

            public CharacterPersistenceResult Save(SessionId session, PersistedCharacter row,
                int expectedSaveRevision)
            {
                _rows[session.Value] = row;

                return CharacterPersistenceResult.Saved(expectedSaveRevision + 1);
            }

            public void Seed(SessionId session, PersistedCharacter row)
            {
                _rows[session.Value] = row;
            }

            /// <summary>Drops one application, as the stamp's own transaction does.</summary>
            /// <remarks>The backend retires a reward's application evidence inside the
            /// transaction that stamps its delivery. A double that stamped without retiring
            /// would grow a ledger for ten minutes and make the soak report a leak the
            /// server does not have.</remarks>
            public void Retire(string rewardId, CharacterId character, InstanceId pet)
            {
                string session = SessionPrefix + character.Value;

                if (!_rows.TryGetValue(session, out PersistedCharacter row)) return;

                var kept = new List<PersistedRewardApplication>();

                foreach (PersistedRewardApplication applied in row.RewardApplications)
                {
                    if (applied.RewardId == rewardId && applied.Pet == pet) continue;

                    kept.Add(applied);
                }

                _rows[session] = new PersistedCharacter(row.Character, row.Account,
                    row.Server, row.Name, row.Gender, row.Level, row.Experience,
                    row.CurrentHealth, row.CurrentMana, row.Class, row.Job, row.Map,
                    row.Spawn, row.Stats, row.Appearance, row.Skills, row.SaveRevision,
                    row.Items, row.InventoryCapacity, row.DevilFruit, row.DevilFruitSource,
                    row.Pets, row.ActivePet, kept);
            }

            /// <summary>How much reward evidence is still held, across everybody.</summary>
            public int ApplicationsInFlight()
            {
                var counted = 0;

                foreach (PersistedCharacter row in _rows.Values)
                {
                    counted += row.RewardApplications.Count;
                }

                return counted;
            }
        }

        /// <summary>The reward outbox, in memory, under the same rules as the real one.</summary>
        private sealed class MemoryOutbox : IMonsterRewardOutbox
        {
            private readonly Dictionary<string, PersistedMonsterReward> _byDefeat =
                new Dictionary<string, PersistedMonsterReward>();

            private readonly MemoryCharacterStore _store;

            public MemoryOutbox(MemoryCharacterStore store) => _store = store;

            /// <summary>Every defeat this world has ever written down.</summary>
            public int RecordedCount => _byDefeat.Count;

            /// <summary>Rewards still owing something.</summary>
            public int PendingCount
            {
                get
                {
                    var counted = 0;

                    foreach (PersistedMonsterReward reward in _byDefeat.Values)
                    {
                        if (!reward.IsComplete) counted++;
                    }

                    return counted;
                }
            }

            public MonsterRewardOutboxResult Record(SessionId session,
                PersistedMonsterReward reward)
            {
                if (_byDefeat.TryGetValue(reward.Defeat.Value,
                    out PersistedMonsterReward already))
                {
                    return MonsterRewardOutboxResult.Recorded(already.RewardId,
                        already.Revision, true);
                }

                _byDefeat[reward.Defeat.Value] = Copy(reward, reward.Experience,
                    reward.PetExperience, false, 1);

                return MonsterRewardOutboxResult.Recorded(reward.RewardId, 1, false);
            }

            public IReadOnlyList<PersistedMonsterReward> Pending(SessionId session)
            {
                var pending = new List<PersistedMonsterReward>();

                foreach (PersistedMonsterReward reward in _byDefeat.Values)
                {
                    if (!reward.IsComplete) pending.Add(reward);
                }

                return pending;
            }

            public MonsterRewardOutboxResult Progress(SessionId session, string rewardId,
                int revision, IReadOnlyList<CharacterId> experienceDelivered,
                IReadOnlyList<MonsterRewardLootEntry> lootClaimed,
                bool? cursorCommitted, bool? lootPublished, bool complete,
                IReadOnlyList<InstanceId> petExperienceDelivered = null)
            {
                var keys = new List<string>(_byDefeat.Keys);

                for (var k = 0; k < keys.Count; k++)
                {
                    PersistedMonsterReward stored = _byDefeat[keys[k]];

                    if (stored.RewardId != rewardId) continue;

                    if (stored.Revision != revision)
                    {
                        return MonsterRewardOutboxResult.Failed(
                            MonsterRewardOutboxFailure.StaleRevision, "somebody wrote first");
                    }

                    var grants = new List<MonsterRewardGrant>();

                    foreach (MonsterRewardGrant grant in stored.Experience)
                    {
                        bool paid = grant.IsDelivered;

                        for (var i = 0; !paid && experienceDelivered != null
                            && i < experienceDelivered.Count; i++)
                        {
                            paid = experienceDelivered[i] == grant.Character;
                        }

                        if (paid && !grant.IsDelivered)
                        {
                            _store.Retire(rewardId, grant.Character, default);
                        }

                        grants.Add(new MonsterRewardGrant(grant.Character, grant.Experience,
                            paid));
                    }

                    var pets = new List<MonsterRewardPetGrant>();

                    foreach (MonsterRewardPetGrant grant in stored.PetExperience)
                    {
                        bool paid = grant.IsDelivered;

                        for (var i = 0; !paid && petExperienceDelivered != null
                            && i < petExperienceDelivered.Count; i++)
                        {
                            paid = petExperienceDelivered[i] == grant.Pet;
                        }

                        if (paid && !grant.IsDelivered)
                        {
                            _store.Retire(rewardId, grant.Owner, grant.Pet);
                        }

                        pets.Add(new MonsterRewardPetGrant(grant.Owner, grant.Pet,
                            grant.Experience, paid));
                    }

                    _byDefeat[keys[k]] = Copy(stored, grants, pets,
                        complete || stored.IsComplete, revision + 1);

                    return MonsterRewardOutboxResult.Recorded(rewardId, revision + 1, false);
                }

                return MonsterRewardOutboxResult.Failed(
                    MonsterRewardOutboxFailure.UnknownReward, "no such reward");
            }

            private static PersistedMonsterReward Copy(PersistedMonsterReward reward,
                IReadOnlyList<MonsterRewardGrant> experience,
                IReadOnlyList<MonsterRewardPetGrant> pets, bool complete, int revision)
            {
                return new PersistedMonsterReward(reward.RewardId, reward.Defeat,
                    reward.Monster, reward.Map, reward.Killer, reward.Loot,
                    reward.LootPolicy, reward.Claimant, reward.X, reward.Y, reward.Z,
                    reward.Party, reward.Cursor, reward.HasCursor,
                    new List<MonsterRewardGrant>(experience),
                    new List<MonsterRewardLootEntry>(reward.Entries),
                    reward.IsCursorCommitted, reward.IsLootPublished, complete, revision,
                    new List<MonsterRewardPetGrant>(pets));
            }
        }

        /// <summary>The session authority a process with no login server in front of it needs.</summary>
        private sealed class AlwaysAdmits : IWorldSessionAuthority
        {
            public WorldAdmission Admit(WorldJoinClaim claim) => default;

            public bool ConfirmArrival(SessionId session) => true;

            public bool Release(SessionId session) => true;
        }

        private WorldServerBootstrap _bootstrap;
        private MemoryCharacterStore _store;
        private MemoryOutbox _outbox;
        private MonsterWorldRuntime _monsters;
        private ServerCombatPipeline _combat;

        private readonly List<LivingCharacter> _actors = new List<LivingCharacter>();

        private int _characters = 10;
        private int _monsterTarget = 100;
        private float _minutes = 10f;
        private float _sampleSeconds = 30f;
        private string _map = string.Empty;
        private string _monster = string.Empty;
        private string _pet = string.Empty;
        private string _evolvedPet = string.Empty;
        private string _class = string.Empty;

        private long _sequence;
        private int _defeats;
        private int _round;
        private int _errors;
        private int _evolved;

        private IEnumerator Start()
        {
            _characters = IntArgument(CharactersFlag, _characters);
            _monsterTarget = IntArgument(MonstersFlag, _monsterTarget);
            _minutes = FloatArgument(MinutesFlag, _minutes);
            _sampleSeconds = FloatArgument(SampleFlag, _sampleSeconds);
            _map = StringArgument(MapFlag, _map);
            _monster = StringArgument(MonsterFlag, _monster);
            _pet = StringArgument(PetFlag, _pet);
            _evolvedPet = StringArgument(EvolvedPetFlag, _evolvedPet);
            _class = StringArgument(ClassFlag, _class);

            Application.logMessageReceived += OnLog;

            if (!TheContentItWasToldToUse())
            {
                Application.Quit(1);

                yield break;
            }

            // One frame, so the bootstrap has finished its own Awake before it is asked to
            // compose a second time.
            yield return null;

            if (!TryTakeOverTheWorld())
            {
                Debug.LogError("[soak] no world to drive; the harness is stopping");

                Application.Quit(1);

                yield break;
            }

            Populate();

            Debug.Log("[soak] driving " + _actors.Count
                + " authoritative server-runtime actors (not socket clients) and "
                + _monsters.AliveCount + " monsters on " + _map
                + " for " + _minutes + " minutes");

            float finish = Time.realtimeSinceStartup + _minutes * 60f;
            float nextSample = Time.realtimeSinceStartup;

            while (Time.realtimeSinceStartup < finish)
            {
                Act();

                if (Time.realtimeSinceStartup >= nextSample)
                {
                    Sample();

                    nextSample = Time.realtimeSinceStartup + _sampleSeconds;
                }

                yield return null;
            }

            Sample();

            Debug.Log("[soak] finished minutes=" + _minutes
                + " rounds=" + _round
                + " defeats=" + _defeats
                + " evolutions=" + _evolved
                + " errors=" + _errors);

            Application.logMessageReceived -= OnLog;

            Application.Quit(0);
        }

        /// <summary>
        /// Refuses to start until it has been told which content to soak.
        /// </summary>
        /// <remarks>
        /// <b>This file names no map, no monster, no pet and no class.</b> Which content a
        /// soak drives is content, and a default written down here would be a second place
        /// the world's ids live -- renaming a monster would then break a harness nobody
        /// thought to look at, and adding one would be a code change. The command line says
        /// what to drive, and a run that was not told refuses rather than guessing.
        /// </remarks>
        private bool TheContentItWasToldToUse()
        {
            var missing = new List<string>();

            if (string.IsNullOrEmpty(_map)) missing.Add(MapFlag);
            if (string.IsNullOrEmpty(_monster)) missing.Add(MonsterFlag);
            if (string.IsNullOrEmpty(_pet)) missing.Add(PetFlag);
            if (string.IsNullOrEmpty(_evolvedPet)) missing.Add(EvolvedPetFlag);
            if (string.IsNullOrEmpty(_class)) missing.Add(ClassFlag);

            if (missing.Count == 0) return true;

            Debug.LogError("[soak] refusing to start: this harness names no content of its"
                + " own, so it must be told what to drive. Missing: "
                + string.Join(" ", missing));

            return false;
        }

        /// <summary>Counts anything the world complained about, so the soak can report it.</summary>
        private void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception
                || type == LogType.Assert)
            {
                _errors++;
            }
        }

        /// <summary>Recomposes the shipped world with in-memory persistence, and starts it.</summary>
        /// <remarks>Through the same Compose the scene and the shipped-scene tests use: the
        /// authorities, the simulation and the tick are the production ones, and only the
        /// two stores behind them are substituted.</remarks>
        private bool TryTakeOverTheWorld()
        {
            WorldServerBootstrap[] found = Object.FindObjectsByType<WorldServerBootstrap>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (found.Length != 1)
            {
                Debug.LogError("[soak] expected one world bootstrap, found " + found.Length);

                return false;
            }

            _bootstrap = found[0];

            _store = new MemoryCharacterStore();
            _outbox = new MemoryOutbox(_store);

            _bootstrap.StopServer();
            _bootstrap.Compose(new AlwaysAdmits(), default, _store, null, null, _outbox);

            if (!_bootstrap.IsWorldReady)
            {
                Debug.LogError("[soak] shipped content faults: "
                    + string.Join("; ", _bootstrap.ContentFaults));

                return false;
            }

            // The world rightly keeps its monsters and its combat pipeline to itself; a
            // development driver is not a reason to widen production surface, so it reaches
            // them the same way the shipped-scene fixtures do.
            _monsters = Collaborator<MonsterWorldRuntime>(_bootstrap.Simulation, "_monsters");
            _combat = Collaborator<ServerCombatPipeline>(_bootstrap.Simulation, "_combat");

            if (_monsters == null || _combat == null)
            {
                Debug.LogError("[soak] the world exposed no monsters or no combat pipeline");

                return false;
            }

            return _bootstrap.StartServer();
        }

        private static T Collaborator<T>(object owner, string field) where T : class
        {
            FieldInfo info = owner?.GetType().GetField(field,
                BindingFlags.NonPublic | BindingFlags.Instance);

            return info?.GetValue(owner) as T;
        }

        /// <summary>Fills the world with characters, pets and monsters.</summary>
        private void Populate()
        {
            for (var i = 0; i < _characters; i++)
            {
                // Generated per run, not written down: there is no account database in
                // front of this process, and inventing one identity per actor is the whole
                // difference between a soak and a login server. Nothing here is a constant
                // a production path could ever reach.
                string character = Prefix + i;
                string session = SessionPrefix + character;
                string account = AccountPrefix + character;

                _store.Seed(new SessionId(session), new PersistedCharacter(
                    new CharacterId(character), new AccountId(account),
                    new ServerId(ServerPrefix), character, 1, 30, 0, 104, 35,
                    new DefinitionId(_class), default, new DefinitionId(_map),
                    default, new[]
                    {
                        new PersistedStat(new DefinitionId("stat.str"), 10),
                        new PersistedStat(new DefinitionId("stat.vit"), 8),
                        new PersistedStat(new DefinitionId("stat.int"), 3),
                    }, null, null, 1, null, 0, default, null,
                    new[]
                    {
                        // Half of them arrive already evolved, so the aura form and its
                        // canonical buff are under load from the first tick rather than
                        // only once somebody happens to reach the threshold. The other
                        // half start as base followers and evolve during the run, which is
                        // the path players actually take.
                        i % 2 == 0
                            ? new PersistedPet(new InstanceId(PetPrefix + i),
                                new DefinitionId(_pet), 1, 0, 0)
                            : new PersistedPet(new InstanceId(PetPrefix + i),
                                new DefinitionId(_evolvedPet), 3, 250, 1),
                    },
                    default));

                WorldSpawnResult spawned = _bootstrap.Simulation.Admit(i + 1,
                    WorldAdmission.Admitted(new SessionId(session),
                        new AccountId(account), new CharacterId(character),
                        new ServerId(ServerPrefix), new ChannelId(ChannelPrefix),
                        new DefinitionId(_map), new Revision(1), new Revision(1),
                        SessionState.EnteringWorld),
                    new CombatTeam(1));

                if (!spawned.IsSpawned)
                {
                    Debug.LogError("[soak] " + character + " could not enter: "
                        + spawned.Detail);

                    continue;
                }

                _actors.Add(spawned.Character);

                // Every admission in production recovers what the world still owes; the
                // soak must exercise that path too, not only the happy one.
                if (_bootstrap.Rewards != null) _bootstrap.Rewards.RecoverPending();

                // A follower out from the start, so the pet runtime, its buff and its
                // follow point are part of what is being soaked.
                if (_bootstrap.PetAuthority != null)
                {
                    _bootstrap.PetAuthority.Activate(i + 1, new InstanceId(PetPrefix + i));
                }
            }

            // Nests rather than a flat list of monsters: respawn is part of what a ten
            // minute soak has to survive.
            int nests = Mathf.Max(1, _monsterTarget / 4);

            for (var i = 0; i < nests; i++)
            {
                _monsters.AddSpawnPoint(new MonsterSpawnPoint(new DefinitionId(_monster),
                    new CombatPosition(i % 10 * 6f, 0f, i / 10 * 6f), 5f, 4, 30f,
                    new DefinitionId(_map)));
            }

            _monsters.PopulateAll();
        }

        /// <summary>One round of ordinary play, pressed through the production authorities.</summary>
        private void Act()
        {
            _round++;

            if (_actors.Count == 0) return;

            // Everybody moves, so movement, replication and the monsters' target search all
            // do real work rather than deciding nothing changed.
            for (var i = 0; i < _actors.Count; i++)
            {
                LivingCharacter actor = _actors[i];

                if (actor == null || actor.Combatant == null) continue;

                float angle = _round * 0.05f + i;

                actor.Combatant.Position = new CombatPosition(
                    Mathf.Cos(angle) * 12f + i, 0f, Mathf.Sin(angle) * 12f);
            }

            // A kill every so often: combat, a defeat, a reward, character and pet
            // experience, and a save -- each through the authority that owns it.
            if (_round % 30 == 0) Fight();

            if (_bootstrap.PetAuthority == null) return;

            // Pet churn: away, and back again on a later round.
            if (_round % 240 == 0)
            {
                _bootstrap.PetAuthority.Deactivate(
                    _actors[_round / 240 % _actors.Count].ConnectionId);
            }

            if (_round % 240 == 120)
            {
                int who = _round / 240 % _actors.Count;

                _bootstrap.PetAuthority.Activate(_actors[who].ConnectionId,
                    new InstanceId(PetPrefix + who));
            }

            // The base followers are earning experience from every defeat. Asking whether
            // one can evolve yet is what a client does; the authority decides, refuses
            // while the pet is short of its authored threshold, and one day says yes --
            // at which point the aura buff appears on a live owner mid-soak.
            if (_round % 60 == 30) TryEvolveSomebody();
        }

        /// <summary>Offers one pet to the evolution authority. It may well say no.</summary>
        private void TryEvolveSomebody()
        {
            int who = _round / 60 % _actors.Count;

            LivingCharacter owner = _actors[who];

            if (owner == null) return;

            CharacterPetResult result = _bootstrap.PetAuthority.Evolve(owner.ConnectionId,
                new InstanceId(PetPrefix + who));

            if (result.IsAccepted) _evolved++;
        }

        /// <summary>Kills one monster the way a player would.</summary>
        private void Fight()
        {
            IReadOnlyList<LivingMonster> alive = _monsters.All();

            LivingMonster victim = null;

            for (var i = 0; i < alive.Count; i++)
            {
                if (!alive[i].IsAlive) continue;

                victim = alive[i];

                break;
            }

            if (victim == null)
            {
                // Everything is dead and still inside its authored respawn delay. A fresh
                // nest keeps the soak fighting rather than quietly going idle -- which is
                // the exact failure the previous soak had.
                _monsters.AddSpawnPoint(new MonsterSpawnPoint(new DefinitionId(_monster),
                    new CombatPosition(_round % 40, 0f, 0f), 4f, 4, 30f,
                    new DefinitionId(_map)));

                _monsters.PopulateAll();

                return;
            }

            LivingCharacter killer = _actors[_round % _actors.Count];

            if (killer == null || killer.Combatant == null) return;

            if (!_monsters.TryResolve(victim.Instance, out ICombatant target)) return;

            // Melee reach is authored and enforced; a swing from across the map would only
            // be testing the range check.
            killer.Combatant.Position = victim.State.Position;

            for (var i = 0; i < 400 && target.CurrentHealth > 0; i++)
            {
                _combat.Tick(10f);

                ServerCombatResult result = _combat.Execute(killer.ConnectionId,
                    new CombatCommand(killer.Character, victim.Instance, default, 0,
                        ++_sequence));

                if (!result.IsAccepted) break;
            }

            if (target.CurrentHealth <= 0) _defeats++;
        }

        /// <summary>One line an operator can read a trend out of.</summary>
        private void Sample()
        {
            var summoned = 0;
            var statuses = 0;

            for (var i = 0; i < _actors.Count; i++)
            {
                LivingCharacter actor = _actors[i];

                if (actor == null) continue;

                if (actor.Companion != null && actor.Companion.IsSummoned) summoned++;

                if (actor.Status != null) statuses += actor.Status.ActiveCount;
            }

            Debug.Log("[soak] t=" + Mathf.RoundToInt(Time.realtimeSinceStartup) + "s"
                + " round=" + _round
                + " ticks=" + _bootstrap.Ticks
                + " defeats=" + _defeats
                + " characters=" + (_bootstrap.Characters == null
                    ? 0 : _bootstrap.Characters.Count)
                + " monstersAlive=" + _monsters.AliveCount
                + " spawners=" + _monsters.SpawnerCount
                + " summonedPets=" + summoned
                + " evolutions=" + _evolved
                + " statuses=" + statuses
                + " rewardsRecorded=" + _outbox.RecordedCount
                + " rewardsPending=" + _outbox.PendingCount
                + " rewardsHeld=" + (_bootstrap.Rewards == null
                    ? 0 : _bootstrap.Rewards.HeldCount)
                + " applicationsInFlight=" + _store.ApplicationsInFlight()
                + " monoUsedMB=" + UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong()
                    / (1024 * 1024)
                + " monoHeapMB=" + UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong()
                    / (1024 * 1024)
                + " errors=" + _errors);
        }

        // ---- the command line ---------------------------------------------------------------

        private static bool HasFlag(string flag)
        {
            string[] arguments = System.Environment.GetCommandLineArgs();

            for (var i = 0; i < arguments.Length; i++)
            {
                if (arguments[i] == flag) return true;
            }

            return false;
        }

        private static string StringArgument(string prefix, string fallback)
        {
            string[] arguments = System.Environment.GetCommandLineArgs();

            for (var i = 0; i < arguments.Length; i++)
            {
                if (arguments[i].StartsWith(prefix))
                {
                    return arguments[i].Substring(prefix.Length);
                }
            }

            return fallback;
        }

        private static int IntArgument(string prefix, int fallback)
        {
            return int.TryParse(StringArgument(prefix, string.Empty), out int value)
                ? value
                : fallback;
        }

        private static float FloatArgument(string prefix, float fallback)
        {
            return float.TryParse(StringArgument(prefix, string.Empty), out float value)
                ? value
                : fallback;
        }
    }
}

#endif
