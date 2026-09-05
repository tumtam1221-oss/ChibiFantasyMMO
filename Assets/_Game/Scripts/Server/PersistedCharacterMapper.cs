using System.Collections.Generic;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;

namespace ChibiFantasy.Server
{
    /// <summary>What building a domain character from a stored row produced.</summary>
    public readonly struct CharacterLoadOutcome
    {
        private CharacterLoadOutcome(bool ok, CharacterPersistenceFailure failure,
            Character character, CharacterSkillsState skills, string detail)
        {
            IsOk = ok;
            Failure = failure;
            Character = character;
            Skills = skills;
            Detail = detail;
        }

        public bool IsOk { get; }

        public CharacterPersistenceFailure Failure { get; }

        /// <summary>The existing Phase 04–06 aggregate. Not a new model.</summary>
        public Character Character { get; }

        /// <summary>
        /// Learned skills, alongside rather than inside the aggregate.
        /// </summary>
        /// <remarks><c>Character</c> was defined in Phase 04 with six sub-states and skills
        /// are not one of them. Adding a seventh to hold this would change a type five phases
        /// of code depend on, for the convenience of one caller.</remarks>
        public CharacterSkillsState Skills { get; }

        public string Detail { get; }

        public static CharacterLoadOutcome Ok(Character character, CharacterSkillsState skills)
        {
            return new CharacterLoadOutcome(true, CharacterPersistenceFailure.None, character,
                skills, null);
        }

        public static CharacterLoadOutcome Failed(CharacterPersistenceFailure failure,
            string detail)
        {
            return new CharacterLoadOutcome(false, failure, null, null, detail);
        }

        public override string ToString()
        {
            return IsOk ? "loaded " + Character.Identity.CharacterId : "failed: " + Failure;
        }
    }

