using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// Moves items between two containers.
    /// </summary>
    /// <remarks>
    /// <b>Deposit and withdraw are the same operation.</b> Which container is the bag and
    /// which is the warehouse is the caller's business; there is one implementation and two
    /// names for readability, because writing it twice would be the duplication that
    /// having a single container type exists to avoid.
    ///
    /// <b>Atomic by checking first.</b> The destination is asked how much room it has for
    /// the whole amount before a single unit leaves the source. A transfer that cannot
    /// complete in full is refused outright and both containers are left untouched, which
    /// is stronger than the partial behaviour <see cref="ItemContainerState.Add"/> allows
    /// when adding from nowhere: a half-finished transfer would leave a player's items
    /// split across two windows with no record of why.
    /// </remarks>
    public static class ItemContainerTransfer
    {
        /// <summary>Inventory to storage. Rejected transfers change neither side.</summary>
        public static ItemContainerResult Deposit(ItemContainerState from, ItemContainerState to,
            int fromSlot, int quantity, IDefinitionRegistry<ItemDefinition> items)
        {
            return Transfer(from, to, fromSlot, quantity, items);
        }

        /// <summary>Storage to inventory. The same operation, named for the caller.</summary>
        public static ItemContainerResult Withdraw(ItemContainerState from, ItemContainerState to,
            int fromSlot, int quantity, IDefinitionRegistry<ItemDefinition> items)
        {
            return Transfer(from, to, fromSlot, quantity, items);
        }

        /// <summary>
        /// Moves a quantity out of one container's slot and into another container.
        /// </summary>
        public static ItemContainerResult Transfer(ItemContainerState from, ItemContainerState to,
            int fromSlot, int quantity, IDefinitionRegistry<ItemDefinition> items)
        {
            if (from == null || to == null)
                return ItemContainerResult.Rejected(ItemContainerRejection.NoItem);

            if (ReferenceEquals(from, to))
                return ItemContainerResult.Rejected(ItemContainerRejection.SameSlot);

            if (!from.IsValidIndex(fromSlot))
                return ItemContainerResult.Rejected(ItemContainerRejection.SlotOutOfRange);

            if (quantity <= 0)
                return ItemContainerResult.Rejected(ItemContainerRejection.InvalidQuantity);

            ItemSlot source = from.GetSlot(fromSlot);

            if (source.IsEmpty)
                return ItemContainerResult.Rejected(ItemContainerRejection.SourceEmpty);

            if (quantity > source.Quantity)
                return ItemContainerResult.Rejected(ItemContainerRejection.InsufficientQuantity);

            if (items == null || !items.TryGet(source.DefinitionId, out ItemDefinition definition)
                || definition == null)
            {
                return ItemContainerResult.Rejected(ItemContainerRejection.UnknownDefinition);
            }

            // Ask before taking. This is the whole of the atomicity guarantee.
            if (to.RoomFor(source.DefinitionId, items) < quantity)
                return ItemContainerResult.Rejected(ItemContainerRejection.ContainerFull);

            bool stackable = definition.Stackable && definition.MaxStackSize > 1
                             && source.IsStackableContent;

            // Non-stackable content moves as the very same instance, so a sword keeps its
            // identity -- and its enhancement level -- across the transfer.
            if (!stackable)
            {
                GameInstance moving = source.Content;

                ItemContainerResult taken = from.RemoveAt(fromSlot, source.Quantity);
                if (!taken.IsAccepted) return taken;

                ItemContainerResult placed = to.Add(moving, items);

                if (!placed.IsAccepted)
                {
                    // The room check said this would fit. Put it back rather than lose it.
                    from.PlaceAt(fromSlot, moving);
                    return ItemContainerResult.Rejected(ItemContainerRejection.ContainerFull);
                }

                return ItemContainerResult.Accepted(source.Quantity, 0, fromSlot, placed.PrimarySlot);
            }

            ItemContainerResult removed = from.RemoveAt(fromSlot, quantity);
            if (!removed.IsAccepted) return removed;

            var carried = new ItemInstance(
                Core.InstanceId.New(), source.DefinitionId, to.Owner, quantity);

            ItemContainerResult added = to.Add(carried, items);

            if (!added.IsAccepted || added.Remainder > 0)
            {
                // Should be unreachable after the room check; restore rather than trust it.
                int restore = added.IsAccepted ? added.Remainder : quantity;
                from.Add(new ItemInstance(Core.InstanceId.New(), source.DefinitionId,
                    from.Owner, restore), items);

                return ItemContainerResult.Rejected(ItemContainerRejection.ContainerFull);
            }

            return ItemContainerResult.Accepted(quantity, 0, fromSlot, added.PrimarySlot);
        }
    }
}
