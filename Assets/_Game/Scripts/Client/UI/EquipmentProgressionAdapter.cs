using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.UI;

namespace ChibiFantasy.Client.UI
{
    /// <summary>
    /// Turns equipment progression into view data. The read half, like
    /// <see cref="InventoryViewAdapter"/>.
    /// </summary>
    /// <remarks>
    /// <b>Reads only.</b> Everything here resolves through
    /// <see cref="EquipmentModifierResolver"/>, which is pure, so building a preview cannot
    /// change a piece however many times a panel asks. That is the property the "preview
    /// must not mutate" rule needs, and it holds because of what the resolver is rather
    /// than because of care taken here.
    ///
    /// <b>It lives in Client for the same reason the other adapter does.</b> The UI
    /// assembly cannot see Gameplay, and something has to bridge them. It hands out
    /// snapshots; no panel ever holds an <c>EquipmentInstance</c>.
    /// </remarks>
    public static class EquipmentProgressionAdapter
    {
        /// <summary>The registries progression needs.</summary>
        public readonly struct Context
        {
            public Context(IDefinitionRegistry<ItemDefinition> items,
                IDefinitionRegistry<RarityDefinition> rarities = null,
                IDefinitionRegistry<EnhancementDefinition> enhancements = null,
                IDefinitionRegistry<StoneFusionDefinition> recipes = null)
            {
                Items = items;
                Rarities = rarities;
                Enhancements = enhancements;
                Recipes = recipes;
            }

            public IDefinitionRegistry<ItemDefinition> Items { get; }

            public IDefinitionRegistry<RarityDefinition> Rarities { get; }

            public IDefinitionRegistry<EnhancementDefinition> Enhancements { get; }

            public IDefinitionRegistry<StoneFusionDefinition> Recipes { get; }

            public bool IsUsable => Items != null;

            public EquipmentModifierResolver.Context Resolver =>
                new EquipmentModifierResolver.Context(Items, Rarities, Enhancements);
        }

        /// <summary>
        /// Everything an enhancement panel needs for one piece.
        /// </summary>
        /// <remarks>
        /// The preview is <see cref="EquipmentModifierResolver.CollectAtLevel"/> at one
        /// level higher. It is the same function that produces the current figures, so the
        /// two cannot disagree and there is no separate preview calculation to drift.
        /// </remarks>
        public static EnhancementViewData BuildEnhancement(ItemContainerState inventory,
            int slotIndex, in Context context)
        {
            if (inventory == null || !context.IsUsable || !inventory.IsValidIndex(slotIndex))
                return EnhancementViewData.None;

            ItemSlot slot = inventory.GetSlot(slotIndex);
            var piece = slot.IsEmpty ? null : slot.Content as EquipmentInstance;
            if (piece == null) return EnhancementViewData.None;

            return Build(piece, inventory, slotIndex, context);
        }

        /// <summary>The same, for a worn piece. Costs are still read from the bag.</summary>
        public static EnhancementViewData BuildEnhancement(CharacterEquipmentState equipment,
            EquipmentSlot slot, ItemContainerState inventory, in Context context)
        {
            if (equipment == null || !context.IsUsable) return EnhancementViewData.None;

            EquipmentInstance worn;
            if (!equipment.TryGet(slot, out worn) || worn == null) return EnhancementViewData.None;

            return Build(worn, inventory, -1, context);
        }

        private static EnhancementViewData Build(EquipmentInstance piece,
            ItemContainerState inventory, int slotIndex, in Context context)
        {
            ItemDefinition definition;
            if (!context.Items.TryGet(piece.DefinitionId, out definition) || definition == null)
                return EnhancementViewData.None;

            var equipment = definition as EquipmentDefinition;
            if (equipment == null || !equipment.Enhanceable) return EnhancementViewData.None;

            EquipmentModifierResolver.Context resolver = context.Resolver;

            int level = piece.EnhancementLevel;
            int ceiling = EquipmentModifierResolver.MaxEnhancementLevel(piece, equipment, resolver);

            EnhancementDefinition rule = EquipmentModifierResolver.ResolveRule(equipment, resolver);

            EnhancementStep step = default;
            bool hasStep = rule != null
                && EquipmentModifierResolver.TryGetStep(rule, level, out step);

            bool atCeiling = ceiling > 0 && level >= ceiling;

            var current = new List<StatModifier>();
            EquipmentModifierResolver.Collect(piece, resolver, current);

            var preview = new List<StatModifier>();
            if (hasStep && !atCeiling)
            {
                EquipmentModifierResolver.CollectAtLevel(piece, level + 1, resolver, preview);
            }

            DefinitionId currency = rule == null ? DefinitionId.None : rule.CurrencyItem;

            return EnhancementViewData.From(piece.DefinitionId, piece.InstanceId,
                definition.NameKey, slotIndex, level, ceiling,
                hasStep && !atCeiling, step.SuccessChance,
                step.MaterialItem, NameKeyOf(step.MaterialItem, context), step.MaterialAmount,
                Held(inventory, step.MaterialItem),
                currency, NameKeyOf(currency, context), step.CurrencyCost,
                Held(inventory, currency),
                step.FailureBehavior, current.ToArray(), preview.ToArray());
        }