    /// <summary>
    /// Turns a stored row into the character the domain already knows, and back.
    /// </summary>
    /// <remarks>
    /// <b>It builds the existing aggregate; it does not define a new one.</b> Every state
    /// produced here — <see cref="CharacterState"/>, <see cref="CharacterClassState"/>,
    /// <see cref="CharacterAppearanceState"/>, <see cref="CharacterProgressionState"/>,
    /// <see cref="CharacterStatsState"/>, <see cref="CharacterResourceState"/> — is the one
    /// Phases 04 to 08 built. There is deliberately no server-side character model, because
    /// a second model is a second set of rules that will disagree with the first.
    ///
    /// <b>A row the domain refuses is a typed failure, not an exception.</b>
    /// <c>CharacterStatsState.Set</c> throws on a negative value, by design — a base stat
    /// cannot be negative. The database column is signed so that a corrupt or hand-edited
    /// row is *storable and visible* rather than silently wrapping to four billion. Those two
    /// decisions meet here: a negative stat is detected, named, and refused. Loading it would
    /// throw from inside world entry, leaving a half-built character and a connection nobody
    /// disconnects; clamping it would hide a data problem an operator needs to see.
    ///
    /// <b>Nothing here reads a clock, a scene or a registry.</b> It is a pure mapping, so it
    /// is tested as one.
    /// </remarks>
    public static class PersistedCharacterMapper
    {
        /// <summary>
        /// Builds the domain aggregate, or explains why the stored row cannot become one.
        /// </summary>
        /// <param name="persisted">The row.</param>
        /// <param name="limits">
        /// Resource ceilings, derived from stats and equipment by the caller. Passed in
        /// rather than computed here because <see cref="DerivedStatsCalculator"/> needs
        /// content this mapper deliberately does not hold.
        /// </param>
        public static CharacterLoadOutcome ToDomain(PersistedCharacter persisted,
            ResourceLimits limits)
        {
            if (persisted == null)
            {
                return CharacterLoadOutcome.Failed(CharacterPersistenceFailure.Corrupt,
                    "no row");
            }

            if (!persisted.Character.IsValid)
            {
                return CharacterLoadOutcome.Failed(CharacterPersistenceFailure.Corrupt,
                    "row has no character id");
            }

            if (!persisted.Account.IsValid)
            {
                return CharacterLoadOutcome.Failed(CharacterPersistenceFailure.Corrupt,
                    "row has no account");
            }

            if (persisted.Level < 1)
            {
                return CharacterLoadOutcome.Failed(CharacterPersistenceFailure.Corrupt,
                    "level below one");
            }

            if (persisted.Experience < 0)
            {
                return CharacterLoadOutcome.Failed(CharacterPersistenceFailure.Corrupt,
                    "negative experience");
            }

            // Ownership is the account projected onto Phase 08's OwnerId. The same
            // projection AuthenticatedAccount and WorldAdmission make, and the only one.
            var owner = new OwnerId(persisted.Account.Value);

            var identity = new CharacterState(persisted.Character, owner, persisted.Name,
                (CharacterGender)persisted.Gender);

            var characterClass = new CharacterClassState(persisted.Character, persisted.Class);

            if (persisted.Job.IsValid) characterClass.SetJob(persisted.Job);

            var appearance = new CharacterAppearanceState(persisted.Character);

            for (int i = 0; i < persisted.Appearance.Count; i++)
            {
                PersistedAppearance entry = persisted.Appearance[i];

                if (!entry.Option.IsValid) continue;

                var slot = (AppearanceSlot)entry.Slot;

                if (slot == AppearanceSlot.None) continue;

                appearance.Select(slot, entry.Option);
            }

            var progression = new CharacterProgressionState(persisted.Character,
                persisted.Level, persisted.Experience);

            var stats = new CharacterStatsState(persisted.Character);

            for (int i = 0; i < persisted.Stats.Count; i++)
            {
                PersistedStat stat = persisted.Stats[i];

                if (!stat.Stat.IsValid)
                {
                    return CharacterLoadOutcome.Failed(CharacterPersistenceFailure.Corrupt,
                        "stat row with no stat id");
                }

                if (stat.Value < 0)
                {
                    // The domain refuses this and is right to. Reported rather than
                    // clamped, so somebody fixes the row.
                    return CharacterLoadOutcome.Failed(CharacterPersistenceFailure.Corrupt,
                        "negative stat " + stat.Stat);
                }

                stats.Set(stat.Stat, stat.Value);
            }

            var skills = new CharacterSkillsState(persisted.Character);

            for (int i = 0; i < persisted.Skills.Count; i++)
            {
                PersistedSkill skill = persisted.Skills[i];

                if (!skill.Skill.IsValid)
                {
                    return CharacterLoadOutcome.Failed(CharacterPersistenceFailure.Corrupt,
                        "skill row with no skill id");
                }

                if (skill.Level < 1)
                {
                    return CharacterLoadOutcome.Failed(CharacterPersistenceFailure.Corrupt,
                        "skill " + skill.Skill + " below level one");
                }

                skills.Learn(skill.Skill);
                skills.SetRank(skill.Skill, skill.Level);
            }

            // Resources are clamped to the limits the caller derived, so a character saved
            // with more health than their current equipment allows arrives at the ceiling
            // rather than above it. Saved health above the maximum is what happens when a
            // ring comes off while offline; it is not corruption.
            var resources = new CharacterResourceState(persisted.Character, limits,
                persisted.CurrentHealth, persisted.CurrentMana);

            resources.ClampTo(limits);

            return CharacterLoadOutcome.Ok(
                new Character(identity, characterClass, appearance, progression, stats,
                    resources),
                skills);
        }

        /// <summary>
        /// Turns the domain aggregate back into a row.
        /// </summary>
        /// <remarks>
        /// <b>The location comes from the location state, not the character.</b> Phase 11
        /// owns where a character is, and reading it from anywhere else would be the second
        /// source of truth this project keeps avoiding.
        ///
        /// The save revision is carried through unchanged: a writer presents what it loaded.
        /// Incrementing it here would let a server overwrite a newer save simply by saving
        /// twice.
        /// </remarks>
        public static PersistedCharacter ToPersisted(Character character,
            CharacterSkillsState skills, CharacterLocationState location, ServerId server,
            AccountId account, int saveRevision, ItemContainerState inventory = null,
            CharacterEquipmentState equipment = null,
            CharacterDevilFruitState devilFruit = null)
        {
            if (character == null) return null;

            var stats = new List<PersistedStat>();

            for (int i = 0; i < character.Stats.Stats.Count; i++)
            {
                CharacterStatEntry entry = character.Stats.Stats[i];
                stats.Add(new PersistedStat(entry.Stat, entry.Value));
            }

            var appearance = new List<PersistedAppearance>();

            foreach (AppearanceSlot slot in new[]
                     {
                         AppearanceSlot.Face, AppearanceSlot.Eyes, AppearanceSlot.Hair,
                         AppearanceSlot.HairColor, AppearanceSlot.SkinTone,
                     })
            {
                DefinitionId option = character.Appearance.Get(slot);

                if (!option.IsValid) continue;

                appearance.Add(new PersistedAppearance((int)slot, option));
            }

            var learned = new List<PersistedSkill>();

            if (skills != null)
            {
                for (int i = 0; i < skills.Skills.Count; i++)
                {
                    CharacterSkillEntry entry = skills.Skills[i];
                    learned.Add(new PersistedSkill(entry.Skill, entry.Rank));
                }
            }

            return new PersistedCharacter(
                character.Identity.CharacterId,
                account,
                server,
                character.Identity.Name,
                (int)character.Identity.Gender,
                character.Progression.Level,
                character.Progression.Experience,
                character.Resources.CurrentHealth,
                character.Resources.CurrentMana,
                character.Class.BaseClass,
                character.Class.CurrentJob,
                location == null ? default : location.CurrentMap,
                location == null ? default : location.CurrentSpawnPoint,
                stats,
                appearance,
                learned,
                saveRevision,
                ItemsOf(inventory, equipment),
                inventory == null ? 0 : inventory.Capacity,
                devilFruit == null ? default : devilFruit.ActiveFruit,
                devilFruit == null ? null : devilFruit.SourceInstance.Value);
        }

