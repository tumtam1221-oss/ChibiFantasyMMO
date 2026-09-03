using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Drops, world loot and quests.
    /// </summary>
    /// <remarks>
    /// Three expensive failures are guarded here, and most of what follows exists for them:
    /// a kill paying out twice, a pile of loot being taken twice, and a quest paying its
    /// reward every time a UI refreshes. All three are the same shape -- something that must
    /// happen exactly once -- and all three are settled by a single claim rather than by
    /// several systems agreeing.
    ///
    /// Every chance, quantity and reward is a FIXTURE. No service knows any of them.
    /// </remarks>
    internal sealed class DropLootQuestTests : MonsterTestBase
    {
        private const string CommonTable = "drop.common";
        private const string KillQuest = "quest.kill";
        private const string CollectQuest = "quest.collect";
        private const string TalkQuest = "quest.talk";
        private const string Elder = "npc.elder";

        private DropResolver.Context Drops(IRandomResultSource results = null,
            IRandomRangeSource ranges = null, int killerLevel = 10)
        {
            return new DropResolver.Context(Items, DropTables,
                results ?? AlwaysSucceeds.Instance, ranges ?? AlwaysSucceeds.Instance,
                killerLevel);
        }

        private QuestService.Context QuestContext(int level = 10)
        {
            return new QuestService.Context(Quests, Items, level, Owner);
        }

        private LootPickupService.Context Pickup(CharacterId character = default)
        {
            return new LootPickupService.Context(Items, Owner,
                character.IsValid ? character : Character);
        }

        // ---- drops ---------------------------------------------------------------------

        [Test]
        public void A_guaranteed_entry_always_drops()
        {
            AddDropTable(CommonTable, new[]
            {
                new DropEntry(new DefinitionId(Coin), 5, 5)
            });

            var loot = new List<LootResult>();
            int count = DropResolver.Resolve(InstanceId.New(), new DefinitionId(CommonTable),
                Drops(AlwaysFails.Instance), loot);

            Assert.That(count, Is.EqualTo(1), "a blank chance is guaranteed, not never");
            Assert.That(loot[0].Item, Is.EqualTo(new DefinitionId(Coin)));
            Assert.That(loot[0].Quantity, Is.EqualTo(5));
        }

        [Test]
        public void A_chance_entry_drops_only_when_the_roll_says_so()
        {
            AddDropTable(CommonTable, new[]
            {
                new DropEntry(new DefinitionId(Hide), 1, 1, chance: 0.5f)
            });

            var lucky = new List<LootResult>();
            DropResolver.Resolve(InstanceId.New(), new DefinitionId(CommonTable),
                Drops(AlwaysSucceeds.Instance), lucky);
            Assert.That(lucky.Count, Is.EqualTo(1));

            var unlucky = new List<LootResult>();
            DropResolver.Resolve(InstanceId.New(), new DefinitionId(CommonTable),
                Drops(AlwaysFails.Instance), unlucky);
            Assert.That(unlucky, Is.Empty);
        }

        [Test]
        public void A_one_in_a_million_drop_is_testable()
        {
            AddDropTable(CommonTable, new[]
            {
                new DropEntry(new DefinitionId(Relic), 1, 1, chance: 0.000001f)
            });

            var missed = new List<LootResult>();
            DropResolver.Resolve(InstanceId.New(), new DefinitionId(CommonTable),
                Drops(new ThresholdResultSource(0.5f)), missed);
            Assert.That(missed, Is.Empty);

            var hit = new List<LootResult>();
            DropResolver.Resolve(InstanceId.New(), new DefinitionId(CommonTable),
                Drops(new ThresholdResultSource(0.0000001f)), hit);
            Assert.That(hit.Count, Is.EqualTo(1),
                "a rare card or a Devil Fruit is only a small number here");
        }

        [Test]
        public void Every_entry_gets_its_own_roll()
        {
            // A monster is not "one item": a guaranteed coin, a likely hide and a rare relic
            // can all land from one kill.
            AddDropTable(CommonTable, new[]
            {
                new DropEntry(new DefinitionId(Coin), 1, 1),
                new DropEntry(new DefinitionId(Hide), 1, 1, chance: 0.9f),
                new DropEntry(new DefinitionId(Relic), 1, 1, chance: 0.01f)
            });

            var scripted = new ScriptedResultSource(true, false);

            var loot = new List<LootResult>();
            DropResolver.Resolve(InstanceId.New(), new DefinitionId(CommonTable),
                Drops(scripted), loot);

            Assert.That(loot.Count, Is.EqualTo(2), "the coin plus the hide");
            Assert.That(scripted.Calls, Is.EqualTo(2),
                "the guaranteed entry is not rolled for at all");
        }

        [Test]
        public void A_quantity_range_is_rolled_and_a_fixed_quantity_is_not()
        {
            AddDropTable(CommonTable, new[]
            {
                new DropEntry(new DefinitionId(Coin), 1, 10),
                new DropEntry(new DefinitionId(Hide), 3, 3)
            });

            var scripted = new ScriptedResultSource().WithNumbers(7);

            var loot = new List<LootResult>();
            DropResolver.Resolve(InstanceId.New(), new DefinitionId(CommonTable),
                Drops(ranges: scripted), loot);

            Assert.That(loot[0].Quantity, Is.EqualTo(7));
            Assert.That(loot[1].Quantity, Is.EqualTo(3));
            Assert.That(scripted.RangeCalls, Is.EqualTo(1),
                "a fixed quantity needs no roll");
        }

        [Test]
        public void A_capped_table_stops_after_its_limit()
        {
            AddDropTable(CommonTable, new[]
            {
                new DropEntry(new DefinitionId(Coin), 1, 1),
                new DropEntry(new DefinitionId(Hide), 1, 1),
                new DropEntry(new DefinitionId(Relic), 1, 1)
            }, maxEntries: 2);

            var loot = new List<LootResult>();
            DropResolver.Resolve(InstanceId.New(), new DefinitionId(CommonTable), Drops(), loot);

            Assert.That(loot.Count, Is.EqualTo(2));
        }

        [Test]
        public void A_level_banded_entry_only_applies_inside_its_band()
        {
            AddDropTable(CommonTable, new[]
            {
                new DropEntry(new DefinitionId(Relic), 1, 1, minKillerLevel: 20)
            });

            var lowLevel = new List<LootResult>();
            DropResolver.Resolve(InstanceId.New(), new DefinitionId(CommonTable),
                Drops(killerLevel: 5), lowLevel);
            Assert.That(lowLevel, Is.Empty);

            var highLevel = new List<LootResult>();
            DropResolver.Resolve(InstanceId.New(), new DefinitionId(CommonTable),
                Drops(killerLevel: 25), highLevel);
            Assert.That(highLevel.Count, Is.EqualTo(1));
        }

        [Test]
        public void Malformed_and_unresolvable_entries_are_skipped_not_dropped()
        {
            AddDropTable(CommonTable, new[]
            {
                new DropEntry(DefinitionId.None, 1, 1),
                new DropEntry(new DefinitionId(Coin), 0, 0),
                new DropEntry(new DefinitionId("item.deleted.by.patch"), 1, 1),
                new DropEntry(new DefinitionId(Hide), 2, 2)
            });

            var loot = new List<LootResult>();
            DropResolver.Resolve(InstanceId.New(), new DefinitionId(CommonTable), Drops(), loot);

            Assert.That(loot.Count, Is.EqualTo(1), "only the one good row");
            Assert.That(loot[0].Item, Is.EqualTo(new DefinitionId(Hide)));
        }

        [Test]
        public void An_unresolvable_table_drops_nothing_without_failing()
        {
            var loot = new List<LootResult>();

            Assert.That(DropResolver.Resolve(InstanceId.New(),
                new DefinitionId("drop.nowhere"), Drops(), loot), Is.EqualTo(0));
            Assert.That(DropResolver.Resolve(InstanceId.New(), DefinitionId.None, Drops(), loot),
                Is.EqualTo(0));
            Assert.That(loot, Is.Empty);
        }

        // ---- defeat --------------------------------------------------------------------

        [Test]
        public void A_defeat_reports_experience_and_loot_exactly_once()
        {
            AddDropTable(CommonTable, new[] { new DropEntry(new DefinitionId(Coin), 3, 3) });
            AddMonster("monster.rich", level: 7, experience: 250, currency: 40,
                lootTable: CommonTable);

            MonsterRuntimeState monster = Spawn("monster.rich");
            monster.ApplyHealthDelta(-monster.MaxHealth);

            var killer = InstanceId.New();
            var loot = new List<LootResult>();

            MonsterDefeatResult first = MonsterDefeatService.Resolve(monster, killer,
                Drops(), loot);

            Assert.That(first.IsClaimed, Is.True);
            Assert.That(first.ExperienceReward, Is.EqualTo(250));
            Assert.That(first.CurrencyReward, Is.EqualTo(40));
            Assert.That(first.MonsterLevel, Is.EqualTo(7));
            Assert.That(first.Killer, Is.EqualTo(killer));
            Assert.That(first.Participants.Count, Is.EqualTo(1));
            Assert.That(loot.Count, Is.EqualTo(1));

            MonsterDefeatResult second = MonsterDefeatService.Resolve(monster, killer,
                Drops(), loot);

            Assert.That(second.IsClaimed, Is.False,
                "two killing blows in one frame must pay out once");
            Assert.That(second.ExperienceReward, Is.EqualTo(0));
            Assert.That(loot.Count, Is.EqualTo(1), "and no second roll happened");
        }

        [Test]
        public void A_living_monster_owes_nothing()
        {
            MonsterRuntimeState monster = Spawn(Grunt);

            var loot = new List<LootResult>();
            MonsterDefeatResult result = MonsterDefeatService.Resolve(monster, InstanceId.New(),
                Drops(), loot);

            Assert.That(result.IsClaimed, Is.False);
            Assert.That(loot, Is.Empty);
        }

        [Test]
        public void A_defeat_with_nothing_to_drop_makes_no_loot_object()
        {
            MonsterRuntimeState monster = Spawn(Grunt);   // no loot table authored
            monster.ApplyHealthDelta(-monster.MaxHealth);

            var loot = new List<LootResult>();
            MonsterDefeatResult defeat = MonsterDefeatService.Resolve(monster, InstanceId.New(),
                Drops(), loot);

            Assert.That(defeat.IsClaimed, Is.True);
            Assert.That(MonsterDefeatService.CreateLoot(defeat, loot, CombatPosition.Zero),
                Is.Null, "an empty pile must not appear in the world");
        }

        // ---- world loot ----------------------------------------------------------------

        [Test]
        public void Picking_up_creates_an_ordinary_owned_item()
        {
            LootObjectState pile = Pile(new LootResult(InstanceId.New(),
                new DefinitionId(Coin), 5));
            ItemContainerState bag = Container(8);

            LootPickupResult result = LootPickupService.TryPickUp(pile, 0, bag, Pickup());

            Assert.That(result.IsAccepted, Is.True, result.ToString());
            Assert.That(bag.CountOf(new DefinitionId(Coin)), Is.EqualTo(5));

            ItemSlot slot = bag.GetSlot(0);
            Assert.That(slot.Content.Owner, Is.EqualTo(Owner),
                "loot arrives already owned, not reassigned afterwards");
            Assert.That(slot.InstanceId.IsValid, Is.True,
                "a real instance, which a future trade can operate on");
        }

        [Test]
        public void The_same_entry_cannot_be_taken_twice()
        {
            LootObjectState pile = Pile(new LootResult(InstanceId.New(),
                new DefinitionId(Coin), 5));
            ItemContainerState bag = Container(8);

            Assert.That(LootPickupService.TryPickUp(pile, 0, bag, Pickup()).IsAccepted, Is.True);

            LootPickupResult second = LootPickupService.TryPickUp(pile, 0, bag, Pickup());

            Assert.That(second.Reason, Is.EqualTo(LootPickupRejection.AlreadyTaken),
                "two players clicking together must not both receive it");
            Assert.That(bag.CountOf(new DefinitionId(Coin)), Is.EqualTo(5));
            Assert.That(pile.IsEmpty, Is.True);
        }

        [Test]
        public void A_full_bag_refuses_and_the_loot_stays_in_the_world()
        {
            LootObjectState pile = Pile(new LootResult(InstanceId.New(),
                new DefinitionId(Relic), 1));

            ItemContainerState bag = Container(1);
            bag.Add(Stack(Hide, 1), Items);   // the only slot, and relics do not stack

            LootPickupResult result = LootPickupService.TryPickUp(pile, 0, bag, Pickup());

            Assert.That(result.Reason, Is.EqualTo(LootPickupRejection.InventoryFull));
            Assert.That(pile.IsTaken(0), Is.False,
                "a refused pickup must not have marked it taken");
            Assert.That(pile.IsEmpty, Is.False, "it is still there to come back for");
        }

        [Test]
        public void Loot_stacks_through_the_existing_container_rules()
        {
            LootObjectState pile = Pile(new LootResult(InstanceId.New(),
                new DefinitionId(Hide), 40));

            ItemContainerState bag = Container(8);
            bag.Add(Stack(Hide, 50), Items);

            LootPickupService.TryPickUp(pile, 0, bag, Pickup());

            Assert.That(bag.CountOf(new DefinitionId(Hide)), Is.EqualTo(90));
            Assert.That(bag.OccupiedSlots, Is.EqualTo(1), "it merged, using no new slot");
        }

        [Test]
        public void What_will_not_fit_goes_back_into_the_world()
        {
            // Hide stacks to 99. One slot already holds 90, and nothing else is free.
            LootObjectState pile = Pile(new LootResult(InstanceId.New(),
                new DefinitionId(Hide), 40));

            ItemContainerState bag = Container(1);
            bag.Add(Stack(Hide, 90), Items);

            LootPickupResult result = LootPickupService.TryPickUp(pile, 0, bag, Pickup());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.QuantityTaken, Is.EqualTo(9));
            Assert.That(result.Remainder, Is.EqualTo(31));
            Assert.That(bag.CountOf(new DefinitionId(Hide)), Is.EqualTo(99));
            Assert.That(pile.IsTaken(0), Is.False);
            Assert.That(pile.Contents[0].Quantity, Is.EqualTo(31),
                "destroying the remainder is the one outcome a player never accepts");
        }

        [Test]
        public void Expired_loot_cannot_be_taken()
        {
            LootObjectState pile = Pile(new LootResult(InstanceId.New(),
                new DefinitionId(Coin), 1), lifetime: 10f);

            pile.Tick(11f);

            Assert.That(pile.IsExpired, Is.True);
            Assert.That(LootPickupService.TryPickUp(pile, 0, Container(8), Pickup()).Reason,
                Is.EqualTo(LootPickupRejection.Expired));
        }

        [Test]
        public void Owner_only_loot_refuses_everyone_else_forever()
        {
            var mine = new CharacterId("char:mine");
            var theirs = new CharacterId("char:theirs");

            LootObjectState pile = Pile(new LootResult(InstanceId.New(),
                new DefinitionId(Relic), 1), policy: LootPolicy.OwnerOnly, eligible: mine);

            pile.Tick(9999f);

            Assert.That(LootPickupService.TryPickUp(pile, 0, Container(8), Pickup(theirs)).Reason,
                Is.EqualTo(LootPickupRejection.NotEligible));
            Assert.That(LootPickupService.TryPickUp(pile, 0, Container(8), Pickup(mine))
                .IsAccepted, Is.True);
        }

        [Test]
        public void Personal_loot_opens_up_once_its_window_lapses()
        {
            var mine = new CharacterId("char:mine");
            var passerby = new CharacterId("char:passerby");

            LootObjectState pile = Pile(new LootResult(InstanceId.New(),
                new DefinitionId(Coin), 1), policy: LootPolicy.Personal, eligible: mine,
                personalWindow: 30f);

            Assert.That(LootPickupService.TryPickUp(pile, 0, Container(8), Pickup(passerby)).Reason,
                Is.EqualTo(LootPickupRejection.NotEligible));

            pile.Tick(31f);

            Assert.That(LootPickupService.TryPickUp(pile, 0, Container(8), Pickup(passerby))
                .IsAccepted, Is.True, "the courtesy window lapsed");
        }

        [Test]
        public void Party_loot_reads_as_personal_until_a_party_system_exists()
        {
            var mine = new CharacterId("char:mine");
            var other = new CharacterId("char:other");

            LootObjectState pile = Pile(new LootResult(InstanceId.New(),
                new DefinitionId(Coin), 1), policy: LootPolicy.Party, eligible: mine);

            Assert.That(LootPickupService.TryPickUp(pile, 0, Container(8), Pickup(other)).Reason,
                Is.EqualTo(LootPickupRejection.NotEligible),
                "the stricter reading, so nobody takes what they should not");
        }

        [Test]
        public void Free_for_all_loot_is_open_to_anyone()
        {
            LootObjectState pile = Pile(new LootResult(InstanceId.New(),
                new DefinitionId(Coin), 1), policy: LootPolicy.FreeForAll);

            Assert.That(LootPickupService.TryPickUp(pile, 0, Container(8),
                Pickup(new CharacterId("char:anyone"))).IsAccepted, Is.True);
        }

        [Test]
        public void Taking_everything_takes_what_fits_and_leaves_the_rest()
        {
            var source = InstanceId.New();
            LootObjectState pile = new LootObjectState(InstanceId.New(), source,
                CombatPosition.Zero, new[]
                {
                    new LootResult(source, new DefinitionId(Coin), 5),
                    new LootResult(source, new DefinitionId(Hide), 5),
                    new LootResult(source, new DefinitionId(Relic), 1)
                });

            ItemContainerState bag = Container(2);

            int taken = LootPickupService.TryPickUpAll(pile, bag, Pickup());

            Assert.That(taken, Is.EqualTo(2), "two slots, two stacks");
            Assert.That(pile.IsTaken(2), Is.False, "the relic is still there");
        }

        // ---- quests --------------------------------------------------------------------

        [Test]
        public void A_quest_can_be_accepted_and_starts_active()
        {
            AddQuest(KillQuest, new[]
            {
                new QuestObjective(QuestObjectiveType.KillMonster, new DefinitionId(Grunt), 3)
            });

            var state = new CharacterQuestState(Character);

            QuestResult result = QuestService.TryAccept(state, new DefinitionId(KillQuest),
                QuestContext());

            Assert.That(result.IsAccepted, Is.True, result.ToString());
            Assert.That(state.StatusOf(new DefinitionId(KillQuest)),
                Is.EqualTo(QuestStatus.Active));
        }

        [Test]
        public void Taking_the_same_quest_twice_is_refused()
        {
            AddQuest(KillQuest, new[]
            {
                new QuestObjective(QuestObjectiveType.KillMonster, new DefinitionId(Grunt), 3)
            });

            var state = new CharacterQuestState(Character);
            QuestService.TryAccept(state, new DefinitionId(KillQuest), QuestContext());

            Assert.That(QuestService.TryAccept(state, new DefinitionId(KillQuest),
                QuestContext()).Reason, Is.EqualTo(QuestRejection.AlreadyActive));
        }

        [Test]
        public void An_under_levelled_character_is_refused()
        {
            AddQuest(KillQuest, new[]
            {
                new QuestObjective(QuestObjectiveType.KillMonster, new DefinitionId(Grunt), 1)
            }, levelRequirement: 20);

            var state = new CharacterQuestState(Character);

            Assert.That(QuestService.TryAccept(state, new DefinitionId(KillQuest),
                QuestContext(level: 5)).Reason, Is.EqualTo(QuestRejection.LevelTooLow));
            Assert.That(QuestService.TryAccept(state, new DefinitionId(KillQuest),
                QuestContext(level: 20)).IsAccepted, Is.True);
        }

        [Test]
        public void A_prerequisite_must_be_completed_first()
        {
            AddQuest(KillQuest, new[]
            {
                new QuestObjective(QuestObjectiveType.KillMonster, new DefinitionId(Grunt), 1)
            }, rewards: new[] { new QuestReward(QuestRewardType.Experience, default, 10) });

            AddQuest(CollectQuest, new[]
            {
                new QuestObjective(QuestObjectiveType.CollectItem, new DefinitionId(Hide), 1)
            }, prerequisites: new[] { new DefinitionId(KillQuest) });

            var state = new CharacterQuestState(Character);

            Assert.That(QuestService.TryAccept(state, new DefinitionId(CollectQuest),
                QuestContext()).Reason, Is.EqualTo(QuestRejection.PrerequisiteNotMet));

            QuestService.TryAccept(state, new DefinitionId(KillQuest), QuestContext());
            QuestService.ReportProgress(state, QuestObjectiveType.KillMonster,
                new DefinitionId(Grunt), 1, QuestContext());
            QuestService.TryTurnIn(state, new DefinitionId(KillQuest), Container(8), QuestContext());

            Assert.That(QuestService.TryAccept(state, new DefinitionId(CollectQuest),
                QuestContext()).IsAccepted, Is.True);
        }

        [Test]
        public void A_quest_with_no_objectives_cannot_be_taken()
        {
            AddQuest("quest.empty", new QuestObjective[0]);

            var state = new CharacterQuestState(Character);

            Assert.That(QuestService.TryAccept(state, new DefinitionId("quest.empty"),
                QuestContext()).Reason, Is.EqualTo(QuestRejection.NoObjectives),
                "it could never be finished, so it would stick in the log for good");
        }

        [Test]
        public void Killing_advances_a_kill_objective_and_completes_it()
        {
            AddQuest(KillQuest, new[]
            {
                new QuestObjective(QuestObjectiveType.KillMonster, new DefinitionId(Grunt), 3)
            });

            var state = new CharacterQuestState(Character);
            QuestService.TryAccept(state, new DefinitionId(KillQuest), QuestContext());

            var id = new DefinitionId(KillQuest);

            QuestService.ReportProgress(state, QuestObjectiveType.KillMonster,
                new DefinitionId(Grunt), 1, QuestContext());

            QuestProgress progress;
            state.TryGet(id, out progress);
            Assert.That(progress.CountAt(0), Is.EqualTo(1));
            Assert.That(progress.Status, Is.EqualTo(QuestStatus.Active));

            QuestService.ReportProgress(state, QuestObjectiveType.KillMonster,
                new DefinitionId(Grunt), 2, QuestContext());

            Assert.That(state.StatusOf(id), Is.EqualTo(QuestStatus.ReadyToComplete));
        }

        [Test]
        public void Over_delivering_does_not_accumulate_a_surplus()
        {
            AddQuest(KillQuest, new[]
            {
                new QuestObjective(QuestObjectiveType.KillMonster, new DefinitionId(Grunt), 3)
            });

            var state = new CharacterQuestState(Character);
            QuestService.TryAccept(state, new DefinitionId(KillQuest), QuestContext());

            QuestService.ReportProgress(state, QuestObjectiveType.KillMonster,
                new DefinitionId(Grunt), 20, QuestContext());

            QuestProgress progress;
            state.TryGet(new DefinitionId(KillQuest), out progress);

            Assert.That(progress.CountAt(0), Is.EqualTo(3), "clamped at what was required");
        }

        [Test]
        public void The_wrong_monster_and_the_wrong_type_advance_nothing()
        {
            AddQuest(KillQuest, new[]
            {
                new QuestObjective(QuestObjectiveType.KillMonster, new DefinitionId(Grunt), 3)
            });

            var state = new CharacterQuestState(Character);
            QuestService.TryAccept(state, new DefinitionId(KillQuest), QuestContext());

            Assert.That(QuestService.ReportProgress(state, QuestObjectiveType.KillMonster,
                new DefinitionId(Docile), 5, QuestContext()), Is.EqualTo(0));

            Assert.That(QuestService.ReportProgress(state, QuestObjectiveType.CollectItem,
                new DefinitionId(Grunt), 5, QuestContext()), Is.EqualTo(0));

            QuestProgress progress;
            state.TryGet(new DefinitionId(KillQuest), out progress);
            Assert.That(progress.CountAt(0), Is.EqualTo(0));
        }

        [Test]
        public void Collect_and_talk_objectives_work_the_same_way()
        {
            AddQuest(CollectQuest, new[]
            {
                new QuestObjective(QuestObjectiveType.CollectItem, new DefinitionId(Hide), 2)
            });

            AddQuest(TalkQuest, new[]
            {
                new QuestObjective(QuestObjectiveType.TalkToNpc, new DefinitionId(Elder), 1)
            });

            var state = new CharacterQuestState(Character);
            QuestService.TryAccept(state, new DefinitionId(CollectQuest), QuestContext());
            QuestService.TryAccept(state, new DefinitionId(TalkQuest), QuestContext());

            QuestService.ReportProgress(state, QuestObjectiveType.CollectItem,
                new DefinitionId(Hide), 2, QuestContext());
            QuestService.ReportProgress(state, QuestObjectiveType.TalkToNpc,
                new DefinitionId(Elder), 1, QuestContext());

            Assert.That(state.StatusOf(new DefinitionId(CollectQuest)),
                Is.EqualTo(QuestStatus.ReadyToComplete));
            Assert.That(state.StatusOf(new DefinitionId(TalkQuest)),
                Is.EqualTo(QuestStatus.ReadyToComplete));
        }

        [Test]
        public void One_kill_can_advance_several_quests_at_once()
        {
            AddQuest(KillQuest, new[]
            {
                new QuestObjective(QuestObjectiveType.KillMonster, new DefinitionId(Grunt), 5)
            });

            AddQuest("quest.kill2", new[]
            {
                new QuestObjective(QuestObjectiveType.KillMonster, new DefinitionId(Grunt), 2)
            });

            var state = new CharacterQuestState(Character);
            QuestService.TryAccept(state, new DefinitionId(KillQuest), QuestContext());
            QuestService.TryAccept(state, new DefinitionId("quest.kill2"), QuestContext());

            int advanced = QuestService.ReportProgress(state, QuestObjectiveType.KillMonster,
                new DefinitionId(Grunt), 1, QuestContext());

            Assert.That(advanced, Is.EqualTo(2), "and nothing was polled to find that out");
        }

        [Test]
        public void An_objective_with_no_target_matches_anything_of_its_type()
        {
            AddQuest("quest.any", new[]
            {
                new QuestObjective(QuestObjectiveType.KillMonster, DefinitionId.None, 2)
            });

            var state = new CharacterQuestState(Character);
            QuestService.TryAccept(state, new DefinitionId("quest.any"), QuestContext());

            QuestService.ReportProgress(state, QuestObjectiveType.KillMonster,
                new DefinitionId(Grunt), 1, QuestContext());
            QuestService.ReportProgress(state, QuestObjectiveType.KillMonster,
                new DefinitionId(Docile), 1, QuestContext());

            Assert.That(state.StatusOf(new DefinitionId("quest.any")),
                Is.EqualTo(QuestStatus.ReadyToComplete), "kill ten of anything");
        }

        // ---- turn-in -------------------------------------------------------------------

        [Test]
        public void Turning_in_pays_the_authored_rewards_exactly_once()
        {
            AddQuest(KillQuest, new[]
            {
                new QuestObjective(QuestObjectiveType.KillMonster, new DefinitionId(Grunt), 1)
            }, rewards: new[]
            {
                new QuestReward(QuestRewardType.Experience, default, 500),
                new QuestReward(QuestRewardType.Currency, default, 100),
                new QuestReward(QuestRewardType.Item, new DefinitionId(Hide), 3)
            });

            var state = new CharacterQuestState(Character);
            ItemContainerState bag = Container(8);

            QuestService.TryAccept(state, new DefinitionId(KillQuest), QuestContext());
            QuestService.ReportProgress(state, QuestObjectiveType.KillMonster,
                new DefinitionId(Grunt), 1, QuestContext());

            QuestResult result = QuestService.TryTurnIn(state, new DefinitionId(KillQuest),
                bag, QuestContext());

            Assert.That(result.IsAccepted, Is.True, result.ToString());
            Assert.That(result.ExperienceGranted, Is.EqualTo(500));
            Assert.That(result.CurrencyGranted, Is.EqualTo(100));
            Assert.That(bag.CountOf(new DefinitionId(Hide)), Is.EqualTo(3));
            Assert.That(state.StatusOf(new DefinitionId(KillQuest)),
                Is.EqualTo(QuestStatus.Completed));

            for (int i = 0; i < 10; i++)
            {
                QuestResult again = QuestService.TryTurnIn(state, new DefinitionId(KillQuest),
                    bag, QuestContext());

                Assert.That(again.Reason, Is.EqualTo(QuestRejection.AlreadyCompleted));
            }

            Assert.That(bag.CountOf(new DefinitionId(Hide)), Is.EqualTo(3),
                "a UI refreshing ten times must not pay ten times");
        }

        [Test]
        public void An_unfinished_quest_cannot_be_turned_in()
        {
            AddQuest(KillQuest, new[]
            {
                new QuestObjective(QuestObjectiveType.KillMonster, new DefinitionId(Grunt), 5)
            }, rewards: new[] { new QuestReward(QuestRewardType.Item, new DefinitionId(Hide), 1) });

            var state = new CharacterQuestState(Character);
            ItemContainerState bag = Container(8);

            QuestService.TryAccept(state, new DefinitionId(KillQuest), QuestContext());
            QuestService.ReportProgress(state, QuestObjectiveType.KillMonster,
                new DefinitionId(Grunt), 2, QuestContext());

            Assert.That(QuestService.TryTurnIn(state, new DefinitionId(KillQuest), bag,
                QuestContext()).Reason, Is.EqualTo(QuestRejection.ObjectivesIncomplete));
            Assert.That(bag.OccupiedSlots, Is.EqualTo(0));
        }

        [Test]
        public void A_quest_that_was_never_taken_cannot_be_turned_in()
        {
            AddQuest(KillQuest, new[]
            {
                new QuestObjective(QuestObjectiveType.KillMonster, new DefinitionId(Grunt), 1)
            });

            var state = new CharacterQuestState(Character);

            Assert.That(QuestService.TryTurnIn(state, new DefinitionId(KillQuest), Container(8),
                QuestContext()).Reason, Is.EqualTo(QuestRejection.NotActive));
        }

        [Test]
        public void A_full_bag_refuses_the_turn_in_and_leaves_the_quest_ready()
        {
            AddQuest(KillQuest, new[]
            {
                new QuestObjective(QuestObjectiveType.KillMonster, new DefinitionId(Grunt), 1)
            }, rewards: new[] { new QuestReward(QuestRewardType.Item, new DefinitionId(Relic), 1) });

            var state = new CharacterQuestState(Character);

            ItemContainerState bag = Container(1);
            bag.Add(Stack(Hide, 1), Items);

            QuestService.TryAccept(state, new DefinitionId(KillQuest), QuestContext());
            QuestService.ReportProgress(state, QuestObjectiveType.KillMonster,
                new DefinitionId(Grunt), 1, QuestContext());

            QuestResult result = QuestService.TryTurnIn(state, new DefinitionId(KillQuest),
                bag, QuestContext());

            Assert.That(result.Reason, Is.EqualTo(QuestRejection.InventoryFull));
            Assert.That(state.StatusOf(new DefinitionId(KillQuest)),
                Is.EqualTo(QuestStatus.ReadyToComplete),
                "completing it for nothing would lose the reward for good");

            bag.RemoveAt(0, 1);

            Assert.That(QuestService.TryTurnIn(state, new DefinitionId(KillQuest), bag,
                QuestContext()).IsAccepted, Is.True, "and it can still be claimed later");
        }

        [Test]
        public void A_reward_naming_nothing_that_exists_is_refused_before_anything_is_paid()
        {
            AddQuest(KillQuest, new[]
            {
                new QuestObjective(QuestObjectiveType.KillMonster, new DefinitionId(Grunt), 1)
            }, rewards: new[]
            {
                new QuestReward(QuestRewardType.Experience, default, 500),
                new QuestReward(QuestRewardType.Item, new DefinitionId("item.deleted"), 1)
            });

            var state = new CharacterQuestState(Character);
            ItemContainerState bag = Container(8);

            QuestService.TryAccept(state, new DefinitionId(KillQuest), QuestContext());
            QuestService.ReportProgress(state, QuestObjectiveType.KillMonster,
                new DefinitionId(Grunt), 1, QuestContext());

            QuestResult result = QuestService.TryTurnIn(state, new DefinitionId(KillQuest),
                bag, QuestContext());

            Assert.That(result.Reason, Is.EqualTo(QuestRejection.InvalidReward));
            Assert.That(result.ExperienceGranted, Is.EqualTo(0),
                "the good reward was not paid for a turn-in that was refused");
            Assert.That(state.StatusOf(new DefinitionId(KillQuest)),
                Is.EqualTo(QuestStatus.ReadyToComplete));
        }

        [Test]
        public void A_repeatable_quest_can_be_taken_again_and_starts_from_zero()
        {
            AddQuest(KillQuest, new[]
            {
                new QuestObjective(QuestObjectiveType.KillMonster, new DefinitionId(Grunt), 2)
            }, rewards: new[] { new QuestReward(QuestRewardType.Experience, default, 10) },
                repeatable: true);

            var state = new CharacterQuestState(Character);
            var id = new DefinitionId(KillQuest);

            QuestService.TryAccept(state, id, QuestContext());
            QuestService.ReportProgress(state, QuestObjectiveType.KillMonster,
                new DefinitionId(Grunt), 2, QuestContext());
            QuestService.TryTurnIn(state, id, Container(8), QuestContext());

            Assert.That(QuestService.TryAccept(state, id, QuestContext()).IsAccepted, Is.True);

            QuestProgress progress;
            state.TryGet(id, out progress);

            Assert.That(progress.CountAt(0), Is.EqualTo(0));
            Assert.That(progress.Status, Is.EqualTo(QuestStatus.Active));
        }

        [Test]
        public void A_non_repeatable_quest_cannot_be_taken_again()
        {
            AddQuest(KillQuest, new[]
            {
                new QuestObjective(QuestObjectiveType.KillMonster, new DefinitionId(Grunt), 1)
            }, rewards: new[] { new QuestReward(QuestRewardType.Experience, default, 10) });

            var state = new CharacterQuestState(Character);
            var id = new DefinitionId(KillQuest);

            QuestService.TryAccept(state, id, QuestContext());
            QuestService.ReportProgress(state, QuestObjectiveType.KillMonster,
                new DefinitionId(Grunt), 1, QuestContext());
            QuestService.TryTurnIn(state, id, Container(8), QuestContext());

            Assert.That(QuestService.TryAccept(state, id, QuestContext()).Reason,
                Is.EqualTo(QuestRejection.AlreadyCompleted));
        }

        // ---- integration ---------------------------------------------------------------

        [Test]
        public void A_kill_pays_experience_drops_loot_and_advances_a_quest()
        {
            AddDropTable(CommonTable, new[] { new DropEntry(new DefinitionId(Hide), 2, 2) });
            AddMonster("monster.quarry", level: 6, experience: 120, lootTable: CommonTable);

            AddQuest(KillQuest, new[]
            {
                new QuestObjective(QuestObjectiveType.KillMonster,
                    new DefinitionId("monster.quarry"), 1)
            }, rewards: new[] { new QuestReward(QuestRewardType.Experience, default, 300) });

            var quests = new CharacterQuestState(Character);
            QuestService.TryAccept(quests, new DefinitionId(KillQuest), QuestContext());

            MonsterRuntimeState monster = Spawn("monster.quarry");
            monster.ApplyHealthDelta(-monster.MaxHealth);

            // 1. the defeat, claimed once
            var loot = new List<LootResult>();
            MonsterDefeatResult defeat = MonsterDefeatService.Resolve(monster, InstanceId.New(),
                Drops(), loot);

            Assert.That(defeat.IsClaimed, Is.True);
            Assert.That(defeat.ExperienceReward, Is.EqualTo(120));

            // 2. the loot, into the world and then into a bag
            LootObjectState pile = MonsterDefeatService.CreateLoot(defeat, loot,
                monster.Position);

            ItemContainerState bag = Container(8);
            Assert.That(LootPickupService.TryPickUpAll(pile, bag, Pickup()), Is.EqualTo(1));
            Assert.That(bag.CountOf(new DefinitionId(Hide)), Is.EqualTo(2));

            // 3. the quest, told what happened
            QuestService.ReportProgress(quests, QuestObjectiveType.KillMonster,
                defeat.MonsterDefinitionId, 1, QuestContext());

            Assert.That(quests.StatusOf(new DefinitionId(KillQuest)),
                Is.EqualTo(QuestStatus.ReadyToComplete));

            QuestResult turnIn = QuestService.TryTurnIn(quests, new DefinitionId(KillQuest),
                bag, QuestContext());

            Assert.That(turnIn.ExperienceGranted, Is.EqualTo(300));
        }

        [Test]
        public void Picking_an_item_up_can_advance_a_collect_quest()
        {
            AddQuest(CollectQuest, new[]
            {
                new QuestObjective(QuestObjectiveType.CollectItem, new DefinitionId(Hide), 5)
            });

            var quests = new CharacterQuestState(Character);
            QuestService.TryAccept(quests, new DefinitionId(CollectQuest), QuestContext());

            LootObjectState pile = Pile(new LootResult(InstanceId.New(),
                new DefinitionId(Hide), 5));

            ItemContainerState bag = Container(8);
            LootPickupResult picked = LootPickupService.TryPickUp(pile, 0, bag, Pickup());

            Assert.That(picked.IsAccepted, Is.True);

            QuestService.ReportProgress(quests, QuestObjectiveType.CollectItem,
                picked.Item, picked.QuantityTaken, QuestContext());

            Assert.That(quests.StatusOf(new DefinitionId(CollectQuest)),
                Is.EqualTo(QuestStatus.ReadyToComplete));
        }

        [Test]
        public void No_definition_id_is_compared_against_a_literal_in_any_of_these_services()
        {
            string[] sources =
            {
                System.IO.File.ReadAllText("Assets/_Game/Scripts/Gameplay/DropResolver.cs"),
                System.IO.File.ReadAllText("Assets/_Game/Scripts/Gameplay/LootPickupService.cs"),
                System.IO.File.ReadAllText("Assets/_Game/Scripts/Gameplay/QuestService.cs"),
                System.IO.File.ReadAllText("Assets/_Game/Scripts/Gameplay/MonsterDefeatService.cs")
            };

            string[] mustNotAppear =
            {
                Coin, Hide, Relic, Grunt, CommonTable, KillQuest, Elder,
                "Goblin", "Potion", "DevilFruit", "Card"
            };

            foreach (string source in sources)
            {
                foreach (string forbidden in mustNotAppear)
                {
                    Assert.That(source, Does.Not.Contain(forbidden),
                        "a service names '" + forbidden + "'; behaviour must come from data");
                }
            }
        }

        // ---- helpers -------------------------------------------------------------------

        private LootObjectState Pile(LootResult entry,
            LootPolicy policy = LootPolicy.FreeForAll, CharacterId eligible = default,
            float lifetime = 0f, float personalWindow = 0f)
        {
            return new LootObjectState(InstanceId.New(), entry.Source, CombatPosition.Zero,
                new[] { entry }, policy, eligible, lifetime, personalWindow);
        }
    }
}
