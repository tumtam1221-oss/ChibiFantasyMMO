using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>Why an owned object may not change hands.</summary>
    /// <remarks>
    /// One vocabulary shared by trade and by shops, so a player is told the same thing
    /// whichever way they tried to move something, and so a second system cannot invent a
    /// seventh reason nobody handles.
    /// </remarks>
    public enum ItemTransferBlock
    {
        None = 0,

        /// <summary>No instance, or no registry to resolve it through.</summary>
        MissingContext = 1,

        /// <summary>The definition could not be resolved.</summary>
        UnknownDefinition = 2,

        /// <summary>Content authored the item as untradable.</summary>
        NotTradable = 3,

        /// <summary>It is bound to its owner.</summary>
        Bound = 4,

        /// <summary>An open trade session is holding it.</summary>
        ReservedForTrade = 5,

        /// <summary>A shop listing is holding it.</summary>
        Listed = 6,

        /// <summary>It is worn.</summary>
        Equipped = 7,

        /// <summary>It is socketed into a piece of equipment.</summary>
        Socketed = 8,

        /// <summary>It was spent to activate a Devil Fruit.</summary>
        ConsumedByDevilFruit = 9,

        /// <summary>It is not the offering character's.</summary>
        NotOwned = 10,

        /// <summary>It changed since the offer was made.</summary>
        StaleRevision = 11,

        /// <summary>The container the offer names no longer holds it.</summary>
        NotHeld = 12
    }

    /// <summary>
    /// The single answer to "may this change hands".
    /// </summary>
    /// <remarks>
    /// <b>One place, asked by everything.</b> Trade and player shops both call
    /// <see cref="CanTransfer"/>. There is no trade-specific tradability check and no
    /// shop-specific one, so the two can never disagree about whether a sword may move, and
    /// a rule added here is enforced by both at once.
    ///
    /// <b>It asks each authority rather than keeping its own copy.</b> Tradability is read
    /// off <see cref="ItemDefinition.Tradable"/>; reservations off
    /// <see cref="GameInstance.LockState"/>; being worn off
    /// <see cref="CharacterEquipmentState"/>; being socketed off the equipment that holds it.
    /// Nothing is cached and nothing is duplicated, which is why there is no flag anywhere
    /// that can drift out of step with the fact it describes.
    ///
    /// <b>Some rules are structural rather than checked.</b> A pet is a
    /// <c>PetInstance</c> and never enters a container, so it cannot be offered at all; a
    /// consumed Devil Fruit item has already left the bag. Those are still named here --
    /// <see cref="ItemTransferBlock.ConsumedByDevilFruit"/> is checked defensively -- because
    /// a rule that holds only by accident of another system's behaviour should be stated
    /// where somebody changing that system will see it.
    ///
    /// It reads only. Nothing here reserves, moves or mutates.
    /// </remarks>
    public static class ItemTransferRules
    {
        /// <summary>Everything the rules consult.</summary>
        public readonly struct Context
        {
            public Context(IDefinitionRegistry<ItemDefinition> items,
                CharacterEquipmentState equipment = null,
                CharacterDevilFruitState devilFruit = null,
                IReadOnlyList<EquipmentInstance> socketHolders = null)
            {
                Items = items;
                Equipment = equipment;
                DevilFruit = devilFruit;
                SocketHolders = socketHolders;
            }

            public IDefinitionRegistry<ItemDefinition> Items { get; }

            /// <summary>What the owner is wearing. Optional; without it, worn gear is not detected.</summary>
            public CharacterEquipmentState Equipment { get; }

            /// <summary>The owner's active fruit, for the defensive consumed-copy check.</summary>
            public CharacterDevilFruitState DevilFruit { get; }

            /// <summary>
            /// Equipment whose card sockets should be searched.
            /// </summary>
            /// <remarks>Supplied by the caller because "every piece that might hold this
            /// card" is a question about a character's whole estate, which this file cannot
            /// see. A caller that supplies none gets every other rule.</remarks>
            public IReadOnlyList<EquipmentInstance> SocketHolders { get; }

            public bool IsUsable => Items != null;
        }

        /// <summary>
        /// Whether one owned object may be given to somebody else.
        /// </summary>
        /// <param name="instance">The exact copy being offered.</param>
        /// <param name="claimedOwner">Who says it is theirs. Checked, never trusted.</param>
        /// <param name="context">The authorities to consult.</param>
        /// <param name="expectedRevision">
        /// The revision the offer was made against. Supply it to refuse an offer built on a
        /// stale view; leave it null to skip the check. Nullable because
        /// <see cref="Revision.Initial"/> is itself the default value, so no sentinel could
        /// distinguish "not supplied" from "expects the first revision".
        /// </param>
        public static ItemTransferBlock CanTransfer(GameInstance instance, OwnerId claimedOwner,
            in Context context, Revision? expectedRevision = null)
        {
            if (instance == null || !context.IsUsable) return ItemTransferBlock.MissingContext;

            ItemDefinition definition;
            if (!context.Items.TryGet(instance.DefinitionId, out definition) || definition == null)
                return ItemTransferBlock.UnknownDefinition;

            if (claimedOwner.IsValid && instance.Owner != claimedOwner)
                return ItemTransferBlock.NotOwned;

            if (expectedRevision.HasValue && instance.Revision != expectedRevision.Value)
                return ItemTransferBlock.StaleRevision;

            if (!definition.Tradable) return ItemTransferBlock.NotTradable;

            switch (instance.LockState)
            {
                case ItemLockState.Bound: return ItemTransferBlock.Bound;
                case ItemLockState.Reserved: return ItemTransferBlock.ReservedForTrade;
                case ItemLockState.Listed: return ItemTransferBlock.Listed;
            }

            if (IsEquipped(instance.InstanceId, context)) return ItemTransferBlock.Equipped;

            if (IsSocketed(instance.InstanceId, context)) return ItemTransferBlock.Socketed;

            if (WasSpentOnDevilFruit(instance.InstanceId, context))
                return ItemTransferBlock.ConsumedByDevilFruit;

            return ItemTransferBlock.None;
        }

        /// <summary>Convenience: whether a transfer is allowed at all.</summary>
        /// <remarks>What a tooltip greys a row out with, so the panel asks the same question
        /// the service will answer.</remarks>
        public static bool IsTransferable(GameInstance instance, OwnerId claimedOwner,
            in Context context)
        {
            return CanTransfer(instance, claimedOwner, context) == ItemTransferBlock.None;
        }

        /// <summary>Whether this exact copy is worn.</summary>
        public static bool IsEquipped(InstanceId instance, in Context context)
        {
            if (context.Equipment == null || !instance.IsValid) return false;

            return context.Equipment.SlotOf(instance) != EquipmentSlot.None;
        }

        /// <summary>
        /// Whether this exact copy sits in some piece of equipment's card sockets.
        /// </summary>
        /// <remarks>A socketed card has already left the container, so this is a second line
        /// of defence rather than the only one. It matters because a card's identity survives
        /// socketing on purpose, which is exactly what would let a careless caller offer one.</remarks>
        public static bool IsSocketed(InstanceId instance, in Context context)
        {
            IReadOnlyList<EquipmentInstance> holders = context.SocketHolders;
            if (holders == null || !instance.IsValid) return false;

            for (int i = 0; i < holders.Count; i++)
            {
                if (holders[i] == null) continue;
                if (holders[i].HasCardInstance(instance)) return true;
            }

            return false;
        }

        /// <summary>
        /// Whether this copy was the one spent to take on a Devil Fruit's power.
        /// </summary>
        /// <remarks>Defensive. Activation removes the item from the container, so in normal
        /// operation there is nothing left to offer; this refuses the case where a caller
        /// held a reference across the activation.</remarks>
        public static bool WasSpentOnDevilFruit(InstanceId instance, in Context context)
        {
            if (context.DevilFruit == null || !instance.IsValid) return false;
            if (!context.DevilFruit.HasActiveFruit) return false;

            return context.DevilFruit.SourceInstance == instance;
        }
    }
}