        /// <summary>
        /// Reads a bag out as rows, one per occupied slot.
        /// </summary>
        /// <remarks>
        /// <b>Occupied slots only.</b> An empty slot is the absence of a row, not a row
        /// saying "nothing" -- the same shape the <c>container_slot</c> table already has,
        /// so nothing has to be translated on the way down.
        ///
        /// The slot index travels with each item because a player arranges their bag and
        /// expects to find it that way. Recomputing positions by re-adding everything on
        /// load would silently reorder it.
        /// </remarks>
        private static IReadOnlyList<PersistedItem> ItemsOf(ItemContainerState inventory,
            CharacterEquipmentState equipment)
        {
            var items = new List<PersistedItem>();

            if (inventory != null)
            {
                IReadOnlyList<ItemSlot> slots = inventory.Slots;

                for (int i = 0; i < slots.Count; i++)
                {
                    GameInstance content = slots[i].Content;

                    if (content == null) continue;

                    items.Add(Row(content, slots[i].Index, Data.EquipmentSlot.None));
                }
            }

            if (equipment != null)
            {
                foreach (KeyValuePair<Data.EquipmentSlot, EquipmentInstance> worn in
                    equipment.Equipped)
                {
                    if (worn.Value == null) continue;

                    // No container slot: a worn piece is in no bag, which is the same thing
                    // the database's unique keys say.
                    items.Add(Row(worn.Value, -1, worn.Key));
                }
            }

            return items;
        }

        /// <summary>
        /// One item as a row, wherever it lives.
        /// </summary>
        /// <remarks>
        /// Quantity lives on a stackable <c>ItemInstance</c>; anything else is a single
        /// object, which is the rule the container itself applies. Enhancement, rarity,
        /// stones and cards are read off the piece rather than recomputed, because they are
        /// per-copy facts that no definition can supply.
        /// </remarks>
        private static PersistedItem Row(GameInstance content, int slotIndex,
            Data.EquipmentSlot equipmentSlot)
        {
            int quantity = content is ItemInstance stack ? stack.Quantity : 1;

            var piece = content as EquipmentInstance;

            if (piece == null)
            {
                return new PersistedItem(content.InstanceId, content.DefinitionId, quantity,
                    slotIndex, (int)content.LockState, (int)equipmentSlot);
            }

            var enchants = new List<PersistedEnchant>();

            for (int i = 0; i < piece.Enchants.Count; i++)
            {
                EquipmentEnchant enchant = piece.Enchants[i];

                enchants.Add(new PersistedEnchant(enchant.Stone, enchant.SocketIndex,
                    enchant.Rank));
            }

            var cards = new List<PersistedCard>();

            for (int i = 0; i < piece.Cards.Count; i++)
            {
                EquipmentCardSocket card = piece.Cards[i];

                cards.Add(new PersistedCard(card.Card, card.SocketIndex, card.CardInstance));
            }

            return new PersistedItem(content.InstanceId, content.DefinitionId, quantity,
                slotIndex, (int)content.LockState, (int)equipmentSlot,
                piece.EnhancementLevel, piece.Rarity, enchants, cards);
        }