        /// <summary>
        /// Fills <paramref name="into"/> with one entry per socket, empty ones included.
        /// </summary>
        /// <remarks>Empty sockets are produced so the panel draws a fixed shape and a player
        /// can see how much room is left.</remarks>
        public static void BuildEnchants(EquipmentInstance piece, in Context context,
            List<EnchantSlotViewData> into)
        {
            if (into == null) return;

            into.Clear();
            if (piece == null || !context.IsUsable) return;

            ItemDefinition definition;
            if (!context.Items.TryGet(piece.DefinitionId, out definition)) return;

            var equipment = definition as EquipmentDefinition;
            if (equipment == null) return;

            int capacity = EquipmentModifierResolver.EnchantCapacity(piece, equipment,
                context.Resolver);

            for (int socket = 0; socket < capacity; socket++)
            {
                into.Add(FindSocket(piece, socket, context));
            }

            // A stone in a socket beyond the current capacity is still shown: content may
            // have narrowed a tier, and a hidden stone is worse than a visible surprise.
            IReadOnlyList<EquipmentEnchant> enchants = piece.Enchants;

            for (int i = 0; i < enchants.Count; i++)
            {
                if (enchants[i].SocketIndex < capacity) continue;
                into.Add(Describe(enchants[i], context));
            }
        }

        /// <summary>What a recipe would cost and produce, against a container.</summary>
        public static FusionViewData BuildFusion(ItemContainerState inventory,
            DefinitionId recipeId, in Context context)
        {
            if (!context.IsUsable || context.Recipes == null) return FusionViewData.None;

            StoneFusionDefinition recipe;
            if (!recipeId.IsValid || !context.Recipes.TryGet(recipeId, out recipe) || recipe == null)
                return FusionViewData.None;

            FusionIngredient[] inputs = recipe.Inputs;
            var lines = new FusionIngredientViewData[inputs.Length];

            for (int i = 0; i < inputs.Length; i++)
            {
                ItemDefinition item;
                context.Items.TryGet(inputs[i].Item, out item);

                lines[i] = new FusionIngredientViewData(inputs[i].Item,
                    item == null ? default : item.NameKey,
                    item == null ? AssetRef.None : item.Icon,
                    inputs[i].Quantity, Held(inventory, inputs[i].Item));
            }

            ItemDefinition result;
            context.Items.TryGet(recipe.Result, out result);

            return FusionViewData.From(recipeId, recipe.NameKey, recipe.Result,
                result == null ? default : result.NameKey,
                result == null ? AssetRef.None : result.Icon,
                recipe.ResultQuantity, recipe.SuccessChance,
                recipe.CurrencyItem, recipe.CurrencyCost, Held(inventory, recipe.CurrencyItem),
                lines);
        }

        /// <summary>
        /// Layers a piece's progression onto a tooltip.
        /// </summary>
        /// <remarks>Separate from the tooltip's own construction because progression needs
        /// registries the UI assembly does not have. See
        /// <see cref="ItemTooltipData.WithProgression"/>.</remarks>
        public static ItemTooltipData WithProgression(ItemTooltipData tooltip,
            EquipmentInstance piece, in Context context)
        {
            if (!tooltip.IsValid || piece == null || !context.IsUsable) return tooltip;

            ItemDefinition definition;
            if (!context.Items.TryGet(piece.DefinitionId, out definition) || definition == null)
                return tooltip;

            var equipment = definition as EquipmentDefinition;
            if (equipment == null) return tooltip;

            EquipmentModifierResolver.Context resolver = context.Resolver;

            DefinitionId rarityId = EquipmentModifierResolver.EffectiveRarityId(piece, definition);
            RarityDefinition rarity = EquipmentModifierResolver.ResolveRarity(piece, definition,
                resolver);

            var sockets = new List<EnchantSlotViewData>();
            BuildEnchants(piece, context, sockets);

            var effective = new List<StatModifier>();
            EquipmentModifierResolver.Collect(piece, resolver, effective);

            return tooltip.WithProgression(piece.EnhancementLevel, rarityId,
                rarity == null ? default : rarity.NameKey,
                EquipmentModifierResolver.EnchantCapacity(piece, equipment, resolver),
                sockets.ToArray(), effective.ToArray());
        }

        private static EnchantSlotViewData FindSocket(EquipmentInstance piece, int socketIndex,
            in Context context)
        {
            IReadOnlyList<EquipmentEnchant> enchants = piece.Enchants;

            for (int i = 0; i < enchants.Count; i++)
            {
                if (enchants[i].SocketIndex != socketIndex) continue;
                return Describe(enchants[i], context);
            }

            return EnchantSlotViewData.Empty(socketIndex);
        }

        private static EnchantSlotViewData Describe(EquipmentEnchant enchant, in Context context)
        {
            ItemDefinition stone;
            context.Items.TryGet(enchant.Stone, out stone);

            return EnchantSlotViewData.From(enchant.SocketIndex, enchant.Stone, enchant.Rank,
                stone);
        }

        /// <summary>
        /// An item's authored name key.
        /// </summary>
        /// <remarks>Resolved here because the UI cannot: a key is authored content, and a
        /// panel that built one from an id would invent a key nobody wrote.</remarks>
        private static LocalizationKey NameKeyOf(DefinitionId item, in Context context)
        {
            if (!item.IsValid) return default;

            ItemDefinition definition;
            return context.Items.TryGet(item, out definition) && definition != null
                ? definition.NameKey
                : default;
        }

        /// <summary>How many of an item a container holds. Zero for anything unresolvable.</summary>
        private static int Held(ItemContainerState inventory, DefinitionId item)
        {
            if (inventory == null || !item.IsValid) return 0;
            return inventory.CountOf(item);
        }
    }
}
