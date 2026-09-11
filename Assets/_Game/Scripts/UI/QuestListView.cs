using System.Collections.Generic;
using System.Text;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using UnityEngine;
using UnityEngine.UI;

namespace ChibiFantasy.UI
{
    /// <summary>Which half of the journal is being looked at.</summary>
    public enum QuestListTab
    {
        /// <summary>Quests that could be taken now.</summary>
        Available = 0,

        /// <summary>Quests taken and not yet handed in.</summary>
        Active = 1,

        /// <summary>
        /// Quests already handed in.
        /// </summary>
        /// <remarks>
        /// <b>The tab that was missing, and what its absence looked like.</b> Handing in the
        /// only quest a character had emptied both other tabs, cleared the mark above the
        /// giver's head, and left a journal reading "Nothing here." -- every one of which was
        /// correct, and which together read exactly like a broken quest system. A player has
        /// no way to tell "you have done everything" from "the game lost your quests" unless
        /// the game can show them what they have done.
        ///
        /// The log already remembered completed quests; the migration's own note says they
        /// are kept rather than deleted because prerequisites and non-repeatable quests both
        /// need the history. This is the half of that decision the player can see.
        /// </remarks>
        Completed = 2
    }

    /// <summary>
    /// The quest journal: what is on offer, what is under way, and what each pays.
    /// </summary>
    /// <remarks>
    /// <b>It draws; it decides nothing.</b> Both lists arrive already built, acceptance is an
    /// event somebody else acts on, and every number on screen comes out of
    /// <see cref="QuestViewData"/> -- which was built from the definition and the player's
    /// authoritative log. There is no quest name, objective count or reward amount written
    /// anywhere in this file, which is what stops the panel disagreeing with the game the
    /// first time content changes.
    ///
    /// <b>Rewards are drawn by kind, not by name.</b> Experience reads as a number, an item
    /// reads as a name and a count, and anything else falls back to saying what it is. A new
    /// reward type therefore appears -- honestly, if plainly -- instead of vanishing.
    ///
    /// <b>Accepting is the NPC's job, and the panel says so.</b> A quest that a giver hands
    /// out cannot be taken from a bench across town; the button is replaced by the name of
    /// the person to go and see. The server enforces that independently -- this is the half
    /// that explains it rather than leaving a button that always fails.
    /// </remarks>
    public sealed class QuestListView : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private Text titleLabel;
        [SerializeField] private Text listLabel;
        [SerializeField] private Text detailLabel;
        [SerializeField] private Button availableTab;
        [SerializeField] private Button activeTab;
        [SerializeField] private Button completedTab;
        [SerializeField] private Button acceptButton;
        [SerializeField] private Text acceptLabel;
        [SerializeField] private Button closeButton;
        [SerializeField] private Image blocker;

        private readonly List<QuestViewData> _available = new List<QuestViewData>();
        private readonly List<QuestViewData> _active = new List<QuestViewData>();
        private readonly List<QuestViewData> _completed = new List<QuestViewData>();

        private Text availableTabLabel;
        private Text activeTabLabel;
        private Text completedTabLabel;
        private Text closeLabel;

        private readonly List<Button> _rowButtons = new List<Button>();
        private readonly List<Text> _rowLabels = new List<Text>();
        private readonly List<Image> _rowBackgrounds = new List<Image>();

        private RectTransform rowHost;

        /// <summary>Where the list column starts and how tall a row is.</summary>
        /// <remarks>Matched to the list label this replaced, so the panel's proportions are
        /// unchanged: the left column is the same strip it always was.</remarks>
        private const float RowTop = -40f;
        private const float RowHeight = 24f;
        private const float RowStep = 26f;
        private const float RowWidth = 190f;

        /// <summary>Raised when the player asks to take the selected quest.</summary>
        public event System.Action<DefinitionId> AcceptRequested;

        /// <summary>Raised when the panel closes.</summary>
        public event System.Action Closed;

        /// <summary>Whether the journal is on screen.</summary>
        public bool IsVisible { get; private set; }

        /// <summary>Which tab is being shown.</summary>
        public QuestListTab Tab { get; private set; } = QuestListTab.Available;

