using System;
using System.Linq;
using System.Reflection;
using ChibiFantasy.Client.UI;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.UI;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The progression UI: previews, tooltips and the command boundary.
    /// </summary>
    /// <remarks>
    /// The rule this suite exists to protect is that looking is not doing. A preview
    /// resolves modifiers at a level the piece is not at, and asking for one -- however
    /// many times, from however many panels -- must leave the piece untouched. That holds
    /// because <c>EquipmentModifierResolver</c> is pure, and these tests check it stays
    /// that way.
    /// </remarks>
    internal sealed class EquipmentProgressionUiTests : ItemContainerTestBase
    {
        private const string Blade = "equip.blade";
        private const string RuleId = "rule.blade";
        private const string Material = "item.enhance.stone";
        private const string StoneStr = "stone.str";
        private const string GreatStone = "stone.great";
        private const string Rare = "rarity.rare";

        private GameObject _host;
        private InventoryUiController _controller;
        private ItemContainerState _inventory;
        private ItemContainerState _storage;
        private CharacterEquipmentState _equipment;
        private EquipmentDefinition _blade;

        [SetUp]
        public void CreateController()
        {
            _host = new GameObject("ProgressionUiHost");
            _controller = _host.AddComponent<InventoryUiController>();

            AddItem(Material, stackable: true, maxStack: 999);
            AddRarity(Rare, order: 10,
                modifiers: new[] { new StatModifier(new DefinitionId(Str), StatModifierKind.Flat, 7f) },
                bonusSlots: 1);

            AddEnhancementRule(RuleId, maxLevel: 3, steps: new[]
            {
                Step(0, 1f,
                    new[] { new StatModifier(new DefinitionId(Str), StatModifierKind.Flat, 2f) },
                    Material, 2, 0),
                Step(1, 0.5f,
                    new[] { new StatModifier(new DefinitionId(Str), StatModifierKind.Flat, 5f) },
                    Material, 3, 0),
                Step(2, 0.25f,
                    new[] { new StatModifier(new DefinitionId(Str), StatModifierKind.Flat, 9f) },
                    Material, 4, 0)
            });

            _blade = AddEquipment(Blade, EquipmentSlot.MainHand, level: 0,
                modifiers: new[] { new StatModifier(new DefinitionId(Str), StatModifierKind.Flat, 10f) });
            SetPrivate(_blade, "_enhanceable", true);
            SetPrivate(_blade, "_maxEnhancementLevel", 3);
            SetPrivate(_blade, "_enhancementRule", new DefinitionId(RuleId));
            SetPrivate(_blade, "_statusStoneSlots", 2);
            SetPrivate(_blade, "_equipmentCategory", EquipmentCategory.Weapon);

            AddStone(StoneStr,
                new[] { new StatModifier(new DefinitionId(Str), StatModifierKind.Flat, 3f) });
            AddStone(GreatStone,
                new[] { new StatModifier(new DefinitionId(Str), StatModifierKind.Flat, 12f) });

            _inventory = Container(8);
            _storage = Container(8);
            _equipment = new CharacterEquipmentState(new CharacterId("char:test"));
        }

        [TearDown]
        public void DestroyController()
        {
            if (_host != null) UnityEngine.Object.DestroyImmediate(_host);
        }

        private EquipmentInstance Bind(int material = 20)
        {
            EquipmentInstance piece = Gear(Blade);
            _inventory.Add(piece, Items);
            if (material > 0) _inventory.Add(Stack(Material, material), Items);

            _controller.Bind(_inventory, _storage, _equipment, Items, 10);
            _controller.BindProgression(Rarities, Enhancements, FusionRecipes,
                AlwaysSucceeds.Instance);

            return piece;
        }

        // ---- previews do not mutate ----------------------------------------------------

        [Test]
        public void Building_an_enhancement_preview_changes_nothing()
        {
            EquipmentInstance piece = Bind();

            Revision before = piece.Revision;
            int levelBefore = piece.EnhancementLevel;
            int materialBefore = _inventory.CountOf(new DefinitionId(Material));
            Revision bagBefore = _inventory.Revision;

            for (int i = 0; i < 25; i++)
            {
                EnhancementViewData view = _controller.BuildEnhancementView(0);
                Assert.That(view.IsValid, Is.True);
            }

            Assert.That(piece.Revision, Is.EqualTo(before));
            Assert.That(piece.EnhancementLevel, Is.EqualTo(levelBefore));
            Assert.That(_inventory.CountOf(new DefinitionId(Material)), Is.EqualTo(materialBefore));
            Assert.That(_inventory.Revision, Is.EqualTo(bagBefore),
                "twenty-five previews must cost exactly nothing");
        }

        [Test]
        public void The_preview_shows_what_the_next_level_would_be_worth()
        {
            EquipmentInstance piece = Bind();

            EnhancementViewData view = _controller.BuildEnhancementView(0);

            Assert.That(view.CurrentLevel, Is.EqualTo(0));
            Assert.That(view.MaxLevel, Is.EqualTo(3));
            Assert.That(TotalStr(view.CurrentModifiers), Is.EqualTo(10f), "base only at +0");
            Assert.That(TotalStr(view.PreviewModifiers), Is.EqualTo(12f), "base 10 + level-1's 2");
            Assert.That(view.MaterialAmount, Is.EqualTo(2));
            Assert.That(view.MaterialHeld, Is.EqualTo(20));
            Assert.That(view.SuccessChance, Is.EqualTo(1f));
        }

        [Test]
        public void The_preview_follows_the_piece_as_it_climbs()
        {
            EquipmentInstance piece = Bind();

            _controller.SubmitEnhance(0);

            EnhancementViewData view = _controller.BuildEnhancementView(0);

            Assert.That(view.CurrentLevel, Is.EqualTo(1));
            Assert.That(TotalStr(view.CurrentModifiers), Is.EqualTo(12f));
            Assert.That(TotalStr(view.PreviewModifiers), Is.EqualTo(15f),
                "base 10 + level-2's 5, not a running total");
            Assert.That(view.SuccessChance, Is.EqualTo(0.5f), "the next step's own odds");
        }

        [Test]
        public void A_preview_includes_the_tier_and_the_stones_already_socketed()
        {
            EquipmentInstance piece = Bind();
            piece.SetRarity(new DefinitionId(Rare));
            piece.AddEnchant(new EquipmentEnchant(new DefinitionId(StoneStr), 0));

            EnhancementViewData view = _controller.BuildEnhancementView(0);

            Assert.That(TotalStr(view.CurrentModifiers), Is.EqualTo(20f), "10 + 7 + 3");
            Assert.That(TotalStr(view.PreviewModifiers), Is.EqualTo(22f), "and +2 for level 1");
        }

        [Test]
        public void A_piece_at_the_ceiling_offers_no_attempt_but_still_reports_its_worth()
        {
            EquipmentInstance piece = Bind();

            for (int i = 0; i < 3; i++) _controller.SubmitEnhance(0);
            Assert.That(piece.EnhancementLevel, Is.EqualTo(3));

            EnhancementViewData view = _controller.BuildEnhancementView(0);

            Assert.That(view.IsAtCeiling, Is.True);
            Assert.That(view.CanAttempt, Is.False);
            Assert.That(EnhancementPanel.CanOffer(view), Is.False);
            Assert.That(TotalStr(view.CurrentModifiers), Is.EqualTo(19f));
            Assert.That(view.PreviewModifiers, Is.Empty, "there is no next level to preview");
        }

        [Test]
        public void A_view_reports_short_materials_without_refusing_to_be_built()
        {
            Bind(material: 1);

            EnhancementViewData view = _controller.BuildEnhancementView(0);

            Assert.That(view.IsValid, Is.True);
            Assert.That(view.HasEnoughMaterial, Is.False);
            Assert.That(EnhancementPanel.CanOffer(view), Is.False,
                "the button greys out, and the service would refuse anyway");
        }

        [Test]
        public void Nothing_enhanceable_selected_gives_an_invalid_view()
        {
            Bind();

            Assert.That(_controller.BuildEnhancementView(1).IsValid, Is.False,
                "slot 1 holds material");
            Assert.That(_controller.BuildEnhancementView(7).IsValid, Is.False, "slot 7 is empty");
            Assert.That(_controller.BuildEnhancementView(99).IsValid, Is.False);
            Assert.That(EnhancementPanel.FormatTitle(EnhancementViewData.None, null), Is.Empty);
            Assert.That(EnhancementPanel.FormatDetail(EnhancementViewData.None, null), Is.Empty);
        }

        [Test]
        public void Building_a_fusion_preview_changes_nothing()
        {
            Bind();
            _inventory.Add(Stack(StoneStr, 10), Items);

            AddFusionRecipe("fuse.great", new[]
            {
                new FusionIngredient(new DefinitionId(StoneStr), 3)
            }, GreatStone);

            Revision before = _inventory.Revision;

            for (int i = 0; i < 25; i++)
            {
                FusionViewData view = _controller.BuildFusionView(new DefinitionId("fuse.great"));
                Assert.That(view.IsValid, Is.True);
                Assert.That(view.CanAttempt, Is.True);
            }

            Assert.That(_inventory.Revision, Is.EqualTo(before));
            Assert.That(_inventory.CountOf(new DefinitionId(StoneStr)), Is.EqualTo(10));
            Assert.That(_inventory.CountOf(new DefinitionId(GreatStone)), Is.EqualTo(0));
        }

        [Test]
        public void A_fusion_view_reports_held_against_required()
        {
            Bind();
            _inventory.Add(Stack(StoneStr, 2), Items);

            AddFusionRecipe("fuse.great", new[]
            {
                new FusionIngredient(new DefinitionId(StoneStr), 3)
            }, GreatStone);

            FusionViewData view = _controller.BuildFusionView(new DefinitionId("fuse.great"));

            Assert.That(view.Inputs.Count, Is.EqualTo(1));
            Assert.That(view.Inputs[0].Required, Is.EqualTo(3));
            Assert.That(view.Inputs[0].Held, Is.EqualTo(2));
            Assert.That(view.Inputs[0].IsSatisfied, Is.False);
            Assert.That(view.CanAttempt, Is.False);
            Assert.That(StoneFusionPanel.FormatDetail(view, null), Does.Contain("2/3"));
        }

        [Test]
        public void An_unresolvable_recipe_gives_an_invalid_view()
        {
            Bind();

            Assert.That(_controller.BuildFusionView(new DefinitionId("fuse.nope")).IsValid,
                Is.False);
            Assert.That(_controller.BuildFusionView(DefinitionId.None).IsValid, Is.False);
            Assert.That(StoneFusionPanel.FormatTitle(FusionViewData.None, null), Is.Empty);
        }

        // ---- commands go through the services ------------------------------------------

        [Test]
        public void The_controller_enhances_through_the_service_and_refreshes()
        {
            EquipmentInstance piece = Bind();

            EnhancementResult result = _controller.SubmitEnhance(0);

            Assert.That(result.IsAccepted, Is.True, result.ToString());
            Assert.That(piece.EnhancementLevel, Is.EqualTo(1));
            Assert.That(_inventory.CountOf(new DefinitionId(Material)), Is.EqualTo(18));
            Assert.That(_controller.LastEnhancementResult.IsUpgrade, Is.True);
        }

        [Test]
        public void A_refused_enhancement_keeps_the_service_reason_and_changes_nothing()
        {
            EquipmentInstance piece = Bind(material: 0);

            Revision before = _inventory.Revision;
            EnhancementResult result = _controller.SubmitEnhance(0);

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(result.Reason, Is.EqualTo(EnhancementRejection.MissingMaterial),
                "the UI reports the service's reason rather than inventing one");
            Assert.That(piece.EnhancementLevel, Is.EqualTo(0));
            Assert.That(_inventory.Revision, Is.EqualTo(before));
        }

        [Test]
        public void The_controller_sockets_through_the_service()
        {
            EquipmentInstance piece = Bind();
            _inventory.Add(Stack(StoneStr, 3), Items);

            int stoneSlot = -1;
            for (int i = 0; i < _inventory.Capacity; i++)
            {
                if (_inventory.GetSlot(i).DefinitionId == new DefinitionId(StoneStr)) stoneSlot = i;
            }

            EnchantResult result = _controller.SubmitEnchant(0, stoneSlot);

            Assert.That(result.IsAccepted, Is.True, result.ToString());
            Assert.That(piece.EnchantCount, Is.EqualTo(1));
            Assert.That(_inventory.CountOf(new DefinitionId(StoneStr)), Is.EqualTo(2));
        }

        [Test]
        public void The_controller_fuses_through_the_service()
        {
            Bind();
            _inventory.Add(Stack(StoneStr, 6), Items);

            AddFusionRecipe("fuse.great", new[]
            {
                new FusionIngredient(new DefinitionId(StoneStr), 3)
            }, GreatStone);

            FusionResult result = _controller.SubmitFusion(new DefinitionId("fuse.great"));

            Assert.That(result.IsAccepted, Is.True, result.ToString());
            Assert.That(_inventory.CountOf(new DefinitionId(StoneStr)), Is.EqualTo(3));
            Assert.That(_inventory.CountOf(new DefinitionId(GreatStone)), Is.EqualTo(1));
        }

        [Test]
        public void A_screen_that_never_bound_progression_is_refused_rather_than_half_working()
        {
            EquipmentInstance piece = Gear(Blade);
            _inventory.Add(piece, Items);
            _inventory.Add(Stack(Material, 20), Items);

            _controller.Bind(_inventory, _storage, _equipment, Items, 10);
            // BindProgression deliberately not called.

            EnhancementResult result = _controller.SubmitEnhance(0);

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(result.Reason, Is.EqualTo(EnhancementRejection.MissingContext));
            Assert.That(piece.EnhancementLevel, Is.EqualTo(0));
            Assert.That(_inventory.CountOf(new DefinitionId(Material)), Is.EqualTo(20));
        }

        // ---- tooltip -------------------------------------------------------------------

        [Test]
        public void The_tooltip_shows_the_level_the_tier_and_the_sockets()
        {
            EquipmentInstance piece = Bind();
            piece.SetRarity(new DefinitionId(Rare));
            _controller.SubmitEnhance(0);
            _controller.SubmitEnhance(0);

            _inventory.Add(Stack(StoneStr, 1), Items);
            int stoneSlot = -1;
            for (int i = 0; i < _inventory.Capacity; i++)
            {
                if (_inventory.GetSlot(i).DefinitionId == new DefinitionId(StoneStr)) stoneSlot = i;
            }

            _controller.SubmitEnchant(0, stoneSlot);
            _controller.OnInventoryClicked(0);

            ItemTooltipData tooltip = _controller.BuildSelectionTooltip();

            Assert.That(tooltip.IsValid, Is.True);
            Assert.That(tooltip.EnhancementLevel, Is.EqualTo(2));
            Assert.That(tooltip.RarityId, Is.EqualTo(new DefinitionId(Rare)));
            Assert.That(tooltip.EnchantCapacity, Is.EqualTo(3), "two authored plus one from Rare");
            Assert.That(tooltip.Enchants.Count, Is.EqualTo(3), "empty sockets are shown too");
            Assert.That(tooltip.HasEnchants, Is.True);
            Assert.That(TotalStr(tooltip.EffectiveModifiers), Is.EqualTo(25f),
                "base 10 + rare 7 + level-2's 5 + stone 3");

            Assert.That(ItemTooltipView.FormatTitle(tooltip, null), Does.Contain("+2"));

            string body = ItemTooltipView.FormatBody(tooltip, null);
            Assert.That(body, Does.Contain("Sockets 1/3"));
            Assert.That(body, Does.Contain("Total"));
        }

        [Test]
        public void The_tooltip_tracks_live_state_as_it_changes()
        {
            EquipmentInstance piece = Bind();
            _controller.OnInventoryClicked(0);

            Assert.That(_controller.BuildSelectionTooltip().EnhancementLevel, Is.EqualTo(0));

            _controller.SubmitEnhance(0);
            _controller.OnInventoryClicked(0);

            Assert.That(_controller.BuildSelectionTooltip().EnhancementLevel, Is.EqualTo(1),
                "the tooltip reads the piece, not a cached copy");
        }

        [Test]
        public void A_non_equipment_tooltip_carries_no_progression()
        {
            Bind();
            _controller.OnInventoryClicked(1);

            ItemTooltipData tooltip = _controller.BuildSelectionTooltip();

            Assert.That(tooltip.IsValid, Is.True);
            Assert.That(tooltip.HasEnhancement, Is.False);
            Assert.That(tooltip.HasRarity, Is.False);
            Assert.That(tooltip.EnchantCapacity, Is.EqualTo(0));
        }

        // ---- boundary ------------------------------------------------------------------

        [Test]
        public void The_progression_ui_holds_no_gameplay_state()
        {
            Assembly ui = typeof(EnhancementViewData).Assembly;
            Assert.That(ui.GetName().Name, Is.EqualTo("ChibiFantasy.UI"));

            foreach (Type type in ui.GetTypes())
            {
                FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Static
                    | BindingFlags.Public | BindingFlags.NonPublic);

                foreach (FieldInfo field in fields)
                {
                    string held = field.FieldType.FullName ?? string.Empty;

                    Assert.That(held, Does.Not.Contain("ChibiFantasy.Gameplay"),
                        type.Name + "." + field.Name + " holds gameplay state");
                    Assert.That(held, Does.Not.Contain("EquipmentInstance"),
                        type.Name + "." + field.Name + " holds an owned piece rather than a snapshot");
                }
            }
        }

        [Test]
        public void Progression_mutations_happen_only_in_the_controller()
        {
            // Structural: the three services may be called from exactly one file, which is
            // the command boundary the architecture depends on.
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts",
                "*.cs", System.IO.SearchOption.AllDirectories);

            foreach (string file in files)
            {
                string normalized = file.Replace('\\', '/');
                if (normalized.Contains("/Client/UI/InventoryUiController.cs")) continue;
                if (normalized.Contains("/Gameplay/")) continue;

                string source = System.IO.File.ReadAllText(file);

                Assert.That(source, Does.Not.Contain("EnhancementService.TryEnhance"),
                    normalized + " mutates enhancement outside the command boundary");
                Assert.That(source, Does.Not.Contain("EnchantService.TryEnchant"),
                    normalized + " mutates enchants outside the command boundary");
                Assert.That(source, Does.Not.Contain("StoneFusionService.TryFuse"),
                    normalized + " mutates inventory by fusion outside the command boundary");
            }
        }

        [Test]
        public void No_duplicate_progression_system_was_introduced()
        {
            string[] forbidden =
            {
                "EnhancementSystem", "EnhancementSystem2", "RaritySystem", "RaritySystem2",
                "StatStoneSystem", "StatStoneSystem2", "EnchantSystem", "FusionSystem",
                "FusionInventory", "EquipmentModel", "CharacterEquipmentModel",
                "BuffManager", "IconManager", "TooltipManager"
            };

            Type[] types = typeof(EnhancementViewData).Assembly.GetTypes()
                .Concat(typeof(EnhancementService).Assembly.GetTypes())
                .Concat(typeof(InventoryUiController).Assembly.GetTypes())
                .ToArray();

            foreach (Type type in types)
            {
                Assert.That(forbidden, Does.Not.Contain(type.Name),
                    type.FullName + " duplicates something that already exists");
            }
        }

        private static float TotalStr(System.Collections.Generic.IReadOnlyList<StatModifier> list)
        {
            var id = new DefinitionId(Str);
            float total = 0f;

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Stat == id && list[i].Kind == StatModifierKind.Flat)
                {
                    total += list[i].Value;
                }
            }

            return total;
        }
    }
}
