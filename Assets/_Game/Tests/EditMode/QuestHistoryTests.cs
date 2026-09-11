using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.UI;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// A quest you have finished is still a quest you did.
    /// </summary>
    /// <remarks>
    /// <b>The report this exists for.</b> "ทำไมไม่มีเควสขึ้น" -- why is no quest showing.
    /// Nothing was broken: the character had handed in the only quest in the game, it is not
    /// repeatable, and so Available was empty, Active was empty, and the guide wore no mark.
    /// Every one of those was correct, and together they were indistinguishable from a quest
    /// system that had lost the player's progress.
    ///
    /// The log had kept the completed quest all along -- the migration says so in its own
    /// notes -- and there was simply nowhere on screen it could be seen. So the fix is a
    /// third tab, and these are the tests that it shows what it claims to and that finishing
    /// a quest never again empties the journal completely.
    /// </remarks>
    [TestFixture]
    public sealed class QuestHistoryTests
    {
        private static readonly DefinitionId Finished = new DefinitionId("quest.done");
        private static readonly DefinitionId Ongoing = new DefinitionId("quest.doing");

        private static QuestViewData View(DefinitionId quest, QuestStatusView status)
        {
            // No name key: the point of these tests is the tab and the wording around it, and
            // an unnamed quest exercises the readable-identifier fallback at the same time.
            return QuestViewData.From(quest, LocalizationKey.None, LocalizationKey.None,
                QuestType.Normal, status, 0, false,
                new QuestObjectiveViewData[0], new QuestRewardViewData[0]);
        }

        private static QuestListView Panel()
        {
            var host = new GameObject("Journal", typeof(RectTransform));

            var view = host.AddComponent<QuestListView>();

            view.EnsureVisuals();

            return view;
        }

        private static void Drop(QuestListView view)
        {
            if (view != null) Object.DestroyImmediate(view.gameObject);
        }

        // ---- the tab exists and shows the right list ------------------------------------------

        [Test]
        public void TheJournalHasSomewhereToShowWhatHasBeenFinished()
        {
            Assert.That(System.Enum.IsDefined(typeof(QuestListTab), QuestListTab.Completed),
                Is.True);
        }

        [Test]
        public void TheCompletedTabListsTheFinishedQuestAndNotTheOngoingOne()
        {
            QuestListView view = Panel();

            try
            {
                view.Bind(new List<QuestViewData>(),
                    new List<QuestViewData> { View(Ongoing, QuestStatusView.Active) },
                    new List<QuestViewData> { View(Finished, QuestStatusView.Completed) });

                view.Show(QuestListTab.Completed);

                Assert.That(view.ListText, Does.Contain("Done"),
                    "quest.done reads as 'Done' through the readable fallback");
                Assert.That(view.ListText, Does.Not.Contain("Doing"));
            }
            finally
            {
                Drop(view);
            }
        }

        [Test]
        public void FinishingTheOnlyQuestNoLongerLeavesAnEmptyJournal()
        {
            // The exact state the report was made in: nothing available, nothing active, one
            // quest completed. Before the tab existed every pane read "Nothing here."
            QuestListView view = Panel();

            try
            {
                view.Bind(new List<QuestViewData>(), new List<QuestViewData>(),
                    new List<QuestViewData> { View(Finished, QuestStatusView.Completed) });

                view.Show(QuestListTab.Available);
                Assert.That(view.ListText, Is.EqualTo("Nothing here."));

                view.Show(QuestListTab.Completed);

                Assert.That(view.ListText, Is.Not.EqualTo("Nothing here."),
                    "the player has no way to tell 'all done' from 'progress lost'");
            }
            finally
            {
                Drop(view);
            }
        }

        [Test]
        public void ACompletedQuestCannotBeAcceptedFromTheJournal()
        {
            QuestListView view = Panel();

            try
            {
                view.Bind(new List<QuestViewData>(), new List<QuestViewData>(),
                    new List<QuestViewData> { View(Finished, QuestStatusView.Completed) });

                view.CanAcceptHere = _ => true;

                view.Show(QuestListTab.Completed);

                Assert.That(view.CanAccept, Is.False,
                    "history is read, not taken");
            }
            finally
            {
                Drop(view);
            }
        }

        [Test]
        public void TheCompletedTabSaysTheQuestIsFinishedRatherThanWhereToGo()
        {
            // The Available tab tells a player who to speak to and the Active tab tells them
            // who to return to. History has no next step, and saying nothing at all is how a
            // pane reads as unfinished.
            QuestListView view = Panel();

            try
            {
                view.Bind(new List<QuestViewData>(), new List<QuestViewData>(),
                    new List<QuestViewData> { View(Finished, QuestStatusView.Completed) });

                view.GiverName = _ => "Harbor Guide";

                view.Show(QuestListTab.Completed);

                Assert.That(view.DetailText, Does.Contain("Finished"));
                Assert.That(view.DetailText, Does.Not.Contain("Speak with Harbor Guide"));
                Assert.That(view.DetailText, Does.Not.Contain("Return to Harbor Guide"));
            }
            finally
            {
                Drop(view);
            }
        }

        [Test]
        public void TheTitleNamesTheTabBeingLookedAt()
        {
            QuestListView view = Panel();

            try
            {
                view.Bind(new List<QuestViewData>(), new List<QuestViewData>(),
                    new List<QuestViewData> { View(Finished, QuestStatusView.Completed) });

                view.Show(QuestListTab.Completed);

                Assert.That(UiText.Of(view.Text, UiStrings.QuestTitleCompleted),
                    Is.EqualTo("QUESTS - COMPLETED"));
            }
            finally
            {
                Drop(view);
            }
        }

        [Test]
        public void ACallerWithNoHistoryToShowStillWorks()
        {
            // The two-argument Bind is what every screen and test wrote before the tab
            // existed. It must keep compiling and must show an empty third tab rather than
            // the active list under a Completed heading.
            QuestListView view = Panel();

            try
            {
                view.Bind(new List<QuestViewData>(),
                    new List<QuestViewData> { View(Ongoing, QuestStatusView.Active) });

                view.Show(QuestListTab.Completed);

                Assert.That(view.Current, Is.Empty);
                Assert.That(view.ListText, Is.EqualTo("Nothing here."));
            }
            finally
            {
                Drop(view);
            }
        }

        // ---- the list is a list, not a picture of one ------------------------------------------

        [Test]
        public void EveryQuestInTheListCanBeClicked()
        {
            // The report: "มันคลิกดูเควสที่ 2 ที่รับมาไม่ได้" -- the second quest could not be
            // opened. The list column was a single Text label. It looked like a list, one
            // quest per line with the selected one marked, and not one line was a click
            // target: Select(int) was public and nothing in the view ever called it. With a
            // single quest in a tab that is invisible; with two, the second is unreachable.
            QuestListView view = Panel();

            try
            {
                view.Bind(new List<QuestViewData>(),
                    new List<QuestViewData>
                    {
                        View(Ongoing, QuestStatusView.Active),
                        View(Finished, QuestStatusView.Active)
                    });

                view.Show(QuestListTab.Active);

                var buttons = new List<UnityEngine.UI.Button>();

                foreach (UnityEngine.UI.Button button in
                    view.GetComponentsInChildren<UnityEngine.UI.Button>(true))
                {
                    if (button.gameObject.name.StartsWith("Row")) buttons.Add(button);
                }

                Assert.That(buttons.Count, Is.GreaterThanOrEqualTo(2),
                    "there is no way to click the second quest");

                Assert.That(buttons[0].gameObject.activeSelf, Is.True);
                Assert.That(buttons[1].gameObject.activeSelf, Is.True);
            }
            finally
            {
                Drop(view);
            }
        }

        [Test]
        public void ClickingTheSecondQuestDescribesTheSecondQuest()
        {
            QuestListView view = Panel();

            try
            {
                view.Bind(new List<QuestViewData>(),
                    new List<QuestViewData>
                    {
                        View(Ongoing, QuestStatusView.Active),
                        View(Finished, QuestStatusView.Active)
                    });

                view.Show(QuestListTab.Active);

                // It opens on the first, which is what made the defect invisible.
                Assert.That(view.Selected, Is.EqualTo(Ongoing));

                view.Select(1);

                Assert.That(view.Selected, Is.EqualTo(Finished));
                Assert.That(view.DetailText, Does.Contain("Done"));
            }
            finally
            {
                Drop(view);
            }
        }

        [Test]
        public void ClosingTheJournalTakesTheRowsWithIt()
        {
            // Rows are buttons now, and a button left active over the world after the panel
            // closed is an invisible click target nothing would ever explain.
            QuestListView view = Panel();

            try
            {
                view.Bind(new List<QuestViewData>(),
                    new List<QuestViewData> { View(Ongoing, QuestStatusView.Active) });

                view.Show(QuestListTab.Active);
                view.Close();

                foreach (UnityEngine.UI.Button button in
                    view.GetComponentsInChildren<UnityEngine.UI.Button>(true))
                {
                    if (!button.gameObject.name.StartsWith("Row")) continue;

                    Assert.That(button.gameObject.activeSelf, Is.False,
                        "a row survived the journal closing");
                }
            }
            finally
            {
                Drop(view);
            }
        }

        [Test]
        public void TheJournalStillDoesNotFreezeTheWorldOnAnyTab()
        {
            // Asked for directly, and easy to undo by accident while adding a tab.
            QuestListView view = Panel();

            try
            {
                view.Show(QuestListTab.Completed);

                Assert.That(view.BlocksTheWorld, Is.False);
            }
            finally
            {
                Drop(view);
            }
        }
    }
}
