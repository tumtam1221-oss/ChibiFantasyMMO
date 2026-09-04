using System.Collections.Generic;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;

namespace ChibiFantasy.Server
{
    /// <summary>Why a character could not be brought into the world.</summary>
    public enum WorldSpawnRejection
    {
        None = 0,

        /// <summary>The connection was never admitted, or has been displaced.</summary>
        NotAdmitted = 1,

        /// <summary>The authority could not be asked, or refused.</summary>
        PersistenceFailed = 2,

        /// <summary>The stored row cannot become a valid character.</summary>
        CorruptCharacter = 3,

        /// <summary>The map has no authored player spawn, so there is nowhere to stand.</summary>
        NoSpawnPoint = 4,

        /// <summary>This character is already in the world on this server.</summary>
        AlreadySpawned = 5
    }

    /// <summary>What bringing a character into the world produced.</summary>
    public readonly struct WorldSpawnResult
    {
        private WorldSpawnResult(bool ok, WorldSpawnRejection reason, LivingCharacter character,
            string detail)
        {
            IsSpawned = ok;
            Reason = reason;
            Character = character;
            Detail = detail;
        }

        public bool IsSpawned { get; }

        public WorldSpawnRejection Reason { get; }

        public LivingCharacter Character { get; }

        public string Detail { get; }

        public static WorldSpawnResult Spawned(LivingCharacter character)
        {
            return new WorldSpawnResult(true, WorldSpawnRejection.None, character, null);
        }

        public static WorldSpawnResult Refused(WorldSpawnRejection reason, string detail = null)
        {
            return new WorldSpawnResult(false, reason, null, detail);
        }

        public override string ToString()
        {
            return IsSpawned ? "spawned " + Character.Character : "refused: " + Reason;
        }
    }

    /// <summary>
    /// One character the server is currently authoritative for.
    /// </summary>
    /// <remarks>
    /// <b>The runtime pieces, held together, owned by nothing else.</b> The domain aggregate
    /// Phase 04 defined, the skills Phase 06 defined, the location Phase 11 defined, and the
    /// bookkeeping a save needs. It defines none of them — it is the place they live while a
    /// player is connected.
    ///
    /// <see cref="IsDirty"/> is the whole of "do not write every frame". A save happens at
    /// lifecycle points, and only when something actually changed.
    /// </remarks>
    public sealed class LivingCharacter
    {
        internal LivingCharacter(int connectionId, SessionId session, AccountId account,
            ServerId server, ChannelId channel, Character character, CharacterSkillsState skills,
            CharacterLocationState location, SpawnPointDefinition spawn, int saveRevision)
        {
            ConnectionId = connectionId;
            Session = session;
            Account = account;
            Server = server;
            Channel = channel;
            Domain = character;
            Skills = skills;
            Location = location;
            Spawn = spawn;
            SaveRevision = saveRevision;
        }

        public int ConnectionId { get; internal set; }

        public SessionId Session { get; }

        public AccountId Account { get; }

        /// <summary>The account projected onto Phase 08 ownership. Not a second model.</summary>
        public OwnerId Owner => new OwnerId(Account.Value);

        public ServerId Server { get; }

        public ChannelId Channel { get; }

        /// <summary>The Phase 04 aggregate. There is no second character model.</summary>
        public Character Domain { get; }

        public CharacterSkillsState Skills { get; }

        /// <summary>Phase 11's location state, the one the travel system moves.</summary>
        public CharacterLocationState Location { get; }

        public SpawnPointDefinition Spawn { get; }

        public CharacterId Character => Domain.Identity.CharacterId;

        /// <summary>The revision this was loaded at. Presented again when saving.</summary>
        public int SaveRevision { get; internal set; }

        /// <summary>Whether anything has changed since the last accepted save.</summary>
        /// <remarks>The reason a server does not write every frame: an unchanged character
        /// is skipped entirely, so an idle player costs the database nothing.</remarks>
        public bool IsDirty { get; private set; }

        /// <summary>The last movement sequence applied, so a replay is detectable.</summary>
        public long LastMovementSequence { get; internal set; }

        public long LastMovementTimestamp { get; internal set; }

        /// <summary>
        /// The last combat sequence accepted, so a replayed attack is refused.
        /// </summary>
        /// <remarks>Kept apart from the movement sequence because the two streams are
        /// independent: a player moving while not attacking would otherwise advance a
        /// counter their next attack is measured against.</remarks>
        public long LastCombatSequence { get; internal set; }

        /// <summary>
        /// This character's identity in the combat system.
        /// </summary>
        /// <remarks>
        /// Phase 07's <c>ICombatant</c> is keyed by <see cref="InstanceId"/> while a
        /// character is keyed by <see cref="CharacterId"/>; both are GUID strings, so the
        /// projection is exact and reversible rather than a mapping table that could drift.
        /// The same trick <c>AccountIdentity.ToOwnerId</c> uses, for the same reason: one
        /// identity, seen from two systems.
        /// </remarks>
        public InstanceId CombatantId => new InstanceId(Character.Value);

