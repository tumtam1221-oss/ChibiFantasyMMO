using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// Sockets status stones into equipment. The only thing that may change an enchant set.
    /// </summary>
    /// <remarks>
    /// <b>One model for every stone.</b> There is no STR stone path and no fire stone path.
    /// A stone's effect is its authored <see cref="StatusStoneConfig.StatModifiers"/> and
    /// its restrictions are authored references; this class reads them and compares no
    /// <see cref="DefinitionId"/> to a literal. A new stone is content.
    ///
    /// <b>Validate fully, then mutate.</b> The same contract every other service here
    /// keeps: compatibility, capacity, duplicates and the stone's own presence in the bag
    /// are all settled before a write, so a refused attempt costs nothing by construction.
    ///
    /// <b>What is stored is a reference, not a copy.</b> The socket record holds the
    /// stone's id, and its modifiers are read from the definition every time stats are
    /// resolved. Re-authoring a stone therefore updates every piece already carrying it,
    /// and no owned copy holds a stale duplicate of authored numbers.
    /// </remarks>
    public static class EnchantService
    {
        /// <summary>Everything an attempt needs beyond the piece and the stone.</summary>
        public readonly struct Context
        {
            public Context(IDefinitionRegistry<ItemDefinition> items,
                IDefinitionRegistry<RarityDefinition> rarities = null,
                IRandomResultSource results = null,
                OwnerId owner = default)
            {
                Items = items;
                Rarities = rarities;
                Results = results ?? AlwaysSucceeds.Instance;
                Owner = owner;
            }

            public IDefinitionRegistry<ItemDefinition> Items { get; }

            /// <summary>Optional. Without it a tier's bonus sockets cannot apply.</summary>
            public IDefinitionRegistry<RarityDefinition> Rarities { get; }

            /// <summary>Decides success. Defaults to always, so a missing generator is safe.</summary>
            public IRandomResultSource Results { get; }

            public OwnerId Owner { get; }

            public EquipmentModifierResolver.Context Resolver =>
                new EquipmentModifierResolver.Context(Items, Rarities);
        }

        /// <summary>
        /// Sockets the stone in one inventory slot into the piece in another.
        /// </summary>
        /// <param name="inventory">Container holding both.</param>
        /// <param name="equipmentSlot">Slot holding the piece.</param>
        /// <param name="stoneSlot">Slot holding the stone.</param>
        /// <param name="context">Registries, the roll source and the acting owner.</param>
        public static EnchantResult TryEnchant(ItemContainerState inventory, int equipmentSlot,
            int stoneSlot, in Context context)
        {
            if (inventory == null || context.Items == null)
                return EnchantResult.Rejected(EnchantRejection.MissingContext);

            if (!inventory.IsValidIndex(equipmentSlot))
                return EnchantResult.Rejected(EnchantRejection.InvalidEquipment);

            ItemSlot slot = inventory.GetSlot(equipmentSlot);
            var piece = slot.IsEmpty ? null : slot.Content as EquipmentInstance;

            if (piece == null) return EnchantResult.Rejected(EnchantRejection.InvalidEquipment);

            return TryEnchant(piece, inventory, stoneSlot, context);
        }

        /// <summary>Sockets a stone into a worn piece.</summary>
        /// <remarks>The stone still comes out of the bag: being worn changes where the piece
        /// lives, not where its cost is paid from.</remarks>
        public static EnchantResult TryEnchant(CharacterEquipmentState equipment,
            EquipmentSlot slot, ItemContainerState inventory, int stoneSlot, in Context context)
        {
            if (equipment == null || inventory == null || context.Items == null)
                return EnchantResult.Rejected(EnchantRejection.MissingContext);

            EquipmentInstance worn;
            if (!equipment.TryGet(slot, out worn) || worn == null)
                return EnchantResult.Rejected(EnchantRejection.InvalidEquipment);

            return TryEnchant(worn, inventory, stoneSlot, context);
        }

        private static EnchantResult TryEnchant(EquipmentInstance piece,
            ItemContainerState inventory, int stoneSlot, in Context context)
        {
            InstanceId id = piece.InstanceId;

            if (context.Owner.IsValid && piece.Owner != context.Owner)
                return EnchantResult.Rejected(EnchantRejection.NotOwner, id);

            ItemDefinition definition;
            if (!context.Items.TryGet(piece.DefinitionId, out definition) || definition == null)
                return EnchantResult.Rejected(EnchantRejection.InvalidDefinition, id);

            var equipment = definition as EquipmentDefinition;
            if (equipment == null)
                return EnchantResult.Rejected(EnchantRejection.InvalidDefinition, id);

            // ---- the stone -------------------------------------------------------------

            if (!inventory.IsValidIndex(stoneSlot))
                return EnchantResult.Rejected(EnchantRejection.InvalidStone, id);

            ItemSlot source = inventory.GetSlot(stoneSlot);
            if (source.IsEmpty) return EnchantResult.Rejected(EnchantRejection.InvalidStone, id);

            DefinitionId stoneId = source.DefinitionId;

            ItemDefinition stone;
            if (!context.Items.TryGet(stoneId, out stone) || stone == null)
                return EnchantResult.Rejected(EnchantRejection.InvalidStone, id, stoneId);

            if (!stone.IsStatusStone)
                return EnchantResult.Rejected(EnchantRejection.NotAStone, id, stoneId);

            if (source.Quantity < 1)
                return EnchantResult.Rejected(EnchantRejection.InvalidStone, id, stoneId);

            StatusStoneConfig config = stone.StoneConfig;

            if (config.SuccessChance > 1f)
                return EnchantResult.Rejected(EnchantRejection.InvalidRule, id, stoneId);

            // ---- compatibility, all authored -------------------------------------------

            EnchantRejection fit = CheckCompatibility(piece, equipment, config, context);
            if (fit != EnchantRejection.None)
                return EnchantResult.Rejected(fit, id, stoneId);

            // ---- capacity and duplicates -----------------------------------------------

            int capacity = EquipmentModifierResolver.EnchantCapacity(piece, equipment,
                context.Resolver);

            int socket = piece.FirstFreeSocket(capacity);
            if (socket < 0) return EnchantResult.Rejected(EnchantRejection.NoCapacity, id, stoneId);

            if (CountOf(piece, stoneId) >= config.MaxPerEquipment)
                return EnchantResult.Rejected(EnchantRejection.DuplicateNotAllowed, id, stoneId);

            // The roll is a question, not a mutation, so it is asked before the boundary --
            // which is what lets the consumption be decided once instead of taken and
            // handed back.
            bool succeeded = context.Results.Succeeds(config.SuccessChance);

            bool consumesStone = succeeded
                || config.FailureBehavior != EnchantFailureBehavior.KeepStone;

            // ---- mutation boundary: nothing below is allowed to fail --------------------

            if (consumesStone) inventory.RemoveAt(stoneSlot, 1);

            if (succeeded)
            {
                piece.AddEnchant(new EquipmentEnchant(stoneId, socket));

                return EnchantResult.Accepted(EnchantOutcome.Socketed, id, stoneId, socket,
                    1, piece.EnchantCount, piece.Revision);
            }

            return ApplyFailure(piece, config, id, stoneId, consumesStone ? 1 : 0);
        }

        /// <summary>
        /// Carries out whatever the stone authored for a failed roll.
        /// </summary>
        /// <remarks>
        /// Nothing is improvised: a stone survives, is lost, or takes the sockets with it
        /// only because the stone said so. No behaviour destroys the equipment -- that is an
        /// enhancement rule's business, not a stone's, and a stone that could break a sword
        /// would be a consequence no player expects from socketing.
        /// </remarks>
        private static EnchantResult ApplyFailure(EquipmentInstance piece,
            StatusStoneConfig config, InstanceId id, DefinitionId stoneId, int consumed)
        {
            switch (config.FailureBehavior)
            {
                case EnchantFailureBehavior.ClearSockets:
                    ClearSockets(piece);
                    return EnchantResult.Accepted(EnchantOutcome.FailedSocketsCleared, id, stoneId,
                        -1, consumed, piece.EnchantCount, piece.Revision);

                case EnchantFailureBehavior.KeepStone:
                    return EnchantResult.Accepted(EnchantOutcome.FailedStoneKept, id, stoneId,
                        -1, consumed, piece.EnchantCount, piece.Revision);

                default:
                    return EnchantResult.Accepted(EnchantOutcome.FailedStoneLost, id, stoneId,
                        -1, consumed, piece.EnchantCount, piece.Revision);
            }
        }

        /// <summary>
        /// Whether the stone fits the piece.
        /// </summary>
        /// <remarks>
        /// Every restriction is authored on the stone, and an empty restriction means
        /// unrestricted -- so a stone authored before a field existed fits everything
        /// rather than nothing.
        /// </remarks>
        private static EnchantRejection CheckCompatibility(EquipmentInstance piece,
            EquipmentDefinition equipment, StatusStoneConfig config, in Context context)
        {
            if (config.AllowedCategory != EquipmentCategory.None
                && equipment.EquipmentCategory != config.AllowedCategory)
            {
                return EnchantRejection.NotCompatible;
            }

            EquipmentSubtype[] subtypes = config.AllowedSubtypes;
            if (subtypes.Length > 0 && !Contains(subtypes, equipment.Subtype))
                return EnchantRejection.NotCompatible;

            EquipmentSlot[] slots = config.AllowedSlots;
            if (slots.Length > 0 && !Contains(slots, equipment.Slot))
                return EnchantRejection.NotCompatible;

            DefinitionId[] rarities = config.AllowedRarities;
            if (rarities.Length > 0)
            {
                DefinitionId tier = EquipmentModifierResolver.EffectiveRarityId(piece, equipment);
                if (!Contains(rarities, tier)) return EnchantRejection.NotCompatible;
            }

            if (config.MinimumItemLevel > 0 && equipment.LevelRequirement < config.MinimumItemLevel)
                return EnchantRejection.RequirementNotMet;

            return EnchantRejection.None;
        }

        private static void ClearSockets(EquipmentInstance piece)
        {
            // Walked highest-first so removing one cannot skip the next.
            for (int i = piece.EnchantCount - 1; i >= 0; i--)
            {
                piece.RemoveEnchantAt(piece.Enchants[i].SocketIndex);
            }
        }

        private static int CountOf(EquipmentInstance piece, DefinitionId stone)
        {
            int count = 0;

            for (int i = 0; i < piece.Enchants.Count; i++)
            {
                if (piece.Enchants[i].Stone == stone) count++;
            }

            return count;
        }

        private static bool Contains(EquipmentSubtype[] values, EquipmentSubtype value)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == value) return true;
            }

            return false;
        }

        private static bool Contains(EquipmentSlot[] values, EquipmentSlot value)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == value) return true;
            }

            return false;
        }

        private static bool Contains(DefinitionId[] values, DefinitionId value)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == value) return true;
            }

            return false;
        }
    }
}
