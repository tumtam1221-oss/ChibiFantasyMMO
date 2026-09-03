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

            // Per-copy progression is layered on afterwards by WithProgression, because
            // resolving it needs registries the UI assembly does not have.
            _enchants = NoEnchants;
            _effective = NoModifiers;
            EnhancementLevel = 0;
            RarityId = DefinitionId.None;
            RarityNameKey = default;
            EnchantCapacity = 0;
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

        /// <summary>
        /// This copy's enhancement level. Zero for unenhanced and for anything that is not
        /// equipment.
        /// </summary>
        /// <remarks>Per-copy progression, so it arrives from the instance rather than the
        /// definition. See <see cref="WithProgression"/>.</remarks>
        public int EnhancementLevel { get; }

        /// <summary>The tier this copy is actually at, override resolved.</summary>
        public DefinitionId RarityId { get; }

        /// <summary>The tier's own name key, when the caller could resolve the tier.</summary>
        public LocalizationKey RarityNameKey { get; }

        /// <summary>Stones socketed into this copy.</summary>
        public IReadOnlyList<EnchantSlotViewData> Enchants =>
            _enchants ?? (IReadOnlyList<EnchantSlotViewData>)NoEnchants;

        /// <summary>How many sockets the piece has in total, filled or not.</summary>
        public int EnchantCapacity { get; }

        /// <summary>
        /// Everything the piece grants right now.
        /// </summary>
        /// <remarks>
        /// Base, tier, level and stones together, computed by
        /// <c>EquipmentModifierResolver</c> and copied in. Distinct from
        /// <see cref="StatModifiers"/>, which is only what the item itself authors: a
        /// tooltip wants to show both "this sword is worth 10 STR" and "this one, +5 and
        /// Rare, is worth 25".
        ///
        /// Still not a character's effective stats. Nothing here knows the wearer.
        /// </remarks>
        public IReadOnlyList<StatModifier> EffectiveModifiers =>
            _effective ?? (IReadOnlyList<StatModifier>)NoModifiers;

        public bool HasEnhancement => EnhancementLevel > 0;

        public bool HasRarity => RarityId.IsValid;

        /// <summary>
        /// How many sockets actually hold a stone.
        /// </summary>
        /// <remarks><see cref="Enchants"/> carries empty sockets too, so a player can see
        /// the room left. Counting its length would report a piece with one stone in three
        /// sockets as full.</remarks>
        public int FilledEnchantCount
        {
            get
            {
                IReadOnlyList<EnchantSlotViewData> sockets = Enchants;
                int filled = 0;

                for (int i = 0; i < sockets.Count; i++)
                {
                    if (sockets[i].IsOccupied) filled++;
                }

                return filled;
            }
        }

        public bool HasEnchants => FilledEnchantCount > 0;

        private readonly EnchantSlotViewData[] _enchants;
        private readonly StatModifier[] _effective;

        private static readonly EnchantSlotViewData[] NoEnchants = new EnchantSlotViewData[0];

        /// <summary>
        /// Returns a copy carrying this piece's per-copy progression.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="From"/> because progression needs registries the UI
        /// assembly does not have -- the tier, the enhancement track, each stone. The
        /// Client resolves them and layers the result on, which keeps the definition-only
        /// path working unchanged for every caller that does not care.
        /// </remarks>
        public ItemTooltipData WithProgression(int enhancementLevel, DefinitionId rarityId,
            LocalizationKey rarityNameKey, int enchantCapacity,
            EnchantSlotViewData[] enchants, StatModifier[] effective)
        {
            if (!IsValid) return this;

            return new ItemTooltipData(this, enchants, effective,
                enhancementLevel < 0 ? 0 : enhancementLevel, rarityId, rarityNameKey,
                enchantCapacity < 0 ? 0 : enchantCapacity);
        }

        /// <summary>Copy constructor for <see cref="WithProgression"/>.</summary>
        private ItemTooltipData(in ItemTooltipData other, EnchantSlotViewData[] enchants,
            StatModifier[] effective, int enhancementLevel, DefinitionId rarityId,
            LocalizationKey rarityNameKey, int enchantCapacity)
        {
            IsValid = other.IsValid;
            DefinitionId = other.DefinitionId;
            NameKey = other.NameKey;
            DescriptionKey = other.DescriptionKey;
            Category = other.Category;
            Quantity = other.Quantity;
            IsEquipment = other.IsEquipment;
            Slot = other.Slot;
            LevelRequirement = other.LevelRequirement;
            _allowedClasses = other._allowedClasses;
            _allowedJobs = other._allowedJobs;
            _modifiers = other._modifiers;
            IsUsable = other.IsUsable;
            UseType = other.UseType;
            WarpDestination = other.WarpDestination;
            WarpDestinationName = other.WarpDestinationName;

            _enchants = enchants ?? NoEnchants;
            _effective = effective ?? NoModifiers;

            EnhancementLevel = enhancementLevel;
            RarityId = rarityId;
            RarityNameKey = rarityNameKey;
            EnchantCapacity = enchantCapacity;
        }

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