        /// <summary>Which quest the detail pane is describing. Invalid when none.</summary>
        public DefinitionId Selected { get; private set; }

        /// <summary>
        /// Whether this panel stops the world taking clicks. Always false, by decision.
        /// </summary>
        /// <remarks>Kept rather than deleted so the difference from the NPC dialogue -- which
        /// does block -- is a stated property with a test behind it, not something a reader
        /// has to infer from missing code.</remarks>
        public bool BlocksTheWorld => false;

        /// <summary>What the list column currently reads.</summary>
        public string ListText { get; private set; } = string.Empty;

        /// <summary>What the detail column currently reads.</summary>
        public string DetailText { get; private set; } = string.Empty;

        /// <summary>Whether the Accept button can be pressed right now.</summary>
        public bool CanAccept { get; private set; }

        /// <summary>Where keys are translated. Optional.</summary>
        public ILocalizedTextSource Text { get; set; }

        /// <summary>
        /// Answers who hands a quest out, so the panel can say where to go.
        /// </summary>
        /// <remarks>A delegate rather than a registry, because the panel has no business
        /// holding content: it asks a question and draws the answer.</remarks>
        public System.Func<DefinitionId, string> GiverName { get; set; }

        /// <summary>Whether the player is close enough to a giver to take a quest now.</summary>
        /// <remarks>Supplied so the button can be right rather than optimistic. Defaults to
        /// "no", which is the honest answer for a journal opened in the middle of a field.</remarks>
        public System.Func<DefinitionId, bool> CanAcceptHere { get; set; }

        // ---- building ---------------------------------------------------------------------

        /// <summary>Creates the child graphics when a prefab was not authored.</summary>
        public void EnsureVisuals()
        {
            if (root == null) root = gameObject;

            var background = GetComponent<Image>();

            if (background == null)
            {
                background = gameObject.AddComponent<Image>();
                background.color = new Color(0.08f, 0.09f, 0.12f, 0.96f);
            }

            if (titleLabel == null)
            {
                titleLabel = WorldUiBuilder.CreateLabel(transform, 16);
                titleLabel.name = "Title";
                titleLabel.text = UiText.Of(Text, UiStrings.QuestTitle);
            }

            if (availableTab == null)
            {
                availableTab = WorldUiBuilder.CreateButton(transform,
                    UiText.Of(Text, UiStrings.QuestTabAvailable), new Vector2(8f, 8f));

                availableTabLabel = availableTab.GetComponentInChildren<Text>();

                availableTab.onClick.AddListener(() => Show(QuestListTab.Available));
            }

            if (activeTab == null)
            {
                activeTab = WorldUiBuilder.CreateButton(transform,
                    UiText.Of(Text, UiStrings.QuestTabActive), new Vector2(126f, 8f));

                activeTabLabel = activeTab.GetComponentInChildren<Text>();

                activeTab.onClick.AddListener(() => Show(QuestListTab.Active));
            }

            if (completedTab == null)
            {
                completedTab = WorldUiBuilder.CreateButton(transform,
                    UiText.Of(Text, UiStrings.QuestTabCompleted), new Vector2(244f, 8f));

                completedTabLabel = completedTab.GetComponentInChildren<Text>();

                completedTab.onClick.AddListener(() => Show(QuestListTab.Completed));
            }

            if (listLabel == null)
            {
                listLabel = WorldUiBuilder.CreateLabel(transform, 13);
                listLabel.name = "List";
                listLabel.rectTransform.anchoredPosition = new Vector2(8f, RowTop);
                listLabel.rectTransform.sizeDelta = new Vector2(-330f, 300f);
            }

            if (rowHost == null)
            {
                var host = new GameObject("Rows", typeof(RectTransform));

                host.transform.SetParent(transform, false);

                rowHost = (RectTransform)host.transform;
                rowHost.anchorMin = new Vector2(0f, 1f);
                rowHost.anchorMax = new Vector2(0f, 1f);
                rowHost.pivot = new Vector2(0f, 1f);
                rowHost.anchoredPosition = new Vector2(8f, RowTop);
                rowHost.sizeDelta = new Vector2(RowWidth, 300f);
            }

            if (detailLabel == null)
            {
                detailLabel = WorldUiBuilder.CreateLabel(transform, 13);
                detailLabel.name = "Detail";
                detailLabel.rectTransform.anchoredPosition = new Vector2(210f, -40f);
                detailLabel.rectTransform.sizeDelta = new Vector2(-220f, 300f);
                detailLabel.color = new Color(0.86f, 0.89f, 0.94f);
            }

            // Right of the three tabs, which end at 354 on a 640-wide panel. A fourth tab
            // would collide with this row and is the thing to notice before adding one.
            if (acceptButton == null)
            {
                acceptButton = WorldUiBuilder.CreateButton(transform,
                    UiText.Of(Text, UiStrings.CommonAccept), new Vector2(400f, 8f));

                acceptLabel = acceptButton.GetComponentInChildren<Text>();

                acceptButton.onClick.AddListener(Accept);
            }

            if (closeButton == null)
            {
                closeButton = WorldUiBuilder.CreateButton(transform,
                    UiText.Of(Text, UiStrings.CommonClose), new Vector2(518f, 8f));

                closeLabel = closeButton.GetComponentInChildren<Text>();

                closeButton.onClick.AddListener(Close);
            }

            EnsureBlocker();

            Hide();
        }

