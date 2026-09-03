using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.UI
{
    /// <summary>
    /// One paperdoll position and what is worn in it.
    /// </summary>
    /// <remarks>
    /// <b>Keyed by the existing <see cref="EquipmentSlot"/>.</b> Phase 04 authored the ten
    /// slots the game has; no slot list is defined here and none is invented. A panel
    /// draws whichever subset it is configured with.
    ///
    /// Like <see cref="ItemSlotViewData"/> this is a copied snapshot with no gameplay
    /// object in it, so a view cannot reach through it into
    /// <c>CharacterEquipmentState</c>.
    /// </remarks>
    public readonly struct EquipmentSlotViewData
    {
        private EquipmentSlotViewData(EquipmentSlot slot, DefinitionId definitionId,
            InstanceId instanceId, AssetRef icon, LocalizationKey nameKey)
        {
            Slot = slot;
            DefinitionId = definitionId;
            InstanceId = instanceId;
            Icon = icon;
            NameKey = nameKey;
        }

        public EquipmentSlot Slot { get; }

        public DefinitionId DefinitionId { get; }

        public InstanceId InstanceId { get; }

        public AssetRef Icon { get; }

        public LocalizationKey NameKey { get; }

        public bool IsEmpty => !DefinitionId.IsValid;

        public bool IsOccupied => DefinitionId.IsValid;

        /// <summary>See <see cref="ItemSlotViewData.HasIcon"/>: missing art is ordinary.</summary>
        public bool HasIcon => IsOccupied && Icon.IsValid;

        /// <summary>An empty paperdoll position.</summary>
        public static EquipmentSlotViewData Empty(EquipmentSlot slot)
        {
            return new EquipmentSlotViewData(slot, DefinitionId.None, InstanceId.None,
                AssetRef.None, default);
        }

        /// <summary>
        /// A position showing a worn piece.
        /// </summary>
        /// <remarks>The slot drawn is the one passed in rather than the one the definition
        /// authors, because the panel is laying out a fixed paperdoll and an unresolvable
        /// definition must still leave the position on screen.</remarks>
        public static EquipmentSlotViewData From(EquipmentSlot slot, DefinitionId definitionId,
            InstanceId instanceId, ItemDefinition definition)
        {
            if (!definitionId.IsValid) return Empty(slot);

            if (definition == null)
            {
                return new EquipmentSlotViewData(slot, definitionId, instanceId,
                    AssetRef.None, default);
            }

            return new EquipmentSlotViewData(slot, definitionId, instanceId,
                definition.Icon, definition.NameKey);
        }

        public override string ToString()
        {
            return IsEmpty ? Slot + ": empty" : Slot + ": " + DefinitionId;
        }
    }
}
