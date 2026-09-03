using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Rarity, enhancement level and sockets reaching effective stats.
    /// </summary>
    /// <remarks>
    /// The expensive failure in equipment progression is not a wrong number, it is a number
    /// that drifts: a modifier applied twice, a tier counted once per enhancement, a
    /// contribution left behind after unequipping. Most of what follows exists to hold that
    /// line, because a drifted stat looks plausible and compounds silently.
    ///
    /// Every value is a FIXTURE authored on a definition. Nothing in the resolver knows any
    /// of them.
    /// </remarks>
    internal sealed class EquipmentProgressionTests : ItemContainerTestBase
    {
        private const string Blade = "equip.blade";
        private const string RuleId = "rule.blade";
        private const string Normal = "rarity.normal";
        private const string Rare = "rarity.rare";
        private const string StoneStr = "stone.str";
        private const string StoneVit = "stone.vit";
        private const string Vit = "stat.vit";

        private EquipmentDefinition _blade;

        [SetUp]
        public void AuthorProgression()
        {
            AddRarity(Normal, order: 0);
            AddRarity(Rare, order: 10,
                modifiers: new[] { new StatModifier(new DefinitionId(Str), StatModifierKind.Flat, 7f) },
                bonusSlots: 1, maxEnhancement: 0);

            AddEnhancementRule(RuleId, maxLevel: 3, steps: new[]
            {
                Step(0, 1f, new[] { new StatModifier(new DefinitionId(Str), StatModifierKind.Flat, 2f) }),
                Step(1, 1f, new[] { new StatModifier(new DefinitionId(Str), StatModifierKind.Flat, 5f) }),
                Step(2, 1f, new[] { new StatModifier(new DefinitionId(Str), StatModifierKind.Flat, 9f) })
            });

            _blade = AddEquipment(Blade, EquipmentSlot.MainHand, level: 0,
                modifiers: new[] { new StatModifier(new DefinitionId(Str), StatModifierKind.Flat, 10f) });

            SetPrivate(_blade, "_enhanceable", true);
            SetPrivate(_blade, "_maxEnhancementLevel", 3);
            SetPrivate(_blade, "_enhancementRule", new DefinitionId(RuleId));
            SetPrivate(_blade, "_statusStoneSlots", 2);
            SetPrivate(_blade, "_rarity", new DefinitionId(Normal));

            AddStone(StoneStr,
                new[] { new StatModifier(new DefinitionId(Str), StatModifierKind.Flat, 3f) });
            AddStone(StoneVit,
                new[] { new StatModifier(new DefinitionId(Vit), StatModifierKind.Flat, 4f) });
        }

        private float TotalFlat(List<StatModifier> modifiers, string stat)
        {
            var id = new DefinitionId(stat);
            float total = 0f;

            for (int i = 0; i < modifiers.Count; i++)
            {
                if (modifiers[i].Stat == id && modifiers[i].Kind == StatModifierKind.Flat)
                {
                    total += modifiers[i].Value;
                }
            }

            return total;
        }

        // ---- defaults ------------------------------------------------------------------

        [Test]
        public void A_new_piece_starts_unenhanced_with_no_stones_and_no_rarity_override()
        {
            EquipmentInstance piece = Gear(Blade);

            Assert.That(piece.EnhancementLevel, Is.EqualTo(0));
            Assert.That(piece.EnchantCount, Is.EqualTo(0));
            Assert.That(piece.Rarity.IsValid, Is.False,
                "an unset override means the authored rarity applies");
            Assert.That(EquipmentModifierResolver.EffectiveRarityId(piece, _blade),
                Is.EqualTo(new DefinitionId(Normal)));
        }

        [Test]
        public void An_unenhanced_piece_contributes_only_what_the_item_authors()
        {
            List<StatModifier> modifiers =
                EquipmentModifierResolver.Collect(Gear(Blade), ResolverContext());

            Assert.That(TotalFlat(modifiers, Str), Is.EqualTo(10f),
                "base only: no tier bonus at Normal, no level, no stones");
        }

        // ---- enhancement resolution ----------------------------------------------------

        [Test]
        public void Each_level_contributes_its_own_modifiers_and_not_the_ones_below_it()
        {
            var context = ResolverContext();

            EquipmentInstance piece = Gear(Blade);
            Assert.That(TotalFlat(EquipmentModifierResolver.Collect(piece, context), Str),
                Is.EqualTo(10f));

            piece.SetEnhancementLevel(1);
            Assert.That(TotalFlat(EquipmentModifierResolver.Collect(piece, context), Str),
                Is.EqualTo(12f), "base 10 + level-1's 2");

            piece.SetEnhancementLevel(2);
            Assert.That(TotalFlat(EquipmentModifierResolver.Collect(piece, context), Str),
                Is.EqualTo(15f), "base 10 + level-2's 5, NOT 10+2+5");

            piece.SetEnhancementLevel(3);
            Assert.That(TotalFlat(EquipmentModifierResolver.Collect(piece, context), Str),
                Is.EqualTo(19f), "base 10 + level-3's 9");
        }

        [Test]
        public void Going_up_and_back_down_returns_exactly_where_it_started()
        {
            var context = ResolverContext();
            EquipmentInstance piece = Gear(Blade);

            float atZero = TotalFlat(EquipmentModifierResolver.Collect(piece, context), Str);

            piece.SetEnhancementLevel(3);
            piece.SetEnhancementLevel(0);

            Assert.That(TotalFlat(EquipmentModifierResolver.Collect(piece, context), Str),
                Is.EqualTo(atZero), "nothing accumulated on the way up");
        }

        [Test]
        public void Resolving_a_hundred_times_gives_the_same_answer_a_hundred_times()
        {
            var context = ResolverContext();
            EquipmentInstance piece = Gear(Blade);
            piece.SetRarity(new DefinitionId(Rare));
            piece.SetEnhancementLevel(2);
            piece.AddEnchant(new EquipmentEnchant(new DefinitionId(StoneStr), 0));

            float first = TotalFlat(EquipmentModifierResolver.Collect(piece, context), Str);

            for (int i = 0; i < 100; i++)
            {
                Assert.That(TotalFlat(EquipmentModifierResolver.Collect(piece, context), Str),
                    Is.EqualTo(first), "recalculation must be pure");
            }

            Assert.That(first, Is.EqualTo(25f), "base 10 + rare 7 + level-2's 5 + stone 3");
        }

        [Test]
        public void A_missing_step_for_a_level_grants_nothing_rather_than_guessing()
        {
            var context = ResolverContext();
            EquipmentInstance piece = Gear(Blade);

            // The track authors steps from 0, 1 and 2. Level 9 has no step that produced it.
            piece.SetEnhancementLevel(9);

            Assert.That(TotalFlat(EquipmentModifierResolver.Collect(piece, context), Str),
                Is.EqualTo(10f), "no authored step means no authored bonus");
        }

        [Test]
        public void Without_an_enhancement_registry_the_level_simply_contributes_nothing()
        {
            var context = new EquipmentModifierResolver.Context(Items, Rarities);
            EquipmentInstance piece = Gear(Blade);
            piece.SetEnhancementLevel(2);

            Assert.That(TotalFlat(EquipmentModifierResolver.Collect(piece, context), Str),
                Is.EqualTo(10f), "less information, never wrong information");
        }

        // ---- rarity --------------------------------------------------------------------

        [Test]
        public void A_rarity_override_replaces_the_authored_tier_without_touching_the_item()
        {
            var context = ResolverContext();
            EquipmentInstance piece = Gear(Blade);

            piece.SetRarity(new DefinitionId(Rare));

            Assert.That(EquipmentModifierResolver.EffectiveRarityId(piece, _blade),
                Is.EqualTo(new DefinitionId(Rare)));
            Assert.That(_blade.Rarity, Is.EqualTo(new DefinitionId(Normal)),
                "the definition is shared by every copy and must not have moved");
            Assert.That(TotalFlat(EquipmentModifierResolver.Collect(piece, context), Str),
                Is.EqualTo(17f), "base 10 + rare 7");
        }

        [Test]
        public void Clearing_the_override_falls_back_to_the_authored_tier()
        {
            EquipmentInstance piece = Gear(Blade);
            piece.SetRarity(new DefinitionId(Rare));
            piece.SetRarity(DefinitionId.None);

            Assert.That(EquipmentModifierResolver.EffectiveRarityId(piece, _blade),
                Is.EqualTo(new DefinitionId(Normal)));
        }

        [Test]
        public void The_tier_bonus_is_counted_once_no_matter_how_high_the_level_goes()
        {
            var context = ResolverContext();
            EquipmentInstance piece = Gear(Blade);
            piece.SetRarity(new DefinitionId(Rare));

            Assert.That(TotalFlat(EquipmentModifierResolver.Collect(piece, context), Str),
                Is.EqualTo(17f), "10 + 7");

            piece.SetEnhancementLevel(3);

            Assert.That(TotalFlat(EquipmentModifierResolver.Collect(piece, context), Str),
                Is.EqualTo(26f), "10 + 7 + 9, and the 7 is not counted four times");
        }

        [Test]
        public void An_unresolvable_rarity_contributes_nothing_rather_than_failing()
        {
            var context = ResolverContext();
            EquipmentInstance piece = Gear(Blade);
            piece.SetRarity(new DefinitionId("rarity.deleted.by.patch"));

            Assert.That(TotalFlat(EquipmentModifierResolver.Collect(piece, context), Str),
                Is.EqualTo(10f), "content removed by a patch must not break a save");
        }

        [Test]
        public void Changing_rarity_advances_the_revision_only_when_it_actually_changes()
        {
            EquipmentInstance piece = Gear(Blade);
            Revision start = piece.Revision;

            piece.SetRarity(new DefinitionId(Rare));
            Revision afterChange = piece.Revision;
            Assert.That(afterChange, Is.Not.EqualTo(start));

            piece.SetRarity(new DefinitionId(Rare));
            Assert.That(piece.Revision, Is.EqualTo(afterChange),
                "a no-op assignment must not look like a mutation");
        }

        // ---- capacity and ceilings -----------------------------------------------------

        [Test]
        public void Socket_capacity_is_the_items_own_plus_whatever_the_tier_adds()
        {
            var context = ResolverContext();
            EquipmentInstance piece = Gear(Blade);

            Assert.That(EquipmentModifierResolver.EnchantCapacity(piece, _blade, context),
                Is.EqualTo(2), "the item authors two");

            piece.SetRarity(new DefinitionId(Rare));

            Assert.That(EquipmentModifierResolver.EnchantCapacity(piece, _blade, context),
                Is.EqualTo(3), "and Rare adds one; a tier can only widen a piece");
        }

        [Test]
        public void The_strictest_authored_ceiling_is_the_one_that_applies()
        {
            var context = ResolverContext();
            EquipmentInstance piece = Gear(Blade);

            // Item 3, track 3, tier none.
            Assert.That(EquipmentModifierResolver.MaxEnhancementLevel(piece, _blade, context),
                Is.EqualTo(3));

            AddRarity("rarity.capped", order: 5, maxEnhancement: 1);
            piece.SetRarity(new DefinitionId("rarity.capped"));

            Assert.That(EquipmentModifierResolver.MaxEnhancementLevel(piece, _blade, context),
                Is.EqualTo(1), "a cap is a restriction, so the lower one wins");
        }

        // ---- sockets -------------------------------------------------------------------

        [Test]
        public void Stones_contribute_what_their_own_definition_authors()
        {
            var context = ResolverContext();
            EquipmentInstance piece = Gear(Blade);

            piece.AddEnchant(new EquipmentEnchant(new DefinitionId(StoneStr), 0));
            piece.AddEnchant(new EquipmentEnchant(new DefinitionId(StoneVit), 1));

            List<StatModifier> modifiers = EquipmentModifierResolver.Collect(piece, context);

            Assert.That(TotalFlat(modifiers, Str), Is.EqualTo(13f), "base 10 + stone 3");
            Assert.That(TotalFlat(modifiers, Vit), Is.EqualTo(4f));
        }

        [Test]
        public void A_ranked_stone_scales_its_flat_contribution()
        {
            var context = ResolverContext();
            EquipmentInstance piece = Gear(Blade);

            piece.AddEnchant(new EquipmentEnchant(new DefinitionId(StoneStr), 0, rank: 3));

            Assert.That(TotalFlat(EquipmentModifierResolver.Collect(piece, context), Str),
                Is.EqualTo(19f), "base 10 + stone 3 at rank 3");
        }

        [Test]
        public void Sockets_stay_numbered_when_one_in_the_middle_is_emptied()
        {
            EquipmentInstance piece = Gear(Blade);
            piece.AddEnchant(new EquipmentEnchant(new DefinitionId(StoneStr), 0));
            piece.AddEnchant(new EquipmentEnchant(new DefinitionId(StoneVit), 1));

            Assert.That(piece.RemoveEnchantAt(0), Is.True);

            Assert.That(piece.EnchantCount, Is.EqualTo(1));
            Assert.That(piece.Enchants[0].SocketIndex, Is.EqualTo(1),
                "the surviving stone must not be renumbered under the player");
            Assert.That(piece.IsSocketOccupied(0), Is.False);
            Assert.That(piece.FirstFreeSocket(2), Is.EqualTo(0));
        }

        [Test]
        public void An_unresolvable_stone_contributes_nothing_and_leaves_the_rest_intact()
        {
            var context = ResolverContext();
            EquipmentInstance piece = Gear(Blade);

            piece.AddEnchant(new EquipmentEnchant(new DefinitionId("stone.deleted"), 0));
            piece.AddEnchant(new EquipmentEnchant(new DefinitionId(StoneStr), 1));

            Assert.That(TotalFlat(EquipmentModifierResolver.Collect(piece, context), Str),
                Is.EqualTo(13f), "the good stone still counts");
        }

        [Test]
        public void An_item_that_is_not_a_stone_cannot_contribute_through_a_socket()
        {
            var context = ResolverContext();
            EquipmentInstance piece = Gear(Blade);

            // Potion is a Consumable, not a StatusStone, however it got in there.
            piece.AddEnchant(new EquipmentEnchant(new DefinitionId(Potion), 0));

            Assert.That(TotalFlat(EquipmentModifierResolver.Collect(piece, context), Str),
                Is.EqualTo(10f), "the authored category is what makes something socketable");
        }

        [Test]
        public void An_invalid_enchant_record_is_refused_rather_than_stored()
        {
            EquipmentInstance piece = Gear(Blade);

            Assert.That(piece.AddEnchant(new EquipmentEnchant(DefinitionId.None, 0)), Is.False);
            Assert.That(piece.AddEnchant(new EquipmentEnchant(new DefinitionId(StoneStr), -1)),
                Is.False);
            Assert.That(piece.EnchantCount, Is.EqualTo(0));
        }

        // ---- the worn set --------------------------------------------------------------

        [Test]
        public void The_worn_set_reports_progression_through_the_widened_seam()
        {
            var equipment = new CharacterEquipmentState(new CharacterId("char:test"));
            ItemContainerState bag = Container(4);

            bag.Add(Gear(Blade), Items);
            EquipmentService.Equip(bag, equipment, 0, new EquipmentService.Context(Items, 1));

            EquipmentInstance worn;
            equipment.TryGet(EquipmentSlot.MainHand, out worn);
            worn.SetRarity(new DefinitionId(Rare));
            worn.SetEnhancementLevel(2);
            worn.AddEnchant(new EquipmentEnchant(new DefinitionId(StoneStr), 0));

            List<StatModifier> full = equipment.CollectModifiers(ResolverContext());
            Assert.That(TotalFlat(full, Str), Is.EqualTo(25f), "10 + 7 + 5 + 3");

            List<StatModifier> baseOnly = equipment.CollectModifiers(Items);
            Assert.That(TotalFlat(baseOnly, Str), Is.EqualTo(10f),
                "the old overload's behaviour is frozen so existing callers cannot shift");
        }

        [Test]
        public void Unequipping_and_re_equipping_duplicates_nothing()
        {
            var equipment = new CharacterEquipmentState(new CharacterId("char:test"));
            ItemContainerState bag = Container(4);
            var context = new EquipmentService.Context(Items, 1);

            bag.Add(Gear(Blade), Items);
            EquipmentService.Equip(bag, equipment, 0, context);

            EquipmentInstance worn;
            equipment.TryGet(EquipmentSlot.MainHand, out worn);
            worn.SetRarity(new DefinitionId(Rare));
            worn.SetEnhancementLevel(3);
            worn.AddEnchant(new EquipmentEnchant(new DefinitionId(StoneStr), 0));

            float before = TotalFlat(equipment.CollectModifiers(ResolverContext()), Str);

            for (int i = 0; i < 5; i++)
            {
                EquipmentService.Unequip(bag, equipment, EquipmentSlot.MainHand, context);
                Assert.That(TotalFlat(equipment.CollectModifiers(ResolverContext()), Str),
                    Is.EqualTo(0f), "nothing worn, nothing contributed");

                int slot = bag.IndexOf(worn.InstanceId);
                EquipmentService.Equip(bag, equipment, slot, context);
            }

            Assert.That(TotalFlat(equipment.CollectModifiers(ResolverContext()), Str),
                Is.EqualTo(before), "five round trips left no residue");
            Assert.That(worn.EnhancementLevel, Is.EqualTo(3), "and the progression survived");
            Assert.That(worn.EnchantCount, Is.EqualTo(1));
        }

        [Test]
        public void Progression_never_touches_the_character_stat_state()
        {
            // The rule enhancement exists to respect: bonuses are collected as modifiers,
            // never written into a character's base stats.
            var equipment = new CharacterEquipmentState(new CharacterId("char:test"));
            ItemContainerState bag = Container(4);

            bag.Add(Gear(Blade), Items);
            EquipmentService.Equip(bag, equipment, 0, new EquipmentService.Context(Items, 1));

            EquipmentInstance worn;
            equipment.TryGet(EquipmentSlot.MainHand, out worn);
            worn.SetEnhancementLevel(3);
            worn.SetRarity(new DefinitionId(Rare));

            System.Reflection.FieldInfo[] fields = typeof(EquipmentModifierResolver)
                .GetFields(System.Reflection.BindingFlags.Static
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic);

            Assert.That(fields, Is.Empty,
                "the resolver holds no state at all, so it cannot accumulate one");
        }

        // ---- preview -------------------------------------------------------------------

        [Test]
        public void Previewing_another_level_changes_nothing_about_the_piece()
        {
            var context = ResolverContext();
            EquipmentInstance piece = Gear(Blade);
            piece.SetEnhancementLevel(1);

            Revision before = piece.Revision;
            int levelBefore = piece.EnhancementLevel;

            var preview = new List<StatModifier>();
            EquipmentModifierResolver.CollectAtLevel(piece, 3, context, preview);

            Assert.That(TotalFlat(preview, Str), Is.EqualTo(19f), "what +3 would be worth");
            Assert.That(piece.EnhancementLevel, Is.EqualTo(levelBefore));
            Assert.That(piece.Revision, Is.EqualTo(before), "a preview is a read");
            Assert.That(TotalFlat(EquipmentModifierResolver.Collect(piece, context), Str),
                Is.EqualTo(12f), "and the piece is still worth what it was");
        }

        [Test]
        public void A_preview_includes_the_tier_and_the_stones_it_already_has()
        {
            var context = ResolverContext();
            EquipmentInstance piece = Gear(Blade);
            piece.SetRarity(new DefinitionId(Rare));
            piece.AddEnchant(new EquipmentEnchant(new DefinitionId(StoneStr), 0));

            var preview = new List<StatModifier>();
            EquipmentModifierResolver.CollectAtLevel(piece, 2, context, preview);

            Assert.That(TotalFlat(preview, Str), Is.EqualTo(25f),
                "10 + 7 + 5 + 3: a preview is the same calculation, at a different level");
        }
    }
}
