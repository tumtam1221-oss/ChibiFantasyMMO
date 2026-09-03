using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.UI;

namespace ChibiFantasy.Client.UI
{
    /// <summary>
    /// Turns fruits, cards and pets into view data. The read half.
    /// </summary>
    /// <remarks>
    /// <b>Reads only.</b> Nothing here activates, sockets, levels or evolves; every output is
    /// a snapshot. Building a panel twenty times costs nothing and changes nothing.
    ///
    /// <b>Hints are advisory.</b> <see cref="PetViewData.CanEvolve"/> exists so a button is
    /// not offered for something the service would refuse, and it is obtained by <em>asking</em>
    /// <see cref="PetService.CanEvolve"/> rather than by re-deriving the rules.
    /// <see cref="PetService"/> and <see cref="CardSocketService"/> re-check and remain the
    /// authority.
    ///
    /// <b>No text is built.</b> Localization keys are copied through; resolving them is the
    /// view's job at the edge.
    /// </remarks>
    public static class CollectibleAdapter
    {
        /// <summary>The registries these views need.</summary>
        public readonly struct Context
        {
            public Context(IDefinitionRegistry<ItemDefinition> items,
                IDefinitionRegistry<DevilFruitDefinition> fruits = null,
                IDefinitionRegistry<CardDefinition> cards = null,
                IDefinitionRegistry<PetDefinition> pets = null)
            {
                Items = items;
                Fruits = fruits;
                Cards = cards;
                Pets = pets;
            }

            public IDefinitionRegistry<ItemDefinition> Items { get; }

            public IDefinitionRegistry<DevilFruitDefinition> Fruits { get; }

            public IDefinitionRegistry<CardDefinition> Cards { get; }

            public IDefinitionRegistry<PetDefinition> Pets { get; }

            public bool IsUsable => Items != null;
        }

        // ---- devil fruit ---------------------------------------------------------------

        /// <summary>What a panel should say about one fruit.</summary>
        public static DevilFruitViewData BuildFruit(DefinitionId fruitId,
            CharacterDevilFruitState state, in Context context)
        {
            if (context.Fruits == null || !fruitId.IsValid) return DevilFruitViewData.None;

            DevilFruitDefinition fruit;
            if (!context.Fruits.TryGet(fruitId, out fruit) || fruit == null)
                return DevilFruitViewData.None;

            bool active = state != null && state.ActiveFruit == fruitId;

            // Effect refusals and category refusals read as one "immune to" line to a player,
            // so they are counted together rather than exposed as two numbers a view would
            // have to add up itself.
            int immunities = fruit.Immunities.Length + fruit.ImmuneCategories.Length;

            return new DevilFruitViewData(fruitId, fruit.NameKey, fruit.DescriptionKey,
                fruit.Icon, fruit.Rarity, active, fruit.PassiveAbility, fruit.ActiveAbility,
                fruit.GrantedEffects.Length, immunities,
                fruit.VisualEffect.IsValid, fruit.SoundEffect.IsValid);
        }

        /// <summary>The fruit a character currently carries, or none.</summary>
        public static DevilFruitViewData BuildActiveFruit(CharacterDevilFruitState state,
            in Context context)
        {
            if (state == null || !state.HasActiveFruit) return DevilFruitViewData.None;
            return BuildFruit(state.ActiveFruit, state, context);
        }

        /// <summary>
        /// Fills <paramref name="into"/> with every authored fruit.
        /// </summary>
        /// <remarks>What a collection screen shows. Enumerating the registry once on demand,
        /// not per frame -- the caller rebuilds when a revision moves.</remarks>
        public static void BuildFruits(CharacterDevilFruitState state, in Context context,
            List<DevilFruitViewData> into)
        {
            if (into == null) return;

            into.Clear();
            if (context.Fruits == null) return;

            IReadOnlyList<DevilFruitDefinition> all = context.Fruits.All;

            for (int i = 0; i < all.Count; i++)
            {
                DevilFruitDefinition fruit = all[i];
                if (fruit == null || !fruit.Enabled) continue;

                into.Add(BuildFruit(fruit.Id, state, context));
            }
        }

