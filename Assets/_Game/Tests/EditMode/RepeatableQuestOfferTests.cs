using ChibiFantasy.Client.UI;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.UI;
using NUnit.Framework;
using UnityEditor;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// A quest being offered shows what taking it would mean, not what the last run left.
    /// </summary>
    /// <remarks>
    /// <b>The report.</b> "ยังไม่ได้กดรับเควส ... ยังไม่ได้ไปกำจัดเลย แต่มันขึ้น 5/5 แล้ว" -- the
    /// quest had not been accepted, nothing had been killed for it, and the panel already
    /// read 5 / 5.
    ///
    /// <b>What was actually happening.</b> Harbour Patrol is repeatable, and a repeatable
    /// quest keeps its counters after it is handed in -- on purpose, because the log is a
    /// record of what happened and prerequisites read it. The offer panel then built its view
    /// straight from that log, so the previous run's finished counters were drawn onto an
    /// offer for a fresh one. Accepting resets them, so the numbers were only ever wrong on
    /// the screen that asked the player to accept: the one place it reads as the quest having
    /// completed itself.
    ///
    /// <b>Why a whole fixture for it.</b> Because the same mistake is available everywhere a
    /// quest is drawn before it is taken -- the journal's Available tab has exactly the same
    /// shape -- and because the quest that exposed it is the one quest in the game a player
    /// is meant to run over and over.
    /// </remarks>
    [TestFixture]
    public sealed class RepeatableQuestOfferTests
    {
        private const string CataloguePath =
            "Assets/_Game/Data/Production/WorldContentCatalogue.asset";

        private const string Repeatable = "quest.harbor_daily_patrol";

        private WorldContentCatalogue _content;
        private WorldViewAdapter.Context _context;

        [SetUp]
        public void LoadShippedContent()
        {
            _content = AssetDatabase.LoadAssetAtPath<WorldContentCatalogue>(CataloguePath);

            Assert.That(_content, Is.Not.Null, CataloguePath + " is missing");

            _context = new WorldViewAdapter.Context(_content.BuildItems(),
                _content.BuildMonsters(), _content.BuildQuests(), _content.BuildNpcs());
        }

        private QuestDefinition Quest(string id)
        {
            QuestDefinition quest;

            Assert.That(_content.BuildQuests().TryGet(new DefinitionId(id), out quest),
                Is.True, id + " is not in the catalogue");

            return quest;
        }

        /// <summary>A log in which the repeatable quest has been run once and handed in.</summary>
        private CharacterQuestState AlreadyRunOnce()
        {
            var state = new CharacterQuestState(default);

            QuestDefinition quest = Quest(Repeatable);

            int required = quest.Objectives[0].RequiredAmount;

            state.AdoptAuthoritative(new DefinitionId(Repeatable), QuestStatus.Completed,
                new[] { required });

            return state;
        }

        // ---- the quest this is about ---------------------------------------------------------

        [Test]
        public void TheGameShipsARepeatableQuestWithAnObjectiveToCount()
        {
            QuestDefinition quest = Quest(Repeatable);

            // Daily now, rather than endlessly repeatable -- but still a quest that can be
            // taken again, which is the property this whole fixture rests on: a second run
            // must not be offered showing the first run's counters.
            Assert.That(quest.ResetsDaily || quest.Repeatable, Is.True,
                "this fixture is about quests that come back and would prove nothing otherwise");

            Assert.That(quest.Objectives.Length, Is.GreaterThan(0));
            Assert.That(quest.Objectives[0].RequiredAmount, Is.GreaterThan(0));
        }

        // ---- the defect ------------------------------------------------------------------------

        [Test]
        public void AnOfferShowsNoProgressEvenWhenTheLastRunFinished()
        {
            CharacterQuestState state = AlreadyRunOnce();

            QuestViewData offer = WorldViewAdapter.BuildQuestOffer(
                new DefinitionId(Repeatable), _context);

            Assert.That(offer.IsValid, Is.True);
            Assert.That(offer.Objectives[0].Current, Is.Zero,
                "an offer showed the previous run's counter, which reads as a quest that "
                + "completed itself");

            // And the log it was built beside really does still hold the finished run, so
            // this is the view being right rather than the history being thrown away.
            QuestProgress kept;

            Assert.That(state.TryGet(new DefinitionId(Repeatable), out kept), Is.True);
            Assert.That(kept.CountAt(0), Is.EqualTo(Quest(Repeatable).Objectives[0].RequiredAmount),
                "the log must keep what happened; only the offer forgets it");
        }

        [Test]
        public void AnOfferIsDrawnAsNotStartedRatherThanAsFinished()
        {
            QuestViewData offer = WorldViewAdapter.BuildQuestOffer(
                new DefinitionId(Repeatable), _context);

            Assert.That(offer.IsReadyToComplete, Is.False);
            Assert.That(offer.IsCompleted, Is.False,
                "an offer drawn as completed puts a hand-back button on a quest nobody took");
        }

        [Test]
        public void WhatAPlayerReadsOnTheOfferIsZeroOfTheRequirement()
        {
            // The whole point, in the words on the screen.
            QuestDefinition quest = Quest(Repeatable);

            QuestViewData offer = WorldViewAdapter.BuildQuestOffer(
                new DefinitionId(Repeatable), _context);

            string drawn = QuestListView.FormatDetail(offer, null);

            Assert.That(drawn, Does.Contain("0 / " + quest.Objectives[0].RequiredAmount));
            Assert.That(drawn,
                Does.Not.Contain(quest.Objectives[0].RequiredAmount + " / "
                    + quest.Objectives[0].RequiredAmount));
        }

        // ---- the log is still the log ------------------------------------------------------------

        [Test]
        public void TheHandBackPanelStillShowsRealProgress()
        {
            // The other half. An offer forgets; a hand-back must not, or a player is asked to
            // turn in a quest that appears to have made no progress at all.
            var state = new CharacterQuestState(default);

            QuestDefinition quest = Quest(Repeatable);

            int required = quest.Objectives[0].RequiredAmount;

            state.AdoptAuthoritative(new DefinitionId(Repeatable),
                QuestStatus.ReadyToComplete, new[] { required });

            QuestViewData view = WorldViewAdapter.BuildQuest(state,
                new DefinitionId(Repeatable), _context);

            Assert.That(view.Objectives[0].Current, Is.EqualTo(required));
            Assert.That(view.IsReadyToComplete, Is.True);
        }

        [Test]
        public void AnActiveQuestStillShowsHowFarAlongItIs()
        {
            var state = new CharacterQuestState(default);

            state.AdoptAuthoritative(new DefinitionId(Repeatable), QuestStatus.Active,
                new[] { 2 });

            QuestViewData view = WorldViewAdapter.BuildQuest(state,
                new DefinitionId(Repeatable), _context);

            Assert.That(view.Objectives[0].Current, Is.EqualTo(2),
                "the tracker and the Active tab are the whole reason counters are carried");
        }

        // ---- a player can tell the two kinds apart ---------------------------------------------------

        [Test]
        public void EveryQuestSaysWhetherItCanBeTakenAgain()
        {
            // Asked for directly: "which ones repeat and which ones do not". Said on both
            // kinds rather than as a badge on one, because the absence of a badge is
            // ambiguous -- a player cannot tell "one time" from "nobody labelled it".
            QuestViewData repeatable = WorldViewAdapter.BuildQuestOffer(
                new DefinitionId(Repeatable), _context);

            QuestViewData once = WorldViewAdapter.BuildQuestOffer(
                new DefinitionId("quest.harbor_first_hunt"), _context);

            Assert.That(repeatable.Repeatable, Is.True,
                "the view reports 'can be taken again' for a daily as well as for an "
                + "endlessly repeatable quest -- a player does not care which mechanism it is");
            Assert.That(once.Repeatable, Is.False,
                "this fixture needs one of each to be worth anything");

            string repeatableText = QuestListView.FormatDetail(repeatable, null);
            string onceText = QuestListView.FormatDetail(once, null);

            // The shipped quest that comes back is a daily now, and it says so specifically
            // rather than only "repeatable": a player reads those differently -- one is a
            // promise it is always there, the other that it is back in the morning.
            Assert.That(repeatable.ResetsDaily, Is.True);

            Assert.That(repeatableText,
                Does.Contain(UiStrings.EnglishFor(UiStrings.QuestDaily)));

            Assert.That(onceText,
                Does.Contain(UiStrings.EnglishFor(UiStrings.QuestOneTime)));

            Assert.That(onceText,
                Does.Not.Contain(UiStrings.EnglishFor(UiStrings.QuestDaily)));

            Assert.That(onceText,
                Does.Not.Contain(UiStrings.EnglishFor(UiStrings.QuestRepeatable)));
        }

        [Test]
        public void TheTwoLabelsAreTranslatedAndDifferent()
        {
            Assert.That(UiStrings.EnglishFor(UiStrings.QuestRepeatable),
                Is.Not.EqualTo(UiStrings.EnglishFor(UiStrings.QuestOneTime)));

            Assert.That(UiStrings.EnglishFor(UiStrings.QuestDaily),
                Is.Not.EqualTo(UiStrings.EnglishFor(UiStrings.QuestOneTime)));

            Assert.That(UiStrings.EnglishFor(UiStrings.QuestDaily),
                Is.Not.EqualTo(UiStrings.EnglishFor(UiStrings.QuestRepeatable)),
                "daily and repeatable must not read the same: one resets, one never runs out");

            Assert.That(UiStrings.EnglishFor(UiStrings.QuestFinishedRepeatableNote),
                Is.Not.EqualTo(UiStrings.EnglishFor(UiStrings.QuestFinishedNote)),
                "a repeatable quest in the history is not closed and must not read as if "
                + "it were");
        }

        // ---- the two panels are not the same panel -------------------------------------------------

        [Test]
        public void AnOfferAndAHandBackSayWhichOneTheyAre()
        {
            // They share a layout, a formatter and a single button whose only difference is
            // one word. A player who cannot tell them apart reads the hand-back for a
            // repeatable quest as a brand new quest that arrived already finished -- which is
            // exactly the report this fixture exists for.
            Assert.That(UiStrings.EnglishFor(UiStrings.NpcQuestOffered),
                Is.Not.Null.And.Not.Empty);

            Assert.That(UiStrings.EnglishFor(UiStrings.NpcQuestFinished),
                Is.Not.Null.And.Not.Empty);

            Assert.That(UiStrings.EnglishFor(UiStrings.NpcQuestOffered),
                Is.Not.EqualTo(UiStrings.EnglishFor(UiStrings.NpcQuestFinished)));
        }
    }
}
