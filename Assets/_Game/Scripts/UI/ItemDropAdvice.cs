using ChibiFantasy.Data;

namespace ChibiFantasy.UI
{
    /// <summary>How a slot should look while something is being dragged over it.</summary>
    public enum SlotDropHint
    {
        /// <summary>Nothing is being dragged, or this slot is not involved.</summary>
        None = 0,

        /// <summary>The slot the drag started from.</summary>
        Source = 1,

        /// <summary>A drop here is worth attempting.</summary>
        Valid = 2,

        /// <summary>A drop here cannot work at all.</summary>
        Invalid = 3
    }

    /// <summary>
    /// A hint about whether a drop is worth attempting.
    /// </summary>
    /// <remarks>
    /// <b>Advisory, and deliberately almost ignorant.</b> It answers only from facts the UI
    /// legitimately owns -- which panel a slot belongs to, which slot the drag started in,
    /// and whether the dragged thing is equipment at all. It does not know stack ceilings,
    /// level requirements, class restrictions or capacity, and it must not learn them: a
    /// copy of those rules here would be a second rulebook that drifts from
    /// <see cref="ChibiFantasy.Gameplay.EquipmentService"/> and
    /// <c>ItemContainerState</c> the first time either changes.
    ///
    /// So <see cref="SlotDropHint.Valid"/> means "not structurally impossible", never
    /// "will succeed". Gameplay remains the only authority, and a hinted-valid drop that
    /// gameplay refuses is a normal outcome the controller reports.
    /// </remarks>
    public static class ItemDropAdvice
    {
        /// <summary>Hint for a container slot under the pointer.</summary>
        public static SlotDropHint ForContainerSlot(ItemDragPayload drag,
            ItemSelectionSource targetSource, int targetSlotIndex)
        {
            if (!drag.IsActive || targetSource == ItemSelectionSource.None) return SlotDropHint.None;

            if (targetSource == ItemSelectionSource.Equipment) return SlotDropHint.Invalid;

            if (drag.Source == targetSource && drag.SlotIndex == targetSlotIndex)
            {
                return SlotDropHint.Source;
            }

            // Off the paperdoll, only into the bag. Storage would mean unequip-then-deposit:
            // two gameplay operations, and nothing exists to make that pair atomic. A
            // half-completed one would leave a piece nowhere, so it is not offered.
            if (drag.Source == ItemSelectionSource.Equipment)
            {
                return targetSource == ItemSelectionSource.Inventory
                    ? SlotDropHint.Valid
                    : SlotDropHint.Invalid;
            }

            // Any container slot can receive from any container: same-container drops are a
            // move, merge or swap, and cross-container drops are a transfer. Which one, and
            // whether it fits, is the container's decision.
            return SlotDropHint.Valid;
        }

        /// <summary>Hint for a paperdoll position under the pointer.</summary>
        /// <remarks>The one thing worth refusing outright is dropping something that cannot
        /// be worn onto the paperdoll -- no gameplay rule is needed to know a potion is not
        /// a helm. Whether a wearable piece fits <em>this</em> position, and whether the
        /// character may wear it, stays with <see cref="ChibiFantasy.Gameplay.EquipmentService"/>.</remarks>
        public static SlotDropHint ForEquipmentSlot(ItemDragPayload drag, EquipmentSlot targetSlot)
        {
            if (!drag.IsActive) return SlotDropHint.None;

            if (drag.Source == ItemSelectionSource.Equipment)
            {
                return drag.EquipmentSlot == targetSlot ? SlotDropHint.Source : SlotDropHint.Invalid;
            }

            if (drag.Source != ItemSelectionSource.Inventory) return SlotDropHint.Invalid;

            return drag.IsEquipment ? SlotDropHint.Valid : SlotDropHint.Invalid;
        }
    }
}
