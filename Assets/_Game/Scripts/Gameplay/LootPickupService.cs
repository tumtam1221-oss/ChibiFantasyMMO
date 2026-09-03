using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// Moves loot from the world into a bag.
    /// </summary>
    /// <remarks>
    /// <b>It uses the inventory that already exists.</b> Room, stacking and the remainder
    /// all come from <see cref="ItemContainerState"/>. There is no loot container and no
    /// second stacking rule; a picked-up item is an ordinary
    /// <see cref="ItemInstance"/> or <see cref="EquipmentInstance"/> from the moment it is
    /// created, which is what lets a future trade or player shop operate on the very same
    /// object.
    ///
    /// <b>The item is minted here, by the taker.</b> Loot in the world is a definition and
    /// a quantity, so nothing owned exists until somebody takes it -- and when it does, it
    /// carries their <see cref="OwnerId"/> from the start rather than being reassigned
    /// afterwards.
    ///
    /// <b>A full bag is a refusal, not a loss.</b> The loot stays exactly where it was. The
    /// only path that removes anything from the world is one where the container accepted
    /// it, and a partial acceptance puts the rest back.
    ///
    /// Eligibility is the pile's decision, never the caller's: a client asserting it may
    /// loot proves nothing.
    /// </remarks>
    public static class LootPickupService
    {
        /// <summary>Everything a pickup needs.</summary>
        public readonly struct Context
        {
            public Context(IDefinitionRegistry<ItemDefinition> items, OwnerId owner,
                CharacterId character = default)
            {
                Items = items;
                Owner = owner;
                Character = character;
            }

            public IDefinitionRegistry<ItemDefinition> Items { get; }

            /// <summary>Stamped on what is created. Loot arrives already owned.</summary>
            public OwnerId Owner { get; }

            /// <summary>Who is taking it, for the eligibility check.</summary>
            public CharacterId Character { get; }

            public bool IsUsable => Items != null;
        }

        /// <summary>
        /// Takes one entry from a pile.
        /// </summary>
        /// <param name="loot">The pile.</param>
        /// <param name="index">Which entry.</param>
        /// <param name="inventory">Where it goes.</param>
        /// <param name="context">Registry, owner and taker.</param>
        public static LootPickupResult TryPickUp(LootObjectState loot, int index,
            ItemContainerState inventory, in Context context)
        {
            if (loot == null || inventory == null || !context.IsUsable)
                return LootPickupResult.Rejected(LootPickupRejection.MissingContext);

            if (index < 0 || index >= loot.Count)
                return LootPickupResult.Rejected(LootPickupRejection.AlreadyTaken, loot.LootId);

            if (loot.IsExpired)
                return LootPickupResult.Rejected(LootPickupRejection.Expired, loot.LootId);

            if (loot.IsTaken(index))
                return LootPickupResult.Rejected(LootPickupRejection.AlreadyTaken, loot.LootId);

            if (!loot.IsEligible(context.Character))
                return LootPickupResult.Rejected(LootPickupRejection.NotEligible, loot.LootId);

            LootResult entry = loot.Contents[index];

            ItemDefinition definition;
            if (!context.Items.TryGet(entry.Item, out definition) || definition == null)
                return LootPickupResult.Rejected(LootPickupRejection.UnknownItem, loot.LootId);

            if (entry.Quantity <= 0)
                return LootPickupResult.Rejected(LootPickupRejection.InvalidQuantity, loot.LootId);

            // Room is checked before the claim, so a full bag never marks anything taken.
            if (inventory.RoomFor(entry.Item, context.Items) <= 0 && inventory.IsFull)
                return LootPickupResult.Rejected(LootPickupRejection.InventoryFull, loot.LootId);

            // ---- claim, then hand over -------------------------------------------------

            if (!loot.TryClaim(index))
            {
                // Someone else took it between the check and here.
                return LootPickupResult.Rejected(LootPickupRejection.AlreadyTaken, loot.LootId);
            }

            GameInstance instance = Create(entry, definition, context.Owner);
            ItemContainerResult added = inventory.Add(instance, context.Items);

            if (!added.IsAccepted)
            {
                // The container refused after all. The claim is undone rather than the loot
                // being lost -- there is no rollback, so the container's answer is what
                // decides whether the claim stands.
                loot.Release(index);
                return LootPickupResult.Rejected(LootPickupRejection.InventoryFull, loot.LootId);
            }

            if (added.Remainder > 0)
            {
                // Part of it fit. The rest goes back into the world rather than vanishing.
                loot.Release(index);
                ReduceRemaining(loot, index, added.Remainder);
            }

            return LootPickupResult.Accepted(loot.LootId, entry.Item,
                entry.Quantity - added.Remainder, added.Remainder);
        }

        /// <summary>
        /// Takes everything a character is allowed to take.
        /// </summary>
        /// <remarks>Stops at nothing: entries that will not fit are simply left, so a
        /// partly-full bag takes what it can and the rest stays lootable.</remarks>
        public static int TryPickUpAll(LootObjectState loot, ItemContainerState inventory,
            in Context context)
        {
            if (loot == null) return 0;

            int taken = 0;

            for (int i = 0; i < loot.Count; i++)
            {
                if (TryPickUp(loot, i, inventory, context).IsAccepted) taken++;
            }

            return taken;
        }

        /// <summary>
        /// Creates the owned item.
        /// </summary>
        /// <remarks>
        /// Equipment becomes an <see cref="EquipmentInstance"/> so it can carry enhancement,
        /// rarity and sockets from the moment it drops; everything else becomes a stackable
        /// <see cref="ItemInstance"/>. Which one it is comes from the authored definition,
        /// not from the drop table.
        ///
        /// A rarity override on the entry is stamped here, which is how one table drops the
        /// same sword at different tiers without the sword being authored twice.
        /// </remarks>
        private static GameInstance Create(LootResult entry, ItemDefinition definition,
            OwnerId owner)
        {
            if (definition is EquipmentDefinition)
            {
                var equipment = new EquipmentInstance(InstanceId.New(), entry.Item, owner);

                if (entry.RarityOverride.IsValid) equipment.SetRarity(entry.RarityOverride);

                return equipment;
            }

            return new ItemInstance(InstanceId.New(), entry.Item, owner, entry.Quantity);
        }

        /// <summary>
        /// Puts an unfitting remainder back into the pile.
        /// </summary>
        /// <remarks>The pile's contents are immutable, so the entry is rebuilt at the lower
        /// quantity through a claim-and-replace. Destroying the remainder would be the one
        /// outcome a player never accepts.</remarks>
        private static void ReduceRemaining(LootObjectState loot, int index, int remainder)
        {
            LootResult entry = loot.Contents[index];
            loot.Replace(index, new LootResult(entry.Source, entry.Item, remainder,
                entry.RarityOverride));
        }
    }
}
