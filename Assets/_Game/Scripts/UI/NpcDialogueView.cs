using ChibiFantasy.Core;
using ChibiFantasy.Data;
using UnityEngine;
using UnityEngine.UI;

namespace ChibiFantasy.UI
{
    /// <summary>
    /// What opens when an NPC agrees to talk.
    /// </summary>
    /// <remarks>
    /// <b>The service entry, not the service.</b> This panel proves that a click reached the
    /// right townsperson and that the server authorised the right role. It does not sell
    /// anything, bank anything, upgrade anything or change anybody's class -- those are
    /// their own gates, and each will replace the body of this panel rather than add a
    /// second door beside it.
    ///
    /// <b>Keyed by role, never by NPC.</b> There is no blacksmith branch and no merchant
    /// branch. The role is a closed technical category the server already resolved, so the
    /// panel can say what it is without knowing who said it -- which is what stops "which
    /// NPC is this" spreading into the UI, where it always ends up as a switch on a name.
    ///
    /// <b>Text comes through the localization source, with a readable fallback.</b> This
    /// project ships no translations yet; a panel that waited for them would be blank.
    /// </remarks>
    public sealed class NpcDialogueView : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private Text titleLabel;
        [SerializeField] private Text bodyLabel;
        [SerializeField] private Button closeButton;
        [SerializeField] private Image blocker;
        [SerializeField] private Button acceptButton;
        [SerializeField] private Text acceptLabel;
        private Text closeLabel;

        /// <summary>Raised when the player closes the panel.</summary>
        public event System.Action Closed;

        /// <summary>Raised when the player takes the quest this NPC was offering.</summary>
        public event System.Action<DefinitionId> QuestAccepted;

        /// <summary>Raised when the player hands a finished quest back to this NPC.</summary>
        public event System.Action<DefinitionId> QuestTurnedIn;

        /// <summary>Whether the quest on screen is being handed in rather than taken.</summary>
        public bool IsTurningIn { get; private set; }

        /// <summary>The quest being offered, if any. Invalid when the NPC offers none.</summary>
        public DefinitionId OfferedQuest { get; private set; }

        /// <summary>Whether an Accept button is on screen.</summary>
        public bool IsOfferingAQuest => OfferedQuest.IsValid;

        /// <summary>Whether the panel is on screen.</summary>
        public bool IsVisible { get; private set; }

        /// <summary>
        /// Whether the world is currently taking clicks.
        /// </summary>
        /// <remarks>True exactly while a conversation is open. Exposed so a test can prove
        /// the modal exists without driving a mouse through the event system.</remarks>
        public bool BlocksTheWorld =>
            blocker != null && blocker.gameObject.activeSelf && blocker.raycastTarget;

        /// <summary>Which NPC is being talked to. Invalid when hidden.</summary>
        public DefinitionId Npc { get; private set; }

        /// <summary>Which service the server authorised.</summary>
        public NpcRole Role { get; private set; }

        /// <summary>What the panel currently reads at the top.</summary>
        public string Title { get; private set; } = string.Empty;

        /// <summary>What the panel currently reads in the body.</summary>
        public string Body { get; private set; } = string.Empty;

        /// <summary>Where keys are translated. Optional.</summary>
        public ILocalizedTextSource Text { get; set; }

