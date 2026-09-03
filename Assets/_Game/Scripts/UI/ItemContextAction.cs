using System.Collections.Generic;

namespace ChibiFantasy.UI
{
    /// <summary>Something a player can ask for from a context menu.</summary>
    /// <remarks>Each value maps to exactly one command on the controller, and therefore to
    /// exactly one existing gameplay service. Nothing here is a new gameplay verb.</remarks>
    public enum ItemContextAction
    {
        None = 0,

        /// <summary>Consume it. Goes to <c>ItemUseService</c>.</summary>
        Use = 1,

        /// <summary>Wear it. Goes to <c>EquipmentService.Equip</c>.</summary>
        Equip = 2,

        /// <summary>Take it off. Goes to <c>EquipmentService.Unequip</c>.</summary>
        Unequip = 3,

        /// <summary>Split the stack. Opens the dialog, then <c>ItemContainerState.Split</c>.</summary>
        Split = 4,

        /// <summary>Inventory to storage. Goes to <c>ItemContainerTransfer.Deposit</c>.</summary>
        MoveToStorage = 5,

        /// <summary>Storage to inventory. Goes to <c>ItemContainerTransfer.Withdraw</c>.</summary>
        Withdraw = 6
    }

    /// <summary>
    /// Which actions a context menu offers.
    /// </summary>
    /// <remarks>
    /// <b>Offering is not permitting.</b> This decides what a menu <em>shows</em>, from the
    /// coarse facts the UI owns: which panel the slot is in, whether the occupant stacks,
    /// whether it is equipment, whether it is authored usable, and whether the storage
    /// panel is open. Whether the action then succeeds is decided by the service it maps
    /// to, and a menu entry that gameplay refuses is a normal outcome.
    ///
    /// <b>The rules are stated once, here.</b> Three panels asking the same question in
    /// three places is how two of them end up disagreeing. Being a pure function over a
    /// snapshot also means the menu's contents are testable without a Canvas.
    ///
    /// It never inspects a <c>DefinitionId</c>: "usable" and "stackable" are authored
    /// flags, so a new consumable needs no change here.
    /// </remarks>
    public static class ItemContextActions
    {
        /// <summary>
        /// Fills <paramref name="into"/> with the actions to show for a container slot.
        /// </summary>
        /// <param name="source">Which panel the slot belongs to.</param>
        /// <param name="slot">The slot's snapshot.</param>
        /// <param name="usable">Whether the definition is authored usable and configured.</param>
        /// <param name="storageOpen">Whether the storage panel is currently on screen.</param>
        /// <param name="into">Caller-owned buffer, cleared first.</param>
        public static void ForContainerSlot(ItemSelectionSource source, ItemSlotViewData slot,
            bool usable, bool storageOpen, List<ItemContextAction> into)
        {
            if (into == null) return;

            into.Clear();
            if (slot.IsEmpty) return;

            if (source == ItemSelectionSource.Inventory)
            {
                if (usable) into.Add(ItemContextAction.Use);
                if (slot.IsEquipment) into.Add(ItemContextAction.Equip);
            }
            else if (source == ItemSelectionSource.Storage)
            {
                into.Add(ItemContextAction.Withdraw);
            }
            else
            {
                return;
            }

            // Offered only where there is something to divide: a stack of one has no split.
            if (SplitBounds.For(slot).IsSplittable) into.Add(ItemContextAction.Split);

            if (source == ItemSelectionSource.Inventory && storageOpen)
            {
                into.Add(ItemContextAction.MoveToStorage);
            }
        }

        /// <summary>Fills <paramref name="into"/> with the actions for a paperdoll position.</summary>
        public static void ForEquipmentSlot(EquipmentSlotViewData slot, List<ItemContextAction> into)
        {
            if (into == null) return;

            into.Clear();
            if (slot.IsEmpty) return;

            into.Add(ItemContextAction.Unequip);
        }
    }
}
