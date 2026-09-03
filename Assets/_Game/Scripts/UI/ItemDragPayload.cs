using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.UI
{
    /// <summary>
    /// What is being dragged, and where it came from.
    /// </summary>
    /// <remarks>
    /// <b>Identity, not just a position.</b> The source is a container, a slot <em>and</em>
    /// an <see cref="InstanceId"/>. A payload keyed on <see cref="DefinitionId"/> alone
    /// would make two potion stacks interchangeable, so a drag that started on slot 2 could
    /// finish by moving slot 7 -- the exact bug the instance id is here to make impossible.
    ///
    /// <b>A snapshot, like every other view type here.</b> It holds no
    /// <c>ItemInstance</c> and no container, so the drag layer has nothing to mutate even
    /// by accident. Dropping submits a request; gameplay decides.
    ///
    /// <see cref="IsStale"/> is what a drop checks first: between picking an item up and
    /// letting go, it can be consumed, merged, sold or moved by something else entirely.
    /// </remarks>
    public readonly struct ItemDragPayload
    {
        private ItemDragPayload(ItemSelectionSource source, int slotIndex, EquipmentSlot equipmentSlot,
            DefinitionId definitionId, InstanceId instanceId, int quantity, AssetRef icon,
            LocalizationKey nameKey, bool isEquipment)
        {
            Source = source;
            SlotIndex = slotIndex;
            EquipmentSlot = equipmentSlot;
            DefinitionId = definitionId;
            InstanceId = instanceId;
            Quantity = quantity;
            Icon = icon;
            NameKey = nameKey;
            IsEquipment = isEquipment;
        }

        /// <summary>Which panel the drag started in.</summary>
        public ItemSelectionSource Source { get; }

        /// <summary>Container slot it started in. Meaningless for equipment.</summary>
        public int SlotIndex { get; }

        /// <summary>Paperdoll position it started in. <c>None</c> for containers.</summary>
        public EquipmentSlot EquipmentSlot { get; }

        public DefinitionId DefinitionId { get; }

        /// <summary>The owned copy that was picked up. What a drop is checked against.</summary>
        public InstanceId InstanceId { get; }

        /// <summary>How many were in the stack when the drag began.</summary>
        public int Quantity { get; }

        public AssetRef Icon { get; }

        public LocalizationKey NameKey { get; }

        /// <summary>Whether the dragged thing can be worn, so a paperdoll can hint.</summary>
        public bool IsEquipment { get; }

        public bool IsActive => Source != ItemSelectionSource.None && InstanceId.IsValid;

        /// <summary>Nothing being dragged.</summary>
        public static ItemDragPayload None => default;

        /// <summary>A drag out of an inventory or storage slot.</summary>
        public static ItemDragPayload FromContainer(ItemSelectionSource source, ItemSlotViewData slot)
        {
            if (slot.IsEmpty || source == ItemSelectionSource.None) return None;

            return new ItemDragPayload(source, slot.SlotIndex, Data.EquipmentSlot.None,
                slot.DefinitionId, slot.InstanceId, slot.Quantity, slot.Icon,
                slot.NameKey, slot.IsEquipment);
        }

        /// <summary>A drag off the paperdoll.</summary>
        public static ItemDragPayload FromEquipment(EquipmentSlotViewData slot)
        {
            if (slot.IsEmpty) return None;

            // Quantity one: equipment never stacks.
            return new ItemDragPayload(ItemSelectionSource.Equipment, (int)slot.Slot, slot.Slot,
                slot.DefinitionId, slot.InstanceId, 1, slot.Icon, slot.NameKey, true);
        }

        /// <summary>
        /// Whether the item this drag picked up is still where it was picked up from.
        /// </summary>
        /// <remarks>
        /// Compares the instance as well as the position, so a slot refilled by a different
        /// stack of the same item reads as stale. A stale drag is cancelled rather than
        /// applied -- acting on it would move whatever happens to be there now.
        /// </remarks>
        public bool StillAt(ItemSlotViewData slot)
        {
            if (!IsActive || slot.IsEmpty) return false;
            return slot.SlotIndex == SlotIndex && slot.InstanceId == InstanceId;
        }

        /// <summary>The paperdoll form of the same question.</summary>
        public bool StillAt(EquipmentSlotViewData slot)
        {
            if (!IsActive || slot.IsEmpty) return false;
            return Source == ItemSelectionSource.Equipment && slot.InstanceId == InstanceId;
        }

        public override string ToString()
        {
            return IsActive
                ? Source + "[" + SlotIndex + "] " + DefinitionId + " x" + Quantity
                : "no drag";
        }
    }
}