        /// <summary>
        /// The journal deliberately does not freeze the world.
        /// </summary>
        /// <remarks>
        /// <b>Only a conversation pins the player in place.</b> A dialogue is something
        /// somebody is saying to you and walking off mid-sentence makes no sense; a journal
        /// is a window you opened yourself, possibly in the middle of a field, and being
        /// unable to walk away from it reads as the game having hung.
        ///
        /// <b>The panel still swallows its own clicks.</b> Its background is a raycast
        /// target, and <c>WorldPointerInput</c> refuses any click that lands on UI -- so
        /// pressing a tab does not also order a walk to whatever is behind it. What stays
        /// live is the ground around the panel, which is the point.
        ///
        /// This method remains as the one place that decision is written down, so the next
        /// person wondering where the journal's modal went finds the answer rather than the
        /// absence of one.
        /// </remarks>
        private void EnsureBlocker()
        {
            blocker = null;
        }

        // ---- opening and closing ----------------------------------------------------------

        /// <summary>Supplies the lists. Safe to call while open.</summary>
        /// <remarks>Completed is optional so a caller with no history to show -- a test, or a
        /// screen built before the tab existed -- still compiles and simply shows an empty
        /// third tab, which is the truth for a character who has finished nothing.</remarks>
        public void Bind(IReadOnlyList<QuestViewData> available,
            IReadOnlyList<QuestViewData> active,
            IReadOnlyList<QuestViewData> completed = null)
        {
            _available.Clear();
            _active.Clear();
            _completed.Clear();

            if (available != null) _available.AddRange(available);
            if (active != null) _active.AddRange(active);
            if (completed != null) _completed.AddRange(completed);

            // A selection that has left the tab it was on -- the quest was just accepted --
            // falls back to nothing rather than describing a quest the list no longer shows.
            if (Selected.IsValid && IndexOf(Current, Selected) < 0) Selected = default;

            if (IsVisible) Redraw();
        }

        /// <summary>Opens the journal on a tab.</summary>
        public void Show(QuestListTab tab)
        {
            EnsureVisuals();

            Tab = tab;
            IsVisible = true;

            if (Selected.IsValid && IndexOf(Current, Selected) < 0) Selected = default;

            // Land on something rather than an empty pane: a journal that opens blank reads
            // as broken even when the list beside it is full.
            if (!Selected.IsValid && Current.Count > 0) Selected = Current[0].QuestId;

            if (root != null) root.SetActive(true);

            Redraw();
        }

        /// <summary>Opens it where it was last, or on Available the first time.</summary>
        public void Show() => Show(Tab);

        /// <summary>Closes it, and tells whoever is listening.</summary>
        public void Close()
        {
            bool was = IsVisible;

            Hide();

            if (was) Closed?.Invoke();
        }

        /// <summary>Opens if closed, closes if open. What a hotkey does.</summary>
        public void Toggle()
        {
            if (IsVisible) Close();
            else Show();
        }

