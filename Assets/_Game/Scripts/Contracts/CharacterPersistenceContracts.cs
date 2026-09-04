using System.Collections.Generic;
using ChibiFantasy.Core;

namespace ChibiFantasy.Contracts
{
    /// <summary>One stat, as it is stored.</summary>
    public readonly struct PersistedStat
    {
        public PersistedStat(DefinitionId stat, int value)
        {
            Stat = stat;
            Value = value;
        }

        public DefinitionId Stat { get; }

        public int Value { get; }
    }

    /// <summary>One appearance choice, as it is stored.</summary>
    public readonly struct PersistedAppearance
    {
        public PersistedAppearance(int slot, DefinitionId option)
        {
            Slot = slot;
            Option = option;
        }

        /// <summary>Mirrors Phase 04 <c>AppearanceSlot</c> numerically.</summary>
        public int Slot { get; }

        public DefinitionId Option { get; }
    }

    /// <summary>One learned skill and its level.</summary>
    public readonly struct PersistedSkill
    {
        public PersistedSkill(DefinitionId skill, int level)
        {
            Skill = skill;
            Level = level;
        }

        public DefinitionId Skill { get; }

        public int Level { get; }
    }

    /// <summary>
    /// One owned item, as the database holds it.
    /// </summary>
    /// <remarks>
    /// <b>The slot is part of the row, not derived.</b> A player arranges their bag and
    /// expects to find it as they left it, so where an item sits is persisted state rather
    /// than something recomputed by re-adding everything on load.
    ///
    /// The lock state travels as an int for the same reason every other enum here does: the
    /// database column is a number, and the domain decides what the numbers mean.
    /// </remarks>
    public readonly struct PersistedItem
    {
        public PersistedItem(InstanceId instance, DefinitionId item, int quantity,
            int slotIndex, int lockState = 0)
        {
            Instance = instance;
            Item = item;
            Quantity = quantity;
            SlotIndex = slotIndex;
            LockState = lockState;
        }

        /// <summary>The item's own identity, minted when it was created and never reused.</summary>
        public InstanceId Instance { get; }

        public DefinitionId Item { get; }

        public int Quantity { get; }

        /// <summary>Where it sits in the container.</summary>
        public int SlotIndex { get; }

        /// <summary>Mirrors <c>ItemLockState</c>: 0 Available, 1 Reserved, 2 Listed, 3 Bound.</summary>
        public int LockState { get; }

        public bool IsValid => Instance.IsValid && Item.IsValid && Quantity > 0
            && SlotIndex >= 0;

        public override string ToString()
        {
            return Item + " x" + Quantity + " @" + SlotIndex;
        }
    }

    /// <summary>
    /// A character as the database holds it, before anything turns it into a domain object.
    /// </summary>
    /// <remarks>
    /// <b>A carrier, not a model.</b> This is deliberately not a second
    /// <c>CharacterState</c>: it has no behaviour, enforces no invariant and cannot be
    /// mutated. It exists so that the shape crossing the wire is separate from the shape
    /// the domain reasons about — a persisted row can hold a value the domain would refuse,
    /// and discovering that must be a typed failure rather than an exception thrown halfway
    /// through building an aggregate.
    ///
    /// <b>Ids and numbers only.</b> No engine type, no resolved definition, no live object.
    /// Definitions live in Unity and are looked up by id; duplicating their contents in the
    /// account database would create a second source of truth that goes stale on the next
    /// content patch.
    /// </remarks>
    public sealed class PersistedCharacter
    {
        public PersistedCharacter(CharacterId character, AccountId account, ServerId server,
            string name, int gender, int level, long experience, int currentHealth,
            int currentMana, DefinitionId characterClass, DefinitionId job, DefinitionId map,
            DefinitionId spawn, IReadOnlyList<PersistedStat> stats,
            IReadOnlyList<PersistedAppearance> appearance, IReadOnlyList<PersistedSkill> skills,
            int saveRevision, IReadOnlyList<PersistedItem> items = null,
            int inventoryCapacity = 0)
        {
            Items = items ?? System.Array.Empty<PersistedItem>();
            InventoryCapacity = inventoryCapacity;
            Character = character;
            Account = account;
            Server = server;
            Name = name;
            Gender = gender;
            Level = level;
            Experience = experience;
            CurrentHealth = currentHealth;
            CurrentMana = currentMana;
            Class = characterClass;
            Job = job;
            Map = map;
            Spawn = spawn;
            Stats = stats ?? System.Array.Empty<PersistedStat>();
            Appearance = appearance ?? System.Array.Empty<PersistedAppearance>();
            Skills = skills ?? System.Array.Empty<PersistedSkill>();
            SaveRevision = saveRevision;
        }

        public CharacterId Character { get; }

        public AccountId Account { get; }

        public ServerId Server { get; }

        public string Name { get; }

        public int Gender { get; }

        public int Level { get; }

        public long Experience { get; }

        public int CurrentHealth { get; }

        public int CurrentMana { get; }

        public DefinitionId Class { get; }

        public DefinitionId Job { get; }

        public DefinitionId Map { get; }

        /// <summary>The authored spawn last stood on. Empty for a character that never has.</summary>
        public DefinitionId Spawn { get; }

