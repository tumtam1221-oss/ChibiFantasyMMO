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
            DefinitionId[] allowedClasses, DefinitionId[] allowedJobs, StatModifier[] modifiers)
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
            if (definition == null || !definitionId.IsValid) return None;

            var equipment = definition as EquipmentDefinition;

            if (equipment == null)
            {
                return new ItemTooltipData(true, definitionId, definition.NameKey,
                    definition.DescriptionKey, definition.Category, quantity,
                    false, EquipmentSlot.None, 0, null, null, null);
            }

            return new ItemTooltipData(true, definitionId, equipment.NameKey,
                equipment.DescriptionKey, equipment.Category, quantity,
                true, equipment.Slot, equipment.LevelRequirement,
                equipment.AllowedClasses, equipment.AllowedJobs, equipment.BaseStatModifiers);
        }
    }
}