        /// <summary>Creates the child graphics when a prefab was not authored.</summary>
        public void EnsureVisuals()
        {
            if (root == null) root = gameObject;

            var background = GetComponent<Image>();

            if (background == null)
            {
                background = gameObject.AddComponent<Image>();
                background.color = new Color(0.09f, 0.10f, 0.13f, 0.94f);
            }

            if (titleLabel == null)
            {
                titleLabel = WorldUiBuilder.CreateLabel(transform, 16);
                titleLabel.name = "Title";
            }

            if (bodyLabel == null)
            {
                bodyLabel = WorldUiBuilder.CreateLabel(transform, 13);
                bodyLabel.name = "Body";
                bodyLabel.rectTransform.anchoredPosition = new Vector2(12f, -38f);

                // Tall enough for an offer, and stopping well above the buttons. Left at
                // the default the text ran straight down over them and out of the panel,
                // which is what the first manual test saw.
                bodyLabel.rectTransform.sizeDelta = new Vector2(-24f, 210f);
                bodyLabel.color = new Color(0.84f, 0.87f, 0.92f);
            }

            if (closeButton == null)
            {
                closeButton = WorldUiBuilder.CreateButton(transform,
                    UiText.Of(Text, UiStrings.CommonClose), new Vector2(8f, 8f));

                closeLabel = closeButton.GetComponentInChildren<Text>();

                closeButton.onClick.AddListener(Close);
            }

            if (acceptButton == null)
            {
                acceptButton = WorldUiBuilder.CreateButton(transform,
                    UiText.Of(Text, UiStrings.CommonAccept), new Vector2(126f, 8f));

                acceptButton.onClick.AddListener(PressQuestButton);

                acceptLabel = acceptButton.GetComponentInChildren<Text>();
            }

            EnsureBlocker();

            Hide();
        }

        /// <summary>
        /// Opens the panel with a quest on offer.
        /// </summary>
        /// <remarks>
        /// <b>This is where a quest is taken, and deliberately the only place.</b> The
        /// journal can show what is on offer anywhere in the world; taking it means standing
        /// in front of the person handing it out. The server enforces that independently, so
        /// this is the half that makes the rule visible rather than a button that fails.
        ///
        /// <b>The offer is formatted by the journal's own formatter.</b> Objective counts and
        /// reward lines are written in exactly one place in this project; a second copy here
        /// would be the one that still said 100 EXP after somebody changed the definition.
        /// </remarks>
        public void ShowQuestOffer(DefinitionId npc, string name, QuestViewData quest)
        {
            Show(npc, NpcRole.Quest, name);

            if (!quest.IsValid) return;

            OfferedQuest = quest.QuestId;

            // Said outright, because the offer and the hand-back are the same panel with
            // the same shape and one word different on one button. A player who could not
            // tell them apart read a repeatable quest they had already run as a quest that
            // had somehow completed itself.
            Body = Body + System.Environment.NewLine + System.Environment.NewLine
                + UiText.Of(Text, UiStrings.NpcQuestOffered)
                + System.Environment.NewLine
                + QuestListView.FormatDetail(quest, Text);

            if (bodyLabel != null) bodyLabel.text = Body;

            if (acceptButton != null) acceptButton.gameObject.SetActive(true);
            if (acceptLabel != null) acceptLabel.text = UiText.Of(Text, UiStrings.CommonAccept);
            if (closeLabel != null) closeLabel.text = UiText.Of(Text, UiStrings.CommonClose);
        }

        /// <summary>
        /// Opens the panel with a finished quest to hand back.
        /// </summary>
        /// <remarks>
        /// <b>The other half of the offer, and the same shape.</b> A player who has done what
        /// was asked walks back to the person who asked and is paid. Showing the objectives
        /// again -- now reading 3 / 3 -- is what makes the button obviously the right one to
        /// press rather than a bare word on a panel.
        ///
        /// The reward lines are the same ones the offer showed, from the same formatter, so
        /// what a player was promised and what they are about to be paid cannot disagree.
        /// </remarks>
        public void ShowQuestTurnIn(DefinitionId npc, string name, QuestViewData quest)
        {
            Show(npc, NpcRole.Quest, name);

            if (!quest.IsValid) return;

            OfferedQuest = quest.QuestId;
            IsTurningIn = true;

            Body = Body + System.Environment.NewLine + System.Environment.NewLine
                + UiText.Of(Text, UiStrings.NpcQuestFinished)
                + System.Environment.NewLine
                + QuestListView.FormatDetail(quest, Text);

            if (bodyLabel != null) bodyLabel.text = Body;

            if (acceptButton != null) acceptButton.gameObject.SetActive(true);
            if (acceptLabel != null) acceptLabel.text = UiText.Of(Text, UiStrings.NpcButtonTurnIn);
            if (closeLabel != null) closeLabel.text = UiText.Of(Text, UiStrings.CommonClose);
        }

