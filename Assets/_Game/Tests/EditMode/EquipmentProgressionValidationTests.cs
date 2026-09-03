using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Catching malformed progression content before a player does.
    /// </summary>
    /// <remarks>
    /// The services already refuse bad rules at runtime, so none of this is a safety net
    /// for the game. It is a safety net for the person authoring content: a missing step at
    /// level 4 should fail in the content pass pointing at the row, not turn up as a player
    /// reporting that their sword stops at +3.
    /// </remarks>
    internal sealed class EquipmentProgressionValidationTests : ItemContainerTestBase
    {
        private const string Material = "item.mat";
        private const string Gold = "item.gold";
        private const string StoneA = "stone.a";
        private const string StoneB = "stone.b";

        private DefinitionValidator _validator;

        [SetUp]
        public void CreateValidator()
        {
            _validator = new DefinitionValidator(new IDefinitionValidationRule[]
            {
                new EquipmentProgressionValidationRule(),
                new StoneFusionValidationRule()
            });

            AddItem(Material, stackable: true, maxStack: 999);
            AddItem(Gold, stackable: true, maxStack: 999999);
            AddStone(StoneA,
                new[] { new StatModifier(new DefinitionId(Str), StatModifierKind.Flat, 1f) });
            AddStone(StoneB,
                new[] { new StatModifier(new DefinitionId(Str), StatModifierKind.Flat, 2f) });
        }

        private ValidationReport Check(IDefinition definition)
        {
            return _validator.Validate(definition, Items);
        }

        private static bool HasError(ValidationReport report, string fragment)
        {
            for (int i = 0; i < report.Messages.Count; i++)
            {
                ValidationMessage message = report.Messages[i];
                if (message.Severity != ValidationSeverity.Error) continue;
                if (message.Message.Contains(fragment)) return true;
            }

            return false;
        }

        // ---- enhancement tracks --------------------------------------------------------

        [Test]
        public void A_well_formed_track_passes()
        {
            EnhancementDefinition rule = AddEnhancementRule("rule.good", maxLevel: 2, steps: new[]
            {
                Step(0, 0.9f, null, Material, 1, 10),
                Step(1, 0.5f, null, Material, 2, 20)
            });
            SetPrivate(rule, "_currencyItem", new DefinitionId(Gold));

            ValidationReport report = Check(rule);

            Assert.That(report.IsValid, Is.True, Describe(report));
        }

        [Test]
        public void A_gap_in_the_levels_is_an_error()
        {
            // Max level 4, but nothing advances from 2: enhancement stops dead there.
            EnhancementDefinition rule = AddEnhancementRule("rule.gap", maxLevel: 4, steps: new[]
            {
                Step(0, 1f, null, Material, 1, 0),
                Step(1, 1f, null, Material, 1, 0),
                Step(3, 1f, null, Material, 1, 0)
            });

            ValidationReport report = Check(rule);

            Assert.That(report.IsValid, Is.False);
            Assert.That(HasError(report, "No step advances from level 2"), Is.True, Describe(report));
        }

        [Test]
        public void Two_steps_from_the_same_level_is_an_error()
        {
            EnhancementDefinition rule = AddEnhancementRule("rule.dupe", maxLevel: 1, steps: new[]
            {
                Step(0, 1f, null, Material, 1, 0),
                Step(0, 0.5f, null, Material, 2, 0)
            });

            Assert.That(HasError(Check(rule), "Two steps advance from level 0"), Is.True);
        }

        [Test]
        public void An_impossible_success_chance_is_an_error()
        {
            EnhancementDefinition high = AddEnhancementRule("rule.high", maxLevel: 1, steps: new[]
            {
                Step(0, 1.5f, null, Material, 1, 0)
            });

            EnhancementDefinition low = AddEnhancementRule("rule.low", maxLevel: 1, steps: new[]
            {
                Step(0, -0.2f, null, Material, 1, 0)
            });

            Assert.That(HasError(Check(high), "outside zero to one"), Is.True);
            Assert.That(HasError(Check(low), "outside zero to one"), Is.True);
        }

        [Test]
        public void Negative_costs_and_amounts_are_errors()
        {
            EnhancementDefinition rule = AddEnhancementRule("rule.negative", maxLevel: 1, steps: new[]
            {
                Step(0, 1f, null, Material, -2, -50)
            });

            ValidationReport report = Check(rule);

            Assert.That(HasError(report, "negative amount of material"), Is.True, Describe(report));
            Assert.That(HasError(report, "negative currency cost"), Is.True, Describe(report));
        }

        [Test]
        public void Requiring_an_unnamed_material_is_an_error()
        {
            EnhancementDefinition rule = AddEnhancementRule("rule.unnamed", maxLevel: 1, steps: new[]
            {
                Step(0, 1f, null, null, 3, 0)
            });

            Assert.That(HasError(Check(rule), "unnamed material"), Is.True);
        }

        [Test]
        public void A_material_that_does_not_exist_is_an_error()
        {
            EnhancementDefinition rule = AddEnhancementRule("rule.ghost", maxLevel: 1, steps: new[]
            {
                Step(0, 1f, null, "item.does.not.exist", 1, 0)
            });

            Assert.That(HasError(Check(rule), "does not resolve"), Is.True);
        }

        [Test]
        public void Charging_currency_with_no_currency_item_is_an_error()
        {
            EnhancementDefinition rule = AddEnhancementRule("rule.nocurrency", maxLevel: 1,
                steps: new[] { Step(0, 1f, null, Material, 1, 100) });

            Assert.That(HasError(Check(rule), "names no currency item"), Is.True);
        }

        [Test]
        public void A_downgrade_authored_at_level_zero_is_an_error()
        {
            EnhancementDefinition rule = AddEnhancementRule("rule.degrade0", maxLevel: 1,
                steps: new[]
                {
                    Step(0, 1f, null, Material, 1, 0, EnhancementFailureBehavior.DegradeLevel)
                });

            Assert.That(HasError(Check(rule), "no level below zero"), Is.True);
        }

        [Test]
        public void A_maximum_below_the_minimum_is_an_error()
        {
            var rule = UnityEngine.ScriptableObject.CreateInstance<EnhancementDefinition>();
            UnityEngine.JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"rule.inverted\"},\"_minLevel\":5,\"_maxLevel\":2}", rule);

            try
            {
                Assert.That(HasError(Check(rule), "below minimum level"), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(rule);
            }
        }

        [Test]
        public void A_track_with_no_steps_warns_rather_than_failing()
        {
            EnhancementDefinition rule = AddEnhancementRule("rule.empty", maxLevel: 0,
                steps: new EnhancementStep[0]);

            ValidationReport report = Check(rule);

            Assert.That(report.IsValid, Is.True, "inert content is not wrong content");
            Assert.That(report.WarningCount, Is.GreaterThan(0));
        }

        // ---- equipment -----------------------------------------------------------------

        [Test]
        public void Equipment_marked_enhanceable_with_no_track_is_an_error()
        {
            EquipmentDefinition blade = AddEquipment("equip.orphan", EquipmentSlot.MainHand, 0);
            SetPrivate(blade, "_enhanceable", true);

            Assert.That(HasError(Check(blade), "names no enhancement track"), Is.True);
        }

        [Test]
        public void Negative_sockets_and_negative_ceilings_are_errors()
        {
            EquipmentDefinition blade = AddEquipment("equip.negative", EquipmentSlot.MainHand, 0);
            SetPrivate(blade, "_statusStoneSlots", -2);
            SetPrivate(blade, "_maxEnhancementLevel", -1);

            ValidationReport report = Check(blade);

            Assert.That(HasError(report, "Status stone slot count is negative"), Is.True);
            Assert.That(HasError(report, "Maximum enhancement level is negative"), Is.True);
        }

        [Test]
        public void A_well_formed_piece_passes()
        {
            AddEnhancementRule("rule.fine", maxLevel: 1,
                steps: new[] { Step(0, 1f, null, Material, 1, 0) });

            EquipmentDefinition blade = AddEquipment("equip.fine", EquipmentSlot.MainHand, 0);
            SetPrivate(blade, "_enhanceable", true);
            SetPrivate(blade, "_maxEnhancementLevel", 1);
            SetPrivate(blade, "_enhancementRule", new DefinitionId("rule.fine"));
            SetPrivate(blade, "_statusStoneSlots", 2);

            Assert.That(Check(blade).IsValid, Is.True);
        }

        // ---- rarity --------------------------------------------------------------------

        [Test]
        public void A_tier_that_would_remove_sockets_is_an_error()
        {
            RarityDefinition rarity = AddRarity("rarity.shrink", order: 1, bonusSlots: -2);

            Assert.That(HasError(Check(rarity), "may only widen a piece"), Is.True);
        }

        [Test]
        public void A_well_formed_tier_passes()
        {
            RarityDefinition rarity = AddRarity("rarity.ok", order: 3, bonusSlots: 1,
                maxEnhancement: 5);

            Assert.That(Check(rarity).IsValid, Is.True);
        }

        // ---- stones --------------------------------------------------------------------

        [Test]
        public void A_stone_with_an_impossible_chance_is_an_error()
        {
            ItemDefinition stone = AddStone("stone.bad",
                new[] { new StatModifier(new DefinitionId(Str), StatModifierKind.Flat, 1f) },
                successChance: 2f);

            Assert.That(HasError(Check(stone), "outside zero to one"), Is.True);
        }

        [Test]
        public void A_stone_that_grants_nothing_warns()
        {
            ItemDefinition stone = AddStone("stone.inert", null);

            ValidationReport report = Check(stone);

            Assert.That(report.IsValid, Is.True);
            Assert.That(report.WarningCount, Is.GreaterThan(0));
        }

        [Test]
        public void An_ordinary_item_is_not_checked_as_a_stone()
        {
            ItemDefinition potion = AddItem("item.plain", stackable: true, maxStack: 10);

            Assert.That(Check(potion).IsValid, Is.True);
            Assert.That(Check(potion).WarningCount, Is.EqualTo(0),
                "a potion granting no socket modifiers is not a finding");
        }

        // ---- fusion recipes ------------------------------------------------------------

        [Test]
        public void A_well_formed_recipe_passes()
        {
            StoneFusionDefinition recipe = AddFusionRecipe("fuse.ok", new[]
            {
                new FusionIngredient(new DefinitionId(StoneA), 3)
            }, StoneB);

            Assert.That(Check(recipe).IsValid, Is.True, Describe(Check(recipe)));
        }

        [Test]
        public void A_recipe_with_no_inputs_is_an_error()
        {
            StoneFusionDefinition recipe = AddFusionRecipe("fuse.free",
                new FusionIngredient[0], StoneB);

            Assert.That(HasError(Check(recipe), "create something from nothing"), Is.True);
        }

        [Test]
        public void A_recipe_whose_result_does_not_exist_is_an_error()
        {
            StoneFusionDefinition recipe = AddFusionRecipe("fuse.ghost", new[]
            {
                new FusionIngredient(new DefinitionId(StoneA), 2)
            }, "stone.nowhere");

            Assert.That(HasError(Check(recipe), "does not resolve"), Is.True);
        }

        [Test]
        public void Zero_and_negative_input_quantities_are_errors()
        {
            StoneFusionDefinition recipe = AddFusionRecipe("fuse.zero", new[]
            {
                new FusionIngredient(new DefinitionId(StoneA), 0),
                new FusionIngredient(new DefinitionId(StoneB), -3)
            }, StoneB);

            Assert.That(HasError(Check(recipe), "cannot be consumed"), Is.True);
        }

        [Test]
        public void A_recipe_charging_currency_with_no_currency_item_is_an_error()
        {
            StoneFusionDefinition recipe = AddFusionRecipe("fuse.nocurrency", new[]
            {
                new FusionIngredient(new DefinitionId(StoneA), 2)
            }, StoneB, currencyCost: 100);

            Assert.That(HasError(Check(recipe), "names no currency item"), Is.True);
        }

        [Test]
        public void An_impossible_recipe_chance_is_an_error()
        {
            StoneFusionDefinition recipe = AddFusionRecipe("fuse.odds", new[]
            {
                new FusionIngredient(new DefinitionId(StoneA), 2)
            }, StoneB, successChance: 3f);

            Assert.That(HasError(Check(recipe), "outside zero to one"), Is.True);
        }

        [Test]
        public void Listing_one_input_twice_warns_because_the_quantities_add_up()
        {
            StoneFusionDefinition recipe = AddFusionRecipe("fuse.twice", new[]
            {
                new FusionIngredient(new DefinitionId(StoneA), 2),
                new FusionIngredient(new DefinitionId(StoneA), 3)
            }, StoneB);

            ValidationReport report = Check(recipe);

            Assert.That(report.IsValid, Is.True, "it is legal, just usually a mistake");
            Assert.That(report.WarningCount, Is.GreaterThan(0));
        }

        [Test]
        public void A_consolation_prize_on_a_certain_recipe_warns()
        {
            StoneFusionDefinition recipe = AddFusionRecipe("fuse.pointless", new[]
            {
                new FusionIngredient(new DefinitionId(StoneA), 2)
            }, StoneB, successChance: 0f, failureResult: StoneA);

            ValidationReport report = Check(recipe);

            Assert.That(report.IsValid, Is.True);
            Assert.That(report.WarningCount, Is.GreaterThan(0),
                "the odds were probably forgotten");
        }

        private static string Describe(ValidationReport report)
        {
            var builder = new System.Text.StringBuilder();

            for (int i = 0; i < report.Messages.Count; i++)
            {
                builder.Append(report.Messages[i].Severity).Append(": ")
                    .Append(report.Messages[i].Message).Append('\n');
            }

            return builder.ToString();
        }
    }
}
