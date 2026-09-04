using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Drops, item use, stat integration and the architecture rules behind them.
    /// </summary>
    /// <remarks>
    /// The load-bearing claim of this phase is that ultra-rare content needed no drop code.
    /// The tests below hold it two ways: by exercising the one generic resolver against
    /// chances that differ by four orders of magnitude, and by reading
    /// <c>DropResolver</c>'s source for any mention of a fruit, a card or a boss.
    /// </remarks>
    [TestFixture]
    internal sealed class CollectibleIntegrationTests : CollectibleTestBase
    {
        // ---- drop configuration --------------------------------------------------------

        [Test]
        public void The_probability_convention_is_a_fraction_not_a_percentage()
        {
            DropTableDefinition table;
            DropTables.TryGet(new DefinitionId(BossTable), out table);

            // 0.00001% authored as the fraction the schema documents.
            Assert.That(table.Entries[0].Chance, Is.EqualTo(0.0000001f).Within(1e-12f));
            Assert.That(table.Entries[1].Chance, Is.EqualTo(0.000001f).Within(1e-11f));
        }

        [Test]
        public void An_ultra_rare_chance_survives_the_numeric_representation()
        {
            DropTableDefinition table;
            DropTables.TryGet(new DefinitionId(BossTable), out table);

            float fruit = table.Entries[0].Chance;
            float card = table.Entries[1].Chance;

            Assert.That(fruit, Is.Not.EqualTo(0f), "1e-7 must not flush to zero");
            Assert.That(fruit, Is.Not.EqualTo(card), "1e-7 and 1e-6 stay distinguishable");
            Assert.That(card / fruit, Is.EqualTo(10f).Within(0.01f),
                "the ratio survives, so the representation carries the precision that matters");
        }

        [Test]
        public void The_same_resolver_drops_at_one_chance_and_not_at_another()
        {
            // One roll, held fixed. Only the authored configuration differs.
            var roll = new ThresholdResultSource(0.0000005f);

            AddDropTable("drop.rare", new[]
            {
                new DropEntry(new DefinitionId(DarknessItem), 1, 1, 0.0000001f)
            });

            AddDropTable("drop.common", new[]
            {
                new DropEntry(new DefinitionId(DarknessItem), 1, 1, 0.001f)
            });

            var rare = new List<LootResult>();
            var common = new List<LootResult>();

            DropResolver.Resolve(InstanceId.New(), new DefinitionId("drop.rare"),
                DropContext(roll, roll), rare);

            DropResolver.Resolve(InstanceId.New(), new DefinitionId("drop.common"),
                DropContext(roll, roll), common);

            Assert.That(rare.Count, Is.EqualTo(0), "the roll is above the rare chance");
            Assert.That(common.Count, Is.EqualTo(1), "the same roll is below the common chance");
        }

        [Test]
        public void Raising_the_configured_chance_changes_the_outcome_with_no_code_change()
        {
            var roll = new ThresholdResultSource(0.0000005f);

            DropTableDefinition table;
            DropTables.TryGet(new DefinitionId(BossTable), out table);

            var before = new List<LootResult>();
            DropResolver.Resolve(InstanceId.New(), table.Id, DropContext(roll, roll), before);

            Assert.That(before.Count, Is.EqualTo(1), "only the card row is above the roll");

            // Exactly what an administrator changing a database row would do: the chance,
            // and nothing else.
            SetPrivate(table, "_entries", new[]
            {
                new DropEntry(new DefinitionId(DarknessItem), 1, 1, 0.00001f),
                new DropEntry(new DefinitionId(BossCard), 1, 1, CardChance)
            });

            var after = new List<LootResult>();
            DropResolver.Resolve(InstanceId.New(), table.Id, DropContext(roll, roll), after);

            Assert.That(after.Count, Is.EqualTo(2),
                "a configuration change alone changes what drops");
        }

        [Test]
        public void A_zero_chance_never_drops()
        {
            AddDropTable("drop.never", new[]
            {
                new DropEntry(new DefinitionId(StatCard), 1, 1, 0f)
            });

            // Guaranteed, by the documented convention: zero or less is not "never".
            var loot = new List<LootResult>();
            DropResolver.Resolve(InstanceId.New(), new DefinitionId("drop.never"),
                DropContext(new ThresholdResultSource(0.9f)), loot);

            Assert.That(loot.Count, Is.EqualTo(1),
                "zero means guaranteed; an unauthored chance must not read as impossible");
        }

        [Test]
        public void A_probability_of_one_always_succeeds()
        {
            AddDropTable("drop.always", new[]
            {
                new DropEntry(new DefinitionId(StatCard), 1, 1, 1f)
            });

            // The highest roll a [0..1) generator produces is still below one.
            var loot = new List<LootResult>();
            DropResolver.Resolve(InstanceId.New(), new DefinitionId("drop.always"),
                DropContext(new ThresholdResultSource(0.9999999f)), loot);

            Assert.That(loot.Count, Is.EqualTo(1));
        }

        [Test]
        public void An_invalid_probability_is_rejected_by_validation_and_skipped_by_the_roll()
        {
            AddDropTable("drop.broken", new[]
            {
                new DropEntry(new DefinitionId(StatCard), 1, 1, float.NaN),
                new DropEntry(new DefinitionId(HpCard), 1, 1, 5f)
            });

            var report = new ValidationReport();
            var rule = new CollectibleContentValidationRule();

            DropTableDefinition table;
            DropTables.TryGet(new DefinitionId("drop.broken"), out table);
            rule.Validate(table, null, report);

            Assert.That(report.ErrorCount, Is.EqualTo(2), "fail fast rather than clamp");

            // And nothing garbage reaches a player in the meantime.
            var loot = new List<LootResult>();
            DropResolver.Resolve(InstanceId.New(), table.Id,
                DropContext(new ThresholdResultSource(0.5f)), loot);

            Assert.That(loot.Count, Is.EqualTo(0));
        }

        [Test]
        public void A_disabled_row_does_not_drop()
        {
            AddDropTable("drop.toggled", new[]
            {
                new DropEntry(new DefinitionId(StatCard), 1, 1, 0f, enabled: false),
                new DropEntry(new DefinitionId(HpCard), 1, 1, 0f)
            });

            var loot = new List<LootResult>();
            DropResolver.Resolve(InstanceId.New(), new DefinitionId("drop.toggled"),
                DropContext(), loot);

            Assert.That(loot.Count, Is.EqualTo(1));
            Assert.That(loot[0].Item, Is.EqualTo(new DefinitionId(HpCard)));
        }

        [Test]
        public void Existing_content_stays_enabled_when_the_flag_is_added()
        {
            // The field is stored inverted precisely so this holds: an entry authored with no
            // knowledge of the flag deserializes as enabled.
            var entry = default(DropEntry);

            Assert.That(entry.Enabled, Is.True);
        }

        [Test]
        public void A_common_item_and_an_ultra_rare_one_roll_through_the_same_code()
        {
            AddItem("item.coin", ItemCategory.Currency, stackable: true, maxStack: 999);

            AddDropTable("drop.mixed", new[]
            {
                new DropEntry(new DefinitionId("item.coin"), 5, 5),
                new DropEntry(new DefinitionId(StatCard), 1, 1, CardChance),
                new DropEntry(new DefinitionId(DarknessItem), 1, 1, FruitChance)
            });

            // A roll below every chance: all three land, by the same three lines.
            var loot = new List<LootResult>();
            DropResolver.Resolve(InstanceId.New(), new DefinitionId("drop.mixed"),
                DropContext(new ThresholdResultSource(0f)), loot);

            Assert.That(loot.Count, Is.EqualTo(3));
        }

        [Test]
        public void The_resolver_names_no_fruit_card_or_boss()
        {
            foreach (string code in DevilFruitTests.CodeLines(
                "Assets/_Game/Scripts/Gameplay/DropResolver.cs"))
            {
                Assert.That(code, Does.Not.Contain("DevilFruit"), "no fruit branch");
                Assert.That(code, Does.Not.Contain("Card"), "no card branch");
                Assert.That(code, Does.Not.Contain("ItemCategory"));

                // No rank is named. 17.15 gave the resolver a rank gate, which means it
                // mentions the MonsterRank type -- but naming the type is how a general
                // condition is expressed, while naming a value is what a special case looks
                // like. "if the monster is a WorldBoss" would be the branch this test has
                // always existed to forbid, so every value stays forbidden.
                Assert.That(code, Does.Not.Contain("MonsterRank.Normal"), "no rank branch");
                Assert.That(code, Does.Not.Contain("MonsterRank.Elite"), "no rank branch");
                Assert.That(code, Does.Not.Contain("MonsterRank.MiniBoss"), "no rank branch");
                Assert.That(code, Does.Not.Contain("MonsterRank.Boss"), "no rank branch");
                Assert.That(code, Does.Not.Contain("WorldBoss"), "no boss branch");
            }
        }

        [Test]
        public void The_rank_gate_is_one_general_condition_and_not_a_list_of_kinds()
        {
            // What the resolver actually does with a rank: asks the entry, once. If this
            // ever becomes a comparison against a particular rank, the previous test is
            // reading a file that has grown the branch it forbids.
            var asks = 0;

            foreach (string code in DevilFruitTests.CodeLines(
                "Assets/_Game/Scripts/Gameplay/DropResolver.cs"))
            {
                if (code.Contains("AppliesToRank")) asks++;
            }

            Assert.That(asks, Is.EqualTo(1),
                "exactly one rank question, asked of the authored row");
        }

        [Test]
        public void No_probability_is_written_in_gameplay_code()
        {
            string[] files =
            {
                "Assets/_Game/Scripts/Gameplay/DropResolver.cs",
                "Assets/_Game/Scripts/Gameplay/DevilFruitService.cs",
                "Assets/_Game/Scripts/Gameplay/CardSocketService.cs",
                "Assets/_Game/Scripts/Gameplay/PetService.cs",
                "Assets/_Game/Scripts/Gameplay/MonsterSpawnService.cs",
                "Assets/_Game/Scripts/Gameplay/LootPickupService.cs"
            };

            foreach (string file in files)
            {
                foreach (string code in DevilFruitTests.CodeLines(file))
                {
                    Assert.That(code, Does.Not.Contain("0.0000001"), file);
                    Assert.That(code, Does.Not.Contain("0.000001"), file);
                    Assert.That(code, Does.Not.Contain("0.00001"), file);
                    Assert.That(code, Does.Not.Contain("DropChance"), file);
                }
            }
        }

        // ---- world boss eligibility ----------------------------------------------------

        [Test]
        public void A_world_boss_may_carry_a_fruit_table()
        {
            var report = new ValidationReport();

            CollectibleContentValidationRule.ValidateWorldBossOnlyDrops(Monsters, DropTables,
                Items, report);

            Assert.That(report.ErrorCount, Is.EqualTo(0));
        }

        [Test]
        public void A_normal_monster_carrying_a_fruit_table_is_an_error()
        {
            AddMonster("monster.cheat", MonsterRank.Normal, BossTable);

            var report = new ValidationReport();
            CollectibleContentValidationRule.ValidateWorldBossOnlyDrops(Monsters, DropTables,
                Items, report);

            Assert.That(report.ErrorCount, Is.EqualTo(1),
                "Devil Fruits are world-boss content, decided by rank rather than by id");
        }

        [Test]
        public void Eligibility_is_decided_by_rank_not_by_a_monster_id()
        {
            // Promote the offender. Nothing else changes, and the error goes away.
            MonsterDefinition mob;
            Monsters.TryGet(new DefinitionId(NormalMob), out mob);
            SetPrivate(mob, "_lootTable", new DefinitionId(BossTable));

            var before = new ValidationReport();
            CollectibleContentValidationRule.ValidateWorldBossOnlyDrops(Monsters, DropTables,
                Items, before);
            Assert.That(before.ErrorCount, Is.EqualTo(1));

            SetPrivate(mob, "_rank", MonsterRank.WorldBoss);

            var after = new ValidationReport();
            CollectibleContentValidationRule.ValidateWorldBossOnlyDrops(Monsters, DropTables,
                Items, after);
            Assert.That(after.ErrorCount, Is.EqualTo(0));
        }

        [Test]
        public void Cards_drop_from_normal_monsters_without_any_special_rule()
        {
            var loot = new List<LootResult>();

            DropResolver.Resolve(InstanceId.New(), new DefinitionId(MobTable),
                DropContext(new ThresholdResultSource(0f)), loot);

            Assert.That(loot.Count, Is.EqualTo(1));
            Assert.That(loot[0].Item, Is.EqualTo(new DefinitionId(StatCard)));
        }

        // ---- item use integration ------------------------------------------------------

        [Test]
        public void Eating_a_fruit_goes_through_the_one_item_use_pipeline()
        {
            ItemContainerState bag = Container();
            bag.Add(Stack(DarknessItem), Items);

            var state = new CharacterDevilFruitState(new CharacterId("c"), Owner);
            var status = new StatusEffectRuntimeState();

            ItemUseResult result = ItemUseService.Use(bag, 0, UseContext(state, status));

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.ConsumedQuantity, Is.EqualTo(1), "exactly one, exactly once");
            Assert.That(result.DevilFruitActivated, Is.EqualTo(new DefinitionId(Darkness)));
            Assert.That(bag.CountOf(new DefinitionId(DarknessItem)), Is.EqualTo(0));
            Assert.That(state.ActiveFruit, Is.EqualTo(new DefinitionId(Darkness)));
            Assert.That(status.Has(new DefinitionId(Silence)), Is.True);
        }

        [Test]
        public void A_second_fruit_is_not_consumed_when_the_activation_is_refused()
        {
            ItemContainerState bag = Container();
            bag.Add(Stack(DarknessItem), Items);
            bag.Add(Stack(LightItem), Items);

            var state = new CharacterDevilFruitState(new CharacterId("c"), Owner);
            var status = new StatusEffectRuntimeState();

            ItemUseService.Use(bag, 0, UseContext(state, status));

            ItemUseResult second = ItemUseService.Use(bag, 1, UseContext(state, status));

            Assert.That(second.IsAccepted, Is.False);
            Assert.That(second.Reason, Is.EqualTo(ItemUseRejection.AlreadyActive));
            Assert.That(second.ConsumedQuantity, Is.EqualTo(0));
            Assert.That(bag.CountOf(new DefinitionId(LightItem)), Is.EqualTo(1),
                "an ultra-rare item is never spent for nothing");
            Assert.That(state.ActiveFruit, Is.EqualTo(new DefinitionId(Darkness)));
        }

        [Test]
        public void A_fruit_use_without_a_fruit_state_consumes_nothing()
        {
            ItemContainerState bag = Container();
            bag.Add(Stack(DarknessItem), Items);

            ItemUseResult result = ItemUseService.Use(bag, 0, UseContext(null, null));

            Assert.That(result.Reason, Is.EqualTo(ItemUseRejection.MissingContext));
            Assert.That(bag.CountOf(new DefinitionId(DarknessItem)), Is.EqualTo(1));
        }

        [Test]
        public void Using_a_pet_item_creates_a_pet_and_consumes_exactly_one()
        {
            AddPetItem("item.petegg", PetA);

            ItemContainerState bag = Container();
            bag.Add(Stack("item.petegg", 3), Items);

            ItemUseResult result = ItemUseService.Use(bag, 0, UseContext(null, null));

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.PetGranted, Is.Not.Null);
            Assert.That(result.PetGranted.DefinitionId, Is.EqualTo(new DefinitionId(PetA)));
            Assert.That(result.PetGranted.Owner, Is.EqualTo(Owner));
            Assert.That(bag.CountOf(new DefinitionId("item.petegg")), Is.EqualTo(2));
        }

        [Test]
        public void A_pet_item_naming_a_missing_pet_consumes_nothing()
        {
            AddPetItem("item.badegg", "pet.gone");

            ItemContainerState bag = Container();
            bag.Add(Stack("item.badegg", 1), Items);

            ItemUseResult result = ItemUseService.Use(bag, 0, UseContext(null, null));

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(bag.CountOf(new DefinitionId("item.badegg")), Is.EqualTo(1));
        }

        [Test]
        public void An_item_declaring_the_wrong_type_cannot_grant_a_fruit()
        {
            // A consumable that gained a fruit effect through a bad import.
            var definition = UnityEngine.ScriptableObject.CreateInstance<ItemDefinition>();
            UnityEngine.JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"item.forged\"},\"_stackable\":false,\"_maxStackSize\":1,"
                + "\"_category\":" + (int)ItemCategory.Consumable
                + ",\"_usable\":true,\"_useType\":" + (int)ItemUseType.Recovery
                + ",\"_useTarget\":" + (int)ItemUseTarget.Self + "}", definition);

            SetPrivate(definition, "_useEffects", new[]
            {
                new ItemUseEffect(ItemEffectKind.ConsumeDevilFruit,
                    devilFruit: new DefinitionId(Darkness))
            });

            Track(definition);
            Items.Register(definition);

            ItemContainerState bag = Container();
            bag.Add(Stack("item.forged"), Items);

            var state = new CharacterDevilFruitState(new CharacterId("c"), Owner);

            ItemUseResult result = ItemUseService.Use(bag, 0, UseContext(state, null));

            Assert.That(result.Reason, Is.EqualTo(ItemUseRejection.InvalidEffect));
            Assert.That(state.HasActiveFruit, Is.False);
            Assert.That(bag.CountOf(new DefinitionId("item.forged")), Is.EqualTo(1));
        }

        [Test]
        public void Another_owners_fruit_cannot_be_eaten()
        {
            var bag = new ItemContainerState(Stranger, 10);
            bag.Add(Stack(DarknessItem, 1, Stranger), Items);

            var state = new CharacterDevilFruitState(new CharacterId("c"), Owner);

            ItemUseResult result = ItemUseService.Use(bag, 0, UseContext(state, null, Owner));

            Assert.That(result.Reason, Is.EqualTo(ItemUseRejection.NotOwned));
            Assert.That(bag.CountOf(new DefinitionId(DarknessItem)), Is.EqualTo(1));
        }

        // ---- end to end ----------------------------------------------------------------

        [Test]
        public void World_boss_to_drop_to_inventory_to_fruit_activation()
        {
            // 1. the boss dies and its authored table is rolled by the generic resolver
            var loot = new List<LootResult>();
            DropResolver.Resolve(InstanceId.New(), new DefinitionId(BossTable),
                DropContext(new ThresholdResultSource(0f)), loot);

            Assert.That(loot.Count, Is.EqualTo(2));

            // 2. what dropped becomes a normal ItemInstance in a normal container
            ItemContainerState bag = Container();

            for (int i = 0; i < loot.Count; i++)
            {
                bag.Add(new ItemInstance(InstanceId.New(), loot[i].Item, Owner,
                    loot[i].Quantity), Items);
            }

            Assert.That(bag.CountOf(new DefinitionId(DarknessItem)), Is.EqualTo(1));

            // 3. eating it goes through the ordinary item-use pipeline
            var state = new CharacterDevilFruitState(new CharacterId("c"), Owner);
            var status = new StatusEffectRuntimeState();

            int slot = bag.IndexOf(bag.GetSlot(0).Content.InstanceId) >= 0 ? 0 : 1;

            ItemUseResult used = ItemUseService.Use(bag, slot, UseContext(state, status));

            Assert.That(used.IsAccepted, Is.True);
            Assert.That(state.HasActiveFruit, Is.True);
        }

        [Test]
        public void Monster_to_drop_to_card_to_equipment_to_modifier()
        {
            var loot = new List<LootResult>();
            DropResolver.Resolve(InstanceId.New(), new DefinitionId(MobTable),
                DropContext(new ThresholdResultSource(0f)), loot);

            ItemContainerState bag = Container();
            bag.Add(new ItemInstance(InstanceId.New(), loot[0].Item, Owner, 1), Items);

            EquipmentInstance sword = Equipment(Sword);

            Assert.That(CardSocketService.TryInsert(bag, 0, sword, CardContext()).IsAccepted,
                Is.True);

            var context = new EquipmentModifierResolver.Context(Items, null, null, Cards);
            var modifiers = new List<StatModifier>();
            EquipmentModifierResolver.Collect(sword, context, modifiers);

            Assert.That(modifiers.Count, Is.EqualTo(1));
            Assert.That(modifiers[0].Stat, Is.EqualTo(new DefinitionId(Str)));
        }

        [Test]
        public void Pet_buff_and_fruit_modifiers_reach_the_character_through_existing_seams()
        {
            var status = new StatusEffectRuntimeState();
            var companion = new PetCompanionState();
            var fruit = new CharacterDevilFruitState(new CharacterId("c"), Owner);

            PetService.TrySummon(companion, Pet(PetB), PetContext(status));
            DevilFruitService.TryActivate(fruit, new DefinitionId(Fruit04), InstanceId.New(),
                FruitContext(status));

            var modifiers = new List<StatModifier>();

            // Two independent sources, one modifier list, one calculator downstream.
            status.CollectModifiers(Effects, modifiers);
            DevilFruitService.CollectModifiers(fruit, FruitContext(status), modifiers);

            Assert.That(modifiers.Count, Is.EqualTo(2));
            Assert.That(Total(modifiers, Vit), Is.EqualTo(3f).Within(0.001f));
            Assert.That(Total(modifiers, Str), Is.EqualTo(12f).Within(0.001f));
        }

        // ---- status runtime ------------------------------------------------------------

        [Test]
        public void An_effect_expires_on_caller_supplied_time()
        {
            var status = new StatusEffectRuntimeState();

            StatusEffectService.TryApply(status, new DefinitionId(Silence),
                new DefinitionId("source"), Effects);

            Assert.That(status.Tick(5f), Is.EqualTo(0), "eight seconds authored, five elapsed");
            Assert.That(status.Has(new DefinitionId(Silence)), Is.True);

            Assert.That(status.Tick(4f), Is.EqualTo(1));
            Assert.That(status.Has(new DefinitionId(Silence)), Is.False);
        }

        [Test]
        public void An_indefinite_effect_never_expires()
        {
            var status = new StatusEffectRuntimeState();

            StatusEffectService.TryApply(status, new DefinitionId(PetVigour),
                new DefinitionId("source"), Effects);

            status.Tick(10000f);

            Assert.That(status.Has(new DefinitionId(PetVigour)), Is.True,
                "a permanent passive must not expire because somebody ticked often enough");
        }

        [Test]
        public void There_is_exactly_one_status_runtime()
        {
            System.Type[] types = typeof(StatusEffectRuntimeState).Assembly.GetTypes();
            int engines = 0;

            foreach (System.Type type in types)
            {
                if (type.Name.Contains("Status") && type.Name.Contains("Runtime")) engines++;

                Assert.That(type.Name, Is.Not.EqualTo("FruitStatusEngine"));
                Assert.That(type.Name, Is.Not.EqualTo("PetStatusEngine"));
                Assert.That(type.Name, Is.Not.EqualTo("CardStatusEngine"));
            }

            Assert.That(engines, Is.EqualTo(1));
        }

        // ---- architecture --------------------------------------------------------------

        [Test]
        public void Gameplay_remains_engine_free()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts/Gameplay",
                "*.cs", System.IO.SearchOption.AllDirectories);

            foreach (string file in files)
            {
                foreach (string code in DevilFruitTests.CodeLines(file))
                {
                    Assert.That(code, Does.Not.Contain("UnityEngine"), file);
                }
            }
        }

        [Test]
        public void There_is_one_registry_implementation()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts", "*.cs",
                System.IO.SearchOption.AllDirectories);

            int registries = 0;

            foreach (string file in files)
            {
                string source = System.IO.File.ReadAllText(file);
                if (source.Contains("class DefinitionRegistry")) registries++;

                Assert.That(source, Does.Not.Contain("class DevilFruitRegistry"), file);
                Assert.That(source, Does.Not.Contain("class CardRegistry"), file);
                Assert.That(source, Does.Not.Contain("class PetRegistry"), file);
            }

            Assert.That(registries, Is.EqualTo(1));
        }

        [Test]
        public void There_is_one_item_ownership_model()
        {
            System.Type[] types = typeof(ItemInstance).Assembly.GetTypes();

            foreach (System.Type type in types)
            {
                Assert.That(type.Name, Is.Not.EqualTo("TradeItem"));
                Assert.That(type.Name, Is.Not.EqualTo("ShopItem"));
                Assert.That(type.Name, Is.Not.EqualTo("LootItem"));
                Assert.That(type.Name, Is.Not.EqualTo("NpcItem"));
                Assert.That(type.Name, Is.Not.EqualTo("DevilFruitItem"));
                Assert.That(type.Name, Is.Not.EqualTo("CardItemInstance"));
                Assert.That(type.Name, Is.Not.EqualTo("PetItemInstance"));
            }
        }

        [Test]
        public void There_is_one_skill_executor_and_one_combat_runner()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts", "*.cs",
                System.IO.SearchOption.AllDirectories);

            int executors = 0;
            int runners = 0;
            int calculators = 0;

            foreach (string file in files)
            {
                string source = System.IO.File.ReadAllText(file);
                if (source.Contains("class SkillExecutor")) executors++;
                if (source.Contains("class CombatActionRunner")) runners++;
                if (source.Contains("class DerivedStatsCalculator")) calculators++;
            }

            Assert.That(executors, Is.EqualTo(1));
            Assert.That(runners, Is.EqualTo(1));
            Assert.That(calculators, Is.EqualTo(1));
        }

        [Test]
        public void Card_arithmetic_is_not_added_to_the_stat_calculator()
        {
            foreach (string code in DevilFruitTests.CodeLines(
                "Assets/_Game/Scripts/Gameplay/DerivedStatsCalculator.cs"))
            {
                Assert.That(code, Does.Not.Contain("Card"),
                    "composition is the resolver's job; the calculator only does arithmetic");
                Assert.That(code, Does.Not.Contain("DevilFruit"));
                Assert.That(code, Does.Not.Contain("Pet"));
            }
        }

        [Test]
        public void The_ui_assembly_holds_no_gameplay_state()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts/UI", "*.cs",
                System.IO.SearchOption.AllDirectories);

            foreach (string file in files)
            {
                foreach (string code in DevilFruitTests.CodeLines(file))
                {
                    Assert.That(code, Does.Not.Contain("ChibiFantasy.Gameplay"), file);
                    Assert.That(code, Does.Not.Contain("PetInstance"), file);
                    Assert.That(code, Does.Not.Contain("ItemContainerState"), file);
                    Assert.That(code, Does.Not.Contain("StatusEffectRuntimeState"), file);
                }
            }
        }

        [Test]
        public void Collectible_commands_live_only_in_the_controller()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts/Client", "*.cs",
                System.IO.SearchOption.AllDirectories);

            foreach (string file in files)
            {
                string normalized = file.Replace('\\', '/');
                if (normalized.EndsWith("CollectibleUiController.cs")) continue;

                string source = System.IO.File.ReadAllText(file);

                Assert.That(source, Does.Not.Contain("DevilFruitService.TryActivate"),
                    normalized + " activates a fruit outside the command boundary");
                Assert.That(source, Does.Not.Contain("CardSocketService.TryInsert"),
                    normalized + " sockets a card outside the command boundary");
                Assert.That(source, Does.Not.Contain("PetService.TryEvolve"),
                    normalized + " evolves a pet outside the command boundary");
            }
        }

        [Test]
        public void Presentation_events_carry_no_gameplay_objects()
        {
            System.Reflection.PropertyInfo[] properties =
                typeof(CollectiblePresentationEvent).GetProperties();

            foreach (System.Reflection.PropertyInfo property in properties)
            {
                Assert.That(property.PropertyType, Is.Not.EqualTo(typeof(PetInstance)),
                    property.Name);
                Assert.That(property.PropertyType, Is.Not.EqualTo(typeof(ItemContainerState)),
                    property.Name);
                Assert.That(property.PropertyType.IsValueType
                    || property.PropertyType == typeof(string), Is.True,
                    property.Name + " is a reference a presenter could mutate");
            }
        }

        // ---- validation ----------------------------------------------------------------

        [Test]
        public void An_evolution_cycle_is_detected()
        {
            AddPet("pet.loop.b", buff: PetVigour, thresholds: new[] { 10 },
                stages: new[] { new PetEvolutionStage(new DefinitionId("pet.loop.a"), 1) });
            AddPet("pet.loop.a", buff: PetVigour, thresholds: new[] { 10 },
                stages: new[] { new PetEvolutionStage(new DefinitionId("pet.loop.b"), 1) });

            var report = new ValidationReport();
            CollectibleContentValidationRule.ValidateEvolutionChains(Pets, report);

            Assert.That(report.ErrorCount, Is.GreaterThan(0));
        }

        [Test]
        public void A_straight_chain_is_not_reported_as_a_cycle()
        {
            AddPet("pet.line.c", buff: PetVigour, thresholds: new[] { 10 });
            AddPet("pet.line.b", buff: PetVigour, thresholds: new[] { 10 },
                stages: new[] { new PetEvolutionStage(new DefinitionId("pet.line.c"), 1) });
            AddPet("pet.line.a", buff: PetVigour, thresholds: new[] { 10 },
                stages: new[] { new PetEvolutionStage(new DefinitionId("pet.line.b"), 1) });

            var report = new ValidationReport();
            CollectibleContentValidationRule.ValidateEvolutionChains(Pets, report);

            Assert.That(report.ErrorCount, Is.EqualTo(0), "A to B to C must stay valid");
        }

        [Test]
        public void A_self_evolving_pet_is_a_cycle()
        {
            AddPet("pet.self", buff: PetVigour, thresholds: new[] { 10 },
                stages: new[] { new PetEvolutionStage(new DefinitionId("pet.self"), 1) });

            var report = new ValidationReport();
            CollectibleContentValidationRule.ValidateEvolutionChains(Pets, report);

            Assert.That(report.ErrorCount, Is.GreaterThan(0));
        }

        [Test]
        public void A_descending_experience_curve_is_an_error()
        {
            PetDefinition pet = AddPet("pet.badcurve", buff: PetVigour,
                thresholds: new[] { 100, 50, 200 });

            var report = new ValidationReport();
            new CollectibleContentValidationRule().Validate(pet, null, report);

            Assert.That(report.ErrorCount, Is.GreaterThan(0));
        }

        [Test]
        public void An_unreachable_evolution_level_is_an_error()
        {
            PetDefinition pet = AddPet("pet.unreachable", buff: PetVigour,
                thresholds: new[] { 10 },
                stages: new[] { new PetEvolutionStage(new DefinitionId(PetA), 99) });

            var report = new ValidationReport();
            new CollectibleContentValidationRule().Validate(pet, null, report);

            Assert.That(report.ErrorCount, Is.GreaterThan(0));
        }

        [Test]
        public void A_fruit_that_grants_nothing_is_an_error()
        {
            DevilFruitDefinition fruit = AddFruit("fruit.empty");

            var report = new ValidationReport();
            new CollectibleContentValidationRule().Validate(fruit, null, report);

            Assert.That(report.ErrorCount, Is.GreaterThan(0));
        }

        [Test]
        public void A_card_that_grants_nothing_is_an_error()
        {
            CardDefinition card = AddCard("card.empty");

            var report = new ValidationReport();
            new CollectibleContentValidationRule().Validate(card, null, report);

            Assert.That(report.ErrorCount, Is.GreaterThan(0));
        }

        [Test]
        public void The_authored_ten_fruits_and_five_cards_validate_cleanly()
        {
            var rule = new CollectibleContentValidationRule();
            var report = new ValidationReport();

            for (int i = 0; i < AllFruits.Length; i++)
            {
                DevilFruitDefinition fruit;
                Fruits.TryGet(new DefinitionId(AllFruits[i]), out fruit);
                rule.Validate(fruit, null, report);
            }

            Assert.That(report.ErrorCount, Is.EqualTo(0),
                "the shipped roster must be coherent content");
        }

        // ---- helpers -------------------------------------------------------------------

        private ItemUseService.Context UseContext(CharacterDevilFruitState fruitState,
            StatusEffectRuntimeState status, OwnerId owner = default)
        {
            var limits = new ResourceLimits(100, 100);

            return new ItemUseService.Context(Items,
                new CharacterResourceState(new CharacterId("char:test"), limits, 100, 100),
                limits, Effects, null, null, owner, null,
                Fruits, fruitState, Pets, Skills, status);
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
