using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// Works out everything one piece of equipment contributes.
    /// </summary>
    /// <remarks>
    /// <b>Resolved, never accumulated.</b> Every call rebuilds the answer from the piece's
    /// <em>current</em> state: its definition, its rarity tier, the single enhancement
    /// level it is at now, and the stones in its sockets. Nothing is added to a running
    /// total anywhere, which is what makes "enhance to +5" produce the +5 modifiers rather
    /// than the sum of +1 through +5, and what makes repeated recalculation identical by
    /// construction rather than by care.
    ///
    /// <b>Order is fixed and stated once.</b> base -> rarity -> enhancement -> enchant ->
    /// card. Cards were appended at the end rather than inserted, so no existing source
    /// moved and Phase 05 and Phase 09 semantics are exactly what they were.
    /// It matters only for readability today, because
    /// <see cref="DerivedStatsCalculator"/> sums flats and then applies percents
    /// regardless of arrival order; stating it here means nobody has to guess later.
    ///
    /// <b>It collects; it does not compute.</b> Not one arithmetic operation on a stat
    /// value appears below. How modifiers stack, round and clamp is
    /// <see cref="DerivedStatsCalculator"/>'s job and is not duplicated -- that is the
    /// whole reason this returns a list of authored <see cref="StatModifier"/> rather than
    /// a total.
    /// </remarks>
    public static class EquipmentModifierResolver
    {
        /// <summary>The registries needed to resolve a piece beyond its own definition.</summary>
        /// <remarks>Each is optional. A caller with no rarity registry gets base and
        /// enhancement modifiers and no tier bonus -- less information, never wrong
        /// information.</remarks>
        public readonly struct Context
        {
            public Context(IDefinitionRegistry<ItemDefinition> items,
                IDefinitionRegistry<RarityDefinition> rarities = null,
                IDefinitionRegistry<EnhancementDefinition> enhancements = null,
                IDefinitionRegistry<CardDefinition> cards = null)
            {
                Items = items;
                Rarities = rarities;
                Enhancements = enhancements;
                Cards = cards;
            }

            public IDefinitionRegistry<ItemDefinition> Items { get; }

            public IDefinitionRegistry<RarityDefinition> Rarities { get; }

            public IDefinitionRegistry<EnhancementDefinition> Enhancements { get; }

            /// <summary>
            /// Where socketed cards are resolved.
            /// </summary>
            /// <remarks>Optional and last, so every existing caller compiles and behaves
            /// unchanged: with no card registry a piece resolves exactly as it did before
            /// cards existed.</remarks>
            public IDefinitionRegistry<CardDefinition> Cards { get; }

            public bool IsUsable => Items != null;
        }

        /// <summary>
        /// Appends everything a worn piece grants to <paramref name="into"/>.
        /// </summary>
        /// <remarks>
        /// Appends rather than clears, so a caller can walk several worn pieces into one
        /// list -- which is exactly what
        /// <see cref="CharacterEquipmentState.CollectModifiers(in Context, List{StatModifier})"/>
        /// does.
        /// </remarks>
        public static void Collect(EquipmentInstance worn, in Context context,
            List<StatModifier> into)
        {
            if (into == null || worn == null || !context.IsUsable) return;

            EquipmentDefinition equipment = ResolveEquipment(worn, context);
            if (equipment == null) return;

            // 1. base -- what the item is worth unenhanced, untiered, unsocketed.
            Append(equipment.BaseStatModifiers, into);

            // 2. rarity -- once for the tier, whatever the level.
            RarityDefinition rarity = ResolveRarity(worn, equipment, context);
            if (rarity != null) Append(rarity.StatModifiers, into);

            // 3. enhancement -- the modifiers of the level it is at, not of every level
            //    it passed through.
            AppendEnhancement(worn, equipment, context, into);

            // 4. enchant -- read off each stone's definition, so re-authoring a stone
            //    updates every piece already carrying it.
            AppendEnchants(worn, context, into);

            // 5. card -- the same rule again, through the card service that owns the socket
            //    set. Delegated rather than duplicated, so there is one reading of what a
            //    socketed card is worth.
            CardSocketService.CollectModifiers(worn, context.Cards, into);
        }

        /// <summary>Convenience overload that allocates.</summary>
        public static List<StatModifier> Collect(EquipmentInstance worn, in Context context)
        {
            var list = new List<StatModifier>();
            Collect(worn, context, list);
            return list;
        }

        /// <summary>
        /// The modifiers a piece would have at some other enhancement level.
        /// </summary>
        /// <remarks>
        /// What a preview is built from. It takes the level as an argument and writes
        /// nothing: the instance is read for its rarity and its stones, never for the level
        /// being asked about, so previewing +6 on a +5 sword cannot change the sword. That
        /// is the property <see cref="Collect"/> shares -- both are pure -- and it is why
        /// there is no separate preview code path to drift.
        /// </remarks>
        public static void CollectAtLevel(EquipmentInstance worn, int enhancementLevel,
            in Context context, List<StatModifier> into)
        {
            if (into == null || worn == null || !context.IsUsable) return;

            EquipmentDefinition equipment = ResolveEquipment(worn, context);
            if (equipment == null) return;

            Append(equipment.BaseStatModifiers, into);

            RarityDefinition rarity = ResolveRarity(worn, equipment, context);
            if (rarity != null) Append(rarity.StatModifiers, into);

            AppendEnhancementAt(equipment, enhancementLevel, context, into);
            AppendEnchants(worn, context, into);
            CardSocketService.CollectModifiers(worn, context.Cards, into);
        }

        /// <summary>
        /// The tier a piece is actually at.
        /// </summary>
        /// <remarks>The instance's override when it set one, otherwise the authored rarity.
        /// Resolved in one place so no caller has to know that precedence, and so a piece
        /// cannot be read as two different tiers by two different screens.</remarks>
        public static DefinitionId EffectiveRarityId(EquipmentInstance worn, ItemDefinition definition)
        {
            if (worn != null && worn.Rarity.IsValid) return worn.Rarity;
            return definition == null ? DefinitionId.None : definition.Rarity;
        }

        /// <summary>Resolves the tier definition, or null.</summary>
        public static RarityDefinition ResolveRarity(EquipmentInstance worn,
            ItemDefinition definition, in Context context)
        {
            if (context.Rarities == null) return null;

            DefinitionId id = EffectiveRarityId(worn, definition);
            if (!id.IsValid) return null;

            RarityDefinition rarity;
            return context.Rarities.TryGet(id, out rarity) ? rarity : null;
        }

        /// <summary>
        /// How many stones a piece can hold.
        /// </summary>
        /// <remarks>The item's authored sockets plus whatever its tier adds. Additive so a
        /// tier can only widen the piece: a subtractive tier would orphan stones already
        /// socketed, which there is no way to undo cleanly.</remarks>
        public static int EnchantCapacity(EquipmentInstance worn, EquipmentDefinition equipment,
            in Context context)
        {
            if (equipment == null) return 0;

            int capacity = equipment.StatusStoneSlots;
            if (capacity < 0) capacity = 0;

            RarityDefinition rarity = ResolveRarity(worn, equipment, context);
            if (rarity != null && rarity.BonusEnchantSlots > 0) capacity += rarity.BonusEnchantSlots;

            return capacity;
        }

        /// <summary>
        /// The highest level a piece may reach.
        /// </summary>
        /// <remarks>
        /// The strictest authored ceiling wins: the item's, the tier's, and the enhancement
        /// track's <see cref="EnhancementDefinition.MaxLevel"/>. A cap is a restriction, so
        /// taking the maximum of several would let one authored value quietly lift another.
        ///
        /// Zero from any single source means "this source imposes none" rather than "no
        /// enhancement allowed", which is what an unauthored field reads as.
        /// </remarks>
        public static int MaxEnhancementLevel(EquipmentInstance worn, EquipmentDefinition equipment,
            in Context context)
        {
            if (equipment == null) return 0;

            int cap = equipment.MaxEnhancementLevel > 0 ? equipment.MaxEnhancementLevel : int.MaxValue;

            RarityDefinition rarity = ResolveRarity(worn, equipment, context);
            if (rarity != null && rarity.MaxEnhancementLevel > 0 && rarity.MaxEnhancementLevel < cap)
            {
                cap = rarity.MaxEnhancementLevel;
            }

            EnhancementDefinition rule = ResolveRule(equipment, context);
            if (rule != null && rule.MaxLevel > 0 && rule.MaxLevel < cap) cap = rule.MaxLevel;

            return cap == int.MaxValue ? 0 : cap;
        }

        /// <summary>Resolves the enhancement track a piece upgrades by, or null.</summary>
        public static EnhancementDefinition ResolveRule(EquipmentDefinition equipment,
            in Context context)
        {
            if (equipment == null || context.Enhancements == null) return null;
            if (!equipment.EnhancementRule.IsValid) return null;

            EnhancementDefinition rule;
            return context.Enhancements.TryGet(equipment.EnhancementRule, out rule) ? rule : null;
        }

        /// <summary>
        /// The authored step that advances from a level.
        /// </summary>
        /// <remarks>Steps are matched by <see cref="EnhancementStep.FromLevel"/> rather than
        /// by array position, so an author may list them in any order and a missing level is
        /// a missing step rather than a silent off-by-one.</remarks>
        public static bool TryGetStep(EnhancementDefinition rule, int fromLevel,
            out EnhancementStep step)
        {
            step = default;
            if (rule == null) return false;

            EnhancementStep[] steps = rule.Steps;
            if (steps == null) return false;

            for (int i = 0; i < steps.Length; i++)
            {
                if (steps[i].FromLevel != fromLevel) continue;

                step = steps[i];
                return true;
            }

            return false;
        }

        private static EquipmentDefinition ResolveEquipment(EquipmentInstance worn, in Context context)
        {
            if (!worn.DefinitionId.IsValid) return null;

            ItemDefinition definition;
            if (!context.Items.TryGet(worn.DefinitionId, out definition)) return null;

            return definition as EquipmentDefinition;
        }

        /// <summary>
        /// Appends the modifiers of the level the piece is at.
        /// </summary>
        /// <remarks>
        /// The step whose <see cref="EnhancementStep.FromLevel"/> is one below the current
        /// level is the one that produced it, and its
        /// <see cref="EnhancementStep.GrantedModifiers"/> are what the piece is worth now.
        /// Level zero grants nothing, so an unenhanced piece contributes only its base.
        /// </remarks>
        private static void AppendEnhancement(EquipmentInstance worn, EquipmentDefinition equipment,
            in Context context, List<StatModifier> into)
        {
            AppendEnhancementAt(equipment, worn.EnhancementLevel, context, into);
        }

        private static void AppendEnhancementAt(EquipmentDefinition equipment, int level,
            in Context context, List<StatModifier> into)
        {
            if (level <= 0) return;

            EnhancementDefinition rule = ResolveRule(equipment, context);
            if (rule == null) return;

            EnhancementStep step;
            if (!TryGetStep(rule, level - 1, out step)) return;

            Append(step.GrantedModifiers, into);
        }

        private static void AppendEnchants(EquipmentInstance worn, in Context context,
            List<StatModifier> into)
        {
            IReadOnlyList<EquipmentEnchant> enchants = worn.Enchants;

            for (int i = 0; i < enchants.Count; i++)
            {
                EquipmentEnchant enchant = enchants[i];
                if (!enchant.IsValid) continue;

                ItemDefinition stone;
                if (!context.Items.TryGet(enchant.Stone, out stone) || stone == null) continue;
                if (!stone.IsStatusStone) continue;

                StatModifier[] modifiers = stone.StoneConfig.StatModifiers;

                // Rank scales a flat modifier and nothing else: a percentage that scaled
                // with rank would compound, and no content authors that yet.
                for (int m = 0; m < modifiers.Length; m++)
                {
                    StatModifier modifier = modifiers[m];

                    if (enchant.Rank > 1 && modifier.Kind == StatModifierKind.Flat)
                    {
                        into.Add(new StatModifier(modifier.Stat, modifier.Kind,
                            modifier.Value * enchant.Rank));
                        continue;
                    }

                    into.Add(modifier);
                }
            }
        }

        private static void Append(StatModifier[] modifiers, List<StatModifier> into)
        {
            if (modifiers == null) return;

            for (int i = 0; i < modifiers.Length; i++) into.Add(modifiers[i]);
        }
    }
}