        /// <summary>Marks the character as needing a save.</summary>
        public void MarkDirty()
        {
            IsDirty = true;
        }

        /// <summary>Records that a save succeeded at a new revision.</summary>
        internal void MarkSaved(int saveRevision)
        {
            SaveRevision = saveRevision;
            IsDirty = false;
        }

        public override string ToString()
        {
            return Character + " (" + Owner + ") on " + Location;
        }
    }

    /// <summary>
    /// Brings characters into the world, keeps them, and writes them back.
    /// </summary>
    /// <remarks>
    /// <b>It composes; it does not decide.</b> Admission is the coordinator's, persistence is
    /// the store's, placement is <see cref="TravelService"/>'s and the domain is the domain's.
    /// This holds the result of all four for as long as a player is connected, which is a job
    /// nothing else was doing.
    ///
    /// <b>One character, one presence.</b> A character already spawned here cannot be spawned
    /// again — the registry refuses rather than producing a second authoritative copy, which
    /// is the corruption the whole design exists to prevent.
    ///
    /// <b>Saves are lifecycle events, not a heartbeat.</b> World entry, disconnect and
    /// shutdown; and only when <see cref="LivingCharacter.IsDirty"/>. A character that has
    /// not changed is skipped entirely, so an idle player costs the database nothing.
    ///
    /// <b>Nothing here is engine-aware.</b> No <c>NetworkBehaviour</c>, no <c>GameObject</c>,
    /// no prefab. Spawning a visible object is the FishNet layer's job; this decides who
    /// exists.
    /// </remarks>
    public sealed class WorldCharacterRegistry
    {
        private readonly ICharacterStateStore _store;
        private readonly IDefinitionRegistry<SpawnPointDefinition> _spawnPoints;

        private readonly Dictionary<int, LivingCharacter> _byConnection =
            new Dictionary<int, LivingCharacter>();

        private readonly Dictionary<string, LivingCharacter> _byCharacter =
            new Dictionary<string, LivingCharacter>();

        public WorldCharacterRegistry(ICharacterStateStore store,
            IDefinitionRegistry<SpawnPointDefinition> spawnPoints)
        {
            _store = store;
            _spawnPoints = spawnPoints;
        }

        public int Count => _byConnection.Count;

        /// <summary>
        /// Loads an admitted character and places it.
        /// </summary>
        /// <remarks>
        /// The order matters and is not arbitrary. The store is asked before anything is
        /// registered, so a failed load leaves nothing behind. The spawn is resolved before
        /// the character is recorded, so a map with nowhere to stand refuses rather than
        /// producing a character at the origin. And the duplicate check runs before either,
        /// so a second spawn attempt costs no round trip.
        /// </remarks>
        public WorldSpawnResult Spawn(int connectionId, in WorldAdmission admission,
            ResourceLimits limits)
        {
            if (!admission.IsAdmitted || !admission.HasCharacter)
            {
                return WorldSpawnResult.Refused(WorldSpawnRejection.NotAdmitted);
            }

            if (_byCharacter.ContainsKey(admission.Character.Value))
            {
                // Cheapest check first: refusing a duplicate must not cost a round trip.
                return WorldSpawnResult.Refused(WorldSpawnRejection.AlreadySpawned);
            }

            if (_store == null)
            {
                return WorldSpawnResult.Refused(WorldSpawnRejection.PersistenceFailed,
                    "no character store configured");
            }

            CharacterPersistenceResult loaded = _store.Load(admission.Session);

            if (!loaded.IsOk)
            {
                return WorldSpawnResult.Refused(WorldSpawnRejection.PersistenceFailed,
                    loaded.Failure + (loaded.Detail == null ? string.Empty : ": " + loaded.Detail));
            }

            CharacterLoadOutcome domain = PersistedCharacterMapper.ToDomain(loaded.Character,
                limits);

            if (!domain.IsOk)
            {
                return WorldSpawnResult.Refused(WorldSpawnRejection.CorruptCharacter,
                    domain.Detail);
            }

            // Where they stood, if it is still authored; otherwise the map's player spawn.
            // Never a coordinate, and never the origin as a fallback.
            SpawnPointDefinition spawn = ResolveSpawn(loaded.Character);

            if (spawn == null)
            {
                return WorldSpawnResult.Refused(WorldSpawnRejection.NoSpawnPoint,
                    "no player spawn on " + loaded.Character.Map);
            }

            var location = new CharacterLocationState(domain.Character.Identity.CharacterId);

            if (!location.ArriveAt(spawn))
            {
                return WorldSpawnResult.Refused(WorldSpawnRejection.NoSpawnPoint,
                    "spawn " + spawn.Id + " refused the arrival");
            }

            var living = new LivingCharacter(connectionId, admission.Session, admission.Account,
                admission.Server, admission.Channel, domain.Character, domain.Skills, location,
                spawn, loaded.Character.SaveRevision);

            _byConnection[connectionId] = living;
            _byCharacter[living.Character.Value] = living;

            return WorldSpawnResult.Spawned(living);
        }

