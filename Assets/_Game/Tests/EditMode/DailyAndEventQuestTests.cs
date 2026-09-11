using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Daily quests come back at midnight, bands retire, and a festival opens and closes.
    /// </summary>
    /// <remarks>
    /// <b>What replaced what.</b> There was one repeatable quest, offered for ever, at every
    /// level. It solved an empty journal and created a different problem: a level-one errand
    /// that a level-thirty player still has to read past every time they open the book.
    /// Bands retire a daily when it stops being worth doing, and the next band takes over.
    ///
    /// <b>The date never comes from a machine clock.</b> Completion days are stamped by
    /// MySQL and compared against a day number MySQL also supplied. A world server reading
    /// its own clock would roll dailies over at whatever midnight that machine believes in,
    /// which is the timezone bug this project already had once between MySQL and PHP.
    /// <see cref="ServerDay"/> and <see cref="WorldDayClock"/> exist to keep that from
    /// happening again, and the tests below pin both.
    /// </remarks>
    [TestFixture]
    public sealed class DailyAndEventQuestTests
    {
        private const string CataloguePath =
            "Assets/_Game/Data/Production/WorldContentCatalogue.asset";

        private readonly List<Object> _made = new List<Object>();

        [TearDown]
        public void CleanUp()
        {
            foreach (Object o in _made) if (o != null) Object.DestroyImmediate(o);
            _made.Clear();
        }

        private QuestDefinition Quest(string id, QuestType type, int levelMin, int levelMax,
            string from = "", string until = "")
        {
            var q = ScriptableObject.CreateInstance<QuestDefinition>();
            _made.Add(q);

            var so = new SerializedObject(q);
            so.FindProperty("_id").FindPropertyRelative("_value").stringValue = id;
            so.FindProperty("_questType").enumValueIndex = (int)type;
            so.FindProperty("_levelRequirement").intValue = levelMin;
            so.FindProperty("_levelMaximum").intValue = levelMax;
            so.FindProperty("_availableFrom").stringValue = from;
            so.FindProperty("_availableUntil").stringValue = until;

            var objs = so.FindProperty("_objectives");
            objs.arraySize = 1;
            var o = objs.GetArrayElementAtIndex(0);
            o.FindPropertyRelative("_type").enumValueIndex = (int)QuestObjectiveType.KillMonster;
            o.FindPropertyRelative("_target").FindPropertyRelative("_value").stringValue =
                "monster.training_slime";
            o.FindPropertyRelative("_requiredAmount").intValue = 3;

            so.ApplyModifiedPropertiesWithoutUndo();
            return q;
        }

        private static DefinitionRegistry<QuestDefinition> RegistryOf(params QuestDefinition[] quests)
        {
            return new DefinitionRegistry<QuestDefinition>(quests);
        }

        private static CharacterQuestState FinishedOn(DefinitionId quest, int day)
        {
            var state = new CharacterQuestState(new CharacterId("probe"));
            state.AdoptAuthoritative(quest, QuestStatus.Completed, new[] { 3 }, day);
            return state;
        }

        // ---- day numbers agree with the database ------------------------------------------

        [Test]
        public void TheDayNumberingMatchesTheDatabasesOwn()
        {
            // Measured against MySQL rather than remembered: TO_DAYS('1970-01-01') is 719528
            // on the deployment, and these three were read back from it. A wrong epoch would
            // shift every festival by years and nothing else would notice.
            Assert.That(ServerDay.FromDate(1970, 1, 1), Is.EqualTo(719528));
            Assert.That(ServerDay.FromDate(2000, 1, 1), Is.EqualTo(730485));
            Assert.That(ServerDay.FromDate(2026, 9, 11), Is.EqualTo(740235));
            Assert.That(ServerDay.FromDate(2026, 12, 25), Is.EqualTo(740340));
        }

        [Test]
        public void AnAuthoredDateThatIsNonsenseIsNeverRatherThanAThrow()
        {
            int day;

            Assert.That(ServerDay.TryParse("2026-02-30", out day), Is.False,
                "a date that does not exist must not become a real day");
            Assert.That(ServerDay.TryParse("not a date", out day), Is.False);
            Assert.That(ServerDay.TryParse("", out day), Is.False);
            Assert.That(day, Is.EqualTo(ServerDay.Never));
        }

        // ---- the clock survives midnight ---------------------------------------------------

        [Test]
        public void TheDayRollsOverWhenTheDatabasesBoundaryPasses()
        {
            // A world server runs for days. It is told the day once, at login, and must
            // still be right tomorrow without ever asking its own machine what date it is.
            double now = 1000.0;
            var clock = new WorldDayClock { ElapsedSeconds = () => now };

            clock.Sync(740235, 3600);              // an hour to go

            Assert.That(clock.Today, Is.EqualTo(740235));

            now += 3599;
            Assert.That(clock.Today, Is.EqualTo(740235), "still today one second before");

            now += 2;
            Assert.That(clock.Today, Is.EqualTo(740236), "midnight passed");

            now += 86400 * 3;
            Assert.That(clock.Today, Is.EqualTo(740239), "three more days of uptime");
        }

        [Test]
        public void AStaleReplyCannotWalkTheDayBackwards()
        {
            // Two characters logging in either side of midnight would otherwise let the
            // older answer re-open a daily somebody has already claimed today.
            double now = 1000.0;
            var clock = new WorldDayClock { ElapsedSeconds = () => now };

            clock.Sync(740235, 10);
            now += 20;

            Assert.That(clock.Today, Is.EqualTo(740236));

            clock.Sync(740235, 86400);

            Assert.That(clock.Today, Is.EqualTo(740236),
                "a late reply from before midnight must not undo the rollover");
        }

        [Test]
        public void AClockNobodyHasSyncedSaysItDoesNotKnow()
        {
            var clock = new WorldDayClock { ElapsedSeconds = () => 0.0 };

            Assert.That(clock.IsKnown, Is.False);
            Assert.That(clock.Today, Is.EqualTo(0));
        }

        // ---- a daily comes back tomorrow ----------------------------------------------------

        [Test]
        public void ADailyDoneTodayIsRefusedUntilTomorrow()
        {
            var quest = Quest("quest.daily", QuestType.Daily, 1, 0);
            var registry = RegistryOf(quest);
            var id = new DefinitionId("quest.daily");

            CharacterQuestState state = FinishedOn(id, 740235);

            var today = new QuestService.Context(registry, null, 5, default, 740235);

            Assert.That(QuestService.CanAccept(state, id, today),
                Is.EqualTo(QuestRejection.AlreadyDoneToday));
        }

        [Test]
        public void ADailyDoneYesterdayCanBeTakenAgain()
        {
            var quest = Quest("quest.daily", QuestType.Daily, 1, 0);
            var registry = RegistryOf(quest);
            var id = new DefinitionId("quest.daily");

            CharacterQuestState state = FinishedOn(id, 740234);

            var today = new QuestService.Context(registry, null, 5, default, 740235);

            Assert.That(QuestService.CanAccept(state, id, today),
                Is.EqualTo(QuestRejection.None));
        }

        [Test]
        public void ADailyStaysFinishedWhenNobodyHasSaidWhatDayItIs()
        {
            // The safe direction. Guessing would hand out a second reward; refusing only
            // withholds one the player can claim once the wire is fixed.
            var quest = Quest("quest.daily", QuestType.Daily, 1, 0);
            var registry = RegistryOf(quest);
            var id = new DefinitionId("quest.daily");

            CharacterQuestState state = FinishedOn(id, 740235);

            var noDate = new QuestService.Context(registry, null, 5);

            Assert.That(QuestService.CanAccept(state, id, noDate),
                Is.EqualTo(QuestRejection.AlreadyCompleted));
        }

        [Test]
        public void AOneTimeQuestIsNotResetByTheNextDay()
        {
            var quest = Quest("quest.once", QuestType.Normal, 1, 0);
            var registry = RegistryOf(quest);
            var id = new DefinitionId("quest.once");

            CharacterQuestState state = FinishedOn(id, 740200);

            var today = new QuestService.Context(registry, null, 5, default, 740235);

            Assert.That(QuestService.CanAccept(state, id, today),
                Is.EqualTo(QuestRejection.AlreadyCompleted));
        }

        [Test]
        public void TakingADailyAgainClearsYesterdaysStamp()
        {
            // Otherwise finishing it again would carry yesterday's day and the day after
            // would refuse it.
            var quest = Quest("quest.daily", QuestType.Daily, 1, 0);
            var registry = RegistryOf(quest);
            var id = new DefinitionId("quest.daily");

            CharacterQuestState state = FinishedOn(id, 740234);

            var today = new QuestService.Context(registry, null, 5, default, 740235);

            QuestResult taken = QuestService.TryAccept(state, id, today);

            Assert.That(taken.IsAccepted, Is.True, taken.Reason.ToString());

            QuestProgress progress;
            state.TryGet(id, out progress);

            Assert.That(progress.CompletedDay, Is.Zero);
            Assert.That(progress.Status, Is.EqualTo(QuestStatus.Active));
        }

        // ---- bands retire ---------------------------------------------------------------------

        [Test]
        public void ABandRefusesBothSidesOfItself()
        {
            var quest = Quest("quest.band", QuestType.Daily, 10, 19);
            var registry = RegistryOf(quest);
            var id = new DefinitionId("quest.band");
            var state = new CharacterQuestState(new CharacterId("probe"));

            Assert.That(QuestService.CanAccept(state, id,
                    new QuestService.Context(registry, null, 9, default, 740235)),
                Is.EqualTo(QuestRejection.LevelTooLow));

            Assert.That(QuestService.CanAccept(state, id,
                    new QuestService.Context(registry, null, 10, default, 740235)),
                Is.EqualTo(QuestRejection.None));

            Assert.That(QuestService.CanAccept(state, id,
                    new QuestService.Context(registry, null, 19, default, 740235)),
                Is.EqualTo(QuestRejection.None));

            Assert.That(QuestService.CanAccept(state, id,
                    new QuestService.Context(registry, null, 20, default, 740235)),
                Is.EqualTo(QuestRejection.LevelTooHigh),
                "an outgrown daily must leave the board, or the journal fills with errands "
                + "nobody would do");
        }

        [Test]
        public void ACeilingOfZeroMeansNoCeiling()
        {
            var quest = Quest("quest.open", QuestType.Normal, 1, 0);
            var registry = RegistryOf(quest);
            var state = new CharacterQuestState(new CharacterId("probe"));

            Assert.That(QuestService.CanAccept(state, new DefinitionId("quest.open"),
                    new QuestService.Context(registry, null, 99, default, 740235)),
                Is.EqualTo(QuestRejection.None));
        }

        // ---- a festival opens and closes ---------------------------------------------------------

        [Test]
        public void AnEventIsShutBeforeAndAfterItsWindow()
        {
            var quest = Quest("quest.festival", QuestType.Event, 1, 0, "2026-12-20", "2027-01-05");
            var registry = RegistryOf(quest);
            var id = new DefinitionId("quest.festival");
            var state = new CharacterQuestState(new CharacterId("probe"));

            int before = ServerDay.FromDate(2026, 12, 19);
            int opening = ServerDay.FromDate(2026, 12, 20);
            int closing = ServerDay.FromDate(2027, 1, 5);
            int after = ServerDay.FromDate(2027, 1, 6);

            Assert.That(QuestService.CanAccept(state, id,
                    new QuestService.Context(registry, null, 5, default, before)),
                Is.EqualTo(QuestRejection.OutsideEventWindow));

            Assert.That(QuestService.CanAccept(state, id,
                    new QuestService.Context(registry, null, 5, default, opening)),
                Is.EqualTo(QuestRejection.None), "the window opens on its first day");

            Assert.That(QuestService.CanAccept(state, id,
                    new QuestService.Context(registry, null, 5, default, closing)),
                Is.EqualTo(QuestRejection.None), "and includes its last day");

            Assert.That(QuestService.CanAccept(state, id,
                    new QuestService.Context(registry, null, 5, default, after)),
                Is.EqualTo(QuestRejection.OutsideEventWindow));
        }

        [Test]
        public void AnEventWithNoDateKnownIsShutRatherThanOpen()
        {
            // A festival that opened because the wire was broken would be a festival running
            // all year.
            var quest = Quest("quest.festival", QuestType.Event, 1, 0, "2026-12-20", "2027-01-05");
            var registry = RegistryOf(quest);
            var state = new CharacterQuestState(new CharacterId("probe"));

            Assert.That(QuestService.CanAccept(state, new DefinitionId("quest.festival"),
                    new QuestService.Context(registry, null, 5)),
                Is.EqualTo(QuestRejection.OutsideEventWindow));
        }

        [Test]
        public void AOneSidedWindowIsAllowed()
        {
            // "From this date onward" is a legitimate thing to author -- content that goes
            // live and stays live.
            var quest = Quest("quest.launch", QuestType.Normal, 1, 0, "2026-12-20", "");
            var registry = RegistryOf(quest);
            var id = new DefinitionId("quest.launch");
            var state = new CharacterQuestState(new CharacterId("probe"));

            Assert.That(QuestService.CanAccept(state, id,
                    new QuestService.Context(registry, null, 5,
                        default, ServerDay.FromDate(2026, 12, 19))),
                Is.EqualTo(QuestRejection.OutsideEventWindow));

            Assert.That(QuestService.CanAccept(state, id,
                    new QuestService.Context(registry, null, 5,
                        default, ServerDay.FromDate(2030, 1, 1))),
                Is.EqualTo(QuestRejection.None));
        }

        // ---- the shipped content ----------------------------------------------------------------

        [Test]
        public void NothingShipsAsEndlesslyRepeatableAnyMore()
        {
            // Asked for directly: the old always-available quest is gone, replaced by
            // dailies that retire. A repeatable quest with no ceiling and no reset is the
            // thing that made the journal fill up.
            var catalogue = AssetDatabase.LoadAssetAtPath<WorldContentCatalogue>(CataloguePath);

            var endless = new List<string>();

            foreach (QuestDefinition quest in catalogue.BuildQuests().All)
            {
                if (quest.Repeatable && !quest.ResetsDaily) endless.Add(quest.Id.Value);
            }

            Assert.That(endless, Is.Empty,
                "these can be taken again immediately, for ever: " + string.Join(", ", endless));
        }

        [Test]
        public void TheDailyBandsCoverTheLevelsWithoutOverlapping()
        {
            // Two dailies offered to the same level is two errands where there should be
            // one; a gap between bands is a level range with no daily at all.
            var catalogue = AssetDatabase.LoadAssetAtPath<WorldContentCatalogue>(CataloguePath);

            var bands = new List<QuestDefinition>();

            foreach (QuestDefinition quest in catalogue.BuildQuests().All)
            {
                if (quest.ResetsDaily) bands.Add(quest);
            }

            Assert.That(bands, Is.Not.Empty, "no daily quest ships at all");

            bands.Sort((a, b) => a.LevelRequirement.CompareTo(b.LevelRequirement));

            for (var i = 0; i < bands.Count; i++)
            {
                Assert.That(bands[i].LevelMaximum, Is.GreaterThan(0),
                    bands[i].Id.Value + " is a daily with no ceiling, so it never retires");

                Assert.That(bands[i].LevelMaximum,
                    Is.GreaterThanOrEqualTo(bands[i].LevelRequirement),
                    bands[i].Id.Value + " has a ceiling below its floor and can never be taken");

                if (i == 0) continue;

                Assert.That(bands[i].LevelRequirement,
                    Is.EqualTo(bands[i - 1].LevelMaximum + 1),
                    bands[i - 1].Id.Value + " and " + bands[i].Id.Value
                    + " overlap or leave a gap between them");
            }
        }

        [Test]
        public void EveryAuthoredEventWindowParses()
        {
            // A typo in a date makes a festival that never opens, silently. This is the
            // only place that would ever say so.
            var catalogue = AssetDatabase.LoadAssetAtPath<WorldContentCatalogue>(CataloguePath);

            var broken = new List<string>();

            foreach (QuestDefinition quest in catalogue.BuildQuests().All)
            {
                if (!quest.HasEventWindow) continue;

                int from = quest.AvailableFromDay;
                int until = quest.AvailableUntilDay;

                if (from != 0 && until != 0 && until < from)
                {
                    broken.Add(quest.Id.Value + " closes before it opens");
                }
            }

            Assert.That(broken, Is.Empty, string.Join(", ", broken));
        }
    }
}
