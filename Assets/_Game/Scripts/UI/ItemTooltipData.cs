using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.UI
{
    /// <summary>
    /// What a tooltip says about one item.
    /// </summary>
    /// <remarks>
    /// <b>Read-only, and it computes nothing.</b> The stat lines are the authored
    /// <see cref="EquipmentDefinition.BaseStatModifiers"/> copied verbatim. There is no
    /// arithmetic here and no second stat calculator: a tooltip that worked out what a
    /// sword would do to a character's effective stats would be a competing implementation
    /// of <c>DerivedStatsCalculator</c>, and the two would drift.
    ///
    /// <b>Only what the schema already carries.</b> Name, category, quantity, slot, level
    /// requirement, class and job restrictions and the authored modifiers are all fields
    /// Phase 04 authored. Enhancement, enchantment, sockets and rarity progression are
    /// deliberately absent -- they belong to a later phase and inventing display for them
    /// now would show numbers nobody authored.
    /// </remarks>
    public readonly struct ItemTooltipData
    {
        private static readonly StatModifier[] NoModifiers = new StatModifier[0];
        private static readonly DefinitionId[] NoIds = new DefinitionId[0];

        private readonly StatModifier[] _modifiers;
        private readonly DefinitionId[] _allowedClasses;
        private readonly DefinitionId[] _allowedJobs;

        private ItemTooltipData(bool valid, DefinitionId definitionId, LocalizationKey nameKey,
            LocalizationKey descriptionKey, ItemCategory category, int quantity,
            bool isEquipment, EquipmentSlot slot, int levelRequirement,
            DefinitionId[] allowedClasses, DefinitionId[] allowedJobs, StatModifier[] modifiers,
            bool usable, ItemUseType useType, DefinitionId warpDestination,
            LocalizationKey warpDestinationName)
        {
            IsValid = valid;
            DefinitionId = definitionId;
            NameKey = nameKey;
            DescriptionKey = descriptionKey;
            Category = category;
            Quantity = quantity;
            IsEquipment = isEquipment;
            Slot = slot;
            LevelRequirement = levelRequirement;
            _allowedClasses = allowedClasses ?? NoIds;
            _allowedJobs = allowedJobs ?? NoIds;
            _modifiers = modifiers ?? NoModifiers;
            IsUsable = usable;
            UseType = useType;
            WarpDestination = warpDestination;
            WarpDestinationName = warpDestinationName;
        }

        /// <summary>False when there is nothing to show. A view draws nothing rather than throwing.</summary>
        public bool IsValid { get; }

        public DefinitionId DefinitionId { get; }

        public LocalizationKey NameKey { get; }

        public LocalizationKey DescriptionKey { get; }

        public ItemCategory Category { get; }

        public int Quantity { get; }

        public bool IsEquipment { get; }

        /// <summary>Meaningful only when <see cref="IsEquipment"/>.</summary>
        public EquipmentSlot Slot { get; }

        /// <summary>Zero means no level gate.</summary>
        public int LevelRequirement { get; }

        /// <summary>Empty means unrestricted, matching the schema's own convention.</summary>
        public IReadOnlyList<DefinitionId> AllowedClasses => _allowedClasses;

        public IReadOnlyList<DefinitionId> AllowedJobs => _allowedJobs;

        /// <summary>Authored modifiers, copied verbatim. Never a computed total.</summary>
        public IReadOnlyList<StatModifier> StatModifiers => _modifiers;

        public bool HasLevelRequirement => LevelRequirement > 0;

        public bool HasClassRestriction => _allowedClasses.Length > 0;

        public bool HasJobRestriction => _allowedJobs.Length > 0;

        public bool HasStatModifiers => _modifiers.Length > 0;

        /// <summary>
        /// Whether a player may use this item.
        /// </summary>
        /// <remarks>Both authored flags together: <c>Usable</c> and a configured
        /// <see cref="ItemUseType"/>. An item with one but not the other is not usable, and
        /// the tooltip must not promise otherwise.</remarks>
        public bool IsUsable { get; }

        /// <summary>What using it is for. See <see cref="ItemUseType"/>.</summary>
        public ItemUseType UseType { get; }

        /// <summary>
        /// Where a warp item goes.
        /// </summary>
        /// <remarks>Copied off the authored effect, not decided here. Invalid for
        /// everything else.</remarks>
        public DefinitionId WarpDestination { get; }

        /// <summary>
        /// The destination map's own name key.
        /// </summary>
        /// <remarks>Resolved from the <see cref="MapDefinition"/> rather than from the
        /// item, so a town renamed once is renamed everywhere and no town name is ever
        /// written into a scroll or into UI code.</remarks>
        public LocalizationKey WarpDestinationName { get; }

        public bool HasWarpDestination => WarpDestination.IsValid;

        /// <summary>Nothing to show. What a view gets for an empty or stale selection.</summary>
        public static ItemTooltipData None => default;

        /// <summary>
        /// Builds a tooltip from an authored definition.
        /// </summary>
        /// <remarks>A null definition yields <see cref="None"/>, which is what happens when
        /// a selection outlives the item it pointed at or content was removed by a
        /// patch.</remarks>
        public static ItemTooltipData From(DefinitionId definitionId, int quantity,
            ItemDefinition definition)
        {
            return From(definitionId, quantity, definition, default);
        }

        /// <summary>
        /// Builds a tooltip, resolving a warp item's destination name.
        /// </summary>
        /// <param name="definitionId">The item.</param>
        /// <param name="quantity">How many are in the stack.</param>
        /// <param name="definition">Authored item.</param>
        /// <param name="warpDestinationName">Name key of the destination map, when the
        /// caller could resolve one. Looking a map up needs a registry the UI assembly does
        /// not have, so the Client resolves it and passes it in.</param>
        public static ItemTooltipData From(DefinitionId definitionId, int quantity,
            ItemDefinition definition, LocalizationKey warpDestinationName)
        {
            if (definition == null || !definitionId.IsValid) return None;

            bool usable = definition.Usable && definition.UseType != ItemUseType.None
                && definition.UseEffects.Length > 0;

            DefinitionId destination = FindWarpDestination(definition);

            var equipment = definition as EquipmentDefinition;

            if (equipment == null)
            {
                return new ItemTooltipData(true, definitionId, definition.NameKey,
                    definition.DescriptionKey, definition.Category, quantity,
                    false, EquipmentSlot.None, 0, null, null, null,
                    usable, definition.UseType, destination, warpDestinationName);
            }

            return new ItemTooltipData(true, definitionId, equipment.NameKey,
                equipment.DescriptionKey, equipment.Category, quantity,
                true, equipment.Slot, equipment.LevelRequirement,
                equipment.AllowedClasses, equipment.AllowedJobs, equipment.BaseStatModifiers,
                usable, equipment.UseType, destination, warpDestinationName);
        }

        /// <summary>
        /// The map an authored warp effect points at, if any.
        /// </summary>
        /// <remarks>Reads the authored configuration; it does not decide it, and it does not
        /// validate it -- whether that destination is a town a scroll may reach is
        /// <c>ItemUseService</c>'s call. The tooltip only reports what the item says.</remarks>
        private static DefinitionId FindWarpDestination(ItemDefinition definition)
        {
            if (definition.UseType != ItemUseType.WarpTown) return DefinitionId.None;

            ItemUseEffect[] effects = definition.UseEffects;

            for (int i = 0; i < effects.Length; i++)
            {
                if (effects[i].Kind == ItemEffectKind.WarpToMap && effects[i].DestinationMap.IsValid)
                {
                    return effects[i].DestinationMap;
                }
            }

            return DefinitionId.None;
        }
    }
}
