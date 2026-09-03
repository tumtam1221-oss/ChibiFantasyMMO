using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// Uses an item. The only thing allowed to spend one.
    /// </summary>
    /// <remarks>
    /// <b>One pipeline, no item knows its own code path.</b> There is no potion service, no
    /// food service and no scroll service. An item's behaviour is
    /// <see cref="ItemDefinition.UseEffects"/>, read at runtime; this class resolves that
    /// configuration, validates it and executes it. No <see cref="DefinitionId"/> is
    /// compared against a literal anywhere below, which is what makes a new consumable a
    /// content change rather than a code change.
    ///
    /// <b>Validate fully, then mutate.</b> Every check runs before anything is written, the
    /// same contract <see cref="ItemContainerState"/> and <see cref="EquipmentService"/>
    /// keep. A refused use leaves the resource state and the stack exactly as they were, so
    /// "a rejection costs nothing" needs no rollback to be true.
    ///
    /// <b>What it does not do.</b> Buffs are <em>resolved and reported</em>, not applied --
    /// see <see cref="ItemBuffGrant"/> for why, and treat that as a real limitation. A warp
    /// is validated and its destination returned; travelling there is a later system's job
    /// and, in a served game, the server's.
    /// </remarks>
    public static class ItemUseService
    {
        /// <summary>Everything a use needs that the container does not carry.</summary>
        public readonly struct Context
        {
            public Context(IDefinitionRegistry<ItemDefinition> items,
                CharacterResourceState resources, ResourceLimits limits,
                IDefinitionRegistry<StatusEffectDefinition> statusEffects = null,
                IDefinitionRegistry<MapDefinition> maps = null,
                OwnerId owner = default,
                List<ItemBuffGrant> grantedBuffs = null)
            {
                Items = items;
                Resources = resources;
                Limits = limits;
                StatusEffects = statusEffects;
                Maps = maps;
                Owner = owner;
                GrantedBuffs = grantedBuffs;
            }

            public IDefinitionRegistry<ItemDefinition> Items { get; }

            /// <summary>The pools a recovery effect fills.</summary>
            public CharacterResourceState Resources { get; }

            /// <summary>
            /// Current ceilings.
            /// </summary>
            /// <remarks>Supplied per call for the reason <see cref="ResourceLimits"/>
            /// explains: a stored maximum goes stale the moment equipment changes.</remarks>
            public ResourceLimits Limits { get; }

            /// <summary>Needed only by items that grant status effects.</summary>
            public IDefinitionRegistry<StatusEffectDefinition> StatusEffects { get; }

            /// <summary>Needed only by items that warp.</summary>
            public IDefinitionRegistry<MapDefinition> Maps { get; }

            /// <summary>
            /// Who is acting.
            /// </summary>
            /// <remarks>Left invalid when a caller has no ownership to assert, in which case
            /// the check is skipped. A server would always supply it -- this is the seam an
            /// authoritative caller uses to refuse someone else's bag.</remarks>
            public OwnerId Owner { get; }

            /// <summary>
            /// Where granted buffs are written, on success only.
            /// </summary>
            /// <remarks>Caller-owned so a use allocates nothing, matching
            /// <c>CharacterEquipmentState.CollectModifiers</c>. Null is fine: the grants are
            /// then counted and discarded.</remarks>
            public List<ItemBuffGrant> GrantedBuffs { get; }
        }

        /// <summary>
        /// Uses one of whatever is in a slot.
        /// </summary>
        /// <param name="inventory">Container holding the item.</param>
        /// <param name="slotIndex">Which slot.</param>
        /// <param name="context">Registries, resources and ceilings.</param>
        public static ItemUseResult Use(ItemContainerState inventory, int slotIndex,
            in Context context)
        {
            if (inventory == null || context.Items == null || context.Resources == null)
                return ItemUseResult.Rejected(ItemUseRejection.MissingContext);

            if (!inventory.IsValidIndex(slotIndex))
                return ItemUseResult.Rejected(ItemUseRejection.SlotOutOfRange);

            ItemSlot slot = inventory.GetSlot(slotIndex);
            if (slot.IsEmpty) return ItemUseResult.Rejected(ItemUseRejection.SourceEmpty);

            GameInstance instance = slot.Content;
            DefinitionId id = instance.DefinitionId;

            ItemDefinition definition;
            if (!context.Items.TryGet(id, out definition) || definition == null)
                return ItemUseResult.Rejected(ItemUseRejection.UnknownDefinition, id, instance.InstanceId);

            if (context.Owner.IsValid && instance.Owner != context.Owner)
                return ItemUseResult.Rejected(ItemUseRejection.NotOwned, id, instance.InstanceId);

            if (slot.Quantity < 1)
                return ItemUseResult.Rejected(ItemUseRejection.InsufficientQuantity, id, instance.InstanceId);

            if (!definition.Usable || definition.UseType == ItemUseType.None)
                return ItemUseResult.Rejected(ItemUseRejection.NotUsable, id, instance.InstanceId);

            if (definition.UseTarget != ItemUseTarget.Self)
                return ItemUseResult.Rejected(ItemUseRejection.InvalidTarget, id, instance.InstanceId);

            ItemUseEffect[] effects = definition.UseEffects;
            if (effects.Length == 0)
                return ItemUseResult.Rejected(ItemUseRejection.NotUsable, id, instance.InstanceId);

            ItemUseRejection shape = CheckClassification(definition.UseType, effects);
            if (shape != ItemUseRejection.None)
                return ItemUseResult.Rejected(shape, id, instance.InstanceId);

            // ---- dry run: resolve and measure every effect, writing nothing -------------

            int plannedHealth = 0;
            int plannedMana = 0;
            int plannedBuffs = 0;
            DefinitionId plannedWarp = default;

            for (int i = 0; i < effects.Length; i++)
            {
                ItemUseEffect effect = effects[i];

                switch (effect.Kind)
                {
                    case ItemEffectKind.RestoreResource:
                    {
                        if (effect.Resource == ItemResource.None)
                            return ItemUseResult.Rejected(ItemUseRejection.InvalidEffect, id, instance.InstanceId);

                        if (effect.Amount < 0 || effect.Percent < 0f)
                            return ItemUseResult.Rejected(ItemUseRejection.InvalidEffect, id, instance.InstanceId);

                        // Measured against what is already planned, so two health effects on
                        // one item cannot both claim the same missing points and report twice
                        // what the character actually gained.
                        int gain = PlannedGain(effect, context, plannedHealth, plannedMana);

                        if (effect.Resource == ItemResource.Health) plannedHealth += gain;
                        else plannedMana += gain;

                        break;
                    }

                    case ItemEffectKind.ApplyStatusEffect:
                    {
                        if (!effect.StatusEffect.IsValid)
                            return ItemUseResult.Rejected(ItemUseRejection.InvalidEffect, id, instance.InstanceId);

                        if (context.StatusEffects == null)
                            return ItemUseResult.Rejected(ItemUseRejection.MissingContext, id, instance.InstanceId);

                        StatusEffectDefinition status;
                        if (!context.StatusEffects.TryGet(effect.StatusEffect, out status) || status == null)
                            return ItemUseResult.Rejected(ItemUseRejection.UnknownStatusEffect, id, instance.InstanceId);

                        plannedBuffs++;
                        break;
                    }

                    case ItemEffectKind.WarpToMap:
                    {
                        if (!effect.DestinationMap.IsValid)
                            return ItemUseResult.Rejected(ItemUseRejection.InvalidEffect, id, instance.InstanceId);

                        if (context.Maps == null)
                            return ItemUseResult.Rejected(ItemUseRejection.MissingContext, id, instance.InstanceId);

                        MapDefinition map;
                        if (!context.Maps.TryGet(effect.DestinationMap, out map) || map == null)
                            return ItemUseResult.Rejected(ItemUseRejection.InvalidDestination, id, instance.InstanceId);

                        // The game rule, read off the map rather than off the item: a scroll
                        // reaches towns. Fields and boss areas are walked to.
                        if (!map.IsTown || map.Category != MapCategory.Town || map.IsBossArea)
                            return ItemUseResult.Rejected(ItemUseRejection.WarpNotAllowed, id, instance.InstanceId);

                        if (plannedWarp.IsValid)
                            return ItemUseResult.Rejected(ItemUseRejection.InvalidEffect, id, instance.InstanceId);

                        plannedWarp = effect.DestinationMap;
                        break;
                    }

                    default:
                        return ItemUseResult.Rejected(ItemUseRejection.InvalidEffect, id, instance.InstanceId);
                }
            }

            if (plannedHealth == 0 && plannedMana == 0 && plannedBuffs == 0 && !plannedWarp.IsValid)
                return ItemUseResult.Rejected(ItemUseRejection.NoEffect, id, instance.InstanceId);

            // ---- everything is resolved and nothing can fail from here ------------------

            InstanceId used = instance.InstanceId;
            int actualHealth = 0;
            int actualMana = 0;
            int grants = 0;

            for (int i = 0; i < effects.Length; i++)
            {
                ItemUseEffect effect = effects[i];

                if (effect.Kind == ItemEffectKind.RestoreResource)
                {
                    if (effect.Resource == ItemResource.Health)
                    {
                        int before = context.Resources.CurrentHealth;
                        context.Resources.ChangeHealth(Requested(effect, context.Limits.MaxHealth), context.Limits);
                        actualHealth += context.Resources.CurrentHealth - before;
                    }
                    else
                    {
                        int before = context.Resources.CurrentMana;
                        context.Resources.ChangeMana(Requested(effect, context.Limits.MaxMana), context.Limits);
                        actualMana += context.Resources.CurrentMana - before;
                    }

                    continue;
                }

                if (effect.Kind != ItemEffectKind.ApplyStatusEffect) continue;

                StatusEffectDefinition status;
                context.StatusEffects.TryGet(effect.StatusEffect, out status);

                // An authored override wins; zero defers to the effect's own duration, so a
                // consumer never has to know which of the two applied.
                float duration = effect.DurationSeconds > 0f
                    ? effect.DurationSeconds
                    : status.DurationSeconds;

                if (context.GrantedBuffs != null)
                {
                    context.GrantedBuffs.Add(new ItemBuffGrant(effect.StatusEffect, duration,
                        status.MaxStacks, status.StackBehavior));
                }

                grants++;
            }

            // Exactly one, exactly once. This cannot be refused: the index, the occupancy
            // and a quantity of at least one were all established above.
            inventory.RemoveAt(slotIndex, 1);

            return ItemUseResult.Accepted(id, used, actualHealth, actualMana, grants, plannedWarp);
        }

        /// <summary>
        /// Whether the authored effects match what the item declared itself to be.
        /// </summary>
        /// <remarks>
        /// The classification must be represented among the effects, so an item labelled
        /// Recovery that restores nothing is caught as authored content rather than
        /// discovered by a player.
        ///
        /// Warp is gated in both directions: only a <see cref="ItemUseType.WarpTown"/> item
        /// may carry a warp effect, and one must. A consumable that gained a warp effect by
        /// a bad data import cannot move a character.
        ///
        /// Mixing is otherwise allowed, because a food that restores health and grants a
        /// buff is one item, not two.
        /// </remarks>
        private static ItemUseRejection CheckClassification(ItemUseType type, ItemUseEffect[] effects)
        {
            bool restores = false;
            bool buffs = false;
            bool warps = false;

            for (int i = 0; i < effects.Length; i++)
            {
                switch (effects[i].Kind)
                {
                    case ItemEffectKind.RestoreResource: restores = true; break;
                    case ItemEffectKind.ApplyStatusEffect: buffs = true; break;
                    case ItemEffectKind.WarpToMap: warps = true; break;
                }
            }

            if (warps && type != ItemUseType.WarpTown) return ItemUseRejection.WarpNotAllowed;

            switch (type)
            {
                case ItemUseType.Recovery:
                    return restores ? ItemUseRejection.None : ItemUseRejection.UnknownUseType;
                case ItemUseType.Buff:
                    return buffs ? ItemUseRejection.None : ItemUseRejection.UnknownUseType;
                case ItemUseType.WarpTown:
                    return warps ? ItemUseRejection.None : ItemUseRejection.UnknownUseType;
                default:
                    return ItemUseRejection.UnknownUseType;
            }
        }

        /// <summary>The authored magnitude: a flat amount plus a fraction of the ceiling.</summary>
        private static int Requested(ItemUseEffect effect, int maximum)
        {
            long flat = effect.Amount;

            if (effect.Percent > 0f)
            {
                flat += (long)(effect.Percent * maximum);
            }

            return flat > int.MaxValue ? int.MaxValue : (int)flat;
        }

        /// <summary>
        /// What a restore effect would actually add, given what earlier effects already took.
        /// </summary>
        /// <remarks>This is the measurement <see cref="ItemUseRejection.NoEffect"/> depends
        /// on: a full-health character gains nothing from a health potion, and refusing is
        /// the only acceptable answer.</remarks>
        private static int PlannedGain(ItemUseEffect effect, in Context context,
            int alreadyPlannedHealth, int alreadyPlannedMana)
        {
            bool health = effect.Resource == ItemResource.Health;

            int maximum = health ? context.Limits.MaxHealth : context.Limits.MaxMana;
            int current = health
                ? context.Resources.CurrentHealth + alreadyPlannedHealth
                : context.Resources.CurrentMana + alreadyPlannedMana;

            int room = maximum - current;
            if (room <= 0) return 0;

            int requested = Requested(effect, maximum);
            return requested < room ? requested : room;
        }
    }
}
