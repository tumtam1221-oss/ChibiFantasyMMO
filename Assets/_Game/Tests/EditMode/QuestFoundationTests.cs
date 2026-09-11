using System.Collections.Generic;
using ChibiFantasy.Client.UI;
using ChibiFantasy.Client.World;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Network;
using ChibiFantasy.Server;
using ChibiFantasy.UI;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Harbor Town's first quest: who offers it, who may take it, and what a player reads.
    /// </summary>
    /// <remarks>
    /// <b>Against the shipped content wherever it can be.</b> The quest, the guide and the
    /// reward are authored assets, and the failure that matters is not "the rules are wrong"
    /// -- <c>QuestTests</c> covers those -- but "the quest nobody can reach", "the mark that
    /// never appears" and "the panel that says 100 EXP after somebody changed the
    /// definition". Every one of those needs the real asset to catch.
    ///
    /// <b>The exploit each test is about.</b> A quest list that could accept remotely, a
    /// client naming its own reward, a client naming a giver it is nowhere near: the door is
    /// only as good as the checks behind it, so each has a test that tries it.
    /// </remarks>
    internal sealed class QuestFoundationTests
    {
        private const string Catalogue =
            "Assets/_Game/Data/Production/WorldContentCatalogue.asset";

        private const string Quest = "quest.harbor_first_hunt";
        private const string Guide = "npc.harbor_guide";
        private const string Blacksmith = "npc.blacksmith";
        private const string Slime = "monster.training_slime";
        private const string Gel = "item.slime_gel";

        private const int Connection = 3;

        private readonly List<Object> _local = new List<Object>();

        private WorldContentCatalogue _content;
        private DefinitionRegistry<QuestDefinition> _quests;
        private DefinitionRegistry<NPCDefinition> _npcs;
        private DefinitionRegistry<SpawnPointDefinition> _spawns;
        private DefinitionRegistry<ItemDefinition> _items;
        private DefinitionRegistry<MonsterDefinition> _monsters;

        [SetUp]
        public void LoadShippedContent()
        {
            _content = UnityEditor.AssetDatabase
                .LoadAssetAtPath<WorldContentCatalogue>(Catalogue);

            Assert.That(_content, Is.Not.Null);

            _quests = _content.BuildQuests();
            _npcs = _content.BuildNpcs();
            _spawns = _content.BuildSpawnPoints();
            _items = _content.BuildItems();
            _monsters = _content.BuildMonsters();
        }

        [TearDown]
        public void CleanUp()
        {
            foreach (Object created in _local)
            {
                if (created != null) Object.DestroyImmediate(created);
            }

            _local.Clear();
        }

        // ---- the authored quest, as it ships ----------------------------------------------

        [Test]
        public void The_first_quest_is_three_training_slimes_for_experience_and_a_gel()
        {
            // Pinned because the panel, the mark and the payout all read it. If somebody
            // retunes the quest this fails and says so, rather than a UI quietly disagreeing.
            Assert.That(_quests.TryGet(new DefinitionId(Quest), out QuestDefinition quest),
                Is.True, "the first quest is not in the catalogue");

            Assert.That(quest.Objectives.Length, Is.EqualTo(1));
            Assert.That(quest.Objectives[0].Type,
                Is.EqualTo(QuestObjectiveType.KillMonster));
            Assert.That(quest.Objectives[0].Target.Value, Is.EqualTo(Slime));
            Assert.That(quest.Objectives[0].RequiredAmount, Is.EqualTo(3));

            Assert.That(quest.Rewards.Length, Is.EqualTo(2));

            var experience = 0;
            var gels = 0;

            foreach (QuestReward reward in quest.Rewards)
            {
                if (reward.Type == QuestRewardType.Experience) experience = reward.Amount;
                if (reward.Type == QuestRewardType.Item && reward.Target.Value == Gel)
                {
                    gels = reward.Amount;
                }
            }

            Assert.That(experience, Is.EqualTo(100), "the experience reward changed");
            Assert.That(gels, Is.GreaterThan(0), "the gel reward is gone");
        }

        [Test]
        public void The_harbor_guide_is_the_one_who_offers_it()
        {
            _npcs.TryGet(new DefinitionId(Guide), out NPCDefinition guide);

            Assert.That(CharacterQuestAuthority.Offers(guide, new DefinitionId(Quest)),
                Is.True, "the guide no longer offers the first quest");
        }

        [Test]
        public void Nobody_else_in_town_offers_it()
        {
            foreach (NPCDefinition npc in _npcs.All)
            {
                if (npc == null || npc.Id.Value == Guide) continue;

                Assert.That(CharacterQuestAuthority.Offers(npc, new DefinitionId(Quest)),
                    Is.False, npc.Id + " also offers the first quest");
            }
        }

        // ---- the mark above the guide ------------------------------------------------------

        [Test]
        public void An_available_quest_puts_a_bang_over_the_guide()
        {
            QuestJournal journal = Journal();

            _npcs.TryGet(new DefinitionId(Guide), out NPCDefinition guide);

            Assert.That(journal.MarkerFor(guide), Is.EqualTo(QuestMarker.Available));
            Assert.That(WorldNpcPresenter.Glyph(QuestMarker.Available), Is.EqualTo("!"));
        }

        [Test]
        public void Accepting_takes_the_bang_away()
        {
            // Against a giver holding only this quest, because the rule under test is about
            // the quest and not about how many others happen to be on the guide's list. The
            // shipped guide offers five, and the case where one is taken and another is
            // still on offer is its own test below.
            QuestJournal journal = Journal();

            NPCDefinition giver = SoleGiver(Quest);

            Adopt(journal, QuestStatus.Active, 0);

            Assert.That(journal.MarkerFor(giver), Is.EqualTo(QuestMarker.None),
                "an active quest must not keep marking its giver: every guide in town "
                + "would wear one permanently");
        }

        [Test]
        public void A_finished_objective_puts_a_question_mark_over_the_guide()
        {
            QuestJournal journal = Journal();

            _npcs.TryGet(new DefinitionId(Guide), out NPCDefinition guide);

            Adopt(journal, QuestStatus.ReadyToComplete, 3);

            Assert.That(journal.MarkerFor(guide), Is.EqualTo(QuestMarker.ReadyToTurnIn));
            Assert.That(WorldNpcPresenter.Glyph(QuestMarker.ReadyToTurnIn), Is.EqualTo("?"));
        }

        [Test]
        public void A_completed_quest_leaves_the_guide_unmarked()
        {
            QuestJournal journal = Journal();

            NPCDefinition giver = SoleGiver(Quest);

            Adopt(journal, QuestStatus.Completed, 3);

            Assert.That(journal.MarkerFor(giver), Is.EqualTo(QuestMarker.None));
            Assert.That(WorldNpcPresenter.Glyph(QuestMarker.None), Is.Empty);
        }

        [Test]
        public void A_giver_with_another_quest_left_keeps_its_mark()
        {
            // The other half, and the reason the two tests above needed their own giver.
            // Finishing one of the guide's quests must not clear the mark while they still
            // have work to hand out -- a player who saw the "!" vanish would assume the
            // guide was done with them and never speak to them again.
            QuestJournal journal = Journal();

            _npcs.TryGet(new DefinitionId(Guide), out NPCDefinition guide);

            Assert.That(guide.Quests.Length, Is.GreaterThan(1),
                "this test is meaningless against a guide with one quest");

            Adopt(journal, QuestStatus.Completed, 3);

            Assert.That(journal.MarkerFor(guide), Is.EqualTo(QuestMarker.Available),
                "the guide still has quests to offer and must still say so");
        }

        [Test]
        public void An_npc_with_no_quests_is_never_marked()
        {
            QuestJournal journal = Journal();

            _npcs.TryGet(new DefinitionId(Blacksmith), out NPCDefinition smith);

            Assert.That(journal.MarkerFor(smith), Is.EqualTo(QuestMarker.None));
        }

        [Test]
        public void The_mark_is_decided_from_content_and_never_from_an_npc_id()
        {
            // The rule the whole marker system rests on: a second quest giver must cost no
            // code. A literal here would mean the next one silently wears no mark.
            foreach (string file in new[]
            {
                "Assets/_Game/Scripts/Client/World/QuestJournal.cs",
                "Assets/_Game/Scripts/Client/World/WorldNpcPresenter.cs",
                "Assets/_Game/Scripts/UI/QuestListView.cs",
                "Assets/_Game/Scripts/Server/CharacterQuestAuthority.cs",
            })
            {
                string code = CodeWithoutComments(file);

                foreach (string forbidden in new[] { Guide, Quest, Slime, Gel })
                {
                    Assert.That(code.Contains(forbidden), Is.False,
                        file + " names '" + forbidden + "'");
                }
            }
        }

        // ---- what the panel shows ----------------------------------------------------------

        [Test]
        public void The_available_list_is_built_from_content_and_not_written_down()
        {
            QuestJournal journal = Journal();

            Assert.That(journal.Available.Count, Is.GreaterThan(0));

            var found = false;

            foreach (QuestViewData view in journal.Available)
            {
                if (view.QuestId.Value == Quest) found = true;
            }

            Assert.That(found, Is.True, "the first quest is not offered to a new character");
        }

        [Test]
        public void Accepting_moves_it_from_available_to_active()
        {
            QuestJournal journal = Journal();

            Adopt(journal, QuestStatus.Active, 0);

            Assert.That(Lists(journal.Available, Quest), Is.False,
                "an accepted quest is still being offered");

            Assert.That(Lists(journal.Active, Quest), Is.True,
                "an accepted quest never appeared under Active");
        }

        [Test]
        public void The_detail_shows_the_objective_the_giver_and_every_reward()
        {
            QuestJournal journal = Journal();

            QuestViewData view = WorldViewAdapter.BuildQuest(journal.State,
                new DefinitionId(Quest), journal.ViewContext);

            string detail = QuestListView.FormatDetail(view, null,
                _ => "Harbor Guide");

            Assert.That(detail, Does.Contain("Harbor Guide"), "no quest giver");
            Assert.That(detail, Does.Contain("Defeat"), "no objective verb");
            Assert.That(detail, Does.Contain("0 / 3"), "no progress out of the requirement");
            Assert.That(detail, Does.Contain("EXP 100"), "no experience reward");
            Assert.That(detail, Does.Contain("Slime Gel"), "no item reward");
        }

        [Test]
        public void Progress_in_the_detail_follows_the_authoritative_counter()
        {
            QuestJournal journal = Journal();

            for (var killed = 0; killed <= 3; killed++)
            {
                Adopt(journal, killed >= 3 ? QuestStatus.ReadyToComplete : QuestStatus.Active,
                    killed);

                QuestViewData view = WorldViewAdapter.BuildQuest(journal.State,
                    new DefinitionId(Quest), journal.ViewContext);

                Assert.That(QuestListView.FormatDetail(view, null),
                    Does.Contain(killed + " / 3"), "at " + killed + " kills");
            }
        }

        [Test]
        public void Every_reward_kind_is_drawn_rather_than_only_the_two_this_quest_has()
        {
            // A panel that only understood this quest would silently omit the first reward a
            // later gate adds, and a player would be surprised by what they were paid.
            Assert.That(QuestListView.FormatReward(
                new QuestRewardViewData(QuestRewardType.Experience, default, default,
                    AssetRef.None, 250), null), Is.EqualTo("EXP 250"));

            Assert.That(QuestListView.FormatReward(
                new QuestRewardViewData(QuestRewardType.Currency, default, default,
                    AssetRef.None, 40), null), Does.Contain("40"));

            Assert.That(QuestListView.FormatReward(
                new QuestRewardViewData(QuestRewardType.Skill,
                    new DefinitionId("skill.power_strike"), default, AssetRef.None, 1), null),
                Does.Contain("Power Strike"),
                "an unfamiliar reward must still say what it is");
        }

        [Test]
        public void An_empty_list_says_so_rather_than_drawing_nothing()
        {
            Assert.That(QuestListView.FormatList(new List<QuestViewData>(), default, null),
                Is.Not.Empty);
        }

        // ---- the panel and the key ----------------------------------------------------------

        [Test]
        public void Ctrl_q_opens_the_journal_and_pressing_it_again_closes_it()
        {
            QuestListView panel = Panel();
            WorldQuestInput input = Input();

            input.Toggled += panel.Toggle;

            input.Press();

            Assert.That(panel.IsVisible, Is.True, "Ctrl+Q did not open the journal");

            input.Press();

            Assert.That(panel.IsVisible, Is.False, "Ctrl+Q did not close it again");
            Assert.That(input.Presses, Is.EqualTo(2));
        }

        [Test]
        public void The_binding_is_an_action_and_needs_the_modifier()
        {
            // Q alone belongs to a later gate's skill bar. The modifier is part of the
            // binding rather than an if-statement somebody can forget.
            string code = CodeWithoutComments(
                "Assets/_Game/Scripts/Client/World/WorldQuestInput.cs");

            Assert.That(code, Does.Contain("new InputAction"));
            Assert.That(code, Does.Contain("OneModifier"));
            Assert.That(code, Does.Contain("<Keyboard>/q"));
            Assert.That(code, Does.Contain("leftCtrl"));
        }

        [Test]
        public void The_binding_resolves_to_real_keys_rather_than_only_compiling()
        {
            // The gap the test above cannot see: Press() drives the event directly, so a
            // malformed composite would still pass it and Ctrl+Q would do nothing in the
            // game. This asks the Input System what the binding actually resolved to.
            WorldQuestInput input = Input();

            Assert.That(input.IsListening, Is.True, "the action is not listening");

            IReadOnlyList<string> paths = input.BoundControls;

            Assert.That(paths, Contains.Item("/Keyboard/q"),
                "Q is not bound, so the gesture can never fire");

            Assert.That(paths, Contains.Item("/Keyboard/leftCtrl"),
                "the modifier is not bound, so Q alone would open the journal");

            Assert.That(paths, Contains.Item("/Keyboard/rightCtrl"),
                "only one control key opens the journal");
        }

        [Test]
        public void The_accept_button_is_wired_to_something_that_is_listening()
        {
            // The bug this exists for. The journal UI was composed before the dialogue was
            // built, so it subscribed to a null and the Accept button raised an event with
            // no listener: a button that did nothing, with no error anywhere to find it by.
            //
            // Checked at the seam rather than through the composition root, because the root
            // needs a network manager and a canvas to run at all. What must hold is that
            // pressing Accept on an offer reaches a listener at all.
            NpcDialogueView dialogue = DialogueOnACanvas();

            var taken = new List<DefinitionId>();

            dialogue.QuestAccepted += q => taken.Add(q);

            QuestJournal journal = Journal();

            QuestViewData offer = WorldViewAdapter.BuildQuest(journal.State,
                new DefinitionId(Quest), journal.ViewContext);

            dialogue.ShowQuestOffer(new DefinitionId(Guide), "Harbor Guide", offer);

            Assert.That(dialogue.IsOfferingAQuest, Is.True,
                "the guide opened with no offer attached, so there is nothing to accept");

            dialogue.AcceptOffer();

            Assert.That(taken, Has.Count.EqualTo(1),
                "pressing Accept reached nobody");

            Assert.That(taken[0].Value, Is.EqualTo(Quest));
        }

        [Test]
        public void The_journal_ui_is_composed_after_the_dialogue_it_subscribes_to()
        {
            // Belt and braces on the ordering itself: the argument makes a null impossible,
            // and this makes the call order visible to anybody rearranging the method.
            string code = CodeWithoutComments(
                "Assets/_Game/Scripts/Client/ClientApplicationBootstrap.cs");

            int built = code.IndexOf("NpcDialogue = BuildNpcDialogue();",
                System.StringComparison.Ordinal);

            int composed = code.IndexOf("ComposeQuestJournalUi(NpcDialogue);",
                System.StringComparison.Ordinal);

            Assert.That(built, Is.GreaterThanOrEqualTo(0), "the dialogue is never built");
            Assert.That(composed, Is.GreaterThan(built),
                "the quest UI is composed before the dialogue exists, so Accept would be "
                + "wired to nothing");
        }

        [Test]
        public void Nothing_outside_the_quest_input_reads_the_quest_key()
        {
            // The convention this client already follows: one component per gesture. A
            // keyboard check in the panel or the composition root is how input ends up
            // scattered across files that have nothing to do with it.
            foreach (string file in new[]
            {
                "Assets/_Game/Scripts/UI/QuestListView.cs",
                "Assets/_Game/Scripts/Client/World/QuestJournal.cs",
            })
            {
                Assert.That(CodeWithoutComments(file).Contains("Keyboard"), Is.False,
                    file + " reads the keyboard itself");
            }
        }

        [Test]
        public void The_journal_leaves_the_world_walkable()
        {
            // A decision, not an oversight. A conversation pins the player in place because
            // somebody is talking to them; a journal is a window they opened themselves,
            // possibly standing in a field, and being unable to walk away from it reads as
            // the game having hung. The NPC dialogue still blocks -- see
            // An_open_conversation_stops_the_world_taking_clicks.
            QuestListView panel = Panel();

            panel.Show(QuestListTab.Available);

            Assert.That(panel.IsVisible, Is.True);
            Assert.That(panel.BlocksTheWorld, Is.False,
                "the journal froze the world, which is what a player reads as a hang");
        }

        [Test]
        public void The_journal_still_swallows_its_own_clicks()
        {
            // What keeps pressing a tab from also ordering a walk to whatever is behind it:
            // the panel's own background is a raycast target, and the pointer refuses any
            // click that lands on UI.
            QuestListView panel = Panel();

            panel.Show(QuestListTab.Available);

            var background = panel.GetComponent<UnityEngine.UI.Image>();

            Assert.That(background, Is.Not.Null, "the panel has no background to catch a click");
            Assert.That(background.raycastTarget, Is.True,
                "clicking the journal would walk the character to whatever is behind it");
        }

        [Test]
        public void Closing_the_journal_puts_it_away_cleanly()
        {
            QuestListView panel = Panel();

            panel.Show(QuestListTab.Available);
            panel.Close();

            Assert.That(panel.IsVisible, Is.False);
            Assert.That(panel.Selected.IsValid, Is.False, "a closed journal still names a quest");
        }

        [Test]
        public void A_quest_that_needs_its_giver_says_who_to_speak_to()
        {
            // A greyed button with no explanation is the thing a player reads as broken.
            QuestListView panel = Panel();
            QuestJournal journal = Journal();

            panel.GiverName = _ => "Harbor Guide";
            panel.CanAcceptHere = _ => false;
            panel.Bind(journal.Available, journal.Active);
            panel.Show(QuestListTab.Available);

            Assert.That(panel.CanAccept, Is.False);
            Assert.That(panel.DetailText, Does.Contain("Speak with Harbor Guide"));
        }

        [Test]
        public void The_journal_selects_something_rather_than_opening_blank()
        {
            QuestListView panel = Panel();
            QuestJournal journal = Journal();

            panel.Bind(journal.Available, journal.Active);
            panel.Show(QuestListTab.Available);

            Assert.That(panel.Selected.IsValid, Is.True);
            Assert.That(panel.DetailText, Does.Contain("Rewards"));
        }

        [Test]
        public void The_journal_cannot_be_used_to_accept_from_across_the_town()
        {
            // The exploit the whole arrangement exists to prevent. The panel refuses because
            // it is told to; the server refuses independently, which the authority tests
            // cover.
            QuestListView panel = Panel();
            QuestJournal journal = Journal();

            panel.CanAcceptHere = _ => false;
            panel.Bind(journal.Available, journal.Active);
            panel.Show(QuestListTab.Available);

            var raised = 0;

            panel.AcceptRequested += _ => raised++;

            panel.Accept();

            Assert.That(raised, Is.EqualTo(0), "the journal accepted a quest remotely");
            Assert.That(panel.CanAccept, Is.False);
        }

        // ---- handing it back -----------------------------------------------------------------

        [Test]
        public void A_finished_quest_opens_a_hand_back_rather_than_another_offer()
        {
            // The gap a player hits the moment they finish: the mark says come back, and
            // the conversation had nothing to press.
            NpcDialogueView dialogue = DialogueOnACanvas();
            QuestJournal journal = Journal();

            Adopt(journal, QuestStatus.ReadyToComplete, 3);

            QuestViewData done = WorldViewAdapter.BuildQuest(journal.State,
                new DefinitionId(Quest), journal.ViewContext);

            dialogue.ShowQuestTurnIn(new DefinitionId(Guide), "Harbor Guide", done);

            Assert.That(dialogue.IsTurningIn, Is.True);
            Assert.That(dialogue.OfferedQuest.Value, Is.EqualTo(Quest));
            Assert.That(dialogue.Body, Does.Contain("3 / 3"),
                "the panel must show the finished objective, not an empty one");
            Assert.That(dialogue.Body, Does.Contain("EXP 100"),
                "what was promised and what is about to be paid must be the same lines");
        }

        [Test]
        public void The_button_hands_back_rather_than_accepting_when_turning_in()
        {
            // One button, two meanings. Raising the wrong event would ask the server to
            // take a quest the player already has and read as nothing happening.
            NpcDialogueView dialogue = DialogueOnACanvas();
            QuestJournal journal = Journal();

            Adopt(journal, QuestStatus.ReadyToComplete, 3);

            var accepted = new List<DefinitionId>();
            var handed = new List<DefinitionId>();

            dialogue.QuestAccepted += q => accepted.Add(q);
            dialogue.QuestTurnedIn += q => handed.Add(q);

            dialogue.ShowQuestTurnIn(new DefinitionId(Guide), "Harbor Guide",
                WorldViewAdapter.BuildQuest(journal.State, new DefinitionId(Quest),
                    journal.ViewContext));

            dialogue.PressQuestButton();

            Assert.That(handed, Has.Count.EqualTo(1), "the hand-back reached nobody");
            Assert.That(accepted, Is.Empty, "handing back asked to accept instead");
        }

        [Test]
        public void An_offer_still_accepts_after_a_hand_back_has_been_shown()
        {
            // The mode has to reset, or the next guide's offer would try to hand in.
            NpcDialogueView dialogue = DialogueOnACanvas();
            QuestJournal journal = Journal();

            Adopt(journal, QuestStatus.ReadyToComplete, 3);

            dialogue.ShowQuestTurnIn(new DefinitionId(Guide), "Harbor Guide",
                WorldViewAdapter.BuildQuest(journal.State, new DefinitionId(Quest),
                    journal.ViewContext));

            dialogue.Close();

            QuestJournal fresh = Journal();

            var accepted = new List<DefinitionId>();
            dialogue.QuestAccepted += q => accepted.Add(q);

            dialogue.ShowQuestOffer(new DefinitionId(Guide), "Harbor Guide",
                WorldViewAdapter.BuildQuest(fresh.State, new DefinitionId(Quest),
                    fresh.ViewContext));

            Assert.That(dialogue.IsTurningIn, Is.False, "the panel is still in hand-back mode");

            dialogue.PressQuestButton();

            Assert.That(accepted, Has.Count.EqualTo(1));
        }

        [Test]
        public void The_journal_says_where_to_claim_a_finished_quest()
        {
            QuestListView panel = Panel();
            QuestJournal journal = Journal();

            Adopt(journal, QuestStatus.ReadyToComplete, 3);

            panel.GiverName = _ => "Harbor Guide";
            panel.Bind(journal.Available, journal.Active);
            panel.Show(QuestListTab.Active);

            Assert.That(panel.DetailText, Does.Contain("Return to Harbor Guide"),
                "a full counter with nothing to do reads as the quest being stuck");
        }

        [Test]
        public void Handing_back_at_the_giver_completes_the_quest_and_pays()
        {
            CharacterQuestAuthority authority = Authority(out WorldCharacterRegistry players);

            LivingCharacter player = AddPlayer(players);

            StandAtTheGuide(player);

            authority.Apply(Connection, Quest, (int)QuestCommand.Accept, Guide, 1);

            for (var i = 0; i < 3; i++)
            {
                authority.ReportKill(player, new DefinitionId(Slime));
            }

            var quest = new DefinitionId(Quest);

            Assert.That(player.Quests.StatusOf(quest),
                Is.EqualTo(QuestStatus.ReadyToComplete));

            long before = player.Domain.Progression.Experience;

            QuestCommandSnapshot answer = authority.Apply(Connection, Quest,
                (int)QuestCommand.TurnIn, Guide, 2);

            Assert.That(answer.Accepted, Is.True, "reason " + answer.Reason);
            Assert.That(player.Quests.StatusOf(quest), Is.EqualTo(QuestStatus.Completed));

            Assert.That(player.Domain.Progression.Experience, Is.GreaterThan(before),
                "the quest completed without paying its experience");
        }

        [Test]
        public void A_completed_quest_cannot_be_handed_in_twice()
        {
            CharacterQuestAuthority authority = Authority(out WorldCharacterRegistry players);

            LivingCharacter player = AddPlayer(players);

            StandAtTheGuide(player);

            authority.Apply(Connection, Quest, (int)QuestCommand.Accept, Guide, 1);

            for (var i = 0; i < 3; i++) authority.ReportKill(player, new DefinitionId(Slime));

            authority.Apply(Connection, Quest, (int)QuestCommand.TurnIn, Guide, 2);

            long paid = player.Domain.Progression.Experience;

            QuestCommandSnapshot again = authority.Apply(Connection, Quest,
                (int)QuestCommand.TurnIn, Guide, 3);

            Assert.That(again.Accepted, Is.False, "a quest was paid twice");
            Assert.That(player.Domain.Progression.Experience, Is.EqualTo(paid));
        }

        [Test]
        public void Handing_back_from_across_the_town_is_refused()
        {
            CharacterQuestAuthority authority = Authority(out WorldCharacterRegistry players);

            LivingCharacter player = AddPlayer(players);

            StandAtTheGuide(player);

            authority.Apply(Connection, Quest, (int)QuestCommand.Accept, Guide, 1);

            for (var i = 0; i < 3; i++) authority.ReportKill(player, new DefinitionId(Slime));

            player.Location.Position = new CombatPosition(90f, 0f, 90f);

            Assert.That(authority.Apply(Connection, Quest, (int)QuestCommand.TurnIn,
                Guide, 2).Accepted, Is.False,
                "a reward was claimed from ninety metres away");
        }

        // ---- what the server allows ----------------------------------------------------------

        [Test]
        public void Standing_with_the_guide_takes_the_quest()
        {
            CharacterQuestAuthority authority = Authority(out WorldCharacterRegistry players);

            LivingCharacter player = AddPlayer(players);

            StandAtTheGuide(player);

            QuestCommandSnapshot answer = authority.Apply(Connection, Quest,
                (int)QuestCommand.Accept, Guide, 1);

            Assert.That(answer.Accepted, Is.True, "reason " + answer.Reason);
            Assert.That(answer.Status, Is.EqualTo((int)QuestStatus.Active));
            Assert.That(player.Quests.IsActive(new DefinitionId(Quest)), Is.True);
        }

        [Test]
        public void Standing_across_the_town_does_not()
        {
            CharacterQuestAuthority authority = Authority(out WorldCharacterRegistry players);

            LivingCharacter player = AddPlayer(players);

            player.Location.Position = new CombatPosition(80f, 0f, 80f);

            QuestCommandSnapshot answer = authority.Apply(Connection, Quest,
                (int)QuestCommand.Accept, Guide, 1);

            Assert.That(answer.Accepted, Is.False,
                "a quest was accepted from eighty metres away");

            Assert.That(player.Quests.IsActive(new DefinitionId(Quest)), Is.False);
        }

        [Test]
        public void Naming_no_giver_at_all_does_not_work()
        {
            CharacterQuestAuthority authority = Authority(out WorldCharacterRegistry players);

            StandAtTheGuide(AddPlayer(players));

            Assert.That(authority.Apply(Connection, Quest, (int)QuestCommand.Accept,
                string.Empty, 1).Accepted, Is.False,
                "a quest with a giver was taken without naming one");
        }

        [Test]
        public void Naming_the_wrong_giver_does_not_work()
        {
            // Standing next to the blacksmith and claiming he hands out the guide's quest.
            CharacterQuestAuthority authority = Authority(out WorldCharacterRegistry players);

            StandAtTheGuide(AddPlayer(players));

            Assert.That(authority.Apply(Connection, Quest, (int)QuestCommand.Accept,
                Blacksmith, 1).Accepted, Is.False);
        }

        [Test]
        public void A_quest_a_client_invented_is_refused()
        {
            CharacterQuestAuthority authority = Authority(out WorldCharacterRegistry players);

            StandAtTheGuide(AddPlayer(players));

            QuestCommandSnapshot answer = authority.Apply(Connection, "quest.free_levels",
                (int)QuestCommand.Accept, Guide, 1);

            Assert.That(answer.Accepted, Is.False);
        }

        [Test]
        public void Taking_the_same_quest_twice_is_refused()
        {
            CharacterQuestAuthority authority = Authority(out WorldCharacterRegistry players);

            LivingCharacter player = AddPlayer(players);

            StandAtTheGuide(player);

            Assert.That(authority.Apply(Connection, Quest, (int)QuestCommand.Accept,
                Guide, 1).Accepted, Is.True);

            QuestCommandSnapshot again = authority.Apply(Connection, Quest,
                (int)QuestCommand.Accept, Guide, 2);

            Assert.That(again.Accepted, Is.False);
            Assert.That(again.Reason, Is.EqualTo((int)QuestRejection.AlreadyActive));
        }

        [Test]
        public void A_command_nobody_authored_is_refused_rather_than_cast()
        {
            CharacterQuestAuthority authority = Authority(out WorldCharacterRegistry players);

            StandAtTheGuide(AddPlayer(players));

            foreach (int nonsense in new[] { -1, 7, int.MaxValue })
            {
                Assert.That(authority.Apply(Connection, Quest, nonsense, Guide, 1).Accepted,
                    Is.False, "command " + nonsense);
            }
        }

        [Test]
        public void A_connection_holding_no_character_is_refused()
        {
            CharacterQuestAuthority authority = Authority(out WorldCharacterRegistry players);

            AddPlayer(players);

            Assert.That(authority.Apply(99, Quest, (int)QuestCommand.Accept, Guide, 1)
                .Accepted, Is.False);
        }

        [Test]
        public void Turning_in_before_the_objective_is_done_is_refused()
        {
            CharacterQuestAuthority authority = Authority(out WorldCharacterRegistry players);

            LivingCharacter player = AddPlayer(players);

            StandAtTheGuide(player);

            authority.Apply(Connection, Quest, (int)QuestCommand.Accept, Guide, 1);

            QuestCommandSnapshot answer = authority.Apply(Connection, Quest,
                (int)QuestCommand.TurnIn, Guide, 2);

            Assert.That(answer.Accepted, Is.False);
            Assert.That(answer.Reason,
                Is.EqualTo((int)QuestRejection.ObjectivesIncomplete));
        }

        [Test]
        public void Every_quest_change_marks_the_character_for_saving()
        {
            // The bug this exists for: a save is skipped for a character that is not dirty,
            // and nothing about a quest marked one. Accepting appeared to persist only
            // because the kill after it granted experience and marked the character on its
            // way past -- so a quest handed in was handed in again after the next login,
            // and the reward paid twice.
            CharacterQuestAuthority authority = Authority(out WorldCharacterRegistry players);

            LivingCharacter player = AddPlayer(players);

            StandAtTheGuide(player);

            players.Save(player, force: true);

            Assert.That(player.IsDirty, Is.False, "the fixture starts clean");

            authority.Apply(Connection, Quest, (int)QuestCommand.Accept, Guide, 1);

            Assert.That(player.IsDirty, Is.True,
                "accepting a quest left nothing to save");

            players.Save(player, force: true);

            authority.ReportKill(player, new DefinitionId(Slime));

            Assert.That(player.IsDirty, Is.True,
                "progress left nothing to save");

            players.Save(player, force: true);

            authority.ReportKill(player, new DefinitionId(Slime));
            authority.ReportKill(player, new DefinitionId(Slime));

            players.Save(player, force: true);

            authority.Apply(Connection, Quest, (int)QuestCommand.TurnIn, Guide, 2);

            Assert.That(player.IsDirty, Is.True,
                "handing a quest in left nothing to save, so it could be handed in again");
        }

        [Test]
        public void A_refused_quest_command_marks_nothing()
        {
            // The other half: a refusal changed no state, so writing the character would be
            // a database round trip for nothing.
            CharacterQuestAuthority authority = Authority(out WorldCharacterRegistry players);

            LivingCharacter player = AddPlayer(players);

            player.Location.Position = new CombatPosition(90f, 0f, 90f);
            players.Save(player, force: true);

            authority.Apply(Connection, Quest, (int)QuestCommand.Accept, Guide, 1);

            Assert.That(player.IsDirty, Is.False);
        }

        [Test]
        public void A_client_in_a_world_does_not_release_the_session_the_save_needs()
        {
            // The defect that cost every quest on the way out. Releasing a session revokes
            // the token the world server saves with, and the client released on quit -- so
            // the server's save presented a dead token and everything since the last
            // periodic save was thrown away. The world already releases the session itself,
            // after saving, which is why the order matters and why this must not race it.
            string code = CodeWithoutComments(
                "Assets/_Game/Scripts/Client/ClientApplicationBootstrap.cs");

            Assert.That(code, Does.Contain("IsInAWorldThatWillReleaseTheSession()"),
                "the client releases its session unconditionally again");

            int guard = code.IndexOf("if (IsInAWorldThatWillReleaseTheSession()) return;",
                System.StringComparison.Ordinal);

            int release = code.IndexOf("http.ReleaseSession(", System.StringComparison.Ordinal);

            Assert.That(guard, Is.GreaterThanOrEqualTo(0), "the guard is gone");
            Assert.That(release, Is.GreaterThan(guard),
                "the release happens before the guard, so it still races the save");
        }

        [Test]
        public void The_world_saves_before_it_hands_the_session_back()
        {
            // The other half of the same rule, on the server. Reversed, the token would be
            // revoked before the character was written and every disconnect would lose
            // whatever had happened since the last save.
            string code = CodeWithoutComments(
                "Assets/_Game/Scripts/Server/WorldServerBootstrap.cs");

            int save = code.IndexOf("Simulation?.Release(connection.ClientId);",
                System.StringComparison.Ordinal);

            int hand = code.IndexOf("Coordinator?.Leave(connection.ClientId);",
                System.StringComparison.Ordinal);

            Assert.That(save, Is.GreaterThanOrEqualTo(0), "the world no longer saves on leave");
            Assert.That(hand, Is.GreaterThan(save),
                "the session is handed back before the character is saved");
        }

        // ---- the kill that advances it -------------------------------------------------------

        [Test]
        public void Three_slimes_finish_the_objective_and_nothing_else_does()
        {
            CharacterQuestAuthority authority = Authority(out WorldCharacterRegistry players);

            LivingCharacter player = AddPlayer(players);

            StandAtTheGuide(player);

            authority.Apply(Connection, Quest, (int)QuestCommand.Accept, Guide, 1);

            var quest = new DefinitionId(Quest);

            // Something else entirely must not count.
            authority.ReportKill(player, new DefinitionId("monster.ancient_slime_king"));

            Assert.That(player.Quests.StatusOf(quest), Is.EqualTo(QuestStatus.Active));

            for (var i = 0; i < 3; i++)
            {
                authority.ReportKill(player, new DefinitionId(Slime));
            }

            Assert.That(player.Quests.StatusOf(quest),
                Is.EqualTo(QuestStatus.ReadyToComplete),
                "three training slimes did not finish the first quest");
        }

        [Test]
        public void A_kill_advances_nothing_for_a_quest_that_was_never_taken()
        {
            CharacterQuestAuthority authority = Authority(out WorldCharacterRegistry players);

            LivingCharacter player = AddPlayer(players);

            Assert.That(authority.ReportKill(player, new DefinitionId(Slime)),
                Is.EqualTo(0));
        }

        [Test]
        public void Progress_is_wired_to_the_one_call_that_claims_a_defeat()
        {
            // Hung off anything earlier in combat, one slime would advance the quest once
            // per hit. The claim is the only step that happens exactly once per monster.
            string code = CodeWithoutComments(
                "Assets/_Game/Scripts/Server/MonsterRewardAuthority.cs");

            int claim = code.IndexOf("MonsterDefeatService.Resolve",
                System.StringComparison.Ordinal);

            // The call, not the property that declares it -- the declaration sits far
            // above the method and would satisfy an ordering check that means nothing.
            int told = code.IndexOf("_killClaimed?.Invoke",
                System.StringComparison.Ordinal);

            Assert.That(claim, Is.GreaterThanOrEqualTo(0), "the defeat claim moved");
            Assert.That(told, Is.GreaterThan(claim),
                "the kill is announced before the defeat is claimed, so a monster nobody "
                + "killed would advance a quest");

            Assert.That(CodeWithoutComments(
                    "Assets/_Game/Scripts/Server/WorldServerBootstrap.cs"),
                Does.Contain("ReportKill"),
                "nothing in production listens for the kill, so no quest would ever move");
        }

        [Test]
        public void The_log_the_server_sends_carries_status_and_counters_and_no_rewards()
        {
            CharacterQuestAuthority authority = Authority(out WorldCharacterRegistry players);

            LivingCharacter player = AddPlayer(players);

            StandAtTheGuide(player);

            authority.Apply(Connection, Quest, (int)QuestCommand.Accept, Guide, 1);
            authority.ReportKill(player, new DefinitionId(Slime));

            QuestLogSnapshot log = authority.SnapshotFor(player);

            Assert.That(log.Count, Is.EqualTo(1));
            Assert.That(log.Quests[0].QuestId, Is.EqualTo(Quest));
            Assert.That(log.Quests[0].Status, Is.EqualTo((int)QuestStatus.Active));
            Assert.That(log.Quests[0].Counters[0], Is.EqualTo(1));

            // The wire carries no reward and no requirement: both are content the client
            // already ships, and a second copy is the one that goes stale.
            string wire = CodeWithoutComments(
                "Assets/_Game/Scripts/Network/QuestSnapshot.cs");

            Assert.That(wire.Contains("Reward"), Is.False,
                "a reward crossed the wire, so a client would have two answers for it");
        }

        [Test]
        public void A_client_adopting_the_log_shows_what_the_server_said()
        {
            CharacterQuestAuthority authority = Authority(out WorldCharacterRegistry players);

            LivingCharacter player = AddPlayer(players);

            StandAtTheGuide(player);

            authority.Apply(Connection, Quest, (int)QuestCommand.Accept, Guide, 1);
            authority.ReportKill(player, new DefinitionId(Slime));
            authority.ReportKill(player, new DefinitionId(Slime));

            QuestJournal journal = Journal();

            foreach (QuestProgressSnapshot entry in authority.SnapshotFor(player).Quests)
            {
                journal.State.AdoptAuthoritative(new DefinitionId(entry.QuestId),
                    (QuestStatus)entry.Status, entry.Counters);
            }

            journal.Rebuild();

            QuestViewData view = WorldViewAdapter.BuildQuest(journal.State,
                new DefinitionId(Quest), journal.ViewContext);

            Assert.That(QuestListView.FormatDetail(view, null), Does.Contain("2 / 3"));
        }

        // ---- helpers ----------------------------------------------------------------------

        /// <summary>
        /// A quest giver who offers exactly one quest.
        /// </summary>
        /// <remarks>Built rather than loaded, because the marker rules are about one quest's
        /// state and the shipped guide legitimately carries several. Using the real guide
        /// made these tests quietly depend on the town having a single quest in it, and they
        /// failed the moment a second was authored -- which was content working, not the
        /// rule breaking.</remarks>
        private NPCDefinition SoleGiver(string quest)
        {
            var npc = ScriptableObject.CreateInstance<NPCDefinition>();

            _local.Add(npc);

            var so = new UnityEditor.SerializedObject(npc);

            so.FindProperty("_id").FindPropertyRelative("_value").stringValue =
                "npc.test_sole_giver";
            so.FindProperty("_nameKey").FindPropertyRelative("_key").stringValue =
                "npc.test_sole_giver.name";
            so.FindProperty("_isQuestGiver").boolValue = true;

            UnityEditor.SerializedProperty offered = so.FindProperty("_quests");

            offered.arraySize = 1;
            offered.GetArrayElementAtIndex(0).FindPropertyRelative("_value").stringValue = quest;

            so.ApplyModifiedPropertiesWithoutUndo();

            return npc;
        }

        private void Adopt(QuestJournal journal, QuestStatus status, int killed)
        {
            journal.State.AdoptAuthoritative(new DefinitionId(Quest), status,
                new[] { killed });

            journal.Rebuild();
        }

        private static bool Lists(IReadOnlyList<QuestViewData> list, string quest)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].QuestId.Value == quest) return true;
            }

            return false;
        }

        private QuestJournal Journal()
        {
            var host = new GameObject("journal");

            _local.Add(host);

            QuestJournal journal = host.AddComponent<QuestJournal>();

            journal.Bind(_quests, _items, _monsters, _npcs);

            return journal;
        }

        /// <summary>An NPC dialogue under a canvas, which is what the world builds.</summary>
        private NpcDialogueView DialogueOnACanvas()
        {
            var canvas = new GameObject("Canvas", typeof(RectTransform));

            _local.Add(canvas);

            var host = new GameObject("dialogue", typeof(RectTransform));

            host.transform.SetParent(canvas.transform, false);

            NpcDialogueView dialogue = host.AddComponent<NpcDialogueView>();

            dialogue.EnsureVisuals();

            return dialogue;
        }

        private QuestListView Panel()
        {
            var canvas = new GameObject("Canvas", typeof(RectTransform));

            _local.Add(canvas);

            var host = new GameObject("panel", typeof(RectTransform));

            host.transform.SetParent(canvas.transform, false);

            QuestListView panel = host.AddComponent<QuestListView>();

            panel.EnsureVisuals();

            return panel;
        }

        private WorldQuestInput Input()
        {
            var host = new GameObject("input");

            _local.Add(host);

            WorldQuestInput input = host.AddComponent<WorldQuestInput>();

            input.Compose();

            return input;
        }

        private CharacterQuestAuthority Authority(out WorldCharacterRegistry players)
        {
            // With the item registry, so the character actually has a bag. Without one the
            // quest's Slime Gel has nowhere to go and turning in is refused -- which is
            // correct behaviour and a wrong test fixture.
            players = new WorldCharacterRegistry(_store, _spawns, _items, 20);

            // With the progression curve, because experience is paid through it. Without
            // one a quest completes and silently pays nothing, which is the bug this
            // fixture would otherwise hide.
            DefinitionRegistry<CharacterProgressionDefinition> curves =
                _content.BuildProgressions();

            CharacterProgressionDefinition curve = curves.All.Count > 0 ? curves.All[0] : null;

            return new CharacterQuestAuthority(players, _quests,
                new NpcInteractionService.Context(_npcs, _spawns, _content.BuildShops(),
                    _quests),
                _items, null, curve);
        }

        /// <summary>Puts the player exactly where the guide is standing.</summary>
        private void StandAtTheGuide(LivingCharacter player)
        {
            _npcs.TryGet(new DefinitionId(Guide), out NPCDefinition guide);
            _spawns.TryGet(guide.SpawnPoint, out SpawnPointDefinition at);

            player.Location.Position = new CombatPosition(at.X, at.Y, at.Z);
        }

        private LivingCharacter AddPlayer(WorldCharacterRegistry players)
        {
            const string character = "char-quest";
            const string session = "session-quest";

            _store.Rows[session] = new PersistedCharacter(
                new CharacterId(character), new AccountId("acc-quest"),
                new ServerId("srv-1"), character, 2, 5, 0, 100, 50,
                new DefinitionId("class.swordsman"), default,
                new DefinitionId("map.harbor_town"), default, null, null, null, 1);

            WorldSpawnResult result = players.Spawn(Connection,
                WorldAdmission.Admitted(new SessionId(session), new AccountId("acc-quest"),
                    new CharacterId(character), new ServerId("srv-1"),
                    new ChannelId("ch-1"), new DefinitionId("map.harbor_town"),
                    new Revision(1), new Revision(1), SessionState.EnteringWorld),
                new ResourceLimits(100, 50));

            Assert.That(result.IsSpawned, Is.True, result.Detail);

            return result.Character;
        }

        private readonly FakeStore _store = new FakeStore();

        private sealed class FakeStore : ICharacterStateStore
        {
            public readonly Dictionary<string, PersistedCharacter> Rows =
                new Dictionary<string, PersistedCharacter>();

            public CharacterPersistenceResult Load(SessionId session)
            {
                return Rows.TryGetValue(session.Value, out PersistedCharacter row)
                    ? CharacterPersistenceResult.Loaded(row)
                    : CharacterPersistenceResult.Failed(CharacterPersistenceFailure.NotOwned);
            }

            public CharacterPersistenceResult Save(SessionId s, PersistedCharacter c, int r)
                => CharacterPersistenceResult.Saved(r + 1);
        }

        /// <summary>A file's code, with its comments removed.</summary>
        private static string CodeWithoutComments(string path)
        {
            var code = new System.Text.StringBuilder();

            foreach (string line in System.IO.File.ReadAllLines(path))
            {
                string trimmed = line.TrimStart();

                if (trimmed.StartsWith("//") || trimmed.StartsWith("*")
                    || trimmed.StartsWith("/*")) continue;

                int comment = line.IndexOf("//", System.StringComparison.Ordinal);

                code.AppendLine(comment >= 0 ? line.Substring(0, comment) : line);
            }

            return code.ToString();
        }
    }
}
