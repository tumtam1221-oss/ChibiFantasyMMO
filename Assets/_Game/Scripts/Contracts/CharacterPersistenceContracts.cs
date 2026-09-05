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

    /// <summary>One status stone in one socket, as the database holds it.</summary>
    /// <remarks>Mirrors <c>equipment_enchant</c> row for row, which is why nothing has to be
    /// translated on the way down.</remarks>
    public readonly struct PersistedEnchant
    {
        public PersistedEnchant(DefinitionId stone, int socketIndex, int rank)
        {
            Stone = stone;
            SocketIndex = socketIndex;
            Rank = rank;
        }

        public DefinitionId Stone { get; }

        public int SocketIndex { get; }

        public int Rank { get; }

        public bool IsValid => Stone.IsValid && SocketIndex >= 0;
    }

    /// <summary>One card in one socket, as the database holds it.</summary>
    /// <remarks>
    /// The card's own instance id travels with it. A socketed card is an owned item that
    /// happens to sit in a piece of equipment rather than a bag, and losing its identity on
    /// the way through would make it a different card when it came out.
    /// </remarks>
    public readonly struct PersistedCard
    {
        public PersistedCard(DefinitionId card, int socketIndex, InstanceId cardInstance)
        {
            Card = card;
            SocketIndex = socketIndex;
            CardInstance = cardInstance;
        }

        public DefinitionId Card { get; }

        public int SocketIndex { get; }

        public InstanceId CardInstance { get; }

        public bool IsValid => Card.IsValid && SocketIndex >= 0;
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
            int slotIndex, int lockState = 0, int equipmentSlot = 0,
            int enhancementLevel = 0, DefinitionId rarity = default,
            IReadOnlyList<PersistedEnchant> enchants = null,
            IReadOnlyList<PersistedCard> cards = null,
            bool isEquipment = false)
        {
            IsEquipment = isEquipment
                || enhancementLevel > 0
                || rarity.IsValid
                || (enchants != null && enchants.Count > 0)
                || (cards != null && cards.Count > 0);

            Instance = instance;
            Item = item;
            Quantity = quantity;
            SlotIndex = slotIndex;
            LockState = lockState;
            EquipmentSlot = equipmentSlot;
            EnhancementLevel = enhancementLevel;
            Rarity = rarity;
            Enchants = enchants ?? System.Array.Empty<PersistedEnchant>();
            Cards = cards ?? System.Array.Empty<PersistedCard>();
        }

        /// <summary>The item's own identity, minted when it was created and never reused.</summary>
        public InstanceId Instance { get; }

        public DefinitionId Item { get; }

        public int Quantity { get; }

        /// <summary>Where it sits in the container. Negative for a worn piece.</summary>
        public int SlotIndex { get; }

        /// <summary>Mirrors <c>ItemLockState</c>: 0 Available, 1 Reserved, 2 Listed, 3 Bound.</summary>
        public int LockState { get; }

        /// <summary>
        /// Which equipment slot it is worn in, by <c>EquipmentSlot</c> ordinal. Zero is None.
        /// </summary>
        /// <remarks>
        /// <b>One list, two homes.</b> A worn piece and a bagged item are both rows in
        /// <c>item_instance</c>, so they are both <see cref="PersistedItem"/> here; what
        /// separates them is this. The database says the same thing with a unique key: an
        /// instance sits in at most one container slot and at most one equipment slot, and
        /// never both.
        /// </remarks>
        public int EquipmentSlot { get; }

        /// <summary>Per-copy enhancement. Zero for anything that is not equipment.</summary>
        public int EnhancementLevel { get; }

        /// <summary>Per-copy rarity override. Invalid means the definition's own.</summary>
        public DefinitionId Rarity { get; }

        /// <summary>Status stones socketed into this piece.</summary>
        public IReadOnlyList<PersistedEnchant> Enchants { get; }

        /// <summary>
        /// Whether this row is a piece of equipment at all.
        /// </summary>
        /// <remarks>
        /// <b>Not the same question as "does it carry anything".</b> A sword that has just
        /// had its last card taken out has no enhancement, no rarity, no stones and no
        /// cards, and is not being worn -- yet it is still a sword, and storage still holds
        /// socket rows for it that have to be cleared.
        ///
        /// Inferring this from the contents was a real defect: such a piece was written as
        /// an ordinary item, the equipment half of the save was skipped entirely, and the
        /// socket row it was supposed to delete survived to be loaded again.
        /// </remarks>
        public bool IsEquipment { get; }

        /// <summary>Cards socketed into this piece.</summary>
        public IReadOnlyList<PersistedCard> Cards { get; }

        /// <summary>Whether this row is a worn piece rather than a bagged item.</summary>
        public bool IsEquipped => EquipmentSlot > 0;

        /// <summary>
        /// Whether the row describes something that can exist.
        /// </summary>
        /// <remarks>A worn piece has no container slot, so a negative index is correct for
        /// one and impossible for the other.</remarks>
        public bool IsValid => Instance.IsValid && Item.IsValid && Quantity > 0
            && (SlotIndex >= 0 || IsEquipped);

        public override string ToString()
        {
            return Item + " x" + Quantity
                + (IsEquipped ? " worn in slot " + EquipmentSlot : " @" + SlotIndex);
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
            int inventoryCapacity = 0, DefinitionId devilFruit = default,
            string devilFruitSource = null)
        {
            DevilFruit = devilFruit;
            DevilFruitSource = devilFruitSource ?? string.Empty;
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

        /// <summary>
        /// The Devil Fruit this character permanently owns, if any.
        /// </summary>
        /// <remarks>
        /// <b>A stable definition id, and nothing else.</b> Not a GUID, not an index, not a
        /// name: those are all things that change when content is re-authored or re-ordered,
        /// and a character would silently wake up with a different power. The modifiers, the
        /// abilities and the immunities are read from the definition this names, never
        /// copied into storage where they would go stale the moment balance changed.
        ///
        /// <b>Invalid means no fruit.</b> Distinct from "a fruit that could not be found",
        /// which is a fault the server reports rather than a state a character can be in.
        /// </remarks>
        public DefinitionId DevilFruit { get; }

        /// <summary>The item instance that was spent, for audit. Empty when none.</summary>
        /// <remarks>Kept because the copy is gone from every container by the time this is
        /// written, so this string is the only remaining record that it existed.</remarks>
        public string DevilFruitSource { get; }

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
