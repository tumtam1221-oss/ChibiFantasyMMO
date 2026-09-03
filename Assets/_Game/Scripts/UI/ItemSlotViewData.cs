using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.UI
{
    /// <summary>
    /// Everything a slot needs to draw itself, and nothing else.
    /// </summary>
    /// <remarks>
    /// <b>A snapshot, not a handle.</b> It copies out the few values a view renders and
    /// holds no <c>ItemInstance</c>, no container and no gameplay object. A view given one
    /// of these has nothing to mutate even if it wanted to, which is what makes "the UI is
    /// not authoritative" a property of the types rather than a rule people remember.
    ///
    /// <b>Built from the Data layer.</b> The UI assembly deliberately does not reference
    /// Gameplay -- that is the boundary the project already drew -- so this is assembled
    /// from an <see cref="ItemDefinition"/> and a few primitives. The Client walks the
    /// container and calls <see cref="From"/>; the rules for what a slot shows live here
    /// and are testable without a container at all.
    ///
    /// <b>Identity is carried, not just the definition.</b> Two potion stacks share a
    /// <see cref="DefinitionId"/> and are different owned things, so
    /// <see cref="InstanceId"/> comes along and selection can tell them apart.
    /// </remarks>
    public readonly struct ItemSlotViewData
    {
        private ItemSlotViewData(int slotIndex, DefinitionId definitionId, InstanceId instanceId,
            int quantity, AssetRef icon, LocalizationKey nameKey, ItemCategory category,
            bool isEquipment)
        {
            SlotIndex = slotIndex;
            DefinitionId = definitionId;
            InstanceId = instanceId;
            Quantity = quantity;
            Icon = icon;
            NameKey = nameKey;
            Category = category;
            IsEquipment = isEquipment;
        }

        public int SlotIndex { get; }

        public DefinitionId DefinitionId { get; }

        /// <summary>Which owned copy this is. What selection keys on.</summary>
        public InstanceId InstanceId { get; }

        public int Quantity { get; }

        /// <summary>Address of the icon. May be <see cref="AssetRef.None"/>.</summary>
        public AssetRef Icon { get; }

        public LocalizationKey NameKey { get; }

        public ItemCategory Category { get; }

        /// <summary>Whether the occupant can be equipped, so a view can offer that action.</summary>
        public bool IsEquipment { get; }

        public bool IsEmpty => !DefinitionId.IsValid;

        public bool IsOccupied => DefinitionId.IsValid;

        /// <summary>
        /// Whether the quantity should be drawn.
        /// </summary>
        /// <remarks>A single item shows no number: a lone sword labelled "1" is noise, and
        /// every inventory a player has used follows this convention. Stated once here so
        /// three panels cannot disagree about it.</remarks>
        public bool ShowQuantity => IsOccupied && Quantity > 1;

        /// <summary>
        /// Whether an icon address exists to load.
        /// </summary>
        /// <remarks>False for unauthored art. A view is expected to draw a placeholder
        /// rather than nothing and must never treat this as an error: missing art is an
        /// ordinary state during development, not a fault.</remarks>
        public bool HasIcon => IsOccupied && Icon.IsValid;

        /// <summary>An empty slot at a position.</summary>
        public static ItemSlotViewData Empty(int slotIndex)
        {
            return new ItemSlotViewData(slotIndex, DefinitionId.None, InstanceId.None,
                0, AssetRef.None, default, ItemCategory.Misc, false);
        }

        /// <summary>
        /// A slot showing an owned item.
        /// </summary>
        /// <remarks>
        /// A null or unresolvable definition produces an <em>occupied but unnamed</em>
        /// slot rather than an exception or an empty one. Content can be removed by a
        /// patch while a save still references it, and a bag that silently loses the row
        /// would hide the problem; showing a slot with no icon and no name makes it
        /// visible without crashing anybody's inventory.
        /// </remarks>
        public static ItemSlotViewData From(int slotIndex, DefinitionId definitionId,
            InstanceId instanceId, int quantity, ItemDefinition definition)
        {
            if (!definitionId.IsValid) return Empty(slotIndex);

            if (definition == null)
            {
                return new ItemSlotViewData(slotIndex, definitionId, instanceId,
                    quantity < 0 ? 0 : quantity, AssetRef.None, default, ItemCategory.Misc, false);
            }

            return new ItemSlotViewData(slotIndex, definitionId, instanceId,
                quantity < 0 ? 0 : quantity, definition.Icon, definition.NameKey,
                definition.Category, definition is EquipmentDefinition);
        }

        /// <summary>
        /// The paperdoll form of a square.
        /// </summary>
        /// <remarks>
        /// Lets <see cref="EquipmentPanel"/> reuse <see cref="ItemSlotView"/> rather than
        /// grow a second slot renderer that would drift from this one -- a worn helm and a
        /// helm in the bag are the same square with the same rules.
        ///
        /// The index is the position in the panel's layout, not a container slot, because
        /// equipment is addressed by <see cref="EquipmentSlot"/> and has no index of its
        /// own. Quantity is one: equipment never stacks, so
        /// <see cref="ShowQuantity"/> is false and no number is drawn.
        /// </remarks>
        public static ItemSlotViewData ForEquipment(int slotIndex, EquipmentSlotViewData equipped)
        {
            if (equipped.IsEmpty) return Empty(slotIndex);

            return new ItemSlotViewData(slotIndex, equipped.DefinitionId, equipped.InstanceId,
                1, equipped.Icon, equipped.NameKey, ItemCategory.Equipment, true);
        }

        public override string ToString()
        {
            return IsEmpty
                ? "[" + SlotIndex + "] empty"
                : "[" + SlotIndex + "] " + DefinitionId + (ShowQuantity ? " x" + Quantity : string.Empty);
        }
    }
}
