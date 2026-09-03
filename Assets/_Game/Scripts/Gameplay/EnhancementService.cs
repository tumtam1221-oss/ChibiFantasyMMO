using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// Enhances equipment. The only thing that may change an enhancement level.
    /// </summary>
    /// <remarks>
    /// <b>Validate fully, then mutate.</b> Every check -- ownership, the rule, the ceiling,
    /// the materials, the cost, the coherence of the authored step -- runs before a single
    /// write. That is the same contract <see cref="ItemContainerState"/>,
    /// <see cref="EquipmentService"/> and <see cref="ItemUseService"/> keep, and it is what
    /// makes "a refused attempt costs nothing" true by construction rather than by
    /// unwinding afterwards.
    ///
    /// <b>The mutation boundary is one block.</b> Once validation passes, materials and
    /// currency are consumed and the outcome applied, in that order, with nothing between
    /// them that can fail. This is <em>not</em> a transaction and is not claimed to be: the
    /// architecture has no rollback, so the design goal is that nothing after the boundary
    /// is capable of failing. Consumption is checked against the same container it will be
    /// taken from, so the removal cannot come up short.
    ///
    /// <b>The roll is injected.</b> See <see cref="IRandomResultSource"/>. Nothing here
    /// decides odds; it asks, and applies what content authored for that answer.
    ///
    /// <b>Nothing here knows an item.</b> No <see cref="DefinitionId"/> is compared to a
    /// literal. Which materials, which odds, which consequence -- all read from the step.
    /// </remarks>
    public static class EnhancementService
    {
        /// <summary>Everything an attempt needs beyond the piece itself.</summary>
        public readonly struct Context
        {
            public Context(IDefinitionRegistry<ItemDefinition> items,
                IDefinitionRegistry<EnhancementDefinition> enhancements,
                IDefinitionRegistry<RarityDefinition> rarities = null,
                IRandomResultSource results = null,
                OwnerId owner = default)
            {
                Items = items;
                Enhancements = enhancements;
                Rarities = rarities;
                Results = results ?? AlwaysSucceeds.Instance;
                Owner = owner;
            }

            public IDefinitionRegistry<ItemDefinition> Items { get; }

            public IDefinitionRegistry<EnhancementDefinition> Enhancements { get; }

            /// <summary>Optional. Without it a tier's enhancement ceiling cannot apply.</summary>
            public IDefinitionRegistry<RarityDefinition> Rarities { get; }

            /// <summary>
            /// Decides success.
            /// </summary>
            /// <remarks>Defaults to <see cref="AlwaysSucceeds"/> rather than to failure: a
            /// caller that forgot to wire a generator must not silently eat a player's
            /// materials.</remarks>
            public IRandomResultSource Results { get; }

            /// <summary>Who is acting. Left invalid when a caller asserts no ownership.</summary>
            public OwnerId Owner { get; }

            public EquipmentModifierResolver.Context Resolver =>
                new EquipmentModifierResolver.Context(Items, Rarities, Enhancements);
        }

        /// <summary>
        /// Attempts to raise the enhancement level of the piece in an inventory slot.
        /// </summary>
        /// <param name="inventory">Container holding both the piece and the materials.</param>
        /// <param name="slotIndex">Which slot the piece is in.</param>
        /// <param name="context">Registries, the roll source and the acting owner.</param>
        public static EnhancementResult TryEnhance(ItemContainerState inventory, int slotIndex,
            in Context context)
        {
            if (inventory == null || context.Items == null || context.Enhancements == null)
                return EnhancementResult.Rejected(EnhancementRejection.MissingContext);

            if (!inventory.IsValidIndex(slotIndex))
                return EnhancementResult.Rejected(EnhancementRejection.InvalidEquipment);

            ItemSlot slot = inventory.GetSlot(slotIndex);
            if (slot.IsEmpty)
                return EnhancementResult.Rejected(EnhancementRejection.InvalidEquipment);

            var piece = slot.Content as EquipmentInstance;
            if (piece == null)
                return EnhancementResult.Rejected(EnhancementRejection.InvalidEquipment);

            return TryEnhance(piece, inventory, context, slotIndex);
        }

        /// <summary>
        /// Attempts to raise the enhancement level of a worn piece.
        /// </summary>
        /// <remarks>Materials still come out of the inventory: a piece being worn changes
        /// where it lives, not where its cost is paid from.</remarks>
        public static EnhancementResult TryEnhance(CharacterEquipmentState equipment,
            EquipmentSlot slot, ItemContainerState inventory, in Context context)
        {
            if (equipment == null || inventory == null
                || context.Items == null || context.Enhancements == null)
            {
                return EnhancementResult.Rejected(EnhancementRejection.MissingContext);
            }

            EquipmentInstance worn;
            if (!equipment.TryGet(slot, out worn) || worn == null)
                return EnhancementResult.Rejected(EnhancementRejection.InvalidEquipment);

            return TryEnhance(worn, inventory, context, -1);
        }

        /// <summary>
        /// The shared path.
        /// </summary>
        /// <param name="pieceSlot">
        /// Where the piece sits in the container, or -1 when it is worn. Needed only so a
        /// destroyed piece can be removed from the right place.
        /// </param>
        private static EnhancementResult TryEnhance(EquipmentInstance piece,
            ItemContainerState inventory, in Context context, int pieceSlot)
        {
            InstanceId id = piece.InstanceId;
            int level = piece.EnhancementLevel;

            if (context.Owner.IsValid && piece.Owner != context.Owner)
                return EnhancementResult.Rejected(EnhancementRejection.NotOwner, id, level);

            ItemDefinition definition;
            if (!context.Items.TryGet(piece.DefinitionId, out definition) || definition == null)
                return EnhancementResult.Rejected(EnhancementRejection.InvalidDefinition, id, level);

            var equipment = definition as EquipmentDefinition;
            if (equipment == null)
                return EnhancementResult.Rejected(EnhancementRejection.InvalidDefinition, id, level);

            if (!equipment.Enhanceable)
                return EnhancementResult.Rejected(EnhancementRejection.NotEnhanceable, id, level);

            EquipmentModifierResolver.Context resolver = context.Resolver;

            EnhancementDefinition rule = EquipmentModifierResolver.ResolveRule(equipment, resolver);
            if (rule == null)
                return EnhancementResult.Rejected(EnhancementRejection.InvalidRule, id, level);

            if (level < rule.MinLevel)
                return EnhancementResult.Rejected(EnhancementRejection.NoStepForLevel, id, level);

            int ceiling = EquipmentModifierResolver.MaxEnhancementLevel(piece, equipment, resolver);
            if (ceiling > 0 && level >= ceiling)
                return EnhancementResult.Rejected(EnhancementRejection.AlreadyMaxLevel, id, level);

            EnhancementStep step;
            if (!EquipmentModifierResolver.TryGetStep(rule, level, out step))
                return EnhancementResult.Rejected(EnhancementRejection.NoStepForLevel, id, level);

            EnhancementRejection shape = CheckStep(step, level);
            if (shape != EnhancementRejection.None)
                return EnhancementResult.Rejected(shape, id, level);

            // ---- cost, checked against the container it will be taken from --------------

            int materialsNeeded = step.MaterialItem.IsValid && step.MaterialAmount > 0
                ? step.MaterialAmount
                : 0;

            if (materialsNeeded > 0 && inventory.CountOf(step.MaterialItem) < materialsNeeded)
                return EnhancementResult.Rejected(EnhancementRejection.MissingMaterial, id, level);

            // Currency is an inventory item like any other; no wallet system exists.
            int currencyNeeded = step.CurrencyCost > 0 ? step.CurrencyCost : 0;
            DefinitionId currencyItem = rule.CurrencyItem;

            if (currencyNeeded > 0)
            {
                if (!currencyItem.IsValid)
                    return EnhancementResult.Rejected(EnhancementRejection.InvalidStep, id, level);

                int held = inventory.CountOf(currencyItem);

                // The same stack can pay for both when the material IS the currency.
                int alsoNeeded = currencyItem == step.MaterialItem ? materialsNeeded : 0;

                if (held < currencyNeeded + alsoNeeded)
                    return EnhancementResult.Rejected(EnhancementRejection.InsufficientCost, id, level);
            }

            // ---- mutation boundary: nothing below is allowed to fail --------------------

            if (materialsNeeded > 0) inventory.RemoveByDefinition(step.MaterialItem, materialsNeeded);
            if (currencyNeeded > 0) inventory.RemoveByDefinition(currencyItem, currencyNeeded);

            bool succeeded = context.Results.Succeeds(step.SuccessChance);

            if (succeeded)
            {
                piece.SetEnhancementLevel(level + 1);

                return EnhancementResult.Accepted(EnhancementOutcome.Upgraded, id, level, level + 1,
                    materialsNeeded, currencyNeeded, piece.Revision);
            }

            return ApplyFailure(piece, inventory, step, id, level, materialsNeeded, currencyNeeded,
                pieceSlot);
        }

        /// <summary>
        /// Carries out whatever content authored for a failed roll.
        /// </summary>
        /// <remarks>
        /// Nothing is improvised. A piece is downgraded, reset or destroyed only because a
        /// step said so; the default is that the level holds and only the materials are
        /// gone. <see cref="EnhancementFailureBehavior.None"/> and
        /// <see cref="EnhancementFailureBehavior.LoseMaterials"/> both mean "keep the
        /// level" -- the materials are already spent above, which is what makes them the
        /// same outcome here.
        /// </remarks>
        private static EnhancementResult ApplyFailure(EquipmentInstance piece,
            ItemContainerState inventory, EnhancementStep step, InstanceId id, int level,
            int materials, int currency, int pieceSlot)
        {
            switch (step.FailureBehavior)
            {
                case EnhancementFailureBehavior.DegradeLevel:
                    piece.SetEnhancementLevel(level - 1);
                    return EnhancementResult.Accepted(EnhancementOutcome.FailedDowngraded, id,
                        level, level - 1, materials, currency, piece.Revision);

                case EnhancementFailureBehavior.ResetToZero:
                    piece.SetEnhancementLevel(0);
                    return EnhancementResult.Accepted(EnhancementOutcome.FailedReset, id,
                        level, 0, materials, currency, piece.Revision);

                case EnhancementFailureBehavior.DestroyItem:
                    if (pieceSlot >= 0) inventory.RemoveAt(pieceSlot, 1);

                    return EnhancementResult.Accepted(EnhancementOutcome.FailedDestroyed, id,
                        level, 0, materials, currency, piece.Revision);

                default:
                    return EnhancementResult.Accepted(EnhancementOutcome.FailedKept, id,
                        level, level, materials, currency, piece.Revision);
            }
        }

        /// <summary>
        /// Whether an authored step is coherent enough to act on.
        /// </summary>
        /// <remarks>
        /// Caught here as well as by content validation because a service must not act on
        /// nonsense that reached runtime anyway. A downgrade authored at level zero is the
        /// case that matters: clamping it to zero would silently turn a downgrade into a
        /// keep, applying a consequence content did not choose.
        /// </remarks>
        private static EnhancementRejection CheckStep(EnhancementStep step, int level)
        {
            if (step.SuccessChance > 1f) return EnhancementRejection.InvalidStep;
            if (step.MaterialAmount < 0) return EnhancementRejection.InvalidStep;
            if (step.CurrencyCost < 0) return EnhancementRejection.InvalidStep;

            if (step.MaterialAmount > 0 && !step.MaterialItem.IsValid)
                return EnhancementRejection.InvalidStep;

            if (step.FailureBehavior == EnhancementFailureBehavior.DegradeLevel && level <= 0)
                return EnhancementRejection.InvalidFailureBehavior;

            return EnhancementRejection.None;
        }
    }
}