        /// <summary>Puts it away without raising anything.</summary>
        public void Hide()
        {
            IsVisible = false;
            Selected = default;
            ListText = string.Empty;
            DetailText = string.Empty;
            CanAccept = false;

            // The rows go with it, or a closed journal leaves a column of buttons floating
            // over the world that still take clicks.
            for (var i = 0; i < _rowButtons.Count; i++)
            {
                if (_rowButtons[i] != null) _rowButtons[i].gameObject.SetActive(false);
            }

            if (listLabel != null) listLabel.text = string.Empty;
            if (detailLabel != null) detailLabel.text = string.Empty;
            if (root != null) root.SetActive(false);
        }

        /// <summary>Describes one quest in the detail pane.</summary>
        public void Select(DefinitionId quest)
        {
            if (IndexOf(Current, quest) < 0) return;

            Selected = quest;

            Redraw();
        }

        /// <summary>Selects by position. What a list button calls.</summary>
        public void Select(int index)
        {
            IReadOnlyList<QuestViewData> list = Current;

            if (index < 0 || index >= list.Count) return;

            Select(list[index].QuestId);
        }

        /// <summary>Asks to take the selected quest.</summary>
        public void Accept()
        {
            if (!IsVisible || !CanAccept || !Selected.IsValid) return;

            AcceptRequested?.Invoke(Selected);
        }

        /// <summary>The list being shown.</summary>
        public IReadOnlyList<QuestViewData> Current
        {
            get
            {
                switch (Tab)
                {
                    case QuestListTab.Active: return _active;
                    case QuestListTab.Completed: return _completed;
                    default: return _available;
                }
            }
        }

        // ---- drawing ----------------------------------------------------------------------

        private void Redraw()
        {
            IReadOnlyList<QuestViewData> list = Current;

            ListText = FormatList(list, Selected, Text);

            int at = IndexOf(list, Selected);

            DetailText = at < 0
                ? UiText.Of(Text, UiStrings.QuestSelect)
                : FormatDetail(list[at], Text, GiverName);

            CanAccept = at >= 0
                && Tab == QuestListTab.Available
                && (CanAcceptHere == null || CanAcceptHere(Selected));

            // A finished quest is paid by the person who asked for it, so say who that is
            // rather than leaving a player with a full counter and nothing to press.
            if (at >= 0 && Tab == QuestListTab.Active && list[at].IsReadyToComplete)
            {
                string who = GiverName == null ? null : GiverName(Selected);

                DetailText = DetailText + System.Environment.NewLine
                    + System.Environment.NewLine
                    + (string.IsNullOrEmpty(who)
                        ? UiText.Of(Text, UiStrings.QuestHintTurnInUnknown)
                        : UiText.Format(Text, UiStrings.QuestHintTurnInNamed, who));
            }

            // A greyed button with no explanation is the thing a player reads as broken.
            // Say where to go instead; the name comes from the same place the detail does.
            if (at >= 0 && Tab == QuestListTab.Available && !CanAccept)
            {
                string giver = GiverName == null ? null : GiverName(Selected);

                DetailText = DetailText + System.Environment.NewLine
                    + System.Environment.NewLine
                    + (string.IsNullOrEmpty(giver)
                        ? UiText.Of(Text, UiStrings.QuestHintAcceptUnknown)
                        : UiText.Format(Text, UiStrings.QuestHintAcceptNamed, giver));
            }

            // History needs no call to action. Saying so is what turns an empty-looking
            // panel into a record of something the player did.
            if (at >= 0 && Tab == QuestListTab.Completed)
            {
                // A repeatable quest in the history is not closed, and saying "finished,
                // thank you" about one a player can take again is the sentence that makes
                // its reappearance look like a fault.
                DetailText = DetailText + System.Environment.NewLine
                    + System.Environment.NewLine
                    + UiText.Of(Text, list[at].Repeatable
                        ? UiStrings.QuestFinishedRepeatableNote
                        : UiStrings.QuestFinishedNote);
            }

            DrawRows(list);

            // Only the empty message now: the quests themselves are buttons, because a
            // player has to be able to read the second one.
            if (listLabel != null)
            {
                listLabel.text = list == null || list.Count == 0 ? ListText : string.Empty;
            }
            if (detailLabel != null) detailLabel.text = DetailText;
            if (acceptButton != null) acceptButton.interactable = CanAccept;

            if (acceptLabel != null) acceptLabel.text = AcceptWord(at);

            if (titleLabel != null) titleLabel.text = UiText.Of(Text, TitleKey(Tab));

            RelabelChrome();
        }

