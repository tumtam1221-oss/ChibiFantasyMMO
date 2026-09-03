using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Socketing status stones, and fusing them.
    /// </summary>
    /// <remarks>
    /// Both systems spend a player's items, so the tests that matter most are the ones
    /// proving a refusal spends nothing and a success spends exactly what was authored.
    /// Fusion has one failure mode worth naming: consuming the inputs and then discovering
    /// there is nowhere to put the result. There is no rollback, so capacity is checked
    /// first and a test holds that line.
    ///
    /// Every stone, recipe and restriction below is a FIXTURE. No service knows any of them.
    /// </remarks>
    internal sealed class EnchantAndFusionTests : ItemContainerTestBase
    {
        private const string Blade = "equip.blade";
        private const string Robe2 = "equip.robe2";
        private const string StoneStr = "stone.str";
        private const string StoneVit = "stone.vit";
        private const string StoneCommon = "stone.common";
        private const string StoneWeaponOnly = "stone.weapon";
        private const string StoneRareOnly = "stone.rareonly";
        private const string StoneRisky = "stone.risky";
        private const string StoneSafe = "stone.safe";
        private const string StoneWiper = "stone.wiper";
        private const string StoneNotFusable = "stone.bound";
        private const string GreatStone = "stone.great";
        private const string Gold = "item.gold";
        private const string Rare = "rarity.rare";
        private const string Vit = "stat.vit";

        private EquipmentDefinition _blade;

        [SetUp]
        public void AuthorEnchanting()
        {
            AddItem(Gold, stackable: true, maxStack: 999999);
            AddRarity(Rare, order: 10, bonusSlots: 1);

            _blade = AddEquipment(Blade, EquipmentSlot.MainHand, level: 0);
            SetPrivate(_blade, "_equipmentCategory", EquipmentCategory.Weapon);
            SetPrivate(_blade, "_statusStoneSlots", 2);

            EquipmentDefinition robe = AddEquipment(Robe2, EquipmentSlot.Body, level: 0);
            SetPrivate(robe, "_equipmentCategory", EquipmentCategory.Armor);
            SetPrivate(robe, "_statusStoneSlots", 2);

            AddStone(StoneStr,
                new[] { new StatModifier(new DefinitionId(Str), StatModifierKind.Flat, 3f) });
            AddStone(StoneVit,
                new[] { new StatModifier(new DefinitionId(Vit), StatModifierKind.Flat, 4f) });

            // Authored to allow several copies, so capacity can be tested without the
            // duplicate rule getting there first.
            AddStone(StoneCommon,
                new[] { new StatModifier(new DefinitionId(Str), StatModifierKind.Flat, 1f) },
                maxPerEquipment: 9);

            AddStone(StoneWeaponOnly,
                new[] { new StatModifier(new DefinitionId(Str), StatModifierKind.Flat, 5f) },
                category: EquipmentCategory.Weapon);

            AddStone(StoneRareOnly,
                new[] { new StatModifier(new DefinitionId(Str), StatModifierKind.Flat, 9f) },
                rarities: new[] { new DefinitionId(Rare) });

            AddStone(StoneRisky, null, successChance: 0.5f,
                failure: EnchantFailureBehavior.LoseStone);
            AddStone(StoneSafe, null, successChance: 0.5f,
                failure: EnchantFailureBehavior.KeepStone);
            AddStone(StoneWiper, null, successChance: 0.5f,
                failure: EnchantFailureBehavior.ClearSockets);

            AddStone(StoneNotFusable, null, fusable: false);
            AddStone(GreatStone,
                new[] { new StatModifier(new DefinitionId(Str), StatModifierKind.Flat, 12f) });
        }

        private EnchantService.Context EnchantContext(IRandomResultSource results = null,
            OwnerId owner = default)
        {
            return new EnchantService.Context(Items, Rarities,
                results ?? AlwaysSucceeds.Instance, owner);
        }

        private StoneFusionService.Context FusionContext(IRandomResultSource results = null)
        {
            return new StoneFusionService.Context(Items, FusionRecipes,
                results ?? AlwaysSucceeds.Instance, Owner);
        }

        /// <summary>A bag with the piece in slot 0 and a stack of stones in slot 1.</summary>
        private ItemContainerState Bag(out EquipmentInstance piece, string stone,
            int stoneCount = 5, string equipment = Blade)
        {
            ItemContainerState bag = Container(8);
            piece = Gear(equipment);
            bag.Add(piece, Items);
            bag.Add(Stack(stone, stoneCount), Items);
            return bag;
        }

        // ---- enchant success -----------------------------------------------------------

        [Test]
        public void A_stone_goes_into_the_first_free_socket_and_is_consumed()
        {
            EquipmentInstance piece;
            ItemContainerState bag = Bag(out piece, StoneStr);

            EnchantResult result = EnchantService.TryEnchant(bag, 0, 1, EnchantContext());

            Assert.That(result.IsAccepted, Is.True, result.ToString());
            Assert.That(result.Outcome, Is.EqualTo(EnchantOutcome.Socketed));
            Assert.That(result.SocketIndex, Is.EqualTo(0));
            Assert.That(piece.EnchantCount, Is.EqualTo(1));
            Assert.That(bag.CountOf(new DefinitionId(StoneStr)), Is.EqualTo(4),
                "exactly one stone");
        }

        [Test]
        public void Sockets_fill_lowest_first_and_the_revision_advances_each_time()
        {
            EquipmentInstance piece;
            ItemContainerState bag = Bag(out piece, StoneCommon);

            Revision start = piece.Revision;

            Assert.That(EnchantService.TryEnchant(bag, 0, 1, EnchantContext()).SocketIndex,
                Is.EqualTo(0));
            Assert.That(EnchantService.TryEnchant(bag, 0, 1, EnchantContext()).SocketIndex,
                Is.EqualTo(1));

            Assert.That(piece.Revision, Is.EqualTo(start.Next().Next()));
        }

        [Test]
        public void A_socketed_stone_contributes_what_its_own_definition_authors()
        {
            EquipmentInstance piece;
            ItemContainerState bag = Bag(out piece, StoneVit);

            EnchantService.TryEnchant(bag, 0, 1, EnchantContext());

            var modifiers = EquipmentModifierResolver.Collect(piece, ResolverContext());
            float vit = 0f;
            for (int i = 0; i < modifiers.Count; i++)
            {
                if (modifiers[i].Stat == new DefinitionId(Vit)) vit += modifiers[i].Value;
            }

            Assert.That(vit, Is.EqualTo(4f));
        }

        [Test]
        public void A_worn_piece_can_be_socketed_and_the_stone_comes_from_the_bag()
        {
            EquipmentInstance piece;
            ItemContainerState bag = Bag(out piece, StoneStr);
            var equipment = new CharacterEquipmentState(new CharacterId("char:test"));

            EquipmentService.Equip(bag, equipment, 0, new EquipmentService.Context(Items, 1));

            // Equipping emptied slot 0; the stones are still in slot 1.
            EnchantResult result = EnchantService.TryEnchant(equipment, EquipmentSlot.MainHand,
                bag, 1, EnchantContext());

            Assert.That(result.IsAccepted, Is.True, result.ToString());
            Assert.That(piece.EnchantCount, Is.EqualTo(1));
            Assert.That(bag.CountOf(new DefinitionId(StoneStr)), Is.EqualTo(4));
        }

        // ---- enchant refusals ----------------------------------------------------------

        [Test]
        public void A_full_piece_is_refused_and_keeps_the_stone()
        {
            EquipmentInstance piece;
            ItemContainerState bag = Bag(out piece, StoneCommon);

            EnchantService.TryEnchant(bag, 0, 1, EnchantContext());
            EnchantService.TryEnchant(bag, 0, 1, EnchantContext());
            Assert.That(piece.EnchantCount, Is.EqualTo(2));

            Revision before = piece.Revision;
            EnchantResult result = EnchantService.TryEnchant(bag, 0, 1, EnchantContext());

            Assert.That(result.Reason, Is.EqualTo(EnchantRejection.NoCapacity));
            Assert.That(bag.CountOf(new DefinitionId(StoneCommon)), Is.EqualTo(3),
                "two went in; the refused third is still in the bag");
            Assert.That(piece.Revision, Is.EqualTo(before));
        }

        [Test]
        public void A_tier_that_grants_a_socket_makes_room_for_one_more()
        {
            EquipmentInstance piece;
            ItemContainerState bag = Bag(out piece, StoneCommon);
            piece.SetRarity(new DefinitionId(Rare));

            for (int i = 0; i < 3; i++)
            {
                Assert.That(EnchantService.TryEnchant(bag, 0, 1, EnchantContext()).IsAccepted,
                    Is.True, "attempt " + i);
            }

            Assert.That(piece.EnchantCount, Is.EqualTo(3), "two authored plus one from Rare");
            Assert.That(EnchantService.TryEnchant(bag, 0, 1, EnchantContext()).Reason,
                Is.EqualTo(EnchantRejection.NoCapacity));
        }

        [Test]
        public void A_second_copy_of_a_stone_limited_to_one_is_refused()
        {
            EquipmentInstance piece;
            ItemContainerState bag = Bag(out piece, StoneStr);

            Assert.That(EnchantService.TryEnchant(bag, 0, 1, EnchantContext()).IsAccepted, Is.True);

            EnchantResult second = EnchantService.TryEnchant(bag, 0, 1, EnchantContext());

            Assert.That(second.Reason, Is.EqualTo(EnchantRejection.DuplicateNotAllowed));
            Assert.That(piece.EnchantCount, Is.EqualTo(1));
            Assert.That(bag.CountOf(new DefinitionId(StoneStr)), Is.EqualTo(4));
        }

        [Test]
        public void A_stone_authored_to_allow_two_accepts_two()
        {
            const string Twice = "stone.twice";
            AddStone(Twice,
                new[] { new StatModifier(new DefinitionId(Str), StatModifierKind.Flat, 1f) },
                maxPerEquipment: 2);

            EquipmentInstance piece;
            ItemContainerState bag = Bag(out piece, Twice);

            Assert.That(EnchantService.TryEnchant(bag, 0, 1, EnchantContext()).IsAccepted, Is.True);
            Assert.That(EnchantService.TryEnchant(bag, 0, 1, EnchantContext()).IsAccepted, Is.True);
            Assert.That(piece.EnchantCount, Is.EqualTo(2));
        }

        [Test]
        public void A_weapon_stone_is_refused_by_armour()
        {
            EquipmentInstance robe;
            ItemContainerState bag = Bag(out robe, StoneWeaponOnly, equipment: Robe2);

            EnchantResult result = EnchantService.TryEnchant(bag, 0, 1, EnchantContext());

            Assert.That(result.Reason, Is.EqualTo(EnchantRejection.NotCompatible));
            Assert.That(robe.EnchantCount, Is.EqualTo(0));
            Assert.That(bag.CountOf(new DefinitionId(StoneWeaponOnly)), Is.EqualTo(5));
        }

        [Test]
        public void The_same_stone_is_accepted_by_the_category_it_names()
        {
            EquipmentInstance blade;
            ItemContainerState bag = Bag(out blade, StoneWeaponOnly);

            Assert.That(EnchantService.TryEnchant(bag, 0, 1, EnchantContext()).IsAccepted, Is.True);
        }

        [Test]
        public void A_tier_restricted_stone_is_refused_until_the_piece_is_that_tier()
        {
            EquipmentInstance piece;
            ItemContainerState bag = Bag(out piece, StoneRareOnly);

            Assert.That(EnchantService.TryEnchant(bag, 0, 1, EnchantContext()).Reason,
                Is.EqualTo(EnchantRejection.NotCompatible));

            piece.SetRarity(new DefinitionId(Rare));

            Assert.That(EnchantService.TryEnchant(bag, 0, 1, EnchantContext()).IsAccepted, Is.True);
        }

        [Test]
        public void An_item_that_is_not_a_stone_cannot_be_socketed()
        {
            ItemContainerState bag = Container(8);
            EquipmentInstance piece = Gear(Blade);
            bag.Add(piece, Items);
            bag.Add(Stack(Potion, 3), Items);

            EnchantResult result = EnchantService.TryEnchant(bag, 0, 1, EnchantContext());

            Assert.That(result.Reason, Is.EqualTo(EnchantRejection.NotAStone));
            Assert.That(bag.CountOf(new DefinitionId(Potion)), Is.EqualTo(3));
        }

        [Test]
        public void Someone_elses_piece_is_refused()
        {
            EquipmentInstance piece;
            ItemContainerState bag = Bag(out piece, StoneStr);

            EnchantResult result = EnchantService.TryEnchant(bag, 0, 1,
                EnchantContext(owner: new OwnerId("account:other")));

            Assert.That(result.Reason, Is.EqualTo(EnchantRejection.NotOwner));
            Assert.That(bag.CountOf(new DefinitionId(StoneStr)), Is.EqualTo(5));
        }

        [Test]
        public void Bad_slots_and_missing_context_are_refused()
        {
            EquipmentInstance piece;
            ItemContainerState bag = Bag(out piece, StoneStr);

            Assert.That(EnchantService.TryEnchant(bag, 99, 1, EnchantContext()).Reason,
                Is.EqualTo(EnchantRejection.InvalidEquipment));
            Assert.That(EnchantService.TryEnchant(bag, 1, 0, EnchantContext()).Reason,
                Is.EqualTo(EnchantRejection.InvalidEquipment), "slot 1 holds stones");
            Assert.That(EnchantService.TryEnchant(bag, 0, 99, EnchantContext()).Reason,
                Is.EqualTo(EnchantRejection.InvalidStone));
            Assert.That(EnchantService.TryEnchant(bag, 0, 5, EnchantContext()).Reason,
                Is.EqualTo(EnchantRejection.InvalidStone), "slot 5 is empty");
            Assert.That(EnchantService.TryEnchant(null, 0, 1, EnchantContext()).Reason,
                Is.EqualTo(EnchantRejection.MissingContext));
        }

        // ---- enchant failure behaviour -------------------------------------------------

        [Test]
        public void A_failed_roll_that_loses_the_stone_consumes_it_and_sockets_nothing()
        {
            EquipmentInstance piece;
            ItemContainerState bag = Bag(out piece, StoneRisky);

            EnchantResult result = EnchantService.TryEnchant(bag, 0, 1,
                EnchantContext(AlwaysFails.Instance));

            Assert.That(result.IsAccepted, Is.True, "the attempt ran");
            Assert.That(result.Outcome, Is.EqualTo(EnchantOutcome.FailedStoneLost));
            Assert.That(result.StonesConsumed, Is.EqualTo(1));
            Assert.That(bag.CountOf(new DefinitionId(StoneRisky)), Is.EqualTo(4));
            Assert.That(piece.EnchantCount, Is.EqualTo(0));
        }

        [Test]
        public void A_failed_roll_authored_to_keep_the_stone_consumes_nothing()
        {
            EquipmentInstance piece;
            ItemContainerState bag = Bag(out piece, StoneSafe);

            EnchantResult result = EnchantService.TryEnchant(bag, 0, 1,
                EnchantContext(AlwaysFails.Instance));

            Assert.That(result.Outcome, Is.EqualTo(EnchantOutcome.FailedStoneKept));
            Assert.That(result.StonesConsumed, Is.EqualTo(0));
            Assert.That(bag.CountOf(new DefinitionId(StoneSafe)), Is.EqualTo(5),
                "the stone was never taken, not taken and handed back");
        }

        [Test]
        public void A_failed_roll_authored_to_clear_sockets_empties_the_piece()
        {
            EquipmentInstance piece;
            ItemContainerState bag = Bag(out piece, StoneStr);
            bag.Add(Stack(StoneWiper, 2), Items);

            EnchantService.TryEnchant(bag, 0, 1, EnchantContext());
            Assert.That(piece.EnchantCount, Is.EqualTo(1));

            int wiperSlot = bag.IndexOf(bag.GetSlot(2).InstanceId);
            EnchantResult result = EnchantService.TryEnchant(bag, 0, wiperSlot,
                EnchantContext(AlwaysFails.Instance));

            Assert.That(result.Outcome, Is.EqualTo(EnchantOutcome.FailedSocketsCleared));
            Assert.That(piece.EnchantCount, Is.EqualTo(0),
                "everything socketed went with it, because the stone said so");
        }

        [Test]
        public void The_roll_source_decides_and_a_rejection_never_asks_it()
        {
            EquipmentInstance piece;
            ItemContainerState bag = Bag(out piece, StoneRisky);

            var scripted = new ScriptedResultSource(true);

            // Fill both sockets first so the next attempt is refused on capacity.
            EnchantService.TryEnchant(bag, 0, 1, EnchantContext());
            EnchantService.TryEnchant(bag, 0, 1, EnchantContext());

            EnchantService.TryEnchant(bag, 0, 1, EnchantContext(scripted));

            Assert.That(scripted.Calls, Is.EqualTo(0),
                "validation runs first, so a doomed attempt does not consume the sequence");
        }

        // ---- fusion --------------------------------------------------------------------

        [Test]
        public void A_recipe_consumes_exactly_its_inputs_and_produces_its_result()
        {
            AddFusionRecipe("fuse.great", new[]
            {
                new FusionIngredient(new DefinitionId(StoneStr), 3)
            }, GreatStone);

            ItemContainerState bag = Container(8);
            bag.Add(Stack(StoneStr, 10), Items);

            FusionResult result = StoneFusionService.TryFuse(bag,
                new DefinitionId("fuse.great"), FusionContext());

            Assert.That(result.IsAccepted, Is.True, result.ToString());
            Assert.That(result.WasFused, Is.True);
            Assert.That(result.Produced, Is.EqualTo(new DefinitionId(GreatStone)));
            Assert.That(result.ProducedQuantity, Is.EqualTo(1));
            Assert.That(bag.CountOf(new DefinitionId(StoneStr)), Is.EqualTo(7),
                "exactly three, not two and not four");
            Assert.That(bag.CountOf(new DefinitionId(GreatStone)), Is.EqualTo(1));
        }

        [Test]
        public void A_recipe_with_several_different_inputs_consumes_all_of_them()
        {
            AddFusionRecipe("fuse.mixed", new[]
            {
                new FusionIngredient(new DefinitionId(StoneStr), 2),
                new FusionIngredient(new DefinitionId(StoneVit), 3)
            }, GreatStone, resultQuantity: 2);

            ItemContainerState bag = Container(8);
            bag.Add(Stack(StoneStr, 5), Items);
            bag.Add(Stack(StoneVit, 5), Items);

            FusionResult result = StoneFusionService.TryFuse(bag,
                new DefinitionId("fuse.mixed"), FusionContext());

            Assert.That(result.IsAccepted, Is.True, result.ToString());
            Assert.That(bag.CountOf(new DefinitionId(StoneStr)), Is.EqualTo(3));
            Assert.That(bag.CountOf(new DefinitionId(StoneVit)), Is.EqualTo(2));
            Assert.That(bag.CountOf(new DefinitionId(GreatStone)), Is.EqualTo(2));
            Assert.That(result.InputsConsumed, Is.EqualTo(5));
        }

        [Test]
        public void Too_few_inputs_is_refused_and_consumes_nothing()
        {
            AddFusionRecipe("fuse.great", new[]
            {
                new FusionIngredient(new DefinitionId(StoneStr), 3)
            }, GreatStone);

            ItemContainerState bag = Container(8);
            bag.Add(Stack(StoneStr, 2), Items);

            Revision before = bag.Revision;
            FusionResult result = StoneFusionService.TryFuse(bag,
                new DefinitionId("fuse.great"), FusionContext());

            Assert.That(result.Reason, Is.EqualTo(FusionRejection.InsufficientQuantity));
            Assert.That(bag.CountOf(new DefinitionId(StoneStr)), Is.EqualTo(2));
            Assert.That(bag.Revision, Is.EqualTo(before));
        }

        [Test]
        public void A_partly_satisfied_recipe_consumes_nothing_at_all()
        {
            // The failure mode worth naming: enough of the first input, not of the second.
            AddFusionRecipe("fuse.mixed", new[]
            {
                new FusionIngredient(new DefinitionId(StoneStr), 2),
                new FusionIngredient(new DefinitionId(StoneVit), 9)
            }, GreatStone);

            ItemContainerState bag = Container(8);
            bag.Add(Stack(StoneStr, 5), Items);
            bag.Add(Stack(StoneVit, 3), Items);

            FusionResult result = StoneFusionService.TryFuse(bag,
                new DefinitionId("fuse.mixed"), FusionContext());

            Assert.That(result.Reason, Is.EqualTo(FusionRejection.InsufficientQuantity));
            Assert.That(bag.CountOf(new DefinitionId(StoneStr)), Is.EqualTo(5),
                "the input that WAS available must not have been eaten");
            Assert.That(bag.CountOf(new DefinitionId(StoneVit)), Is.EqualTo(3));
        }

        [Test]
        public void A_recipe_that_cannot_be_resolved_is_refused()
        {
            ItemContainerState bag = Container(8);
            bag.Add(Stack(StoneStr, 10), Items);

            Assert.That(StoneFusionService.TryFuse(bag, new DefinitionId("fuse.nope"),
                FusionContext()).Reason, Is.EqualTo(FusionRejection.InvalidRecipe));
            Assert.That(StoneFusionService.TryFuse(bag, DefinitionId.None,
                FusionContext()).Reason, Is.EqualTo(FusionRejection.InvalidRecipe));
        }

        [Test]
        public void A_recipe_with_no_inputs_is_refused_rather_than_creating_something()
        {
            AddFusionRecipe("fuse.free", new FusionIngredient[0], GreatStone);

            ItemContainerState bag = Container(8);

            Assert.That(StoneFusionService.TryFuse(bag, new DefinitionId("fuse.free"),
                FusionContext()).Reason, Is.EqualTo(FusionRejection.NoInputs));
            Assert.That(bag.CountOf(new DefinitionId(GreatStone)), Is.EqualTo(0));
        }

        [Test]
        public void A_stone_content_marked_unfusable_cannot_be_an_input()
        {
            AddFusionRecipe("fuse.bound", new[]
            {
                new FusionIngredient(new DefinitionId(StoneNotFusable), 2)
            }, GreatStone);

            ItemContainerState bag = Container(8);
            bag.Add(Stack(StoneNotFusable, 5), Items);

            FusionResult result = StoneFusionService.TryFuse(bag,
                new DefinitionId("fuse.bound"), FusionContext());

            Assert.That(result.Reason, Is.EqualTo(FusionRejection.InputNotFusable));
            Assert.That(bag.CountOf(new DefinitionId(StoneNotFusable)), Is.EqualTo(5));
        }

        [Test]
        public void A_recipe_whose_result_does_not_exist_is_refused_before_consuming()
        {
            AddFusionRecipe("fuse.ghost", new[]
            {
                new FusionIngredient(new DefinitionId(StoneStr), 2)
            }, "stone.does.not.exist");

            ItemContainerState bag = Container(8);
            bag.Add(Stack(StoneStr, 5), Items);

            FusionResult result = StoneFusionService.TryFuse(bag,
                new DefinitionId("fuse.ghost"), FusionContext());

            Assert.That(result.Reason, Is.EqualTo(FusionRejection.InvalidOutput));
            Assert.That(bag.CountOf(new DefinitionId(StoneStr)), Is.EqualTo(5),
                "consuming and then failing to deliver would destroy a player's materials");
        }

        [Test]
        public void There_must_be_somewhere_to_put_the_result_before_anything_is_spent()
        {
            AddFusionRecipe("fuse.great", new[]
            {
                new FusionIngredient(new DefinitionId(StoneStr), 2)
            }, GreatStone);

            // One slot, holding more stones than the recipe consumes, so nothing frees.
            ItemContainerState bag = Container(1);
            bag.Add(Stack(StoneStr, 10), Items);

            FusionResult result = StoneFusionService.TryFuse(bag,
                new DefinitionId("fuse.great"), FusionContext());

            Assert.That(result.Reason, Is.EqualTo(FusionRejection.InsufficientCapacity));
            Assert.That(bag.CountOf(new DefinitionId(StoneStr)), Is.EqualTo(10),
                "nothing was spent for a result with nowhere to go");
        }

        [Test]
        public void The_slot_the_inputs_free_is_room_the_result_may_use()
        {
            AddFusionRecipe("fuse.great", new[]
            {
                new FusionIngredient(new DefinitionId(StoneStr), 4)
            }, GreatStone);

            // A single slot holding exactly what the recipe eats: consuming it frees the slot.
            ItemContainerState bag = Container(1);
            bag.Add(Stack(StoneStr, 4), Items);

            FusionResult result = StoneFusionService.TryFuse(bag,
                new DefinitionId("fuse.great"), FusionContext());

            Assert.That(result.IsAccepted, Is.True, result.ToString());
            Assert.That(bag.CountOf(new DefinitionId(GreatStone)), Is.EqualTo(1));
            Assert.That(bag.CountOf(new DefinitionId(StoneStr)), Is.EqualTo(0));
        }

        [Test]
        public void A_currency_cost_is_charged_and_refused_when_it_cannot_be_paid()
        {
            AddFusionRecipe("fuse.paid", new[]
            {
                new FusionIngredient(new DefinitionId(StoneStr), 2)
            }, GreatStone, currencyCost: 500, currencyItem: Gold);

            ItemContainerState poor = Container(8);
            poor.Add(Stack(StoneStr, 5), Items);
            poor.Add(Stack(Gold, 100), Items);

            Assert.That(StoneFusionService.TryFuse(poor, new DefinitionId("fuse.paid"),
                FusionContext()).Reason, Is.EqualTo(FusionRejection.InsufficientCost));
            Assert.That(poor.CountOf(new DefinitionId(StoneStr)), Is.EqualTo(5),
                "the stones were not eaten for an attempt that could not be paid for");

            ItemContainerState rich = Container(8);
            rich.Add(Stack(StoneStr, 5), Items);
            rich.Add(Stack(Gold, 1000), Items);

            FusionResult result = StoneFusionService.TryFuse(rich,
                new DefinitionId("fuse.paid"), FusionContext());

            Assert.That(result.IsAccepted, Is.True, result.ToString());
            Assert.That(result.CurrencySpent, Is.EqualTo(500));
            Assert.That(rich.CountOf(new DefinitionId(Gold)), Is.EqualTo(500));
        }

        [Test]
        public void A_failed_fusion_produces_the_authored_consolation()
        {
            AddFusionRecipe("fuse.risky", new[]
            {
                new FusionIngredient(new DefinitionId(StoneStr), 3)
            }, GreatStone, successChance: 0.5f, failureResult: StoneVit, failureResultQuantity: 1);

            ItemContainerState bag = Container(8);
            bag.Add(Stack(StoneStr, 5), Items);

            FusionResult result = StoneFusionService.TryFuse(bag,
                new DefinitionId("fuse.risky"), FusionContext(AlwaysFails.Instance));

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.Outcome, Is.EqualTo(FusionOutcome.FailedWithConsolation));
            Assert.That(bag.CountOf(new DefinitionId(StoneVit)), Is.EqualTo(1));
            Assert.That(bag.CountOf(new DefinitionId(GreatStone)), Is.EqualTo(0));
            Assert.That(bag.CountOf(new DefinitionId(StoneStr)), Is.EqualTo(2),
                "failure still ate the inputs, because the recipe says so");
        }

        [Test]
        public void A_failure_authored_not_to_consume_leaves_the_inputs_alone()
        {
            AddFusionRecipe("fuse.gentle", new[]
            {
                new FusionIngredient(new DefinitionId(StoneStr), 3)
            }, GreatStone, successChance: 0.5f, consumeOnFailure: false);

            ItemContainerState bag = Container(8);
            bag.Add(Stack(StoneStr, 5), Items);

            FusionResult result = StoneFusionService.TryFuse(bag,
                new DefinitionId("fuse.gentle"), FusionContext(AlwaysFails.Instance));

            Assert.That(result.Outcome, Is.EqualTo(FusionOutcome.FailedEmpty));
            Assert.That(result.InputsConsumed, Is.EqualTo(0));
            Assert.That(bag.CountOf(new DefinitionId(StoneStr)), Is.EqualTo(5));
        }

        [Test]
        public void The_roll_source_makes_fusion_deterministic()
        {
            AddFusionRecipe("fuse.risky", new[]
            {
                new FusionIngredient(new DefinitionId(StoneStr), 1)
            }, GreatStone, successChance: 0.5f);

            ItemContainerState bag = Container(8);
            bag.Add(Stack(StoneStr, 10), Items);

            var scripted = new ScriptedResultSource(true, false, true);
            var context = FusionContext(scripted);
            var recipe = new DefinitionId("fuse.risky");

            Assert.That(StoneFusionService.TryFuse(bag, recipe, context).WasFused, Is.True);
            Assert.That(StoneFusionService.TryFuse(bag, recipe, context).WasFused, Is.False);
            Assert.That(StoneFusionService.TryFuse(bag, recipe, context).WasFused, Is.True);

            Assert.That(bag.CountOf(new DefinitionId(GreatStone)), Is.EqualTo(2));
            Assert.That(scripted.Calls, Is.EqualTo(3));
        }

        [Test]
        public void Fusing_creates_no_item_the_recipe_did_not_name()
        {
            AddFusionRecipe("fuse.great", new[]
            {
                new FusionIngredient(new DefinitionId(StoneStr), 3)
            }, GreatStone);

            ItemContainerState bag = Container(8);
            bag.Add(Stack(StoneStr, 9), Items);

            int before = TotalItems(bag);

            StoneFusionService.TryFuse(bag, new DefinitionId("fuse.great"), FusionContext());

            Assert.That(TotalItems(bag), Is.EqualTo(before - 3 + 1),
                "three in, one out, and nothing else appeared");
        }

        [Test]
        public void The_fused_result_belongs_to_the_acting_owner()
        {
            AddFusionRecipe("fuse.great", new[]
            {
                new FusionIngredient(new DefinitionId(StoneStr), 3)
            }, GreatStone);

            ItemContainerState bag = Container(8);
            bag.Add(Stack(StoneStr, 5), Items);

            StoneFusionService.TryFuse(bag, new DefinitionId("fuse.great"), FusionContext());

            int slot = -1;
            for (int i = 0; i < bag.Capacity; i++)
            {
                if (bag.GetSlot(i).DefinitionId == new DefinitionId(GreatStone)) slot = i;
            }

            Assert.That(slot, Is.Not.EqualTo(-1));
            Assert.That(bag.GetSlot(slot).Content.Owner, Is.EqualTo(Owner));
        }

        [Test]
        public void No_definition_id_is_compared_against_a_literal_in_either_service()
        {
            string[] sources =
            {
                System.IO.File.ReadAllText("Assets/_Game/Scripts/Gameplay/EnchantService.cs"),
                System.IO.File.ReadAllText("Assets/_Game/Scripts/Gameplay/StoneFusionService.cs")
            };

            string[] mustNotAppear = { StoneStr, StoneVit, GreatStone, Blade, Gold, Rare, "Stone." };

            foreach (string source in sources)
            {
                foreach (string forbidden in mustNotAppear)
                {
                    Assert.That(source, Does.Not.Contain(forbidden),
                        "a service names '" + forbidden + "'; rules must come from data");
                }
            }
        }

        private static int TotalItems(ItemContainerState container)
        {
            int total = 0;

            for (int i = 0; i < container.Capacity; i++)
            {
                ItemSlot slot = container.GetSlot(i);
                if (slot.IsOccupied) total += slot.Quantity;
            }

            return total;
        }
    }
}
