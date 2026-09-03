using System;
using System.Collections.Generic;
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
    /// The world UI, and the content validation behind it.
    /// </summary>
    /// <remarks>
    /// The rule this suite protects is the one every UI phase has protected: looking is not
    /// doing. Drawing a quest, a loot pile or a health bar must leave gameplay exactly as it
    /// was, and the only thing that changes state is a submitted command reaching a service.
    /// </remarks>
    internal sealed class WorldUiAndValidationTests : MonsterTestBase
    {
        private const string KillQuest = "quest.kill";
        private const string CommonTable = "drop.common";

        private GameObject _host;
        private QuestUiController _controller;
        private CharacterQuestState _quests;
        private ItemContainerState _bag;
        private DefinitionValidator _validator;

        [SetUp]
        public void CreateWorldUi()
        {
            _host = new GameObject("WorldUiHost");
            _controller = _host.AddComponent<QuestUiController>();

            _quests = new CharacterQuestState(Character);
            _bag = Container(8);

            _validator = new DefinitionValidator(new IDefinitionValidationRule[]
            {
                new WorldContentValidationRule()
            });
        }

        [TearDown]
        public void DestroyWorldUi()
        {
            if (_host != null) UnityEngine.Object.DestroyImmediate(_host);
        }

        private void Bind(int level = 10)
        {
            _controller.Bind(_quests, _bag, Items, Monsters, Quests, Character, Owner, level);
        }

        private WorldViewAdapter.Context ViewContext()
        {
            return new WorldViewAdapter.Context(Items, Monsters, Quests);
        }

        private ValidationReport Check(IDefinition definition, IDefinitionLookup lookup = null)
        {
            return _validator.Validate(definition, lookup ?? CompositeLookup());
        }

        /// <summary>Everything the fixtures registered, for reference checking.</summary>
        private IDefinitionLookup CompositeLookup()
        {
            return new TestLookup(Items, Monsters, Quests, Maps, DropTables);
        }

        private static bool HasError(ValidationReport report, string fragment)
        {
            for (int i = 0; i < report.Messages.Count; i++)
            {
                if (report.Messages[i].Severity != ValidationSeverity.Error) continue;
                if (report.Messages[i].Message.Contains(fragment)) return true;
            }

            return false;
        }

        // ---- view data reads only ------------------------------------------------------

        [Test]
        public void Building_a_quest_view_changes_nothing()
        {
            AddQuest(KillQuest, new[]
            {
                new QuestObjective(QuestObjectiveType.KillMonster, new DefinitionId(Grunt), 3)
            }, rewards: new[] { new QuestReward(QuestRewardType.Item, new DefinitionId(Hide), 2) });

            QuestService.TryAccept(_quests, new DefinitionId(KillQuest),
                new QuestService.Context(Quests, Items, 10, Owner));

            Revision before = _quests.Revision;

            for (int i = 0; i < 25; i++)
            {
                QuestViewData view = WorldViewAdapter.BuildQuest(_quests,
                    new DefinitionId(KillQuest), ViewContext());

                Assert.That(view.IsValid, Is.True);
            }

            Assert.That(_quests.Revision, Is.EqualTo(before),
                "twenty-five reads must cost exactly nothing");
        }

        [Test]
        public void A_quest_view_carries_progress_status_objectives_and_rewards()
        {
            AddQuest(KillQuest, new[]
            {
                new QuestObjective(QuestObjectiveType.KillMonster, new DefinitionId(Grunt), 3)
            }, rewards: new[]
            {
                new QuestReward(QuestRewardType.Experience, default, 500),
                new QuestReward(QuestRewardType.Item, new DefinitionId(Hide), 2)
            }, levelRequirement: 5);

            var context = new QuestService.Context(Quests, Items, 10, Owner);
            QuestService.TryAccept(_quests, new DefinitionId(KillQuest), context);
            QuestService.ReportProgress(_quests, QuestObjectiveType.KillMonster,
                new DefinitionId(Grunt), 2, context);

            QuestViewData view = WorldViewAdapter.BuildQuest(_quests,
                new DefinitionId(KillQuest), ViewContext());

            Assert.That(view.Status, Is.EqualTo(QuestStatusView.Active));
            Assert.That(view.LevelRequirement, Is.EqualTo(5));
            Assert.That(view.Objectives.Count, Is.EqualTo(1));
            Assert.That(view.Objectives[0].Current, Is.EqualTo(2));
            Assert.That(view.Objectives[0].Required, Is.EqualTo(3));
            Assert.That(view.Objectives[0].IsComplete, Is.False);
            Assert.That(view.Objectives[0].TargetNameKey.IsValid, Is.True,
                "the monster's own name key, resolved by the Client");
            Assert.That(view.Rewards.Count, Is.EqualTo(2));

            string body = QuestDetailView.FormatBody(view, null);
            Assert.That(body, Does.Contain("2/3"));
            Assert.That(body, Does.Contain("Rewards:"));
        }

        [Test]
        public void A_ready_quest_says_so_in_the_tracker()
        {
            AddQuest(KillQuest, new[]
            {
                new QuestObjective(QuestObjectiveType.KillMonster, new DefinitionId(Grunt), 1)
            });

            var context = new QuestService.Context(Quests, Items, 10, Owner);
            QuestService.TryAccept(_quests, new DefinitionId(KillQuest), context);
            QuestService.ReportProgress(_quests, QuestObjectiveType.KillMonster,
                new DefinitionId(Grunt), 1, context);

            QuestViewData view = WorldViewAdapter.BuildQuest(_quests,
                new DefinitionId(KillQuest), ViewContext());

            Assert.That(view.IsReadyToComplete, Is.True);
            Assert.That(QuestTrackerView.FormatRow(view, null), Does.Contain("complete"));
        }

        [Test]
        public void An_unresolvable_quest_gives_an_invalid_view()
        {
            Assert.That(WorldViewAdapter.BuildQuest(_quests, new DefinitionId("quest.nope"),
                ViewContext()).IsValid, Is.False);
            Assert.That(QuestDetailView.FormatTitle(QuestViewData.None, null), Is.Empty);
            Assert.That(QuestTrackerView.FormatRow(QuestViewData.None, null), Is.Empty);
        }

        [Test]
        public void A_monster_health_view_reports_the_figures_and_hides_a_corpse()
        {
            MonsterRuntimeState monster = Spawn(Grunt);
            monster.ApplyHealthDelta(-25);

            MonsterHealthViewData view = WorldViewAdapter.BuildMonsterHealth(monster);

            Assert.That(view.IsValid, Is.True);
            Assert.That(view.CurrentHealth, Is.EqualTo(75));
            Assert.That(view.Fraction, Is.EqualTo(0.75f));
            Assert.That(view.IsBoss, Is.False);
            Assert.That(MonsterHealthBar.FormatLabel(view, null), Does.Contain("75/100"));

            monster.ApplyHealthDelta(-75);
            Assert.That(WorldViewAdapter.BuildMonsterHealth(monster).IsAlive, Is.False);
            Assert.That(WorldViewAdapter.BuildMonsterHealth(null).IsValid, Is.False);
        }

        [Test]
        public void A_boss_rank_is_reported_for_presentation_only()
        {
            AddMonster("monster.king", level: 40, rank: MonsterRank.Boss);

            Assert.That(WorldViewAdapter.BuildMonsterHealth(Spawn("monster.king")).IsBoss,
                Is.True);
        }

        [Test]
        public void A_loot_view_keeps_taken_entries_so_the_list_does_not_jump()
        {
            var source = InstanceId.New();
            var pile = new LootObjectState(InstanceId.New(), source, CombatPosition.Zero, new[]
            {
                new LootResult(source, new DefinitionId(Coin), 5),
                new LootResult(source, new DefinitionId(Hide), 2)
            });

            pile.TryClaim(0);

            var entries = new List<LootEntryViewData>();
            WorldViewAdapter.BuildLoot(pile, ViewContext(), entries);

            Assert.That(entries.Count, Is.EqualTo(2), "a taken entry is greyed, not removed");
            Assert.That(entries[0].IsTaken, Is.True);
            Assert.That(entries[1].IsTaken, Is.False);
            Assert.That(entries[1].Index, Is.EqualTo(1), "the index a pickup command names");
        }

        // ---- the controller ------------------------------------------------------------

        [Test]
        public void The_controller_accepts_and_turns_in_through_the_service()
        {
            AddQuest(KillQuest, new[]
            {
                new QuestObjective(QuestObjectiveType.KillMonster, new DefinitionId(Grunt), 1)
            }, rewards: new[] { new QuestReward(QuestRewardType.Item, new DefinitionId(Hide), 3) });

            Bind();

            Assert.That(_controller.SubmitAccept(new DefinitionId(KillQuest)).IsAccepted, Is.True);
            Assert.That(_quests.StatusOf(new DefinitionId(KillQuest)),
                Is.EqualTo(QuestStatus.Active));

            _controller.ReportKill(new DefinitionId(Grunt));

            Assert.That(_quests.StatusOf(new DefinitionId(KillQuest)),
                Is.EqualTo(QuestStatus.ReadyToComplete));

            QuestResult turnIn = _controller.SubmitTurnIn(new DefinitionId(KillQuest));

            Assert.That(turnIn.IsAccepted, Is.True, turnIn.ToString());
            Assert.That(_bag.CountOf(new DefinitionId(Hide)), Is.EqualTo(3));
        }

        [Test]
        public void A_refused_turn_in_keeps_the_services_reason_and_changes_nothing()
        {
            AddQuest(KillQuest, new[]
            {
                new QuestObjective(QuestObjectiveType.KillMonster, new DefinitionId(Grunt), 5)
            });

            Bind();
            _controller.SubmitAccept(new DefinitionId(KillQuest));

            QuestResult result = _controller.SubmitTurnIn(new DefinitionId(KillQuest));

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(result.Reason, Is.EqualTo(QuestRejection.ObjectivesIncomplete),
                "the UI reports the service's reason rather than inventing one");
            Assert.That(_bag.OccupiedSlots, Is.EqualTo(0));
        }

        [Test]
        public void Picking_loot_up_through_the_controller_also_advances_a_collect_quest()
        {
            AddQuest("quest.collect", new[]
            {
                new QuestObjective(QuestObjectiveType.CollectItem, new DefinitionId(Hide), 2)
            });

            Bind();
            _controller.SubmitAccept(new DefinitionId("quest.collect"));

            var source = InstanceId.New();
            _controller.SetLoot(new LootObjectState(InstanceId.New(), source,
                CombatPosition.Zero, new[] { new LootResult(source, new DefinitionId(Hide), 2) }));

            LootPickupResult picked = _controller.SubmitPickUp(0);

            Assert.That(picked.IsAccepted, Is.True, picked.ToString());
            Assert.That(_bag.CountOf(new DefinitionId(Hide)), Is.EqualTo(2));
            Assert.That(_quests.StatusOf(new DefinitionId("quest.collect")),
                Is.EqualTo(QuestStatus.ReadyToComplete),
                "the pickup reported itself; nothing scanned the bag to find out");
        }

        [Test]
        public void The_controller_redraws_only_when_something_changed()
        {
            AddQuest(KillQuest, new[]
            {
                new QuestObjective(QuestObjectiveType.KillMonster, new DefinitionId(Grunt), 3)
            });

            Bind();
            _controller.SubmitAccept(new DefinitionId(KillQuest));

            Assert.That(_controller.RefreshIfChanged(), Is.False,
                "the panels must not rebuild while nothing is happening");

            QuestService.ReportProgress(_quests, QuestObjectiveType.KillMonster,
                new DefinitionId(Grunt), 1, new QuestService.Context(Quests, Items, 10, Owner));

            Assert.That(_controller.RefreshIfChanged(), Is.True);
            Assert.That(_controller.RefreshIfChanged(), Is.False);
        }

        [Test]
        public void An_unbound_controller_does_nothing_rather_than_throwing()
        {
            Assert.DoesNotThrow(() => _controller.Refresh());
            Assert.DoesNotThrow(() => _controller.SelectQuest(new DefinitionId(KillQuest)));
            Assert.DoesNotThrow(() => _controller.SetTarget(null));
            Assert.DoesNotThrow(() => _controller.SetLoot(null));
            Assert.That(_controller.RefreshIfChanged(), Is.False);
        }

        // ---- boundary ------------------------------------------------------------------

        [Test]
        public void The_world_ui_holds_no_gameplay_state()
        {
            Assembly ui = typeof(QuestViewData).Assembly;
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
                    Assert.That(held, Does.Not.Contain("MonsterRuntimeState"),
                        type.Name + "." + field.Name + " holds a live monster");
                    Assert.That(held, Does.Not.Contain("LootObjectState"),
                        type.Name + "." + field.Name + " holds a live loot pile");
                }
            }
        }

        [Test]
        public void Quest_and_loot_mutations_happen_only_in_the_controller()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts",
                "*.cs", System.IO.SearchOption.AllDirectories);

            foreach (string file in files)
            {
                string normalized = file.Replace('\\', '/');
                if (normalized.Contains("/Client/UI/QuestUiController.cs")) continue;
                if (normalized.Contains("/Gameplay/")) continue;

                string source = System.IO.File.ReadAllText(file);

                Assert.That(source, Does.Not.Contain("QuestService.TryTurnIn"),
                    normalized + " turns quests in outside the command boundary");
                Assert.That(source, Does.Not.Contain("QuestService.TryAccept"),
                    normalized + " accepts quests outside the command boundary");
                Assert.That(source, Does.Not.Contain("LootPickupService.TryPickUp"),
                    normalized + " takes loot outside the command boundary");
            }
        }

        [Test]
        public void No_duplicate_world_system_was_introduced()
        {
            string[] forbidden =
            {
                "MonsterSystem", "MonsterManager", "MonsterAI", "DropSystem", "DropManager",
                "LootManager", "LootInventory", "LootItem", "QuestManager", "QuestSystem",
                "RewardManager", "KillQuest", "CollectQuest", "TalkQuest",
                "DevilFruitDropSystem", "CardDropSystem", "BossMonsterSystem",
                "TradeItem", "ShopItem"
            };

            Type[] types = typeof(QuestViewData).Assembly.GetTypes()
                .Concat(typeof(QuestService).Assembly.GetTypes())
                .Concat(typeof(MonsterDefinition).Assembly.GetTypes())
                .Concat(typeof(QuestUiController).Assembly.GetTypes())
                .ToArray();

            foreach (Type type in types)
            {
                Assert.That(forbidden, Does.Not.Contain(type.Name),
                    type.FullName + " duplicates something that already exists");
            }
        }

        // ---- content validation --------------------------------------------------------

        [Test]
        public void A_well_formed_monster_drop_table_and_quest_all_pass()
        {
            AddDropTable(CommonTable, new[] { new DropEntry(new DefinitionId(Coin), 1, 5, 0.5f) });

            MonsterDefinition monster = AddMonster("monster.fine", level: 5, experience: 10,
                aggression: MonsterAggressionType.Aggressive, detection: 8f, attackRange: 2f,
                lootTable: CommonTable);

            QuestDefinition quest = AddQuest("quest.fine", new[]
            {
                new QuestObjective(QuestObjectiveType.KillMonster, new DefinitionId(Grunt), 3)
            }, rewards: new[] { new QuestReward(QuestRewardType.Item, new DefinitionId(Hide), 1) });

            DropTableDefinition table;
            DropTables.TryGet(new DefinitionId(CommonTable), out table);

            Assert.That(Check(monster).IsValid, Is.True, Describe(Check(monster)));
            Assert.That(Check(table).IsValid, Is.True, Describe(Check(table)));
            Assert.That(Check(quest).IsValid, Is.True, Describe(Check(quest)));
        }

        [Test]
        public void A_quest_with_no_objectives_is_an_error()
        {
            QuestDefinition quest = AddQuest("quest.empty", new QuestObjective[0]);

            Assert.That(HasError(Check(quest), "could never be completed"), Is.True);
        }

        [Test]
        public void A_quest_requiring_itself_is_an_error()
        {
            QuestDefinition quest = AddQuest("quest.loop", new[]
            {
                new QuestObjective(QuestObjectiveType.KillMonster, new DefinitionId(Grunt), 1)
            }, prerequisites: new[] { new DefinitionId("quest.loop") });

            Assert.That(HasError(Check(quest), "requires itself"), Is.True);
        }

        [Test]
        public void A_reward_naming_nothing_that_exists_is_an_error()
        {
            QuestDefinition quest = AddQuest("quest.ghostreward", new[]
            {
                new QuestObjective(QuestObjectiveType.KillMonster, new DefinitionId(Grunt), 1)
            }, rewards: new[]
            {
                new QuestReward(QuestRewardType.Item, new DefinitionId("item.deleted"), 1)
            });

            Assert.That(HasError(Check(quest), "does not resolve"), Is.True);
        }

        [Test]
        public void An_item_reward_naming_nothing_at_all_is_an_error()
        {
            QuestDefinition quest = AddQuest("quest.blankreward", new[]
            {
                new QuestObjective(QuestObjectiveType.KillMonster, new DefinitionId(Grunt), 1)
            }, rewards: new[] { new QuestReward(QuestRewardType.Item, DefinitionId.None, 1) });

            Assert.That(HasError(Check(quest), "names nothing"), Is.True);
        }

        [Test]
        public void An_objective_requiring_nothing_is_an_error()
        {
            QuestDefinition quest = AddQuest("quest.zero", new[]
            {
                new QuestObjective(QuestObjectiveType.KillMonster, new DefinitionId(Grunt), 0)
            });

            Assert.That(HasError(Check(quest), "already satisfied"), Is.True);
        }

        [Test]
        public void An_impossible_drop_chance_and_quantity_are_errors()
        {
            AddDropTable("drop.bad", new[]
            {
                new DropEntry(new DefinitionId(Coin), 0, 0, chance: 2f)
            });

            DropTableDefinition table;
            DropTables.TryGet(new DefinitionId("drop.bad"), out table);

            ValidationReport report = Check(table);

            Assert.That(HasError(report, "outside zero to one"), Is.True);
            Assert.That(HasError(report, "not a quantity"), Is.True);
        }

        [Test]
        public void A_level_band_no_killer_can_be_inside_is_an_error()
        {
            AddDropTable("drop.band", new[]
            {
                new DropEntry(new DefinitionId(Coin), 1, 1, minKillerLevel: 30, maxKillerLevel: 10)
            });

            DropTableDefinition table;
            DropTables.TryGet(new DefinitionId("drop.band"), out table);

            Assert.That(HasError(Check(table), "no killer can be inside"), Is.True);
        }

        [Test]
        public void A_drop_entry_naming_a_deleted_item_is_an_error()
        {
            AddDropTable("drop.ghost", new[]
            {
                new DropEntry(new DefinitionId("item.deleted"), 1, 1)
            });

            DropTableDefinition table;
            DropTables.TryGet(new DefinitionId("drop.ghost"), out table);

            Assert.That(HasError(Check(table), "does not resolve"), Is.True);
        }

        [Test]
        public void A_monster_with_a_negative_reward_or_range_is_an_error()
        {
            MonsterDefinition monster = AddMonster("monster.negative", level: 5,
                experience: -10, detection: -5f);

            ValidationReport report = Check(monster);

            Assert.That(HasError(report, "Experience reward is negative"), Is.True);
            Assert.That(HasError(report, "range or speed is negative"), Is.True);
        }

        [Test]
        public void An_aggressive_monster_that_notices_nothing_warns()
        {
            MonsterDefinition monster = AddMonster("monster.blind", level: 5,
                aggression: MonsterAggressionType.Aggressive, detection: 0f);

            ValidationReport report = Check(monster);

            Assert.That(report.IsValid, Is.True, "inert is not wrong");
            Assert.That(report.WarningCount, Is.GreaterThan(0));
        }

        [Test]
        public void A_leash_shorter_than_the_reach_warns()
        {
            MonsterDefinition monster = AddMonster("monster.tethered", level: 5,
                aggression: MonsterAggressionType.Aggressive, detection: 10f,
                attackRange: 5f, leash: 1f);

            Assert.That(Check(monster).WarningCount, Is.GreaterThan(0),
                "it could never reach a target");
        }

        [Test]
        public void An_empty_drop_table_warns_rather_than_failing()
        {
            AddDropTable("drop.empty", new DropEntry[0]);

            DropTableDefinition table;
            DropTables.TryGet(new DefinitionId("drop.empty"), out table);

            ValidationReport report = Check(table);

            Assert.That(report.IsValid, Is.True);
            Assert.That(report.WarningCount, Is.GreaterThan(0));
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

        /// <summary>
        /// A lookup spanning every fixture registry.
        /// </summary>
        /// <remarks>Reference checking is inherently cross-type -- a quest names a monster
        /// and an item -- which is exactly why <see cref="IDefinitionLookup"/> exists
        /// separately from a typed registry.</remarks>
        private sealed class TestLookup : IDefinitionLookup
        {
            private readonly IDefinitionLookup[] _sources;

            public TestLookup(params IDefinitionLookup[] sources)
            {
                _sources = sources ?? new IDefinitionLookup[0];
            }

            public bool Contains(DefinitionId id)
            {
                for (int i = 0; i < _sources.Length; i++)
                {
                    if (_sources[i] != null && _sources[i].Contains(id)) return true;
                }

                return false;
            }
        }
    }
}