        /// <summary>
        /// Draws one clickable row per quest.
        /// </summary>
        /// <remarks>
        /// <b>The bug this closes.</b> The list column was a single <c>Text</c>. It looked
        /// like a list -- one quest per line, the selected one marked with "&gt;" -- and
        /// nothing in it could be clicked. <see cref="Select(int)"/> was public and no caller
        /// existed, so the only quest a player could ever read was whichever one
        /// <see cref="Show"/> happened to land on. With one quest in a tab that is invisible;
        /// with two, the second is simply unreachable.
        ///
        /// <b>Pooled, not rebuilt.</b> Rows are created once and re-labelled after that, the
        /// same rule the tracker and the container panel keep, so opening the journal does
        /// not allocate a row per quest per open.
        /// </remarks>
        private void DrawRows(IReadOnlyList<QuestViewData> list)
        {
            int count = list == null ? 0 : list.Count;

            EnsureRows(count);

            for (var i = 0; i < _rowButtons.Count; i++)
            {
                bool used = i < count;

                _rowButtons[i].gameObject.SetActive(used);

                if (!used) continue;

                QuestViewData quest = list[i];

                // Marked in the list too, so the difference is visible without opening each
                // quest in turn -- which is the whole point of having a list.
                string name = NameOf(quest, Text);

                _rowLabels[i].text = quest.ResetsDaily
                    ? UiText.Format(Text, UiStrings.QuestRowDaily, name)
                    : (quest.Repeatable
                        ? UiText.Format(Text, UiStrings.QuestRowRepeatable, name)
                        : name);

                // The selected row is lit rather than prefixed: the "&gt;" marker was doing
                // that job in a label nobody could click, and a highlight reads at a glance.
                bool selected = quest.QuestId == Selected;

                _rowBackgrounds[i].color = selected
                    ? new Color(0.22f, 0.34f, 0.48f, 1f)
                    : new Color(0.16f, 0.18f, 0.22f, 1f);

                _rowLabels[i].color = quest.IsReadyToComplete
                    ? new Color(0.72f, 0.94f, 0.74f)
                    : Color.white;
            }
        }

        private void EnsureRows(int count)
        {
            for (int i = _rowButtons.Count; i < count; i++)
            {
                int index = i;

                var go = new GameObject("Row " + i, typeof(RectTransform));

                go.transform.SetParent(rowHost, false);

                var rect = (RectTransform)go.transform;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.sizeDelta = new Vector2(RowWidth, RowHeight);
                rect.anchoredPosition = new Vector2(0f, -(i * RowStep));

                var image = go.AddComponent<Image>();

                var button = go.AddComponent<Button>();
                button.targetGraphic = image;
                button.onClick.AddListener(() => Select(index));

                var labelGo = new GameObject("Label", typeof(RectTransform));

                labelGo.transform.SetParent(go.transform, false);

                var labelRect = (RectTransform)labelGo.transform;
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = new Vector2(6f, 0f);
                labelRect.offsetMax = new Vector2(-4f, 0f);

                var label = labelGo.AddComponent<Text>();
                label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                label.fontSize = 13;
                label.alignment = TextAnchor.MiddleLeft;
                label.raycastTarget = false;
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.verticalOverflow = VerticalWrapMode.Truncate;

                _rowButtons.Add(button);
                _rowLabels.Add(label);
                _rowBackgrounds.Add(image);
            }
        }

        /// <summary>What the action button says on this tab.</summary>
        /// <remarks>One button across three tabs, because at most one of taking, waiting and
        /// remembering is ever the thing to do with the quest in front of you.</remarks>
        private string AcceptWord(int at)
        {
            switch (Tab)
            {
                case QuestListTab.Active:
                    return UiText.Of(Text, at >= 0 && Current[at].IsReadyToComplete
                        ? UiStrings.QuestButtonReady
                        : UiStrings.QuestButtonInProgress);

                case QuestListTab.Completed:
                    return UiText.Of(Text, UiStrings.QuestButtonDone);

                default:
                    return UiText.Of(Text, UiStrings.CommonAccept);
            }
        }

