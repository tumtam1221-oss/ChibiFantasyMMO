using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Enhancing a piece of equipment.
    /// </summary>
    /// <remarks>
    /// The costly failure is not a refused attempt, it is an accepted one that should not
    /// have been: materials spent on a rejection, a level gained without paying, a sword
    /// destroyed by a rule nobody authored. Most of what follows exists to hold those
    /// lines.
    ///
    /// Every odds figure, material and consequence is a FIXTURE on a definition, and the
    /// roll is injected, so nothing here depends on chance.
    /// </remarks>
    internal sealed class EnhancementServiceTests : ItemContainerTestBase
    {
        private const string Blade = "equip.blade";
        private const string Plain = "equip.plain";
        private const string RuleId = "rule.blade";
        private const string Material = "item.enhance.stone";
        private const string Gold = "item.gold";

        private EquipmentDefinition _blade;

        [SetUp]
        public void AuthorEnhancement()
        {
            AddItem(Material, stackable: true, maxStack: 999);
            AddItem(Gold, stackable: true, maxStack: 999999);

            // Three steps. Each costs 2 material and 100 gold, and each fails differently
            // so the authored consequence is what is being observed, never a default.
            AddEnhancementRule(RuleId, maxLevel: 3, steps: new[]
            {
                Step(0, 1f,
                    new[] { new StatModifier(new DefinitionId(Str), StatModifierKind.Flat, 2f) },
                    Material, 2, 100, EnhancementFailureBehavior.LoseMaterials),
                Step(1, 0.5f,
                    new[] { new StatModifier(new DefinitionId(Str), StatModifierKind.Flat, 5f) },
                    Material, 2, 100, EnhancementFailureBehavior.DegradeLevel),
                Step(2, 0.1f,
                    new[] { new StatModifier(new DefinitionId(Str), StatModifierKind.Flat, 9f) },
                    Material, 2, 100, EnhancementFailureBehavior.DestroyItem)
            });

            SetPrivate(Enhancements.All[0], "_currencyItem", new DefinitionId(Gold));

            _blade = AddEquipment(Blade, EquipmentSlot.MainHand, level: 0,
                modifiers: new[] { new StatModifier(new DefinitionId(Str), StatModifierKind.Flat, 10f) });
            SetPrivate(_blade, "_enhanceable", true);
            SetPrivate(_blade, "_maxEnhancementLevel", 3);
            SetPrivate(_blade, "_enhancementRule", new DefinitionId(RuleId));

            // Same slot, deliberately not enhanceable.
            EquipmentDefinition plain = AddEquipment(Plain, EquipmentSlot.OffHand, level: 0);
            SetPrivate(plain, "_enhanceable", false);
        }

        private EnhancementService.Context Context(IRandomResultSource results = null,
            OwnerId owner = default)
        {
            return new EnhancementService.Context(Items, Enhancements, Rarities,
                results ?? AlwaysSucceeds.Instance, owner);
        }

        /// <summary>A bag holding the piece in slot 0 and enough to pay for several tries.</summary>
        private ItemContainerState Bag(out EquipmentInstance piece, int material = 20,
            int gold = 1000)
        {
            ItemContainerState bag = Container(8);
            piece = Gear(Blade);
            bag.Add(piece, Items);
            if (material > 0) bag.Add(Stack(Material, material), Items);
            if (gold > 0) bag.Add(Stack(Gold, gold), Items);
            return bag;
        }

        // ---- success -------------------------------------------------------------------

        [Test]
        public void A_piece_starts_at_zero_and_the_first_attempt_takes_it_to_one()
        {
            EquipmentInstance piece;
            ItemContainerState bag = Bag(out piece);

            Assert.That(piece.EnhancementLevel, Is.EqualTo(0));

            EnhancementResult result = EnhancementService.TryEnhance(bag, 0, Context());

            Assert.That(result.IsAccepted, Is.True, result.ToString());
            Assert.That(result.Outcome, Is.EqualTo(EnhancementOutcome.Upgraded));
            Assert.That(result.FromLevel, Is.EqualTo(0));
            Assert.That(result.ToLevel, Is.EqualTo(1));
            Assert.That(piece.EnhancementLevel, Is.EqualTo(1));
        }

        [Test]
        public void Successive_attempts_climb_one_level_at_a_time()
        {
            EquipmentInstance piece;
            ItemContainerState bag = Bag(out piece);

            Assert.That(EnhancementService.TryEnhance(bag, 0, Context()).ToLevel, Is.EqualTo(1));
            Assert.That(EnhancementService.TryEnhance(bag, 0, Context()).ToLevel, Is.EqualTo(2));
            Assert.That(EnhancementService.TryEnhance(bag, 0, Context()).ToLevel, Is.EqualTo(3));
            Assert.That(piece.EnhancementLevel, Is.EqualTo(3));
        }

        [Test]
        public void A_successful_attempt_consumes_exactly_what_the_step_authored()
        {
            EquipmentInstance piece;
            ItemContainerState bag = Bag(out piece);

            EnhancementResult result = EnhancementService.TryEnhance(bag, 0, Context());

            Assert.That(result.MaterialsConsumed, Is.EqualTo(2));
            Assert.That(result.CurrencySpent, Is.EqualTo(100));
            Assert.That(bag.CountOf(new DefinitionId(Material)), Is.EqualTo(18));
            Assert.That(bag.CountOf(new DefinitionId(Gold)), Is.EqualTo(900));
        }

        [Test]
        public void A_successful_attempt_advances_the_revision_exactly_once()
        {
            EquipmentInstance piece;
            ItemContainerState bag = Bag(out piece);

            Revision before = piece.Revision;
            EnhancementResult result = EnhancementService.TryEnhance(bag, 0, Context());

            Assert.That(piece.Revision, Is.EqualTo(before.Next()));
            Assert.That(result.Revision, Is.EqualTo(piece.Revision),
                "the result reports the revision a caller can check staleness against");
        }

        [Test]
        public void Enhancement_never_changes_who_owns_the_piece()
        {
            EquipmentInstance piece;
            ItemContainerState bag = Bag(out piece);

            OwnerId before = piece.Owner;
            EnhancementService.TryEnhance(bag, 0, Context());

            Assert.That(piece.Owner, Is.EqualTo(before));
            Assert.That(piece.InstanceId, Is.Not.EqualTo(InstanceId.None),
                "and it is still the same owned copy");
        }

        [Test]
        public void A_worn_piece_can_be_enhanced_and_pays_from_the_bag()
        {
            EquipmentInstance piece;
            ItemContainerState bag = Bag(out piece);
            var equipment = new CharacterEquipmentState(new CharacterId("char:test"));

            EquipmentService.Equip(bag, equipment, 0, new EquipmentService.Context(Items, 1));

            EnhancementResult result = EnhancementService.TryEnhance(equipment,
                EquipmentSlot.MainHand, bag, Context());

            Assert.That(result.IsAccepted, Is.True, result.ToString());
            Assert.That(piece.EnhancementLevel, Is.EqualTo(1));
            Assert.That(bag.CountOf(new DefinitionId(Material)), Is.EqualTo(18),
                "worn changes where the piece lives, not where its cost is paid from");
        }

        // ---- failure behaviour ---------------------------------------------------------

        [Test]
        public void A_failure_authored_to_keep_the_level_keeps_it_and_still_spends()
        {
            EquipmentInstance piece;
            ItemContainerState bag = Bag(out piece);

            EnhancementResult result = EnhancementService.TryEnhance(bag, 0,
                Context(AlwaysFails.Instance));

            Assert.That(result.IsAccepted, Is.True, "the attempt ran; it just did not succeed");
            Assert.That(result.Outcome, Is.EqualTo(EnhancementOutcome.FailedKept));
            Assert.That(piece.EnhancementLevel, Is.EqualTo(0));
            Assert.That(bag.CountOf(new DefinitionId(Material)), Is.EqualTo(18),
                "the materials are what a failed attempt costs");
        }

        [Test]
        public void A_failure_authored_to_downgrade_drops_exactly_one_level()
        {
            EquipmentInstance piece;
            ItemContainerState bag = Bag(out piece);

            EnhancementService.TryEnhance(bag, 0, Context());
            Assert.That(piece.EnhancementLevel, Is.EqualTo(1));

            // The step from 1 authors DegradeLevel.
            EnhancementResult result = EnhancementService.TryEnhance(bag, 0,
                Context(AlwaysFails.Instance));

            Assert.That(result.Outcome, Is.EqualTo(EnhancementOutcome.FailedDowngraded));
            Assert.That(result.ToLevel, Is.EqualTo(0));
            Assert.That(piece.EnhancementLevel, Is.EqualTo(0));
        }

        [Test]
        public void A_failure_authored_to_destroy_removes_the_piece_from_the_bag()
        {
            EquipmentInstance piece;
            ItemContainerState bag = Bag(out piece);

            EnhancementService.TryEnhance(bag, 0, Context());
            EnhancementService.TryEnhance(bag, 0, Context());
            Assert.That(piece.EnhancementLevel, Is.EqualTo(2));

            // The step from 2 authors DestroyItem.
            EnhancementResult result = EnhancementService.TryEnhance(bag, 0,
                Context(AlwaysFails.Instance));

            Assert.That(result.Outcome, Is.EqualTo(EnhancementOutcome.FailedDestroyed));
            Assert.That(result.WasDestroyed, Is.True);
            Assert.That(bag.IndexOf(piece.InstanceId), Is.EqualTo(-1),
                "the piece is gone from the container");
        }

        [Test]
        public void A_failure_authored_to_reset_returns_the_piece_to_zero()
        {
            const string ResetRule = "rule.reset";
            const string ResetBlade = "equip.reset";

            AddEnhancementRule(ResetRule, maxLevel: 3, steps: new[]
            {
                Step(0, 1f, null, Material, 1, 0, EnhancementFailureBehavior.LoseMaterials),
                Step(1, 1f, null, Material, 1, 0, EnhancementFailureBehavior.LoseMaterials),
                Step(2, 1f, null, Material, 1, 0, EnhancementFailureBehavior.ResetToZero)
            });

            EquipmentDefinition blade = AddEquipment(ResetBlade, EquipmentSlot.MainHand, level: 0);
            SetPrivate(blade, "_enhanceable", true);
            SetPrivate(blade, "_maxEnhancementLevel", 3);
            SetPrivate(blade, "_enhancementRule", new DefinitionId(ResetRule));

            ItemContainerState bag = Container(8);
            EquipmentInstance piece = Gear(ResetBlade);
            bag.Add(piece, Items);
            bag.Add(Stack(Material, 20), Items);

            EnhancementService.TryEnhance(bag, 0, Context());
            EnhancementService.TryEnhance(bag, 0, Context());
            Assert.That(piece.EnhancementLevel, Is.EqualTo(2));

            EnhancementResult result = EnhancementService.TryEnhance(bag, 0,
                Context(AlwaysFails.Instance));

            Assert.That(result.Outcome, Is.EqualTo(EnhancementOutcome.FailedReset));
            Assert.That(piece.EnhancementLevel, Is.EqualTo(0));
            Assert.That(bag.IndexOf(piece.InstanceId), Is.Not.EqualTo(-1),
                "reset is not destruction");
        }

        [Test]
        public void A_downgrade_authored_at_level_zero_is_refused_rather_than_clamped()
        {
            const string BadRule = "rule.bad";
            const string BadBlade = "equip.bad";

            AddEnhancementRule(BadRule, maxLevel: 3, steps: new[]
            {
                Step(0, 1f, null, Material, 1, 0, EnhancementFailureBehavior.DegradeLevel)
            });

            EquipmentDefinition blade = AddEquipment(BadBlade, EquipmentSlot.MainHand, level: 0);
            SetPrivate(blade, "_enhanceable", true);
            SetPrivate(blade, "_maxEnhancementLevel", 3);
            SetPrivate(blade, "_enhancementRule", new DefinitionId(BadRule));

            ItemContainerState bag = Container(8);
            bag.Add(Gear(BadBlade), Items);
            bag.Add(Stack(Material, 20), Items);

            EnhancementResult result = EnhancementService.TryEnhance(bag, 0, Context());

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(result.Reason, Is.EqualTo(EnhancementRejection.InvalidFailureBehavior),
                "clamping would apply a consequence content did not choose");
            Assert.That(bag.CountOf(new DefinitionId(Material)), Is.EqualTo(20));
        }

        // ---- rejections cost nothing ---------------------------------------------------

        [Test]
        public void An_attempt_at_the_ceiling_is_refused_and_costs_nothing()
        {
            EquipmentInstance piece;
            ItemContainerState bag = Bag(out piece);

            for (int i = 0; i < 3; i++) EnhancementService.TryEnhance(bag, 0, Context());
            Assert.That(piece.EnhancementLevel, Is.EqualTo(3));

            int materials = bag.CountOf(new DefinitionId(Material));
            int gold = bag.CountOf(new DefinitionId(Gold));
            Revision before = piece.Revision;

            EnhancementResult result = EnhancementService.TryEnhance(bag, 0, Context());

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(result.Reason, Is.EqualTo(EnhancementRejection.AlreadyMaxLevel));
            Assert.That(bag.CountOf(new DefinitionId(Material)), Is.EqualTo(materials));
            Assert.That(bag.CountOf(new DefinitionId(Gold)), Is.EqualTo(gold));
            Assert.That(piece.Revision, Is.EqualTo(before));
        }

        [Test]
        public void A_tier_ceiling_below_the_items_own_is_the_one_that_stops_it()
        {
            AddRarity("rarity.capped", order: 1, maxEnhancement: 1);

            EquipmentInstance piece;
            ItemContainerState bag = Bag(out piece);
            piece.SetRarity(new DefinitionId("rarity.capped"));

            Assert.That(EnhancementService.TryEnhance(bag, 0, Context()).IsAccepted, Is.True);

            EnhancementResult result = EnhancementService.TryEnhance(bag, 0, Context());

            Assert.That(result.Reason, Is.EqualTo(EnhancementRejection.AlreadyMaxLevel));
            Assert.That(piece.EnhancementLevel, Is.EqualTo(1));
        }

        [Test]
        public void Too_little_material_is_refused_and_spends_no_currency()
        {
            EquipmentInstance piece;
            ItemContainerState bag = Bag(out piece, material: 1);

            EnhancementResult result = EnhancementService.TryEnhance(bag, 0, Context());

            Assert.That(result.Reason, Is.EqualTo(EnhancementRejection.MissingMaterial));
            Assert.That(bag.CountOf(new DefinitionId(Material)), Is.EqualTo(1),
                "the one it had must still be there");
            Assert.That(bag.CountOf(new DefinitionId(Gold)), Is.EqualTo(1000),
                "and no currency was taken for an attempt that never ran");
            Assert.That(piece.EnhancementLevel, Is.EqualTo(0));
        }

        [Test]
        public void No_material_at_all_is_refused()
        {
            EquipmentInstance piece;
            ItemContainerState bag = Bag(out piece, material: 0);

            Assert.That(EnhancementService.TryEnhance(bag, 0, Context()).Reason,
                Is.EqualTo(EnhancementRejection.MissingMaterial));
        }

        [Test]
        public void Too_little_currency_is_refused_and_consumes_no_material()
        {
            EquipmentInstance piece;
            ItemContainerState bag = Bag(out piece, gold: 50);

            EnhancementResult result = EnhancementService.TryEnhance(bag, 0, Context());

            Assert.That(result.Reason, Is.EqualTo(EnhancementRejection.InsufficientCost));
            Assert.That(bag.CountOf(new DefinitionId(Material)), Is.EqualTo(20),
                "the material is untouched: validation ran before any consumption");
            Assert.That(bag.CountOf(new DefinitionId(Gold)), Is.EqualTo(50));
        }

        [Test]
        public void Someone_elses_piece_is_refused()
        {
            EquipmentInstance piece;
            ItemContainerState bag = Bag(out piece);

            EnhancementResult result = EnhancementService.TryEnhance(bag, 0,
                Context(owner: new OwnerId("account:other")));

            Assert.That(result.Reason, Is.EqualTo(EnhancementRejection.NotOwner));
            Assert.That(piece.EnhancementLevel, Is.EqualTo(0));
            Assert.That(bag.CountOf(new DefinitionId(Material)), Is.EqualTo(20));
        }

        [Test]
        public void The_owning_account_may_enhance_its_own_piece()
        {
            EquipmentInstance piece;
            ItemContainerState bag = Bag(out piece);

            Assert.That(EnhancementService.TryEnhance(bag, 0, Context(owner: Owner)).IsAccepted,
                Is.True);
        }

        [Test]
        public void A_piece_not_authored_as_enhanceable_is_refused()
        {
            ItemContainerState bag = Container(8);
            bag.Add(Gear(Plain), Items);
            bag.Add(Stack(Material, 20), Items);

            Assert.That(EnhancementService.TryEnhance(bag, 0, Context()).Reason,
                Is.EqualTo(EnhancementRejection.NotEnhanceable));
        }

        [Test]
        public void A_piece_whose_track_cannot_be_resolved_is_refused()
        {
            const string Orphan = "equip.orphan";
            EquipmentDefinition blade = AddEquipment(Orphan, EquipmentSlot.MainHand, level: 0);
            SetPrivate(blade, "_enhanceable", true);
            SetPrivate(blade, "_maxEnhancementLevel", 3);
            SetPrivate(blade, "_enhancementRule", new DefinitionId("rule.deleted.by.patch"));

            ItemContainerState bag = Container(8);
            bag.Add(Gear(Orphan), Items);
            bag.Add(Stack(Material, 20), Items);

            Assert.That(EnhancementService.TryEnhance(bag, 0, Context()).Reason,
                Is.EqualTo(EnhancementRejection.InvalidRule));
        }

        [Test]
        public void A_level_with_no_authored_step_is_refused_rather_than_improvised()
        {
            const string ShortRule = "rule.short";
            const string ShortBlade = "equip.short";

            // Max level 5, but only one step authored: levels 1..4 have no rule.
            AddEnhancementRule(ShortRule, maxLevel: 5, steps: new[]
            {
                Step(0, 1f, null, Material, 1, 0)
            });

            EquipmentDefinition blade = AddEquipment(ShortBlade, EquipmentSlot.MainHand, level: 0);
            SetPrivate(blade, "_enhanceable", true);
            SetPrivate(blade, "_maxEnhancementLevel", 5);
            SetPrivate(blade, "_enhancementRule", new DefinitionId(ShortRule));

            ItemContainerState bag = Container(8);
            bag.Add(Gear(ShortBlade), Items);
            bag.Add(Stack(Material, 20), Items);

            Assert.That(EnhancementService.TryEnhance(bag, 0, Context()).IsAccepted, Is.True);

            EnhancementResult second = EnhancementService.TryEnhance(bag, 0, Context());

            Assert.That(second.Reason, Is.EqualTo(EnhancementRejection.NoStepForLevel));
            Assert.That(bag.CountOf(new DefinitionId(Material)), Is.EqualTo(19),
                "only the first attempt's material was spent");
        }

        [Test]
        public void An_empty_slot_a_bad_index_and_a_non_equipment_item_are_all_refused()
        {
            EquipmentInstance piece;
            ItemContainerState bag = Bag(out piece);

            Assert.That(EnhancementService.TryEnhance(bag, 7, Context()).Reason,
                Is.EqualTo(EnhancementRejection.InvalidEquipment));
            Assert.That(EnhancementService.TryEnhance(bag, 99, Context()).Reason,
                Is.EqualTo(EnhancementRejection.InvalidEquipment));
            Assert.That(EnhancementService.TryEnhance(bag, 1, Context()).Reason,
                Is.EqualTo(EnhancementRejection.InvalidEquipment),
                "slot 1 holds material, not equipment");
            Assert.That(EnhancementService.TryEnhance(null, 0, Context()).Reason,
                Is.EqualTo(EnhancementRejection.MissingContext));
        }

        [Test]
        public void A_piece_whose_definition_is_gone_is_refused()
        {
            EquipmentInstance piece;
            ItemContainerState bag = Bag(out piece);

            var emptyItems = new DefinitionRegistry<ItemDefinition>();
            var context = new EnhancementService.Context(emptyItems, Enhancements);

            Assert.That(EnhancementService.TryEnhance(bag, 0, context).Reason,
                Is.EqualTo(EnhancementRejection.InvalidDefinition));
        }

        // ---- the injected roll ---------------------------------------------------------

        [Test]
        public void The_roll_source_decides_and_the_authored_odds_are_what_it_is_asked()
        {
            EquipmentInstance piece;
            ItemContainerState bag = Bag(out piece);

            // The step from 0 authors 1.0; a roll of 0.99 is below it, so it succeeds.
            Assert.That(EnhancementService.TryEnhance(bag, 0,
                Context(new ThresholdResultSource(0.99f))).IsUpgrade, Is.True);

            // The step from 1 authors 0.5; the same roll is above it, so it fails.
            EnhancementResult second = EnhancementService.TryEnhance(bag, 0,
                Context(new ThresholdResultSource(0.99f)));

            Assert.That(second.IsUpgrade, Is.False);
            Assert.That(second.Outcome, Is.EqualTo(EnhancementOutcome.FailedDowngraded));
        }

        [Test]
        public void A_roll_exactly_on_the_boundary_fails()
        {
            EquipmentInstance piece;
            ItemContainerState bag = Bag(out piece);
            EnhancementService.TryEnhance(bag, 0, Context());   // to +1, where odds are 0.5

            EnhancementResult result = EnhancementService.TryEnhance(bag, 0,
                Context(new ThresholdResultSource(0.5f)));

            Assert.That(result.IsUpgrade, Is.False,
                "the comparison is roll < chance, so a 0.0 chance can never succeed");
        }

        [Test]
        public void A_scripted_sequence_replays_exactly()
        {
            EquipmentInstance piece;
            ItemContainerState bag = Bag(out piece);

            var scripted = new ScriptedResultSource(true, true, false);
            var context = Context(scripted);

            Assert.That(EnhancementService.TryEnhance(bag, 0, context).ToLevel, Is.EqualTo(1));
            Assert.That(EnhancementService.TryEnhance(bag, 0, context).ToLevel, Is.EqualTo(2));

            EnhancementResult third = EnhancementService.TryEnhance(bag, 0, context);

            Assert.That(third.Outcome, Is.EqualTo(EnhancementOutcome.FailedDestroyed));
            Assert.That(scripted.Calls, Is.EqualTo(3));
        }

        [Test]
        public void A_rejected_attempt_never_asks_the_roll_source_anything()
        {
            EquipmentInstance piece;
            ItemContainerState bag = Bag(out piece, material: 0);

            var scripted = new ScriptedResultSource(true);
            EnhancementService.TryEnhance(bag, 0, Context(scripted));

            Assert.That(scripted.Calls, Is.EqualTo(0),
                "validation runs first, so a doomed attempt does not consume the sequence");
        }

        // ---- stats ---------------------------------------------------------------------

        [Test]
        public void The_level_reached_is_what_the_resolver_reports()
        {
            EquipmentInstance piece;
            ItemContainerState bag = Bag(out piece);
            var resolver = ResolverContext();

            EnhancementService.TryEnhance(bag, 0, Context());
            EnhancementService.TryEnhance(bag, 0, Context());

            var modifiers = EquipmentModifierResolver.Collect(piece, resolver);
            float total = 0f;
            for (int i = 0; i < modifiers.Count; i++)
            {
                if (modifiers[i].Stat == new DefinitionId(Str)) total += modifiers[i].Value;
            }

            Assert.That(total, Is.EqualTo(15f), "base 10 + level-2's 5, not 10+2+5");
        }

        [Test]
        public void No_definition_id_is_compared_against_a_literal_in_the_service()
        {
            string source = System.IO.File.ReadAllText(
                "Assets/_Game/Scripts/Gameplay/EnhancementService.cs");

            string[] mustNotAppear = { Material, Gold, Blade, RuleId, "Sword", "Potion", "Gold" };

            foreach (string forbidden in mustNotAppear)
            {
                Assert.That(source, Does.Not.Contain(forbidden),
                    "EnhancementService names '" + forbidden + "'; rules must come from data");
            }
        }
    }
}
