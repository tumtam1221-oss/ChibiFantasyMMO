using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The two properties the first-playable loop rests on that the other tests do not pin:
    /// a monster's defeat advances a kill quest exactly once no matter how the defeat is
    /// reported, and that advance is the defeat itself, not the loot the defeat happened to
    /// drop.
    /// </summary>
    /// <remarks>
    /// <b>Why here and not in the quest tests.</b> <c>QuestService</c> is told "a slime
    /// died" and counts; whether a slime dies exactly once is <c>MonsterDefeatService</c>'s
    /// guard, and the loop only holds if the two meet correctly. The server wires them by
    /// hanging <c>CharacterQuestAuthority.ReportKill</c> off the one call that claims a
    /// defeat (asserted in <c>QuestFoundationTests</c>). This exercises that seam directly:
    /// advance the quest only when the claim succeeds, exactly as the production hook does,
    /// and prove a replayed defeat cannot move the counter.
    ///
    /// <b>Kill, not drop.</b> The gel is a chance drop; a loop that advanced on the gel
    /// would stall whenever it did not roll and reads to a player as "the quest is broken".
    /// These pin that the counter follows the defeat -- it caps at three however much loot
    /// falls, a replay adds nothing, and a defeat that drops nothing at all still counts.
    /// </remarks>
    internal sealed class FirstHuntLoopIntegrationTests : MonsterTestBase
    {
        private const string GelHunt = "quest.first-hunt-gel";
        private const string PoorHunt = "quest.first-hunt-poor";
        private const string Gel = "item.slime-gel-loop";
        private const string GelSlime = "monster.slime-with-gel";
        private const string GelTable = "drop.slime-with-gel";

        private QuestService.Context QuestContext()
        {
            return new QuestService.Context(Quests, Items, 10, Owner);
        }

        /// <summary>Guaranteed drops of a fixed quantity, so the loot is not left to chance.</summary>
        private DropResolver.Context Drops()
        {
            return new DropResolver.Context(Items, DropTables, new AlwaysDrops(),
                new AlwaysDrops());
        }

        /// <summary>The production seam: advance the quest only on a claimed defeat.</summary>
        private int ReportDefeat(MonsterRuntimeState monster, CharacterQuestState quests,
            List<LootResult> loot)
        {
            MonsterDefeatResult defeat = MonsterDefeatService.Resolve(monster,
                InstanceId.New(), Drops(), loot);

            if (!defeat.IsClaimed) return 0;

            return QuestService.ReportProgress(quests, QuestObjectiveType.KillMonster,
                defeat.MonsterDefinitionId, 1, QuestContext());
        }

        [SetUp]
        public void BuildTheHunts()
        {
            AddItem(Gel);
            AddDropTable(GelTable, new[] { new DropEntry(new DefinitionId(Gel), 1, 2) });
            AddMonster(GelSlime, level: 5, experience: 12, lootTable: GelTable);

            AddQuest(GelHunt, new[]
            {
                new QuestObjective(QuestObjectiveType.KillMonster, new DefinitionId(GelSlime), 3),
            }, rewards: new[]
            {
                new QuestReward(QuestRewardType.Experience, default, 100),
                new QuestReward(QuestRewardType.Item, new DefinitionId(Gel), 1),
            });

            // Grunt (from the base) ships with no loot table, so a defeat of it drops
            // nothing -- the fixture for "the kill counts even when nothing falls".
            AddQuest(PoorHunt, new[]
            {
                new QuestObjective(QuestObjectiveType.KillMonster, new DefinitionId(Grunt), 3),
            }, rewards: new[]
            {
                new QuestReward(QuestRewardType.Experience, default, 100),
            });
        }

        [Test]
        public void ThreeDefeatsFinishTheHuntAndAFourthDoesNotOvercount()
        {
            var quests = new CharacterQuestState(new CharacterId("char-hunter"));

            Assert.That(QuestService.TryAccept(quests, new DefinitionId(GelHunt), QuestContext())
                .IsAccepted, Is.True, "fixture: the hunt was not accepted");

            var loot = new List<LootResult>();

            for (var kill = 1; kill <= 3; kill++)
            {
                MonsterRuntimeState slime = Spawn(GelSlime);
                slime.ApplyHealthDelta(-slime.MaxHealth);

                Assert.That(ReportDefeat(slime, quests, loot), Is.EqualTo(1),
                    "defeat " + kill + " did not advance the hunt once");

                quests.TryGet(new DefinitionId(GelHunt), out QuestProgress progress);

                Assert.That(progress.CountAt(0), Is.EqualTo(kill),
                    "defeat " + kill + " did not count exactly once");
                Assert.That(progress.Status == QuestStatus.ReadyToComplete,
                    Is.EqualTo(kill == 3),
                    kill == 3 ? "three defeats did not ready the hunt"
                        : "the hunt readied after only " + kill + " defeats");
            }

            // Loot has been falling the whole time; the objective still caps at three.
            Assert.That(loot.Count, Is.GreaterThan(0), "the fixture never dropped its gel");

            MonsterRuntimeState extra = Spawn(GelSlime);
            extra.ApplyHealthDelta(-extra.MaxHealth);

            Assert.That(ReportDefeat(extra, quests, loot), Is.EqualTo(0),
                "a defeat past 3/3 still advanced the hunt");

            quests.TryGet(new DefinitionId(GelHunt), out QuestProgress capped);

            Assert.That(capped.CountAt(0), Is.EqualTo(3), "the counter ran past the objective");
            Assert.That(capped.Status, Is.EqualTo(QuestStatus.ReadyToComplete));
        }

        [Test]
        public void AReplayedDefeatCannotAdvanceTheHuntTwice()
        {
            var quests = new CharacterQuestState(new CharacterId("char-hunter"));

            QuestService.TryAccept(quests, new DefinitionId(GelHunt), QuestContext());

            MonsterRuntimeState slime = Spawn(GelSlime);
            slime.ApplyHealthDelta(-slime.MaxHealth);

            var loot = new List<LootResult>();

            // Two reports of the one death -- a doubled packet, a second killing blow in the
            // same frame. Only the first claims the defeat, so only the first advances.
            int first = ReportDefeat(slime, quests, loot);
            int replay = ReportDefeat(slime, quests, loot);

            Assert.That(first, Is.EqualTo(1), "the genuine defeat did not advance the hunt");
            Assert.That(replay, Is.EqualTo(0), "a replayed defeat advanced the hunt again");

            quests.TryGet(new DefinitionId(GelHunt), out QuestProgress progress);

            Assert.That(progress.CountAt(0), Is.EqualTo(1),
                "one slime, killed once, counted twice");
        }

        [Test]
        public void TheHuntAdvancesOnTheDefeatEvenWhenNothingDrops()
        {
            var quests = new CharacterQuestState(new CharacterId("char-hunter"));

            QuestService.TryAccept(quests, new DefinitionId(PoorHunt), QuestContext());

            var loot = new List<LootResult>();

            for (var kill = 1; kill <= 3; kill++)
            {
                MonsterRuntimeState slime = Spawn(Grunt);
                slime.ApplyHealthDelta(-slime.MaxHealth);

                Assert.That(ReportDefeat(slime, quests, loot), Is.EqualTo(1),
                    "defeat " + kill + " with no drop did not count");
            }

            Assert.That(loot, Is.Empty, "the fixture dropped something; it must not for this test");

            quests.TryGet(new DefinitionId(PoorHunt), out QuestProgress progress);

            Assert.That(progress.Status, Is.EqualTo(QuestStatus.ReadyToComplete),
                "three defeats that dropped nothing did not finish the hunt: kill progress "
                + "is coupled to the drop");
        }

        /// <summary>Always succeeds, and always picks the low end of a range.</summary>
        private sealed class AlwaysDrops : IRandomResultSource, IRandomRangeSource
        {
            public bool Succeeds(float successChance) => true;

            public int Range(int minInclusive, int maxInclusive) => minInclusive;
        }
    }
}