        private static string TitleKey(QuestListTab tab)
        {
            switch (tab)
            {
                case QuestListTab.Active: return UiStrings.QuestTitleActive;
                case QuestListTab.Completed: return UiStrings.QuestTitleCompleted;
                default: return UiStrings.QuestTitleAvailable;
            }
        }

        /// <summary>
        /// Re-reads the words on the buttons that never change meaning.
        /// </summary>
        /// <remarks>They are written once when the panel is built, which is before a player
        /// can have switched language. Redrawing them here costs three string assignments
        /// and is the difference between a language change that takes effect and one that
        /// takes effect on everything except the tabs.</remarks>
        private void RelabelChrome()
        {
            if (availableTabLabel != null)
            {
                availableTabLabel.text = UiText.Of(Text, UiStrings.QuestTabAvailable);
            }

            if (activeTabLabel != null)
            {
                activeTabLabel.text = UiText.Of(Text, UiStrings.QuestTabActive);
            }

            if (completedTabLabel != null)
            {
                completedTabLabel.text = UiText.Of(Text, UiStrings.QuestTabCompleted);
            }

            if (closeLabel != null) closeLabel.text = UiText.Of(Text, UiStrings.CommonClose);
        }

        private static int IndexOf(IReadOnlyList<QuestViewData> list, DefinitionId quest)
        {
            if (list == null || !quest.IsValid) return -1;

            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].QuestId == quest) return i;
            }

            return -1;
        }

        /// <summary>The left column: one quest per line, the selected one marked.</summary>
        public static string FormatList(IReadOnlyList<QuestViewData> list,
            DefinitionId selected, ILocalizedTextSource text)
        {
            if (list == null || list.Count == 0) return UiText.Of(text, UiStrings.QuestEmpty);

            var built = new StringBuilder();

            for (var i = 0; i < list.Count; i++)
            {
                built.Append(list[i].QuestId == selected ? "> " : "  ");
                built.AppendLine(NameOf(list[i], text));
            }

            return built.ToString().TrimEnd();
        }

        /// <summary>
        /// The right column: everything known about one quest.
        /// </summary>
        /// <remarks>Static and given its inputs so a test can assert exactly what a player
        /// reads without building a canvas -- the same arrangement the other world views
        /// use for their labels.</remarks>
        public static string FormatDetail(QuestViewData quest, ILocalizedTextSource text,
            System.Func<DefinitionId, string> giverName = null)
        {
            if (!quest.IsValid) return UiText.Of(text, UiStrings.QuestSelect);

            var built = new StringBuilder();

            built.AppendLine(NameOf(quest, text));

            // Said on every quest, not only the repeatable ones. A badge that appears on
            // some quests and not others leaves a player working out whether its absence
            // means "one time" or "nobody wrote it down" -- and the whole question a player
            // asks about a finished quest reappearing is which of the two this is.
            built.AppendLine(UiText.Of(text, quest.ResetsDaily
                ? UiStrings.QuestDaily
                : (quest.Repeatable ? UiStrings.QuestRepeatable : UiStrings.QuestOneTime)));

            built.AppendLine();

            string giver = giverName == null ? null : giverName(quest.QuestId);

            if (!string.IsNullOrEmpty(giver))
            {
                built.AppendLine(UiText.Of(text, UiStrings.QuestSectionGiver));
                built.AppendLine(giver);
                built.AppendLine();
            }

            string description = Translate(quest.DescriptionKey, text);

            if (!string.IsNullOrEmpty(description))
            {
                built.AppendLine(UiText.Of(text, UiStrings.QuestSectionDescription));
                built.AppendLine(description);
                built.AppendLine();
            }

            if (quest.LevelRequirement > 0)
            {
                built.AppendLine(UiText.Format(text, UiStrings.QuestRequiresLevel,
                    quest.LevelRequirement));
                built.AppendLine();
            }

            built.AppendLine(UiText.Of(text, UiStrings.QuestSectionObjective));

            IReadOnlyList<QuestObjectiveViewData> objectives = quest.Objectives;

            if (objectives == null || objectives.Count == 0)
            {
                built.AppendLine(UiText.Of(text, UiStrings.CommonNone));
            }
            else
            {
                for (var i = 0; i < objectives.Count; i++)
                {
                    built.AppendLine(FormatObjective(objectives[i], text));
                }
            }

            built.AppendLine();
            built.AppendLine(UiText.Of(text, UiStrings.QuestSectionRewards));

            IReadOnlyList<QuestRewardViewData> rewards = quest.Rewards;

            if (rewards == null || rewards.Count == 0)
            {
                built.AppendLine(UiText.Of(text, UiStrings.CommonNone));
            }
            else
            {
                for (var i = 0; i < rewards.Count; i++)
                {
                    built.AppendLine(FormatReward(rewards[i], text));
                }
            }

            return built.ToString().TrimEnd();
        }

        /// <summary>One objective, with progress when it has any.</summary>
        public static string FormatObjective(QuestObjectiveViewData objective,
            ILocalizedTextSource text)
        {
            // The target is content -- a monster, an item, a place -- so it falls back to
            // reading its identifier as words, never to showing a key.
            string target = UiText.ContentText(text, objective.TargetNameKey, objective.Target);

            string verb = UiText.Of(text, VerbKey(objective.Type));

            return objective.Required > 0
                ? UiText.Format(text, UiStrings.QuestObjectiveProgress,
                    verb, target, objective.Current, objective.Required)
                : UiText.Format(text, UiStrings.QuestObjectivePlain, verb, target);
        }

        /// <summary>
        /// One reward, drawn by kind.
        /// </summary>
        /// <remarks>
        /// <b>No reward is special-cased.</b> Experience is a number, an item is a name and a
        /// count, currency is a count. Anything a later gate adds falls through to naming its
        /// kind and its target, which is plain but true -- far better than a panel that
        /// silently omits a reward it does not recognise and leaves a player surprised.
        /// </remarks>
        public static string FormatReward(QuestRewardViewData reward,
            ILocalizedTextSource text)
        {
            switch (reward.Type)
            {
                case QuestRewardType.Experience:
                    return UiText.Format(text, UiStrings.QuestRewardExperience, reward.Amount);

                case QuestRewardType.Currency:
                    return UiText.Format(text, UiStrings.QuestRewardCurrency, reward.Amount);

                default:
                {
                    string name = UiText.ContentText(text, reward.TargetNameKey, reward.Target);

                    if (string.IsNullOrEmpty(name)) name = reward.Type.ToString();

                    return reward.Amount > 1
                        ? UiText.Format(text, UiStrings.QuestRewardStack, name, reward.Amount)
                        : name;
                }
            }
        }

        private static string VerbKey(QuestObjectiveType type)
        {
            switch (type)
            {
                case QuestObjectiveType.KillMonster: return UiStrings.QuestVerbDefeat;
                case QuestObjectiveType.CollectItem: return UiStrings.QuestVerbCollect;
                case QuestObjectiveType.TalkToNpc: return UiStrings.QuestVerbTalk;
                case QuestObjectiveType.ReachMap: return UiStrings.QuestVerbTravel;
                case QuestObjectiveType.ReachLevel: return UiStrings.QuestVerbReachLevel;
                case QuestObjectiveType.DeliverItem: return UiStrings.QuestVerbDeliver;
                default: return UiStrings.QuestVerbOther;
            }
        }

        private static string NameOf(QuestViewData quest, ILocalizedTextSource text)
        {
            return UiText.ContentText(text, quest.NameKey, quest.QuestId);
        }

        private static string Translate(LocalizationKey key, ILocalizedTextSource text)
        {
            if (text == null || !key.IsValid) return null;

            return text.TryGet(key, out string value) ? value : null;
        }

        /// <summary>
        /// An id read as words, for content with no translation yet.
        /// </summary>
        /// <remarks>"quest.harbor_first_hunt" reads as "Harbor First Hunt". Moved to
        /// <see cref="UiText.Readable"/> when the other views needed the same rule, and kept
        /// here as the name the journal's tests already call.</remarks>
        public static string Readable(DefinitionId id) => UiText.Readable(id);
    }
}