        public IReadOnlyList<PersistedStat> Stats { get; }

        public IReadOnlyList<PersistedAppearance> Appearance { get; }

        public IReadOnlyList<PersistedSkill> Skills { get; }

        /// <summary>
        /// What is in the character's bag.
        /// </summary>
        /// <remarks>Optional on the constructor so that every caller written before items
        /// were persisted still compiles and still means what it meant: an empty list is
        /// "this row carries no inventory", which is what a load from before the column
        /// existed genuinely is.</remarks>
        public IReadOnlyList<PersistedItem> Items { get; }

        /// <summary>
        /// How many slots the bag has. Zero means the server's default.
        /// </summary>
        /// <remarks>Persisted because capacity is bought and expanded in most games of this
        /// kind. Zero rather than a number here, so the default lives in one place on the
        /// server instead of being copied into every row.</remarks>
        public int InventoryCapacity { get; }

        /// <summary>
        /// The revision this was loaded at, presented again when saving.
        /// </summary>
        /// <remarks>Zero means the character has never been saved. A writer must present
        /// what it loaded; presenting something else is how an hour of somebody's progress
        /// gets overwritten by a server that was already replaced.</remarks>
        public int SaveRevision { get; }

        public override string ToString()
        {
            return Character + " level " + Level + " on " + Map;
        }
    }

    /// <summary>Why a character could not be loaded or saved.</summary>
    /// <remarks>
    /// Typed, because "could not load" is four different problems with four different
    /// responses: retry, tell the player, refuse the connection, or page an operator.
    /// </remarks>
    public enum CharacterPersistenceFailure
    {
        None = 0,

        /// <summary>The authority could not be reached.</summary>
        Unreachable = 1,

        /// <summary>The session is not entitled to this character.</summary>
        NotOwned = 2,

        /// <summary>The session has not reached a stage where this makes sense.</summary>
        InvalidState = 3,

        /// <summary>Somebody else wrote first. The caller's view is stale.</summary>
        StaleRevision = 4,

        /// <summary>
        /// The stored row holds a value the domain refuses.
        /// </summary>
        /// <remarks>Its own outcome rather than a crash: a character whose stats went
        /// negative in the database is a data problem an operator must see, and dropping the
        /// player with a typed reason is better than an exception from inside a load.</remarks>
        Corrupt = 5
    }

    /// <summary>What a load or save produced.</summary>
    public readonly struct CharacterPersistenceResult
    {
        private CharacterPersistenceResult(bool ok, CharacterPersistenceFailure failure,
            PersistedCharacter character, int saveRevision, string detail)
        {
            IsOk = ok;
            Failure = failure;
            Character = character;
            SaveRevision = saveRevision;
            Detail = detail;
        }

        public bool IsOk { get; }

        public CharacterPersistenceFailure Failure { get; }

        /// <summary>What was loaded. Null on a save and on any failure.</summary>
        public PersistedCharacter Character { get; }

        /// <summary>The revision after an accepted save.</summary>
        public int SaveRevision { get; }

        /// <summary>Diagnostic text for a log. Never shown to a player.</summary>
        public string Detail { get; }

        public static CharacterPersistenceResult Loaded(PersistedCharacter character)
        {
            return new CharacterPersistenceResult(true, CharacterPersistenceFailure.None,
                character, character?.SaveRevision ?? 0, null);
        }

        public static CharacterPersistenceResult Saved(int saveRevision)
        {
            return new CharacterPersistenceResult(true, CharacterPersistenceFailure.None, null,
                saveRevision, null);
        }

        public static CharacterPersistenceResult Failed(CharacterPersistenceFailure failure,
            string detail = null)
        {
            return new CharacterPersistenceResult(false, failure, null, 0, detail);
        }

        public override string ToString()
        {
            return IsOk ? "ok" : "failed: " + Failure;
        }
    }

    /// <summary>
    /// Where a world server reads and writes character state.
    /// </summary>
    /// <remarks>
    /// <b>The same boundary rule as <see cref="IWorldSessionAuthority"/>.</b> A world server
    /// implemented against this names no HTTP, no PHP and no SQL. The implementation that
    /// speaks to the Phase 15 API lives in Backend, beside the only transport in the project.
    ///
    /// <b>Kept separate from the session authority on purpose.</b> That one answers "who is
    /// this connection"; this one answers "what is this character made of". They are asked at
    /// different moments by different code, and a single interface would oblige every
    /// implementer of one to implement the other.
    ///
    /// <b>No character parameter.</b> Both calls act on the character the session selected,
    /// which the authority resolves server-side. There is nowhere to name a different one,
    /// which is what makes a forged character id unrepresentable rather than refused.
    /// </remarks>
    public interface ICharacterStateStore
    {
        /// <summary>Reads the character behind a session.</summary>
        CharacterPersistenceResult Load(SessionId session);

        /// <summary>
        /// Writes a character back, presenting the revision it was loaded at.
        /// </summary>
        /// <remarks>A stale revision is refused rather than applied. That is the whole
        /// mechanism preventing a replaced world server from overwriting its replacement.</remarks>
        CharacterPersistenceResult Save(SessionId session, PersistedCharacter character,
            int expectedSaveRevision);
    }
}
