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
            CharacterLocationState location, SpawnPointDefinition spawn, int saveRevision,
            CombatTeam team, ItemContainerState inventory = null,
            CharacterEquipmentState equipment = null,
            CharacterDevilFruitState devilFruit = null,
            IReadOnlyList<PetInstance> pets = null)
        {
            Inventory = inventory;
            Equipment = equipment;

            // Phase 12's own types, held rather than modelled. This class keeps a
            // character's pets the way it keeps their bag: the rules about what a pet is,
            // how it levels and which one may be out all live in PetService, and nothing
            // here re-decides any of them.
            _pets = pets == null
                ? new List<PetInstance>()
                : new List<PetInstance>(pets);

            // One companion state per character, always present even when nothing is out,
            // so every reader asks the same object instead of null-checking a second
            // representation into existence -- the same argument the fruit state makes.
            Companion = new PetCompanionState(character.Identity.CharacterId);

            // One live fruit state per character, always present even when empty, so every
            // reader asks the same object rather than null-checking a second representation
            // into existence. Phase 12's type, unchanged: this class holds one, it does not
            // model ownership itself.
            DevilFruit = devilFruit
                ?? new CharacterDevilFruitState(character.Identity.CharacterId,
                    new OwnerId(account.Value));
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

            // Phase 07's own combatant, not a server-side copy of one. Its CombatantId is
            // the character id projected onto InstanceId -- the same projection this class
            // makes -- so self-targeting and id comparison work without a mapping table.
            //
            // Derived stats are null: CharacterCombatant falls back to the character's base
            // stats, which is the honest answer until equipment modifiers are loaded. A
            // fabricated derived block would look authoritative and be wrong.
            Combatant = new CharacterCombatant(character, null, ResourceLimits.None, team);
            Combatant.Position = location == null ? default : location.Position;

            // One status list per character, pointed at from the combatant rather than
            // duplicated inside it. A skill applying a debuff and a validator asking about
            // silence therefore read the same list.
            Status = new StatusEffectRuntimeState(character.Identity.CharacterId);
            Combatant.Status = Status;
        }

        private readonly List<PetInstance> _pets;

        /// <summary>
        /// Rewards whose experience this character or one of their pets already has.
        /// </summary>
        /// <remarks>
        /// <b>The evidence, not a marker.</b> One entry per reward, so a second reward
        /// arriving while the first is still unstamped cannot erase what is known about the
        /// first. Keyed by reward and, for a pet, by the exact instance.
        ///
        /// <b>Durable through the character's own save.</b> Entries are written back with
        /// the progression they describe, so a crash cannot separate them; storage retires
        /// each one as it stamps the delivery it belongs to, so this holds only what is in
        /// flight.
        /// </remarks>
        private readonly HashSet<string> _appliedRewards = new HashSet<string>();

        /// <summary>
        /// Applications this world has made but has not yet managed to save.
        /// </summary>
        /// <remarks>The difference between "the progression includes this reward because
        /// storage says so" and "because we just applied it and the save has not landed".
        /// The first may be stamped delivered; the second may not, because stamping it
        /// would report a payment that no database has seen.</remarks>
        private readonly HashSet<string> _unsavedApplications = new HashSet<string>();

        /// <summary>Every pet this character owns, whichever is out.</summary>
        /// <remarks>Owned entities, not inventory: Phase 12 deliberately gave a pet no bag
        /// slot and no item row, and this keeps that true by holding them somewhere a
        /// container cannot reach.</remarks>
        public IReadOnlyList<PetInstance> Pets => _pets;

        /// <summary>Which pet is out, and whether it is following or an aura.</summary>
        public PetCompanionState Companion { get; }

        /// <summary>Starts owning a pet. The rules about acquiring one are PetService's.</summary>
        internal bool AddPet(PetInstance pet)
        {
            if (pet == null || !pet.InstanceId.IsValid) return false;

            for (var i = 0; i < _pets.Count; i++)
            {
                // One object per identity. Two pets of the same kind are two pets, but the
                // same pet twice is a bookkeeping mistake that would double a buff later.
                if (_pets[i].InstanceId == pet.InstanceId) return false;
            }

            _pets.Add(pet);

            return true;
        }

        /// <summary>The pet with that identity, if this character owns it.</summary>
        /// <summary>Whether this reward's experience is already part of the recipient.</summary>
        /// <param name="rewardId">The reward's own durable id.</param>
        /// <param name="pet">Which pet, or invalid for the character's own experience.</param>
        public bool HasAppliedReward(string rewardId, InstanceId pet = default)
        {
            return !string.IsNullOrEmpty(rewardId)
                && _appliedRewards.Contains(ApplicationKey(rewardId, pet));
        }

        /// <summary>Whether that application has reached storage.</summary>
        /// <remarks>What a delivery stamp may be written against. An application that is
        /// only in memory is not evidence of anything a restart could find.</remarks>
        public bool IsRewardApplicationDurable(string rewardId, InstanceId pet = default)
        {
            return HasAppliedReward(rewardId, pet)
                && !_unsavedApplications.Contains(ApplicationKey(rewardId, pet));
        }

        /// <summary>
        /// Records that a reward's experience has just been applied.
        /// </summary>
        /// <remarks>In memory until the next successful save, which is what carries it to
        /// storage. A world that dies before that save applied nothing durable either, so
        /// recovery pays once.</remarks>
        internal void NoteAppliedReward(string rewardId, InstanceId pet = default)
        {
            if (string.IsNullOrEmpty(rewardId)) return;

            string key = ApplicationKey(rewardId, pet);

            _appliedRewards.Add(key);
            _unsavedApplications.Add(key);
        }

        /// <summary>Restores what storage already knew about, on load.</summary>
        /// <remarks>Durable by definition: it came back from the database.</remarks>
        internal void RestoreAppliedReward(string rewardId, InstanceId pet = default)
        {
            if (string.IsNullOrEmpty(rewardId)) return;

            _appliedRewards.Add(ApplicationKey(rewardId, pet));
        }

        /// <summary>
        /// Forgets an application whose delivery has been stamped.
        /// </summary>
        /// <remarks>
        /// The evidence exists to answer "is this reward still owed but already applied?",
        /// and a stamped delivery has answered it for good: storage retires the row in the
        /// same transaction that stamps it, so keeping it here would only resurrect it on
        /// the next save and leave the table growing with history nothing reads.
        ///
        /// Called only after the stamp is durable. Forgetting first would be forgetting
        /// evidence a restart still needs.
        /// </remarks>
        internal void ForgetAppliedReward(string rewardId, InstanceId pet = default)
        {
            if (string.IsNullOrEmpty(rewardId)) return;

            string key = ApplicationKey(rewardId, pet);

            _appliedRewards.Remove(key);
            _unsavedApplications.Remove(key);
        }

        /// <summary>
        /// Everything applied is now written down.
        /// </summary>
        /// <remarks>Called by the registry when a save succeeds, because the save carries
        /// the applications with the progression they describe -- one landing means both
        /// did.</remarks>
        internal void MarkApplicationsDurable()
        {
            _unsavedApplications.Clear();
        }

        /// <summary>What this character is carrying evidence of, as rows to save.</summary>
        public IReadOnlyList<PersistedRewardApplication> AppliedRewards()
        {
            var rows = new List<PersistedRewardApplication>();

            foreach (string key in _appliedRewards)
            {
                int split = key.IndexOf('\n');

                if (split < 0) continue;

                string rewardId = key.Substring(0, split);
                string pet = key.Substring(split + 1);

                rows.Add(new PersistedRewardApplication(rewardId,
                    string.IsNullOrEmpty(pet) ? default : new InstanceId(pet)));
            }

            return rows;
        }

        /// <summary>
        /// One recipient, one reward, as a key.
        /// </summary>
        /// <remarks>A newline separates the two halves because neither an id nor an
        /// instance may contain one, so no pair of different applications can spell the
        /// same key.</remarks>
        private static string ApplicationKey(string rewardId, InstanceId pet)
        {
            return rewardId + "\n" + (pet.IsValid ? pet.Value : string.Empty);
        }

        public bool TryGetPet(InstanceId instance, out PetInstance pet)
        {
            pet = null;

            if (!instance.IsValid) return false;

            for (var i = 0; i < _pets.Count; i++)
            {
                if (_pets[i].InstanceId != instance) continue;

                pet = _pets[i];

                return true;
            }

            return false;
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

        /// <summary>
        /// The Devil Fruit this character owns. Never null; empty when they own none.
        /// </summary>
        /// <remarks><b>The one live copy.</b> Stat modifiers, skill availability,
        /// persistence and replication all read this object, so none of them can disagree
        /// about what somebody ate.</remarks>
        public CharacterDevilFruitState DevilFruit { get; }

        public CharacterSkillsState Skills { get; }

        /// <summary>Phase 11's location state, the one the travel system moves.</summary>
        public CharacterLocationState Location { get; }

        /// <summary>
        /// The character's bag: Phase 08's container, not a server-side copy of one.
        /// </summary>
        /// <remarks>
        /// <b>The authoritative inventory.</b> Loot arrives here and nowhere else, through
        /// <c>LootPickupService</c>, using the stacking and capacity rules Phase 08 already
        /// defines. There is no loot container and no second stacking algorithm.
        ///
        /// Null on a server composed without an item registry -- a world that cannot resolve
        /// an item definition cannot honestly hold items, and a bag that silently accepted
        /// unknown ids would be worse than none. Every caller checks.
        /// </remarks>
        public ItemContainerState Inventory { get; }

        /// <summary>
        /// What the character is wearing: Phase 04's state, not a server-side copy.
        /// </summary>
        /// <remarks>
        /// The same arrangement as <see cref="Inventory"/>. <c>EquipmentService</c> moves
        /// pieces between the two, <c>EquipmentModifierResolver</c> reads this for stats, and
        /// nothing here reimplements either. Null on a server composed without an item
        /// registry, because a piece of equipment that cannot be resolved to a definition is
        /// not something this server can honestly wear.
        /// </remarks>
        public CharacterEquipmentState Equipment { get; }

        public SpawnPointDefinition Spawn { get; }

        /// <summary>
        /// This character as the combat system sees it.
        /// </summary>
        /// <remarks>
        /// Phase 07's <see cref="CharacterCombatant"/>, held rather than reimplemented. It is
        /// what makes a player targetable by a monster and resolvable by
        /// <c>CombatCommandAuthority</c> -- both of which need an <c>ICombatant</c>, and
        /// neither of which should be handed a second model of the same character.
        /// </remarks>
        public CharacterCombatant Combatant { get; }

        /// <summary>
        /// Every status effect on this character, and everything it refuses.
        /// </summary>
        /// <remarks>
        /// <b>Phase 12's runtime, held rather than reimplemented.</b> It already knows how to
        /// stack, refresh, expire and refuse; what was missing was somewhere for a live
        /// character to keep one. This is that place, and it is the only one -- the
        /// combatant points at this same object, so there is no arrangement in which a
        /// character has two different sets of buffs.
        ///
        /// <b>Server-owned and in memory.</b> Nothing persists it: there is no status table
        /// and temporary combat state is not written to the database, so leaving the world
        /// ends every effect. That is the current policy rather than an oversight, and it is
        /// pinned by a test.
        /// </remarks>
        public StatusEffectRuntimeState Status { get; }

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

        /// <summary>The last travel sequence accepted, so a replayed portal use is refused.</summary>
        /// <remarks>A third independent stream, for the same reason as the other two: a
        /// player who travels must not advance the counter their next attack is measured
        /// against.</remarks>
        public long LastTravelSequence { get; internal set; }

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

        /// <summary>
        /// Records that a movement was accepted, so the next one must be newer.
        /// </summary>
        /// <remarks>
        /// The counterpart to <c>MovementValidator</c>, which reads these two values and
        /// does not own them. Kept as one call rather than two settable properties so a
        /// caller cannot advance the sequence without advancing the clock -- the pair only
        /// means anything together, and a sequence that moved without a timestamp would let
        /// the next move claim an arbitrary gap.
        ///
        /// Movement does not mark the character dirty. A position is saved at lifecycle
        /// points from the location state; marking dirty on every step would defeat the
        /// whole point of not writing every frame.
        /// </remarks>
        public void RecordMovement(long sequence, long timestampMilliseconds)
        {
            LastMovementSequence = sequence;
            LastMovementTimestamp = timestampMilliseconds;

            // The combatant reads its position from here rather than holding a second one.
            // Letting them drift would mean a monster chasing where a player used to be.
            if (Combatant != null && Location != null) Combatant.Position = Location.Position;
        }

        /// <summary>
        /// Forgets the movement stream, for a character that has just arrived somewhere new.
        /// </summary>
        /// <remarks>Positions measured against the old map would look like enormous deltas
        /// on the new one, and every legitimate move would be refused as a speed hack.</remarks>
        public void ResetMovementStream()
        {
            LastMovementSequence = 0;
            LastMovementTimestamp = 0;
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
        private readonly IDefinitionRegistry<ItemDefinition> _items;
        private readonly int _defaultInventoryCapacity;

        /// <summary>Authored fruits, for resolving a persisted id. Null in a world with none.</summary>
        private readonly IDefinitionRegistry<DevilFruitDefinition> _devilFruits;

        /// <summary>The pets this world can resolve. Content, never a copy of one.</summary>
        private readonly IDefinitionRegistry<PetDefinition> _petDefinitions;

        /// <summary>The effects a pet's buff may name, applied through Phase 09's service.</summary>
        private readonly IDefinitionRegistry<StatusEffectDefinition> _statusEffects;

        private readonly Dictionary<int, LivingCharacter> _byConnection =
            new Dictionary<int, LivingCharacter>();

        private readonly Dictionary<string, LivingCharacter> _byCharacter =
            new Dictionary<string, LivingCharacter>();

        /// <param name="store">Where characters are loaded from and written back to.</param>
        /// <param name="spawnPoints">Authored spawns, so arrivals resolve from content.</param>
        /// <param name="items">
        /// Authored items, needed to rebuild a bag. Optional: a world composed without one
        /// gives every character a null inventory, which is the honest answer for a server
        /// that cannot resolve an item id -- and it keeps every caller written before
        /// inventories were loaded working unchanged.
        /// </param>
        /// <param name="defaultInventoryCapacity">
        /// Slots for a character whose row carries no capacity of its own. A number here
        /// rather than in every row, so raising it later is one change.
        /// </param>
        public WorldCharacterRegistry(ICharacterStateStore store,
            IDefinitionRegistry<SpawnPointDefinition> spawnPoints,
            IDefinitionRegistry<ItemDefinition> items = null,
            int defaultInventoryCapacity = 30,
            IDefinitionRegistry<DevilFruitDefinition> devilFruits = null,
            IDefinitionRegistry<PetDefinition> pets = null,
            IDefinitionRegistry<StatusEffectDefinition> statusEffects = null)
        {
            _store = store;
            _spawnPoints = spawnPoints;
            _items = items;
            _defaultInventoryCapacity = defaultInventoryCapacity;
            _devilFruits = devilFruits;
            _petDefinitions = pets;
            _statusEffects = statusEffects;
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
            ResourceLimits limits = default, CombatTeam team = default)
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

            // The bag is rebuilt from the same row the rest of the character came from, in
            // the slots it was saved in.
            var owner = new OwnerId(admission.Account.Value);

            ItemContainerState inventory = _items == null
                ? null
                : PersistedCharacterMapper.ToInventory(loaded.Character, owner, _items,
                    _defaultInventoryCapacity);

            CharacterEquipmentState equipment = _items == null
                ? null
                : PersistedCharacterMapper.ToEquipment(loaded.Character, owner, _items);

            // The fruit they ate, restored by stable id. A row naming a fruit this world
            // does not have is a refusal, not a substitution: silently giving somebody a
            // different power is worse than telling an operator the content is wrong.
            var fruit = new CharacterDevilFruitState(domain.Character.Identity.CharacterId,
                owner);

            if (loaded.Character.DevilFruit.IsValid)
            {
                if (_devilFruits == null
                    || !_devilFruits.TryGet(loaded.Character.DevilFruit,
                        out DevilFruitDefinition _))
                {
                    return WorldSpawnResult.Refused(WorldSpawnRejection.CorruptCharacter,
                        "unknown devil fruit '" + loaded.Character.DevilFruit + "'");
                }

                fruit.Activate(loaded.Character.DevilFruit,
                    new InstanceId(loaded.Character.DevilFruitSource ?? string.Empty));
            }

            // The pets they own, by stable identity. A row naming a pet this world does
            // not have, or carrying impossible progress, is refused for the same reason a
            // missing fruit is: handing somebody a different companion is worse than
            // telling an operator the data is wrong.
            var pets = new List<PetInstance>();

            if (!PersistedCharacterMapper.TryReadPets(loaded.Character, owner,
                _petDefinitions, pets, out string petFault))
            {
                return WorldSpawnResult.Refused(WorldSpawnRejection.CorruptCharacter,
                    petFault);
            }

            var living = new LivingCharacter(connectionId, admission.Session, admission.Account,
                admission.Server, admission.Channel, domain.Character, domain.Skills, location,
                spawn, loaded.Character.SaveRevision, team, inventory, equipment, fruit, pets);

            // What storage already knows this character has been paid, per reward. A
            // reward that is still owed but whose experience is already here reconciles
            // rather than paying twice.
            for (var i = 0; i < loaded.Character.RewardApplications.Count; i++)
            {
                PersistedRewardApplication applied = loaded.Character.RewardApplications[i];

                if (!applied.Exists) continue;

                living.RestoreAppliedReward(applied.RewardId, applied.Pet);
            }

            // Whichever was out when they left. Through PetService, so the one-active rule
            // and the aura decision are made in the one place that owns them.
            if (loaded.Character.ActivePet.IsValid)
            {
                if (!living.TryGetPet(loaded.Character.ActivePet, out PetInstance active))
                {
                    return WorldSpawnResult.Refused(WorldSpawnRejection.CorruptCharacter,
                        "active pet '" + loaded.Character.ActivePet
                        + "' is not owned by this character");
                }

                PetService.TrySummon(living.Companion, active, PetContext(living));
            }

            living.Combatant.SetLimits(limits);

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

        /// <summary>The registries and owner a pet decision is made against.</summary>
        /// <remarks>Built per character so the owner is the one the connection resolved to.
        /// Every rule about pets is PetService's; this only says who is asking.</remarks>
        private PetService.Context PetContext(LivingCharacter living)
        {
            return new PetService.Context(_petDefinitions, _items, _statusEffects,
                living.Status, living.Owner);
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
                living.SaveRevision, living.Inventory, living.Equipment, living.DevilFruit,
                living.Pets, living.Companion, living.AppliedRewards());

            CharacterPersistenceResult result = _store.Save(living.Session, row,
                living.SaveRevision);

            if (result.IsOk)
            {
                living.MarkSaved(result.SaveRevision);

                // The row that just landed carried the reward applications with the
                // progression they describe, so both are now durable or neither is.
                living.MarkApplicationsDurable();
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
