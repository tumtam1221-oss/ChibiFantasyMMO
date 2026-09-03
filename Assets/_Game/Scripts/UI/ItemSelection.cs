using ChibiFantasy.Core;

namespace ChibiFantasy.UI
{
    /// <summary>Which panel a selection points into.</summary>
    public enum ItemSelectionSource
    {
        None = 0,
        Inventory = 1,
        Storage = 2,
        Equipment = 3
    }

    /// <summary>
    /// What the player has clicked.
    /// </summary>
    /// <remarks>
    /// <b>Selection is UI state and only UI state.</b> Nothing in gameplay knows or cares
    /// what is highlighted, which is why this lives here and holds no gameplay object.
    ///
    /// <b>It records identity as well as position.</b> Keying on
    /// <see cref="DefinitionId"/> alone would make two potion stacks indistinguishable and
    /// would let a click on one act on the other. Keying on the slot alone would survive
    /// the item being removed and then act on whatever landed there next --
    /// <see cref="Matches"/> is what closes that: a selection is only still valid if the
    /// same instance is still in the same place.
    /// </remarks>
    public readonly struct ItemSelection
    {
        public ItemSelection(ItemSelectionSource source, int slotIndex, InstanceId instanceId)
        {
            Source = source;
            SlotIndex = slotIndex;
            InstanceId = instanceId;
        }

        public ItemSelectionSource Source { get; }

        /// <summary>
        /// Position within the container.
        /// </summary>
        /// <remarks>Equipment has no container index, so an equipment selection carries the
        /// <see cref="ChibiFantasy.Data.EquipmentSlot"/> here instead. That is why
        /// <see cref="Matches(EquipmentSlotViewData)"/> compares the instance and the source
        /// rather than this value: the two sources number their slots differently and must
        /// never be compared to each other.</remarks>
        public int SlotIndex { get; }

        /// <summary>The owned copy that was clicked.</summary>
        public InstanceId InstanceId { get; }

        public bool IsEmpty => Source == ItemSelectionSource.None || !InstanceId.IsValid;

        /// <summary>Nothing selected.</summary>
        public static ItemSelection None => default;

        /// <summary>
        /// Whether this selection still points at what it was pointing at.
        /// </summary>
        /// <remarks>
        /// The guard against acting on a stale click: an item consumed, moved, sold or
        /// taken by another window leaves a selection whose slot now holds something else
        /// or nothing. Comparing the instance as well as the slot means a stale selection
        /// simply stops matching instead of quietly redirecting to a different item.
        /// </remarks>
        public bool Matches(ItemSlotViewData slot)
        {
            if (IsEmpty || slot.IsEmpty) return false;
            return slot.SlotIndex == SlotIndex && slot.InstanceId == InstanceId;
        }

        /// <summary>The equipment-panel form of the same question.</summary>
        public bool Matches(EquipmentSlotViewData slot)
        {
            if (IsEmpty || slot.IsEmpty) return false;
            return Source == ItemSelectionSource.Equipment && slot.InstanceId == InstanceId;
        }

        public override string ToString()
        {
            return IsEmpty ? "none" : Source + "[" + SlotIndex + "] " + InstanceId;
        }
    }
}
