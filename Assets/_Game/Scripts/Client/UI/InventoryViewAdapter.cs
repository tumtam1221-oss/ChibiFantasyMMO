using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.UI;

namespace ChibiFantasy.Client.UI
{
    /// <summary>
    /// Turns gameplay state into view data. The read half of the UI boundary.
    /// </summary>
    /// <remarks>
    /// <b>Why it lives in Client.</b> The UI assembly does not reference Gameplay, on
    /// purpose -- that reference is what would let a panel reach into a container and
    /// change it. Something has to bridge the two, and the Client is where both are
    /// already visible. Everything here reads; nothing here mutates.
    ///
    /// <b>It copies out, it does not hand over.</b> The output is a list of snapshots. A
    /// panel never holds an <c>ItemContainerState</c>, an <c>ItemInstance</c> or a
    /// <c>CharacterEquipmentState</c>, so "the UI is not authoritative" is enforced by what
    /// the types are rather than by remembering not to.
    ///
    /// <b>Lists are filled, not returned.</b> The caller owns a buffer that is reused, so
    /// a refresh allocates nothing. Refreshes are event-driven, but a container that is
    /// open while a fight is going on can still refresh often enough for garbage to matter.
    /// </remarks>
    public static class InventoryViewAdapter
    {
        /// <summary>
        /// Fills <paramref name="into"/> with one snapshot per slot, in slot order.
        /// </summary>
        /// <remarks>Always exactly <see cref="ItemContainerState.Capacity"/> entries: empty
        /// slots are drawn too, so the grid has a fixed shape and a slot index is its
        /// position in this list.</remarks>
        public static void BuildContainer(ItemContainerState container,
            IDefinitionRegistry<ItemDefinition> items, List<ItemSlotViewData> into)
        {
            if (into == null) return;

            into.Clear();
            if (container == null) return;

            for (int i = 0; i < container.Capacity; i++)
            {
                ItemSlot slot = container.GetSlot(i);

                if (slot.IsEmpty)
                {
                    into.Add(ItemSlotViewData.Empty(i));
                    continue;
                }

                into.Add(ItemSlotViewData.From(i, slot.DefinitionId, slot.InstanceId,
                    slot.Quantity, Resolve(items, slot.DefinitionId)));
            }
        }

        /// <summary>Fills <paramref name="into"/> with one snapshot per worn piece.</summary>
        /// <remarks>Only occupied positions are produced. The paperdoll decides which
        /// positions exist and fills the rest in as empty, because which slots a character
        /// shows is a layout question, not a state one.</remarks>
        public static void BuildEquipment(CharacterEquipmentState equipment,
            IDefinitionRegistry<ItemDefinition> items, List<EquipmentSlotViewData> into)
        {
            if (into == null) return;

            into.Clear();
            if (equipment == null) return;

            foreach (KeyValuePair<EquipmentSlot, EquipmentInstance> worn in equipment.Equipped)
            {
                if (worn.Value == null) continue;

                into.Add(EquipmentSlotViewData.From(worn.Key, worn.Value.DefinitionId,
                    worn.Value.InstanceId, Resolve(items, worn.Value.DefinitionId)));
            }
        }

        /// <summary>The tooltip for a container slot, or <see cref="ItemTooltipData.None"/>.</summary>
        public static ItemTooltipData BuildTooltip(ItemContainerState container, int slotIndex,
            IDefinitionRegistry<ItemDefinition> items)
        {
            if (container == null || !container.IsValidIndex(slotIndex)) return ItemTooltipData.None;

            ItemSlot slot = container.GetSlot(slotIndex);
            if (slot.IsEmpty) return ItemTooltipData.None;

            return ItemTooltipData.From(slot.DefinitionId, slot.Quantity,
                Resolve(items, slot.DefinitionId));
        }

        /// <summary>The tooltip for a worn piece, or <see cref="ItemTooltipData.None"/>.</summary>
        public static ItemTooltipData BuildTooltip(CharacterEquipmentState equipment,
            EquipmentSlot slot, IDefinitionRegistry<ItemDefinition> items)
        {
            if (equipment == null) return ItemTooltipData.None;

            EquipmentInstance worn;
            if (!equipment.TryGet(slot, out worn) || worn == null) return ItemTooltipData.None;

            // One: equipment does not stack, so no count is shown.
            return ItemTooltipData.From(worn.DefinitionId, 1, Resolve(items, worn.DefinitionId));
        }

        /// <summary>
        /// Looks a definition up, tolerating absence.
        /// </summary>
        /// <remarks>A missing definition is not an error here. Content can be removed by a
        /// patch while a save still holds the id; the view data layer already draws that as
        /// an occupied but unnamed slot, which shows the problem instead of hiding it.</remarks>
        private static ItemDefinition Resolve(IDefinitionRegistry<ItemDefinition> items,
            DefinitionId id)
        {
            if (items == null || !id.IsValid) return null;

            ItemDefinition definition;
            return items.TryGet(id, out definition) ? definition : null;
        }
    }
}