        /// <summary>
        /// Rebuilds a bag from rows, each item back in the slot it was saved in.
        /// </summary>
        /// <remarks>
        /// <b>A row the domain refuses is dropped, not thrown on.</b> Loading is the one
        /// place a bad row must not take a player's whole session down with it: an item
        /// whose slot is out of range or whose quantity is impossible is left out, and the
        /// rest of the bag still arrives. The alternative is a character nobody can log in
        /// as.
        ///
        /// Equipment becomes an <c>EquipmentInstance</c> and everything else an
        /// <c>ItemInstance</c>, decided by the authored definition -- the same rule
        /// <c>LootPickupService</c> uses when it mints one, so an item is the same kind of
        /// object however it arrived.
        /// </remarks>
        public static ItemContainerState ToInventory(PersistedCharacter persisted,
            OwnerId owner, IDefinitionRegistry<ItemDefinition> items, int defaultCapacity)
        {
            int capacity = persisted != null && persisted.InventoryCapacity > 0
                ? persisted.InventoryCapacity
                : defaultCapacity;

            var inventory = new ItemContainerState(owner, capacity);

            if (persisted == null || items == null) return inventory;

            for (int i = 0; i < persisted.Items.Count; i++)
            {
                PersistedItem row = persisted.Items[i];

                if (!row.IsValid || row.IsEquipped || row.SlotIndex >= capacity) continue;

                if (!items.TryGet(row.Item, out ItemDefinition definition)
                    || definition == null)
                {
                    // Content the build no longer has. Keeping the row would mean an item
                    // nobody can name, use or sell.
                    continue;
                }

                inventory.Restore(row.SlotIndex, Instance(row, owner, definition));
            }

            return inventory;
        }

        /// <summary>
        /// Rebuilds what a character is wearing.
        /// </summary>
        /// <remarks>
        /// <b>Per-copy state is restored, not recomputed.</b> A +9 sword with a rare tier and
        /// two stones in it must come back as exactly that; rebuilding it from the definition
        /// alone would quietly strip every upgrade a player paid for. That is the defect this
        /// method exists to prevent, and the reason <see cref="PersistedItem"/> carries the
        /// four fields it does.
        ///
        /// A row the domain refuses is dropped rather than thrown on, for the same reason the
        /// bag does it: one bad row must not cost a player their whole session.
        /// </remarks>
        public static CharacterEquipmentState ToEquipment(PersistedCharacter persisted,
            OwnerId owner, IDefinitionRegistry<ItemDefinition> items)
        {
            var equipment = new CharacterEquipmentState(
                persisted == null ? default : persisted.Character);

            if (persisted == null || items == null) return equipment;

            for (int i = 0; i < persisted.Items.Count; i++)
            {
                PersistedItem row = persisted.Items[i];

                if (!row.IsValid || !row.IsEquipped) continue;

                if (!items.TryGet(row.Item, out ItemDefinition definition)
                    || definition == null)
                {
                    continue;
                }

                var piece = Instance(row, owner, definition) as EquipmentInstance;

                if (piece == null) continue;

                equipment.Restore((Data.EquipmentSlot)row.EquipmentSlot, piece);
            }

            return equipment;
        }

        /// <summary>
        /// Rebuilds one owned object from its row.
        /// </summary>
        /// <remarks>Which kind it is comes from the authored definition, not from the row --
        /// the same rule <c>LootPickupService</c> uses when it mints one, so an item is the
        /// same kind of object however it arrived.</remarks>
        private static GameInstance Instance(in PersistedItem row, OwnerId owner,
            ItemDefinition definition)
        {
            if (!(definition is EquipmentDefinition))
            {
                var item = new ItemInstance(row.Instance, row.Item, owner, row.Quantity);

                item.TrySetLockState((ItemLockState)row.LockState);

                return item;
            }

            var piece = new EquipmentInstance(row.Instance, row.Item, owner);

            piece.TrySetLockState((ItemLockState)row.LockState);
            piece.SetEnhancementLevel(row.EnhancementLevel);

            if (row.Rarity.IsValid) piece.SetRarity(row.Rarity);

            for (int i = 0; i < row.Enchants.Count; i++)
            {
                PersistedEnchant enchant = row.Enchants[i];

                if (!enchant.IsValid) continue;

                piece.AddEnchant(new EquipmentEnchant(enchant.Stone, enchant.SocketIndex,
                    enchant.Rank));
            }

            for (int i = 0; i < row.Cards.Count; i++)
            {
                PersistedCard card = row.Cards[i];

                if (!card.IsValid) continue;

                piece.AddCard(new EquipmentCardSocket(card.Card, card.SocketIndex,
                    card.CardInstance));
            }

            return piece;
        }
    }
}
