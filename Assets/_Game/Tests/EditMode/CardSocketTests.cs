using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Cards: ownership, sockets, compatibility, modifiers and removal.
    /// </summary>
    /// <remarks>
    /// Two properties are load-bearing here. A card is <em>moved</em>, never copied and never
    /// destroyed -- so every rejection path is checked for having changed nothing, and
    /// removal is checked for returning the same copy. And cards do not disturb Phase 09:
    /// status stone sockets are a separate set, which several tests assert directly.
    /// </remarks>
    [TestFixture]
    internal sealed class CardSocketTests : CollectibleTestBase
    {
        // ---- ownership -----------------------------------------------------------------

        [Test]
        public void A_card_is_held_as_a_normal_item_instance()
        {
            ItemContainerState bag = Container();
            ItemInstance card = Stack(StatCard);

            bag.Add(card, Items);

            Assert.That(card.InstanceId.IsValid, Is.True);
            Assert.That(card.Owner, Is.EqualTo(Owner));
            Assert.That(card.Quantity, Is.EqualTo(1));
            Assert.That(bag.CountOf(new DefinitionId(StatCard)), Is.EqualTo(1));
        }

        [Test]
        public void A_card_item_is_tradable_while_it_sits_in_a_bag()
        {
            ItemDefinition item;
            Items.TryGet(new DefinitionId(StatCard), out item);

            Assert.That(item.Tradable, Is.True);
            Assert.That(item.Category, Is.EqualTo(ItemCategory.Card));
        }

        // ---- insertion -----------------------------------------------------------------

        [Test]
        public void Inserting_a_card_moves_it_from_the_bag_into_the_socket()
        {
            ItemContainerState bag = Container();
            ItemInstance card = Stack(StatCard);
            bag.Add(card, Items);

            EquipmentInstance sword = Equipment(Sword);

            CardSocketResult result = CardSocketService.TryInsert(bag, 0, sword, CardContext());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.SocketIndex, Is.EqualTo(0));
            Assert.That(sword.CardCount, Is.EqualTo(1));
            Assert.That(bag.CountOf(new DefinitionId(StatCard)), Is.EqualTo(0),
                "the card is in one place, never two");
        }

        [Test]
        public void Insertion_keeps_the_exact_copy_that_went_in()
        {
            ItemContainerState bag = Container();
            ItemInstance card = Stack(StatCard);
            InstanceId identity = card.InstanceId;
            bag.Add(card, Items);

            EquipmentInstance sword = Equipment(Sword);
            CardSocketResult result = CardSocketService.TryInsert(bag, 0, sword, CardContext());

            Assert.That(result.CardInstance, Is.EqualTo(identity));
            Assert.That(sword.Cards[0].CardInstance, Is.EqualTo(identity));
        }

        [Test]
        public void Insertion_advances_the_equipment_revision()
        {
            ItemContainerState bag = Container();
            bag.Add(Stack(StatCard), Items);

            EquipmentInstance sword = Equipment(Sword);
            Revision before = sword.Revision;

            CardSocketService.TryInsert(bag, 0, sword, CardContext());

            Assert.That(sword.Revision.Value, Is.EqualTo(before.Value + 1));
        }

        [Test]
        public void Cards_fill_sockets_lowest_first()
        {
            ItemContainerState bag = Container();
            bag.Add(Stack(StatCard), Items);
            bag.Add(Stack(HpCard), Items);

            EquipmentInstance sword = Equipment(Sword);

            Assert.That(CardSocketService.TryInsert(bag, 0, sword, CardContext()).SocketIndex,
                Is.EqualTo(0));
            Assert.That(CardSocketService.TryInsert(bag, 1, sword, CardContext()).SocketIndex,
                Is.EqualTo(1));
        }

        [Test]
        public void A_full_piece_refuses_another_card()
        {
            ItemContainerState bag = Container();
            bag.Add(Stack(StatCard), Items);
            bag.Add(Stack(HpCard), Items);
            bag.Add(Stack(RankCard), Items);

            EquipmentInstance sword = Equipment(Sword);   // two card sockets

            CardSocketService.TryInsert(bag, 0, sword, CardContext());
            CardSocketService.TryInsert(bag, 1, sword, CardContext());

            Revision after = sword.Revision;

            CardSocketResult third = CardSocketService.TryInsert(bag, 2, sword, CardContext());

            Assert.That(third.Reason, Is.EqualTo(CardSocketRejection.NoFreeSocket));
            Assert.That(sword.CardCount, Is.EqualTo(2));
            Assert.That(sword.Revision, Is.EqualTo(after));
            Assert.That(bag.CountOf(new DefinitionId(RankCard)), Is.EqualTo(1),
                "a refused insertion leaves the card in the bag");
        }

        [Test]
        public void A_piece_with_no_card_sockets_refuses_every_card()
        {
            ItemContainerState bag = Container();
            bag.Add(Stack(StatCard), Items);

            EquipmentInstance ring = Equipment(Ring);

            CardSocketResult result = CardSocketService.TryInsert(bag, 0, ring, CardContext());

            Assert.That(result.Reason, Is.EqualTo(CardSocketRejection.NoFreeSocket));
            Assert.That(bag.CountOf(new DefinitionId(StatCard)), Is.EqualTo(1));
        }

        [Test]
        public void An_occupied_socket_is_refused()
        {
            ItemContainerState bag = Container();
            bag.Add(Stack(StatCard), Items);
            bag.Add(Stack(HpCard), Items);

            EquipmentInstance sword = Equipment(Sword);
            CardSocketService.TryInsert(bag, 0, sword, CardContext(), socketIndex: 0);

            CardSocketResult second = CardSocketService.TryInsert(bag, 1, sword, CardContext(),
                socketIndex: 0);

            Assert.That(second.Reason, Is.EqualTo(CardSocketRejection.SocketOccupied));
        }

        [Test]
        public void A_card_restricted_to_weapons_is_refused_by_armour()
        {
            ItemContainerState bag = Container();
            bag.Add(Stack(WeaponCard), Items);

            EquipmentInstance helm = Equipment(Helm);
            Revision before = helm.Revision;

            CardSocketResult result = CardSocketService.TryInsert(bag, 0, helm, CardContext());

            Assert.That(result.Reason, Is.EqualTo(CardSocketRejection.Incompatible));
            Assert.That(helm.CardCount, Is.EqualTo(0));
            Assert.That(helm.Revision, Is.EqualTo(before));
            Assert.That(bag.CountOf(new DefinitionId(WeaponCard)), Is.EqualTo(1));
        }

        [Test]
        public void The_same_weapon_card_fits_a_weapon()
        {
            ItemContainerState bag = Container();
            bag.Add(Stack(WeaponCard), Items);

            EquipmentInstance sword = Equipment(Sword);

            Assert.That(CardSocketService.TryInsert(bag, 0, sword, CardContext()).IsAccepted,
                Is.True);
        }

        [Test]
        public void A_duplicate_card_is_refused_when_the_limit_is_one()
        {
            ItemContainerState bag = Container();
            bag.Add(Stack(StatCard), Items);
            bag.Add(Stack(StatCard), Items);

            EquipmentInstance sword = Equipment(Sword);

            CardSocketService.TryInsert(bag, 0, sword, CardContext());
            CardSocketResult second = CardSocketService.TryInsert(bag, 1, sword, CardContext());

            Assert.That(second.Reason, Is.EqualTo(CardSocketRejection.DuplicateNotAllowed));
            Assert.That(sword.CardCount, Is.EqualTo(1));
        }

        [Test]
        public void An_authored_limit_above_one_allows_duplicates()
        {
            AddCard("card.stackable", maxPerEquipment: 2, modifiers: new[]
            {
                new StatModifier(new DefinitionId(Str), StatModifierKind.Flat, 1f)
            });
            AddItem("card.stackable", ItemCategory.Card, stackable: false, maxStack: 1);

            ItemContainerState bag = Container();
            bag.Add(Stack("card.stackable"), Items);
            bag.Add(Stack("card.stackable"), Items);

            EquipmentInstance sword = Equipment(Sword);

            Assert.That(CardSocketService.TryInsert(bag, 0, sword, CardContext()).IsAccepted, Is.True);
            Assert.That(CardSocketService.TryInsert(bag, 1, sword, CardContext()).IsAccepted, Is.True);
            Assert.That(sword.CardCount, Is.EqualTo(2));
        }

        [Test]
        public void A_non_card_item_is_refused()
        {
            ItemContainerState bag = Container();
            bag.Add(Stack(EvolutionStone, 1), Items);

            EquipmentInstance sword = Equipment(Sword);

            Assert.That(CardSocketService.TryInsert(bag, 0, sword, CardContext()).Reason,
                Is.EqualTo(CardSocketRejection.NotACard));
        }

        [Test]
        public void A_disabled_card_is_refused()
        {
            AddCard("card.off", enabled: false, modifiers: new[]
            {
                new StatModifier(new DefinitionId(Str), StatModifierKind.Flat, 1f)
            });
            AddItem("card.off", ItemCategory.Card, stackable: false, maxStack: 1);

            ItemContainerState bag = Container();
            bag.Add(Stack("card.off"), Items);

            Assert.That(CardSocketService.TryInsert(bag, 0, Equipment(Sword), CardContext()).Reason,
                Is.EqualTo(CardSocketRejection.CardDisabled));
        }

        [Test]
        public void Another_owners_card_is_refused()
        {
            ItemContainerState bag = Container();
            bag.Add(Stack(StatCard, 1, Stranger), Items);

            EquipmentInstance sword = Equipment(Sword);

            Assert.That(CardSocketService.TryInsert(bag, 0, sword, CardContext(Owner)).Reason,
                Is.EqualTo(CardSocketRejection.NotOwned));
            Assert.That(sword.CardCount, Is.EqualTo(0));
        }

        [Test]
        public void A_stale_revision_is_refused()
        {
            ItemContainerState bag = Container();
            bag.Add(Stack(StatCard), Items);
            bag.Add(Stack(HpCard), Items);

            EquipmentInstance sword = Equipment(Sword);
            Revision stale = sword.Revision;

            // Something else changes the piece first.
            sword.SetEnhancementLevel(1);

            CardSocketResult result = CardSocketService.TryInsert(bag, 0, sword, CardContext(),
                expectedRevision: stale);

            Assert.That(result.Reason, Is.EqualTo(CardSocketRejection.StaleRevision));
            Assert.That(sword.CardCount, Is.EqualTo(0));
        }

        [Test]
        public void A_current_revision_is_accepted()
        {
            ItemContainerState bag = Container();
            bag.Add(Stack(StatCard), Items);

            EquipmentInstance sword = Equipment(Sword);

            CardSocketResult result = CardSocketService.TryInsert(bag, 0, sword, CardContext(),
                expectedRevision: sword.Revision);

            Assert.That(result.IsAccepted, Is.True);
        }

        // ---- removal -------------------------------------------------------------------

        [Test]
        public void Removing_a_card_returns_it_to_the_bag()
        {
            ItemContainerState bag = Container();
            ItemInstance card = Stack(StatCard);
            InstanceId identity = card.InstanceId;
            bag.Add(card, Items);

            EquipmentInstance sword = Equipment(Sword);
            CardSocketService.TryInsert(bag, 0, sword, CardContext());

            CardSocketResult removal = CardSocketService.TryRemove(sword, 0, bag, CardContext());

            Assert.That(removal.IsAccepted, Is.True);
            Assert.That(sword.CardCount, Is.EqualTo(0));
            Assert.That(bag.CountOf(new DefinitionId(StatCard)), Is.EqualTo(1),
                "removal is extraction, never destruction");
            Assert.That(removal.CardInstance, Is.EqualTo(identity),
                "the copy that comes out is the copy that went in");
        }

        [Test]
        public void Removing_from_an_empty_socket_changes_nothing()
        {
            ItemContainerState bag = Container();
            EquipmentInstance sword = Equipment(Sword);
            Revision before = sword.Revision;

            CardSocketResult removal = CardSocketService.TryRemove(sword, 0, bag, CardContext());

            Assert.That(removal.Reason, Is.EqualTo(CardSocketRejection.SocketEmpty));
            Assert.That(sword.Revision, Is.EqualTo(before));
        }

        [Test]
        public void A_full_bag_refuses_the_removal_rather_than_losing_the_card()
        {
            var bag = new ItemContainerState(Owner, 1);
            bag.Add(Stack(StatCard), Items);

            EquipmentInstance sword = Equipment(Sword);
            CardSocketService.TryInsert(bag, 0, sword, CardContext());

            // Fill the only slot with something else.
            bag.Add(Stack(EvolutionStone, 1), Items);

            CardSocketResult removal = CardSocketService.TryRemove(sword, 0, bag, CardContext());

            Assert.That(removal.Reason, Is.EqualTo(CardSocketRejection.NoRoomForCard));
            Assert.That(sword.CardCount, Is.EqualTo(1),
                "the card stays socketed rather than being pulled out with nowhere to go");
        }

        [Test]
        public void Removal_leaves_other_sockets_numbered_as_they_were()
        {
            ItemContainerState bag = Container();
            bag.Add(Stack(StatCard), Items);
            bag.Add(Stack(HpCard), Items);

            EquipmentInstance sword = Equipment(Sword);
            CardSocketService.TryInsert(bag, 0, sword, CardContext());
            CardSocketService.TryInsert(bag, 1, sword, CardContext());

            Assert.That(sword.CardCount, Is.EqualTo(2));

            CardSocketService.TryRemove(sword, 0, bag, CardContext());

            Assert.That(sword.CardCount, Is.EqualTo(1));
            Assert.That(sword.Cards[0].SocketIndex, Is.EqualTo(1),
                "removing socket 0 must not renumber socket 1");
        }

        // ---- modifiers -----------------------------------------------------------------

        [Test]
        public void A_socketed_cards_modifiers_reach_the_equipment_resolver()
        {
            ItemContainerState bag = Container();
            bag.Add(Stack(StatCard), Items);

            EquipmentInstance sword = Equipment(Sword);
            CardSocketService.TryInsert(bag, 0, sword, CardContext());

            var context = new EquipmentModifierResolver.Context(Items, null, null, Cards);
            var modifiers = new List<StatModifier>();

            EquipmentModifierResolver.Collect(sword, context, modifiers);

            Assert.That(Total(modifiers, Str), Is.EqualTo(5f).Within(0.001f));
        }

        [Test]
        public void Without_a_card_registry_a_piece_resolves_exactly_as_before()
        {
            ItemContainerState bag = Container();
            bag.Add(Stack(StatCard), Items);

            EquipmentInstance sword = Equipment(Sword);
            CardSocketService.TryInsert(bag, 0, sword, CardContext());

            var context = new EquipmentModifierResolver.Context(Items);
            var modifiers = new List<StatModifier>();

            EquipmentModifierResolver.Collect(sword, context, modifiers);

            Assert.That(Total(modifiers, Str), Is.EqualTo(0f).Within(0.001f),
                "existing callers must be unaffected by cards existing");
        }

        [Test]
        public void Removing_a_card_removes_its_contribution()
        {
            ItemContainerState bag = Container();
            bag.Add(Stack(StatCard), Items);

            EquipmentInstance sword = Equipment(Sword);
            CardSocketService.TryInsert(bag, 0, sword, CardContext());
            CardSocketService.TryRemove(sword, 0, bag, CardContext());

            var context = new EquipmentModifierResolver.Context(Items, null, null, Cards);
            var modifiers = new List<StatModifier>();

            EquipmentModifierResolver.Collect(sword, context, modifiers);

            Assert.That(Total(modifiers, Str), Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void Re_authoring_a_card_changes_every_piece_already_carrying_it()
        {
            ItemContainerState bag = Container();
            bag.Add(Stack(StatCard), Items);

            EquipmentInstance sword = Equipment(Sword);
            CardSocketService.TryInsert(bag, 0, sword, CardContext());

            // Content is re-authored. Nothing was copied onto the sword, so the sword follows.
            CardDefinition card;
            Cards.TryGet(new DefinitionId(StatCard), out card);
            SetPrivate(card, "_statModifiers", new[]
            {
                new StatModifier(new DefinitionId(Str), StatModifierKind.Flat, 9f)
            });

            var context = new EquipmentModifierResolver.Context(Items, null, null, Cards);
            var modifiers = new List<StatModifier>();

            EquipmentModifierResolver.Collect(sword, context, modifiers);

            Assert.That(Total(modifiers, Str), Is.EqualTo(9f).Within(0.001f));
        }

        [Test]
        public void Conditional_effects_are_reported_but_no_formula_consumes_them_yet()
        {
            ItemContainerState bag = Container();
            bag.Add(Stack(RankCard), Items);

            EquipmentInstance sword = Equipment(Sword);
            CardSocketService.TryInsert(bag, 0, sword, CardContext());

            var effects = new List<CardEffect>();
            CardSocketService.CollectEffects(sword, Cards, effects);

            Assert.That(effects.Count, Is.EqualTo(1));
            Assert.That(effects[0].Kind, Is.EqualTo(CardEffectKind.DamageVersusRank));
            Assert.That(effects[0].AppliesTo(MonsterRank.WorldBoss, ElementType.Neutral), Is.True);
            Assert.That(effects[0].AppliesTo(MonsterRank.Normal, ElementType.Neutral), Is.False);

            // The honest half: they contribute no stat modifier, because nothing computes
            // them yet and faking it would be worse than the gap.
            var modifiers = new List<StatModifier>();
            CardSocketService.CollectModifiers(sword, Cards, modifiers);

            Assert.That(modifiers.Count, Is.EqualTo(0));
        }

        // ---- phase 09 is untouched -----------------------------------------------------

        [Test]
        public void Cards_and_status_stones_use_separate_sockets()
        {
            ItemContainerState bag = Container();
            bag.Add(Stack(StatCard), Items);

            EquipmentInstance sword = Equipment(Sword);   // 2 card sockets, 1 stone socket

            CardSocketService.TryInsert(bag, 0, sword, CardContext());

            Assert.That(sword.CardCount, Is.EqualTo(1));
            Assert.That(sword.EnchantCount, Is.EqualTo(0),
                "socketing a card must not consume an enchant slot");
            Assert.That(sword.IsSocketOccupied(0), Is.False,
                "the stone socket is untouched");
            Assert.That(sword.IsCardSocketOccupied(0), Is.True);
        }

        [Test]
        public void A_stone_socket_and_a_card_socket_are_counted_separately()
        {
            EquipmentDefinition sword;
            ItemDefinition definition;
            Items.TryGet(new DefinitionId(Sword), out definition);
            sword = definition as EquipmentDefinition;

            Assert.That(sword.CardSlots, Is.EqualTo(2));
            Assert.That(sword.StatusStoneSlots, Is.EqualTo(1));
            Assert.That(CardSocketService.CardCapacity(sword), Is.EqualTo(2));
        }

        [Test]
        public void The_card_service_never_touches_the_enchant_set()
        {
            foreach (string code in DevilFruitTests.CodeLines(
                "Assets/_Game/Scripts/Gameplay/CardSocketService.cs"))
            {
                Assert.That(code, Does.Not.Contain("AddEnchant"));
                Assert.That(code, Does.Not.Contain("RemoveEnchantAt"));
                Assert.That(code, Does.Not.Contain("StoneConfig"));
                Assert.That(code, Does.Not.Contain(".Enchants"));
            }
        }

        [Test]
        public void No_card_is_named_in_the_service()
        {
            foreach (string code in DevilFruitTests.CodeLines(
                "Assets/_Game/Scripts/Gameplay/CardSocketService.cs"))
            {
                Assert.That(code, Does.Not.Contain("\"card."));
                Assert.That(code, Does.Not.Contain("\"equip."));
            }
        }

        [Test]
        public void There_is_no_second_card_service()
        {
            System.Type[] types = typeof(CardSocketService).Assembly.GetTypes();

            foreach (System.Type type in types)
            {
                Assert.That(type.Name, Is.Not.EqualTo("CardEquipmentService2"));
                Assert.That(type.Name, Is.Not.EqualTo("CardItemInstance"));
                Assert.That(type.Name, Is.Not.EqualTo("CardInventory"));
            }
        }

        private static float Total(List<StatModifier> modifiers, string stat)
        {
            var id = new DefinitionId(stat);
            float total = 0f;

            for (int i = 0; i < modifiers.Count; i++)
            {
                if (modifiers[i].Stat == id) total += modifiers[i].Value;
            }

            return total;
        }
    }
}