        /// <summary>
        /// Where a loaded character should appear.
        /// </summary>
        /// <remarks>
        /// The spawn they last stood on, if it is still authored and still on their map;
        /// otherwise the map's player spawn. A saved spawn that content has since removed
        /// must not strand a player, and it must not silently move them to another map
        /// either — hence both conditions.
        /// </remarks>
        private SpawnPointDefinition ResolveSpawn(PersistedCharacter persisted)
        {
            if (_spawnPoints == null || !persisted.Map.IsValid) return null;

            if (persisted.Spawn.IsValid)
            {
                for (int i = 0; i < _spawnPoints.All.Count; i++)
                {
                    SpawnPointDefinition candidate = _spawnPoints.All[i];

                    if (candidate == null || candidate.Id != persisted.Spawn) continue;
                    if (candidate.Map != persisted.Map) break;
                    if (candidate.SpawnType != SpawnType.Player) break;

                    return candidate;
                }
            }

            return TravelService.FindPlayerSpawn(persisted.Map, _spawnPoints);
        }

        public bool TryGet(int connectionId, out LivingCharacter character)
        {
            return _byConnection.TryGetValue(connectionId, out character);
        }

        public bool TryGetByCharacter(CharacterId character, out LivingCharacter living)
        {
            living = null;

            return character.IsValid && _byCharacter.TryGetValue(character.Value, out living);
        }

        public bool IsSpawned(CharacterId character)
        {
            return character.IsValid && _byCharacter.ContainsKey(character.Value);
        }

        /// <summary>
        /// Writes a character back if anything changed.
        /// </summary>
        /// <remarks>
        /// <b>An unchanged character is skipped, and that is reported as success.</b> A
        /// caller saving on disconnect should not have to distinguish "nothing to do" from
        /// "it worked" — both mean the database is correct.
        ///
        /// A refused save leaves <see cref="LivingCharacter.IsDirty"/> set, so the next
        /// lifecycle point tries again rather than losing the change quietly.
        /// </remarks>
        public CharacterPersistenceResult Save(LivingCharacter living, bool force = false)
        {
            if (living == null)
            {
                return CharacterPersistenceResult.Failed(CharacterPersistenceFailure.Corrupt,
                    "nothing to save");
            }

            if (!force && !living.IsDirty)
            {
                return CharacterPersistenceResult.Saved(living.SaveRevision);
            }

            if (_store == null)
            {
                return CharacterPersistenceResult.Failed(
                    CharacterPersistenceFailure.Unreachable, "no character store configured");
            }

            PersistedCharacter row = PersistedCharacterMapper.ToPersisted(living.Domain,
                living.Skills, living.Location, living.Server, living.Account,
                living.SaveRevision);

            CharacterPersistenceResult result = _store.Save(living.Session, row,
                living.SaveRevision);

            if (result.IsOk)
            {
                living.MarkSaved(result.SaveRevision);
            }

            return result;
        }

        /// <summary>
        /// Saves and removes a character, for a connection that is leaving.
        /// </summary>
        /// <remarks>Removed whatever the save reported. A character kept in the registry
        /// because its save failed would block the player's own reconnection, which turns a
        /// transient database problem into a lockout.</remarks>
        public CharacterPersistenceResult Despawn(int connectionId)
        {
            if (!_byConnection.TryGetValue(connectionId, out LivingCharacter living))
            {
                return CharacterPersistenceResult.Failed(CharacterPersistenceFailure.NotOwned,
                    "no character on that connection");
            }

            CharacterPersistenceResult result = Save(living);

            _byConnection.Remove(connectionId);

            if (_byCharacter.TryGetValue(living.Character.Value, out LivingCharacter held)
                && ReferenceEquals(held, living))
            {
                _byCharacter.Remove(living.Character.Value);
            }

            return result;
        }

        /// <summary>Every living character, for a shutdown that has to save them all.</summary>
        public IReadOnlyList<LivingCharacter> All()
        {
            var all = new List<LivingCharacter>(_byConnection.Count);

            foreach (KeyValuePair<int, LivingCharacter> pair in _byConnection)
            {
                all.Add(pair.Value);
            }

            return all;
        }

        /// <summary>
        /// Saves everyone and empties the registry.
        /// </summary>
        /// <remarks>What a controlled shutdown calls. Returns how many saves the authority
        /// accepted, so an operator can tell a clean stop from one that lost writes.</remarks>
        public int SaveAllAndClear()
        {
            int saved = 0;

            foreach (LivingCharacter living in All())
            {
                if (Save(living).IsOk) saved++;
            }

            _byConnection.Clear();
            _byCharacter.Clear();

            return saved;
        }
    }
}