        /// <summary>Takes the offered quest, or hands the finished one back.</summary>
        /// <remarks>One button, because a conversation never has both to do at once: a quest
        /// is either on offer or finished, never the same quest in both states.</remarks>
        public void PressQuestButton()
        {
            if (!IsVisible || !OfferedQuest.IsValid) return;

            DefinitionId quest = OfferedQuest;

            if (IsTurningIn) QuestTurnedIn?.Invoke(quest);
            else QuestAccepted?.Invoke(quest);
        }

        /// <summary>Takes the offered quest. Kept as the name a test and a button both use.</summary>
        public void AcceptOffer()
        {
            PressQuestButton();
        }

        /// <summary>
        /// Builds the sheet that swallows clicks while somebody is talking.
        /// </summary>
        /// <remarks>
        /// <b>Why this is the whole of the modal, and why it is presentation.</b>
        /// <c>WorldPointerInput</c> already refuses any click that lands on UI -- it asks
        /// <c>IsPointerOverUi</c> before it walks anywhere. So a transparent full-screen
        /// raycast target is enough to stop the player wandering off mid-conversation, and it
        /// needs no change to movement, to the pointer, or to anything the server decides.
        /// Reaching into the locked input to add a "dialogue is open" flag would have put a
        /// UI concern inside movement, where the next person would have to know it was there.
        ///
        /// <b>A sibling, placed just behind the panel.</b> Not a child: a child would sit
        /// inside the panel's own rect and could not cover the screen, and covering the panel
        /// would eat the Close button -- leaving a conversation nobody could get out of.
        ///
        /// <b>Invisible rather than dimmed.</b> It is there to catch clicks, not to change
        /// how the town looks. Alpha zero still raycasts in uGUI; only an alpha hit-test
        /// threshold would change that, and none is set.
        /// </remarks>
        private void EnsureBlocker()
        {
            if (blocker != null) return;

            Transform parent = transform.parent;

            // With no parent there is no canvas to cover, which is a panel built loose in a
            // test. It still opens and closes; it simply has no screen to block.
            if (parent == null) return;

            var host = new GameObject("Dialogue Blocker", typeof(RectTransform));

            host.transform.SetParent(parent, false);

            var rect = (RectTransform)host.transform;

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            blocker = host.AddComponent<Image>();

            blocker.color = new Color(0f, 0f, 0f, 0f);
            blocker.raycastTarget = true;

            // Immediately behind the panel, so the panel and its Close button stay reachable
            // and everything further back does not.
            host.transform.SetSiblingIndex(transform.GetSiblingIndex());
        }

        /// <summary>
        /// Opens the panel for an authorised interaction.
        /// </summary>
        /// <param name="npc">Who is being talked to.</param>
        /// <param name="role">What the server allowed. Decides what the panel says.</param>
        /// <param name="name">The NPC's display name, already resolved by the caller.</param>
        public void Show(DefinitionId npc, NpcRole role, string name)
        {
            EnsureVisuals();

            Npc = npc;
            Role = role;
            IsVisible = true;

            Title = string.IsNullOrEmpty(name) ? npc.ToString() : name;
            Body = BodyFor(role, Text);

            if (titleLabel != null) titleLabel.text = Title;
            if (bodyLabel != null) bodyLabel.text = Body;
            if (root != null) root.SetActive(true);

            // Raised with the panel, so there is no frame in which the conversation is up
            // and a click still walks away from it.
            if (blocker != null) blocker.gameObject.SetActive(true);

            // Show() is the plain conversation; a quest offer arrives through
            // ShowQuestOffer, which turns the button back on after calling this.
            OfferedQuest = default;
            IsTurningIn = false;

            if (acceptButton != null) acceptButton.gameObject.SetActive(false);
            if (acceptLabel != null) acceptLabel.text = UiText.Of(Text, UiStrings.CommonAccept);
        }