        // ---- cards ---------------------------------------------------------------------

        /// <summary>What a panel should say about one card.</summary>
        public static CardViewData BuildCard(DefinitionId cardId, in Context context,
            bool isSocketed = false, int socketIndex = -1)
        {
            if (context.Cards == null || !cardId.IsValid) return CardViewData.None;

            CardDefinition card;
            if (!context.Cards.TryGet(cardId, out card) || card == null) return CardViewData.None;

            return new CardViewData(cardId, card.NameKey, card.DescriptionKey, card.Icon,
                card.Rarity, card.SourceMonster, card.StatModifiers.Length, card.Effects.Length,
                card.AllowedSlot, card.AllowedCategory, isSocketed, socketIndex);
        }

        /// <summary>Fills <paramref name="into"/> with the cards socketed into one piece.</summary>
        public static void BuildSocketedCards(EquipmentInstance equipment, in Context context,
            List<CardViewData> into)
        {
            if (into == null) return;

            into.Clear();
            if (equipment == null || context.Cards == null) return;

            IReadOnlyList<EquipmentCardSocket> sockets = equipment.Cards;

            for (int i = 0; i < sockets.Count; i++)
            {
                EquipmentCardSocket socket = sockets[i];
                if (!socket.IsValid) continue;

                into.Add(BuildCard(socket.Card, context, true, socket.SocketIndex));
            }
        }

        /// <summary>
        /// How many card sockets a piece has, and how many are filled.
        /// </summary>
        /// <remarks>Filled is counted from the socket set rather than from capacity, so a
        /// piece with three sockets holding one card reads "1/3" -- the Phase 09 tooltip bug
        /// that counted empty sockets as filled is not repeated here.</remarks>
        public static void CardSocketCounts(EquipmentInstance equipment, in Context context,
            out int filled, out int capacity)
        {
            filled = 0;
            capacity = 0;

            if (equipment == null || !context.IsUsable) return;

            ItemDefinition definition;
            if (!context.Items.TryGet(equipment.DefinitionId, out definition)) return;

            var piece = definition as EquipmentDefinition;
            if (piece == null) return;

            capacity = CardSocketService.CardCapacity(piece);
            filled = equipment.CardCount;
        }

        // ---- pets ----------------------------------------------------------------------

        /// <summary>What a panel should say about one owned pet.</summary>
        public static PetViewData BuildPet(PetInstance pet, PetCompanionState companion,
            ItemContainerState materials, in Context context)
        {
            if (pet == null || context.Pets == null) return PetViewData.None;

            PetDefinition definition;
            if (!context.Pets.TryGet(pet.DefinitionId, out definition) || definition == null)
                return PetViewData.None;

            var petContext = new PetService.Context(context.Pets, context.Items);

            PetEvolutionStage stage;
            bool hasStage = PetService.TryGetNextStage(definition, out stage);

            // Asked of the service, never re-derived. A hint that disagreed with the rule
            // would offer a button that then refused.
            bool canEvolve = PetService.CanEvolve(pet, materials, petContext) == PetRejection.None;

            bool summoned = companion != null && companion.IsSummoned
                && companion.Summoned == pet;

            return new PetViewData(pet.DefinitionId, pet.InstanceId, definition.NameKey,
                definition.Icon, pet.Level, pet.Experience,
                PetService.ExperienceForNextLevel(definition, pet.Level),
                definition.EffectiveMaxLevel, canEvolve,
                hasStage ? stage.RequiredLevel : 0,
                definition.BaseBuff, summoned,
                summoned ? companion.IsAuraForm : definition.IsAuraForm,
                definition.FollowBehavior);
        }

        /// <summary>Fills <paramref name="into"/> with every owned pet.</summary>
        public static void BuildPets(IReadOnlyList<PetInstance> owned, PetCompanionState companion,
            ItemContainerState materials, in Context context, List<PetViewData> into)
        {
            if (into == null) return;

            into.Clear();
            if (owned == null) return;

            for (int i = 0; i < owned.Count; i++)
            {
                PetViewData view = BuildPet(owned[i], companion, materials, context);
                if (view.IsValid) into.Add(view);
            }
        }
    }
}
