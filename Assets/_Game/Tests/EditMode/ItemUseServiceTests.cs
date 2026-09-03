using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The generic item-use pipeline.
    /// </summary>
    /// <remarks>
    /// Every item below is a FIXTURE, authored exactly as content would be, and every
    /// figure -- 500 health, 300 mana, 600 seconds, a destination town -- lives on the
    /// definition. <c>ItemUseService</c> is asked to behave correctly without knowing any
    /// of them, which is the whole claim: a new consumable is content, not code.
    ///
    /// The costly failure here is not a rejected use, it is a wrongly accepted one: an item
    /// consumed for no benefit, or an effect applied twice. Those get their own tests.
    /// </remarks>
    internal sealed class ItemUseServiceTests : ItemContainerTestBase
    {
        private const string RedPotion = "item.red";
        private const string BluePotion = "item.blue";
        private const string StrengthFood = "item.food.str";
        private const string FullMeal = "item.meal";
        private const string TownScroll = "item.scroll.town";
        private const string FieldScroll = "item.scroll.field";
        private const string BossScroll = "item.scroll.boss";
        private const string BrokenScroll = "item.scroll.broken";
        private const string DisabledPotion = "item.red.disabled";
        private const string MislabelledPotion = "item.mislabelled";

        private const string StrBuff = "status.str.up";
        private const string DefBuff = "status.def.up";

        private const string TownA = "map.town.a";
        private const string TownB = "map.town.b";
        private const string FieldA = "map.field.a";
        private const string BossA = "map.boss.a";

        private const int MaxHealth = 1000;
        private const int MaxMana = 400;

        private CharacterResourceState _resources;
        private ResourceLimits _limits;
        private List<ItemBuffGrant> _grants;

        [SetUp]
        public void AuthorUseContent()
        {
            _limits = new ResourceLimits(MaxHealth, MaxMana);
            _resources = new CharacterResourceState(new CharacterId("char:test"), _limits, 400, 100);
            _grants = new List<ItemBuffGrant>();

            AddUsable(RedPotion, ItemUseType.Recovery, new[]
            {
                new ItemUseEffect(ItemEffectKind.RestoreResource, ItemResource.Health, 500)
            });

            AddUsable(BluePotion, ItemUseType.Recovery, new[]
            {
                new ItemUseEffect(ItemEffectKind.RestoreResource, ItemResource.Mana, 300)
            });

            AddStatusEffect(StrBuff, 600f, new[]
            {
                new StatModifier(new DefinitionId(Str), StatModifierKind.Flat, 10f)
            });

            AddStatusEffect(DefBuff, 300f);

            AddUsable(StrengthFood, ItemUseType.Buff, new[]
            {
                new ItemUseEffect(ItemEffectKind.ApplyStatusEffect,
                    statusEffect: new DefinitionId(StrBuff))
            });

            // Three effects, one item, authored in this order.
            AddUsable(FullMeal, ItemUseType.Recovery, new[]
            {
                new ItemUseEffect(ItemEffectKind.RestoreResource, ItemResource.Health, 300),
                new ItemUseEffect(ItemEffectKind.RestoreResource, ItemResource.Mana, 100),
                new ItemUseEffect(ItemEffectKind.ApplyStatusEffect,
                    statusEffect: new DefinitionId(StrBuff), durationSeconds: 30f)
            });

            AddMap(TownA, MapCategory.Town, isTown: true);
            AddMap(TownB, MapCategory.Town, isTown: true);
            AddMap(FieldA, MapCategory.Field, isTown: false);
            AddMap(BossA, MapCategory.BossArena, isTown: false, isBossArea: true);

            AddUsable(TownScroll, ItemUseType.WarpTown, new[]
            {
                new ItemUseEffect(ItemEffectKind.WarpToMap,
                    destinationMap: new DefinitionId(TownA))
            }, stackable: true, maxStack: 20);

            AddUsable(FieldScroll, ItemUseType.WarpTown, new[]
            {
                new ItemUseEffect(ItemEffectKind.WarpToMap,
                    destinationMap: new DefinitionId(FieldA))
            });

            AddUsable(BossScroll, ItemUseType.WarpTown, new[]
            {
                new ItemUseEffect(ItemEffectKind.WarpToMap,
                    destinationMap: new DefinitionId(BossA))
            });

            AddUsable(BrokenScroll, ItemUseType.WarpTown, new[]
            {
                new ItemUseEffect(ItemEffectKind.WarpToMap,
                    destinationMap: new DefinitionId("map.does.not.exist"))
            });

            // Configured, but content turned it off.
            AddUsable(DisabledPotion, ItemUseType.Recovery, new[]
            {
                new ItemUseEffect(ItemEffectKind.RestoreResource, ItemResource.Health, 500)
            }, usable: false);

            // Says Recovery, restores nothing: bad authoring, and it must be caught.
            AddUsable(MislabelledPotion, ItemUseType.Recovery, new[]
            {
                new ItemUseEffect(ItemEffectKind.ApplyStatusEffect,
                    statusEffect: new DefinitionId(DefBuff))
            });
        }

        private ItemUseService.Context Context(OwnerId owner = default)
        {
            return new ItemUseService.Context(Items, _resources, _limits,
                StatusEffects, Maps, owner, _grants);
        }

        private ItemContainerState WithStack(string id, int quantity, out ItemContainerState made)
        {
            made = Container(8);
            made.Add(Stack(id, quantity), Items);
            return made;
        }

        // ---- recovery ------------------------------------------------------------------

        [Test]
        public void A_health_item_restores_the_configured_amount_and_spends_one()
        {
            ItemContainerState bag;
            WithStack(RedPotion, 5, out bag);

            ItemUseResult result = ItemUseService.Use(bag, 0, Context());

            Assert.That(result.IsAccepted, Is.True, result.ToString());
            Assert.That(result.HealthRestored, Is.EqualTo(500), "the amount the fixture authored");
            Assert.That(_resources.CurrentHealth, Is.EqualTo(900));
            Assert.That(result.ConsumedQuantity, Is.EqualTo(1));
            Assert.That(bag.GetSlot(0).Quantity, Is.EqualTo(4), "exactly one, exactly once");
        }

        [Test]
        public void A_mana_item_restores_mana_and_leaves_health_alone()
        {
            ItemContainerState bag;
            WithStack(BluePotion, 2, out bag);

            ItemUseResult result = ItemUseService.Use(bag, 0, Context());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.ManaRestored, Is.EqualTo(300));
            Assert.That(result.HealthRestored, Is.EqualTo(0));
            Assert.That(_resources.CurrentMana, Is.EqualTo(400));
            Assert.That(_resources.CurrentHealth, Is.EqualTo(400));
            Assert.That(bag.GetSlot(0).Quantity, Is.EqualTo(1));
        }

        [Test]
        public void Restoring_past_the_ceiling_reports_only_what_was_actually_gained()
        {
            _resources.SetHealth(700, _limits);

            ItemContainerState bag;
            WithStack(RedPotion, 1, out bag);

            ItemUseResult result = ItemUseService.Use(bag, 0, Context());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.HealthRestored, Is.EqualTo(300),
                "300 of the authored 500 fitted; reporting 500 would be a lie");
            Assert.That(_resources.CurrentHealth, Is.EqualTo(MaxHealth));
            Assert.That(bag.GetSlot(0).IsEmpty, Is.True, "the last one was spent");
        }

        [Test]
        public void A_full_character_is_refused_and_keeps_the_item()
        {
            _resources.SetHealth(MaxHealth, _limits);

            ItemContainerState bag;
            WithStack(RedPotion, 5, out bag);

            Revision before = bag.Revision;
            ItemUseResult result = ItemUseService.Use(bag, 0, Context());

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(result.Reason, Is.EqualTo(ItemUseRejection.NoEffect));
            Assert.That(result.ConsumedQuantity, Is.EqualTo(0));
            Assert.That(bag.GetSlot(0).Quantity, Is.EqualTo(5),
                "spending a player's potion for no benefit is never acceptable");
            Assert.That(bag.Revision, Is.EqualTo(before));
        }

        [Test]
        public void A_full_mana_pool_refuses_a_mana_item()
        {
            _resources.SetMana(MaxMana, _limits);

            ItemContainerState bag;
            WithStack(BluePotion, 3, out bag);

            ItemUseResult result = ItemUseService.Use(bag, 0, Context());

            Assert.That(result.Reason, Is.EqualTo(ItemUseRejection.NoEffect));
            Assert.That(bag.GetSlot(0).Quantity, Is.EqualTo(3));
            Assert.That(_resources.CurrentMana, Is.EqualTo(MaxMana));
        }

        [Test]
        public void A_percentage_effect_scales_off_the_supplied_ceiling()
        {
            const string HalfPotion = "item.half";
            AddUsable(HalfPotion, ItemUseType.Recovery, new[]
            {
                new ItemUseEffect(ItemEffectKind.RestoreResource, ItemResource.Health,
                    amount: 0, percent: 0.25f)
            });

            _resources.SetHealth(100, _limits);

            ItemContainerState bag;
            WithStack(HalfPotion, 1, out bag);

            ItemUseResult result = ItemUseService.Use(bag, 0, Context());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.HealthRestored, Is.EqualTo(250), "a quarter of the 1000 ceiling");
        }

        // ---- buffs ---------------------------------------------------------------------

        [Test]
        public void A_buff_item_resolves_its_effect_stat_and_duration_from_data()
        {
            ItemContainerState bag;
            WithStack(StrengthFood, 2, out bag);

            ItemUseResult result = ItemUseService.Use(bag, 0, Context());

            Assert.That(result.IsAccepted, Is.True, result.ToString());
            Assert.That(result.BuffsGranted, Is.EqualTo(1));
            Assert.That(_grants.Count, Is.EqualTo(1));
            Assert.That(_grants[0].StatusEffect, Is.EqualTo(new DefinitionId(StrBuff)));
            Assert.That(_grants[0].DurationSeconds, Is.EqualTo(600f),
                "the status effect's own authored duration");
            Assert.That(bag.GetSlot(0).Quantity, Is.EqualTo(1));

            // The stat and its magnitude are on the status effect, where a future runtime
            // reads them. Nothing was computed here.
            StatusEffectDefinition status;
            StatusEffects.TryGet(new DefinitionId(StrBuff), out status);
            Assert.That(status.StatModifiers.Length, Is.EqualTo(1));
            Assert.That(status.StatModifiers[0].Stat, Is.EqualTo(new DefinitionId(Str)));
            Assert.That(status.StatModifiers[0].Value, Is.EqualTo(10f));
        }

        [Test]
        public void An_authored_duration_override_wins_over_the_status_effects_own()
        {
            ItemContainerState bag;
            WithStack(FullMeal, 1, out bag);

            ItemUseResult result = ItemUseService.Use(bag, 0, Context());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(_grants.Count, Is.EqualTo(1));
            Assert.That(_grants[0].DurationSeconds, Is.EqualTo(30f),
                "the meal authored 30; the status effect authors 600");
        }

        [Test]
        public void A_buff_referencing_a_missing_status_effect_is_refused_and_costs_nothing()
        {
            const string Ghost = "item.ghostbuff";
            AddUsable(Ghost, ItemUseType.Buff, new[]
            {
                new ItemUseEffect(ItemEffectKind.ApplyStatusEffect,
                    statusEffect: new DefinitionId("status.missing"))
            });

            ItemContainerState bag;
            WithStack(Ghost, 4, out bag);

            ItemUseResult result = ItemUseService.Use(bag, 0, Context());

            Assert.That(result.Reason, Is.EqualTo(ItemUseRejection.UnknownStatusEffect));
            Assert.That(bag.GetSlot(0).Quantity, Is.EqualTo(4));
            Assert.That(_grants, Is.Empty);
        }

        [Test]
        public void No_definition_id_is_compared_against_a_literal_in_the_use_pipeline()
        {
            // The structural half of "no item-ID logic": every id these tests author is a
            // fixture string, and the service resolves behaviour purely from the effects.
            // A service that special-cased an id would have to name it, so the source is
            // searched for the fixture names it must not know.
            string source = System.IO.File.ReadAllText(
                "Assets/_Game/Scripts/Gameplay/ItemUseService.cs");

            string[] mustNotAppear =
            {
                RedPotion, BluePotion, StrengthFood, TownScroll, TownA, FieldA, BossA,
                "Potion", "Scroll", "Food", "Meal"
            };

            foreach (string forbidden in mustNotAppear)
            {
                Assert.That(source, Does.Not.Contain(forbidden),
                    "ItemUseService names '" + forbidden + "'; behaviour must come from data");
            }
        }

        // ---- multiple effects ----------------------------------------------------------

        [Test]
        public void Several_authored_effects_all_run_once_for_one_item()
        {
            ItemContainerState bag;
            WithStack(FullMeal, 3, out bag);

            ItemUseResult result = ItemUseService.Use(bag, 0, Context());

            Assert.That(result.IsAccepted, Is.True, result.ToString());
            Assert.That(result.HealthRestored, Is.EqualTo(300));
            Assert.That(result.ManaRestored, Is.EqualTo(100));
            Assert.That(result.BuffsGranted, Is.EqualTo(1));
            Assert.That(_resources.CurrentHealth, Is.EqualTo(700));
            Assert.That(_resources.CurrentMana, Is.EqualTo(200));
            Assert.That(result.ConsumedQuantity, Is.EqualTo(1));
            Assert.That(bag.GetSlot(0).Quantity, Is.EqualTo(2),
                "three effects, still one item spent");
        }

        [Test]
        public void Two_effects_on_the_same_pool_cannot_both_claim_the_same_missing_points()
        {
            const string Double = "item.double";
            AddUsable(Double, ItemUseType.Recovery, new[]
            {
                new ItemUseEffect(ItemEffectKind.RestoreResource, ItemResource.Health, 500),
                new ItemUseEffect(ItemEffectKind.RestoreResource, ItemResource.Health, 500)
            });

            _resources.SetHealth(400, _limits);

            ItemContainerState bag;
            WithStack(Double, 1, out bag);

            ItemUseResult result = ItemUseService.Use(bag, 0, Context());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.HealthRestored, Is.EqualTo(600),
                "600 points were missing; the second effect could only supply the rest");
            Assert.That(_resources.CurrentHealth, Is.EqualTo(MaxHealth));
        }

        [Test]
        public void A_multi_effect_item_still_works_when_one_pool_is_already_full()
        {
            _resources.SetMana(MaxMana, _limits);

            ItemContainerState bag;
            WithStack(FullMeal, 1, out bag);

            ItemUseResult result = ItemUseService.Use(bag, 0, Context());

            Assert.That(result.IsAccepted, Is.True,
                "the health and the buff still land, so the item is not wasted");
            Assert.That(result.ManaRestored, Is.EqualTo(0));
            Assert.That(result.HealthRestored, Is.EqualTo(300));
            Assert.That(result.BuffsGranted, Is.EqualTo(1));
        }

        // ---- warp ----------------------------------------------------------------------

        [Test]
        public void A_town_scroll_resolves_its_destination_from_data_and_spends_one()
        {
            ItemContainerState bag;
            WithStack(TownScroll, 3, out bag);

            ItemUseResult result = ItemUseService.Use(bag, 0, Context());

            Assert.That(result.IsAccepted, Is.True, result.ToString());
            Assert.That(result.HasWarp, Is.True);
            Assert.That(result.WarpDestination, Is.EqualTo(new DefinitionId(TownA)),
                "the destination the fixture authored, not one chosen by code");
            Assert.That(bag.GetSlot(0).Quantity, Is.EqualTo(2));
        }

        [Test]
        public void Two_scrolls_differ_only_in_their_authored_destination()
        {
            const string ScrollB = "item.scroll.b";
            AddUsable(ScrollB, ItemUseType.WarpTown, new[]
            {
                new ItemUseEffect(ItemEffectKind.WarpToMap,
                    destinationMap: new DefinitionId(TownB))
            });

            ItemContainerState bag = Container(8);
            bag.Add(Stack(TownScroll, 1), Items);
            bag.Add(Stack(ScrollB, 1), Items);

            ItemUseResult first = ItemUseService.Use(bag, 0, Context());
            ItemUseResult second = ItemUseService.Use(bag, 1, Context());

            Assert.That(first.WarpDestination, Is.EqualTo(new DefinitionId(TownA)));
            Assert.That(second.WarpDestination, Is.EqualTo(new DefinitionId(TownB)));
        }

        [Test]
        public void A_scroll_pointing_at_a_field_is_refused_and_costs_nothing()
        {
            ItemContainerState bag;
            WithStack(FieldScroll, 2, out bag);

            ItemUseResult result = ItemUseService.Use(bag, 0, Context());

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(result.Reason, Is.EqualTo(ItemUseRejection.WarpNotAllowed),
                "fields are walked to; a scroll must not grant access");
            Assert.That(result.HasWarp, Is.False);
            Assert.That(bag.GetSlot(0).Quantity, Is.EqualTo(2));
        }

        [Test]
        public void A_scroll_pointing_at_a_boss_area_is_refused_and_costs_nothing()
        {
            ItemContainerState bag;
            WithStack(BossScroll, 2, out bag);

            ItemUseResult result = ItemUseService.Use(bag, 0, Context());

            Assert.That(result.Reason, Is.EqualTo(ItemUseRejection.WarpNotAllowed));
            Assert.That(bag.GetSlot(0).Quantity, Is.EqualTo(2));
        }

        [Test]
        public void A_scroll_pointing_at_nothing_is_refused_and_costs_nothing()
        {
            ItemContainerState bag;
            WithStack(BrokenScroll, 2, out bag);

            ItemUseResult result = ItemUseService.Use(bag, 0, Context());

            Assert.That(result.Reason, Is.EqualTo(ItemUseRejection.InvalidDestination));
            Assert.That(bag.GetSlot(0).Quantity, Is.EqualTo(2));
        }

        [Test]
        public void A_warp_effect_on_an_item_that_is_not_a_warp_item_cannot_move_anyone()
        {
            // What a bad data import looks like: a potion that acquired a warp effect.
            const string Smuggled = "item.smuggled";
            AddUsable(Smuggled, ItemUseType.Recovery, new[]
            {
                new ItemUseEffect(ItemEffectKind.RestoreResource, ItemResource.Health, 100),
                new ItemUseEffect(ItemEffectKind.WarpToMap,
                    destinationMap: new DefinitionId(TownA))
            });

            ItemContainerState bag;
            WithStack(Smuggled, 1, out bag);

            ItemUseResult result = ItemUseService.Use(bag, 0, Context());

            Assert.That(result.Reason, Is.EqualTo(ItemUseRejection.WarpNotAllowed),
                "an item must declare itself a warp before it may move a character");
            Assert.That(result.HasWarp, Is.False);
            Assert.That(bag.GetSlot(0).Quantity, Is.EqualTo(1));
            Assert.That(_resources.CurrentHealth, Is.EqualTo(400), "and nothing was applied");
        }

        [Test]
        public void A_warp_item_with_no_warp_effect_is_refused()
        {
            const string Empty = "item.scroll.empty";
            AddUsable(Empty, ItemUseType.WarpTown, new[]
            {
                new ItemUseEffect(ItemEffectKind.RestoreResource, ItemResource.Health, 10)
            });

            ItemContainerState bag;
            WithStack(Empty, 1, out bag);

            Assert.That(ItemUseService.Use(bag, 0, Context()).Reason,
                Is.EqualTo(ItemUseRejection.UnknownUseType));
            Assert.That(bag.GetSlot(0).Quantity, Is.EqualTo(1));
        }

        [Test]
        public void A_warp_without_a_map_registry_is_refused_rather_than_guessed()
        {
            ItemContainerState bag;
            WithStack(TownScroll, 1, out bag);

            var noMaps = new ItemUseService.Context(Items, _resources, _limits,
                StatusEffects, null, default, _grants);

            Assert.That(ItemUseService.Use(bag, 0, noMaps).Reason,
                Is.EqualTo(ItemUseRejection.MissingContext));
            Assert.That(bag.GetSlot(0).Quantity, Is.EqualTo(1));
        }

        // ---- refusals ------------------------------------------------------------------

        [Test]
        public void An_item_content_turned_off_cannot_be_used()
        {
            ItemContainerState bag;
            WithStack(DisabledPotion, 3, out bag);

            ItemUseResult result = ItemUseService.Use(bag, 0, Context());

            Assert.That(result.Reason, Is.EqualTo(ItemUseRejection.NotUsable));
            Assert.That(bag.GetSlot(0).Quantity, Is.EqualTo(3));
            Assert.That(_resources.CurrentHealth, Is.EqualTo(400));
        }

        [Test]
        public void An_item_with_no_use_configuration_cannot_be_used()
        {
            ItemContainerState bag;
            WithStack(Potion, 3, out bag);   // the plain fixture item: usable never authored

            Assert.That(ItemUseService.Use(bag, 0, Context()).Reason,
                Is.EqualTo(ItemUseRejection.NotUsable));
            Assert.That(bag.GetSlot(0).Quantity, Is.EqualTo(3));
        }

        [Test]
        public void An_item_whose_label_and_effects_disagree_is_refused()
        {
            ItemContainerState bag;
            WithStack(MislabelledPotion, 2, out bag);

            Assert.That(ItemUseService.Use(bag, 0, Context()).Reason,
                Is.EqualTo(ItemUseRejection.UnknownUseType),
                "an item labelled Recovery that restores nothing is bad authoring");
            Assert.That(bag.GetSlot(0).Quantity, Is.EqualTo(2));
        }

        [Test]
        public void A_use_configured_with_no_effects_is_refused()
        {
            const string Hollow = "item.hollow";
            AddUsable(Hollow, ItemUseType.Recovery, new ItemUseEffect[0]);

            ItemContainerState bag;
            WithStack(Hollow, 1, out bag);

            Assert.That(ItemUseService.Use(bag, 0, Context()).Reason,
                Is.EqualTo(ItemUseRejection.NotUsable));
        }

        [Test]
        public void A_non_self_target_is_refused_rather_than_treated_as_self()
        {
            const string AllyPotion = "item.ally";
            AddUsable(AllyPotion, ItemUseType.Recovery, new[]
            {
                new ItemUseEffect(ItemEffectKind.RestoreResource, ItemResource.Health, 500)
            }, target: ItemUseTarget.Ally);

            ItemContainerState bag;
            WithStack(AllyPotion, 1, out bag);

            ItemUseResult result = ItemUseService.Use(bag, 0, Context());

            Assert.That(result.Reason, Is.EqualTo(ItemUseRejection.InvalidTarget));
            Assert.That(_resources.CurrentHealth, Is.EqualTo(400),
                "silently healing the user would be a different item than the one authored");
            Assert.That(bag.GetSlot(0).Quantity, Is.EqualTo(1));
        }

        [Test]
        public void An_effect_missing_the_field_its_kind_needs_is_refused()
        {
            const string Blank = "item.blank";
            AddUsable(Blank, ItemUseType.Recovery, new[]
            {
                new ItemUseEffect(ItemEffectKind.RestoreResource, ItemResource.None, 500)
            });

            ItemContainerState bag;
            WithStack(Blank, 1, out bag);

            Assert.That(ItemUseService.Use(bag, 0, Context()).Reason,
                Is.EqualTo(ItemUseRejection.InvalidEffect),
                "a blank resource must not be read as a default pool");
        }

        [Test]
        public void A_negative_amount_is_refused_rather_than_draining_a_pool()
        {
            const string Poison = "item.negative";
            AddUsable(Poison, ItemUseType.Recovery, new[]
            {
                new ItemUseEffect(ItemEffectKind.RestoreResource, ItemResource.Health, -500)
            });

            ItemContainerState bag;
            WithStack(Poison, 1, out bag);

            Assert.That(ItemUseService.Use(bag, 0, Context()).Reason,
                Is.EqualTo(ItemUseRejection.InvalidEffect));
            Assert.That(_resources.CurrentHealth, Is.EqualTo(400));
        }

        [Test]
        public void An_empty_slot_and_a_bad_index_are_refused()
        {
            ItemContainerState bag = Container(4);

            Assert.That(ItemUseService.Use(bag, 0, Context()).Reason,
                Is.EqualTo(ItemUseRejection.SourceEmpty));
            Assert.That(ItemUseService.Use(bag, 99, Context()).Reason,
                Is.EqualTo(ItemUseRejection.SlotOutOfRange));
            Assert.That(ItemUseService.Use(bag, -1, Context()).Reason,
                Is.EqualTo(ItemUseRejection.SlotOutOfRange));
            Assert.That(ItemUseService.Use(null, 0, Context()).Reason,
                Is.EqualTo(ItemUseRejection.MissingContext));
        }

        [Test]
        public void An_item_whose_definition_is_gone_is_refused_rather_than_consumed()
        {
            ItemContainerState bag = Container(4);
            bag.Add(Stack(RedPotion, 2), Items);

            // A registry that no longer knows the item: what a content patch looks like.
            var emptyRegistry = new DefinitionRegistry<ItemDefinition>();
            var context = new ItemUseService.Context(emptyRegistry, _resources, _limits,
                StatusEffects, Maps, default, _grants);

            Assert.That(ItemUseService.Use(bag, 0, context).Reason,
                Is.EqualTo(ItemUseRejection.UnknownDefinition));
            Assert.That(bag.GetSlot(0).Quantity, Is.EqualTo(2));
        }

        [Test]
        public void Someone_elses_bag_is_refused_when_an_owner_is_asserted()
        {
            ItemContainerState bag;
            WithStack(RedPotion, 2, out bag);

            ItemUseResult result = ItemUseService.Use(bag, 0, Context(new OwnerId("account:other")));

            Assert.That(result.Reason, Is.EqualTo(ItemUseRejection.NotOwned));
            Assert.That(bag.GetSlot(0).Quantity, Is.EqualTo(2));
            Assert.That(_resources.CurrentHealth, Is.EqualTo(400));
        }

        [Test]
        public void The_owning_account_may_use_its_own_item()
        {
            ItemContainerState bag;
            WithStack(RedPotion, 2, out bag);

            Assert.That(ItemUseService.Use(bag, 0, Context(Owner)).IsAccepted, Is.True);
            Assert.That(bag.GetSlot(0).Quantity, Is.EqualTo(1));
        }

        [Test]
        public void A_use_with_no_resource_state_is_refused_rather_than_consuming()
        {
            ItemContainerState bag;
            WithStack(RedPotion, 2, out bag);

            var noResources = new ItemUseService.Context(Items, null, _limits);

            Assert.That(ItemUseService.Use(bag, 0, noResources).Reason,
                Is.EqualTo(ItemUseRejection.MissingContext));
            Assert.That(bag.GetSlot(0).Quantity, Is.EqualTo(2));
        }

        [Test]
        public void A_buff_with_no_status_registry_is_refused_rather_than_consuming()
        {
            ItemContainerState bag;
            WithStack(StrengthFood, 2, out bag);

            var noStatus = new ItemUseService.Context(Items, _resources, _limits,
                null, Maps, default, _grants);

            Assert.That(ItemUseService.Use(bag, 0, noStatus).Reason,
                Is.EqualTo(ItemUseRejection.MissingContext));
            Assert.That(bag.GetSlot(0).Quantity, Is.EqualTo(2));
        }

        [Test]
        public void Using_the_last_one_empties_the_slot_without_touching_the_others()
        {
            ItemContainerState bag = Container(8);
            bag.Add(Stack(RedPotion, 1), Items);
            bag.Add(Stack(BluePotion, 4), Items);

            ItemUseResult result = ItemUseService.Use(bag, 0, Context());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(bag.GetSlot(0).IsEmpty, Is.True);
            Assert.That(bag.GetSlot(1).Quantity, Is.EqualTo(4),
                "the other stack is a different instance and must not move");
        }

        [Test]
        public void Two_stacks_of_the_same_item_are_spent_independently()
        {
            // A stack ceiling of 20 is what forces two slots to hold one DefinitionId, which
            // is the situation where identifying an item by definition alone goes wrong.
            const string Small = "item.small";
            AddUsable(Small, ItemUseType.Recovery, new[]
            {
                new ItemUseEffect(ItemEffectKind.RestoreResource, ItemResource.Health, 50)
            }, maxStack: 20);

            ItemContainerState bag = Container(8);
            bag.Add(Stack(Small, 20), Items);
            bag.Add(Stack(Small, 15), Items);

            Assert.That(bag.GetSlot(0).Quantity, Is.EqualTo(20));
            Assert.That(bag.GetSlot(1).Quantity, Is.EqualTo(15));
            Assert.That(bag.GetSlot(0).InstanceId, Is.Not.EqualTo(bag.GetSlot(1).InstanceId));

            ItemUseService.Use(bag, 0, Context());

            Assert.That(bag.GetSlot(0).Quantity, Is.EqualTo(19));
            Assert.That(bag.GetSlot(1).Quantity, Is.EqualTo(15),
                "using slot 0 must never reach slot 1");
        }
    }
}