        /// <summary>Closes it, and tells whoever is listening.</summary>
        public void Close()
        {
            bool was = IsVisible;

            Hide();

            if (was) Closed?.Invoke();
        }

        /// <summary>Puts it away without raising anything.</summary>
        public void Hide()
        {
            Npc = default;
            Role = NpcRole.Generic;
            OfferedQuest = default;
            IsTurningIn = false;
            IsVisible = false;

            if (acceptButton != null) acceptButton.gameObject.SetActive(false);
            Title = string.Empty;
            Body = string.Empty;

            if (titleLabel != null) titleLabel.text = string.Empty;
            if (bodyLabel != null) bodyLabel.text = string.Empty;
            if (root != null) root.SetActive(false);

            // Dropped with it. A blocker left behind is an invisible sheet over the whole
            // world that nothing would ever explain.
            if (blocker != null) blocker.gameObject.SetActive(false);
        }

        /// <summary>The localization key a role's text is looked up under.</summary>
        public static LocalizationKey KeyFor(NpcRole role)
        {
            return new LocalizationKey("npc.service." + role.ToString().ToLowerInvariant());
        }

        /// <summary>
        /// What the panel says for a role.
        /// </summary>
        /// <remarks>
        /// <b>Keyed by role, never by NPC.</b> Each role opens a different screen and is
        /// validated differently -- the same reason <c>NpcRole</c> exists at all. Looking a
        /// line up by NPC id here is the thing that is forbidden, and there is no NPC id in
        /// scope to look one up by.
        ///
        /// The English ships in <see cref="UiStrings"/> under the same keys, so a project
        /// with no translation files at all still has a readable panel.
        /// </remarks>
        public static string BodyFor(NpcRole role, ILocalizedTextSource text)
        {
            return UiText.Of(text, KeyFor(role).Key);
        }

        /// <summary>
        /// What to tell a player whose interaction was refused.
        /// </summary>
        /// <remarks>Only the reasons a player can act on are worded for them. The rest are
        /// content or wiring faults they cannot do anything about, and telling somebody
        /// "RoleUnavailable" is telling them nothing -- so those become one honest sentence
        /// and the detail stays in the log.</remarks>
        public static string RefusalText(NpcInteractionRejectionCode reason,
            ILocalizedTextSource text = null)
        {
            return UiText.Of(text, RefusalKey(reason));
        }

        /// <summary>Which of the four sentences a refusal reason is worded as.</summary>
        private static string RefusalKey(NpcInteractionRejectionCode reason)
        {
            switch (reason)
            {
                case NpcInteractionRejectionCode.TooFar: return UiStrings.NpcRefusedTooFar;
                case NpcInteractionRejectionCode.WrongMap: return UiStrings.NpcRefusedWrongMap;
                case NpcInteractionRejectionCode.NpcDisabled: return UiStrings.NpcRefusedDisabled;
                default: return UiStrings.NpcRefusedOther;
            }
        }
    }

    /// <summary>
    /// The refusal reasons this UI words differently, by value.
    /// </summary>
    /// <remarks>
    /// <b>A mirror, and deliberately a thin one.</b> The UI assembly cannot see
    /// <c>NpcInteractionRejection</c> -- Gameplay is not one of its references and making it
    /// one to read an enum would drag a rules assembly into a presentation one. The numbers
    /// are the same numbers, and <c>NpcDialogueRejectionParity</c> in the tests fails if
    /// anybody changes one without the other.
    /// </remarks>
    public enum NpcInteractionRejectionCode
    {
        None = 0,
        MissingContext = 1,
        UnknownNpc = 2,
        NpcDisabled = 3,
        WrongMap = 4,
        TooFar = 5,
        RoleNotOffered = 6,
        RoleUnavailable = 7,
        NpcNotPlaced = 8
    }
}
