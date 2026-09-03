using System.Text;
using ChibiFantasy.Data;
using UnityEngine;
using UnityEngine.UI;

namespace ChibiFantasy.UI
{
    /// <summary>
    /// One quest in full: what it asks, how far along it is, and what it pays.
    /// </summary>
    /// <remarks>
    /// <b>It draws a snapshot and raises events.</b> Accepting and turning in are requests;
    /// <c>QuestService</c> decides, and a refused turn-in leaves the panel showing exactly
    /// what it showed before, because a refused service call changed nothing to redraw.
    ///
    /// <b>The buttons are advisory.</b> Turn-in is offered when the snapshot says the quest
    /// is ready; whether the rewards actually fit is the service's answer, and a full bag
    /// is a normal refusal the panel reports.
    /// </remarks>
    public sealed class QuestDetailView : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private Text titleText;
        [SerializeField] private Text bodyText;
        [SerializeField] private Text resultText;
        [SerializeField] private Button acceptButton;
        [SerializeField] private Button turnInButton;

        /// <summary>Raised when the player asks to take the quest.</summary>
        public event System.Action Accepted;

        /// <summary>Raised when the player asks to hand it in.</summary>
        public event System.Action TurnedIn;

        public bool IsVisible { get; private set; }

        /// <summary>What the panel currently describes.</summary>
        public QuestViewData Data { get; private set; }

        public string Title { get; private set; }

        public string Body { get; private set; }

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
                background.color = new Color(0.09f, 0.10f, 0.13f, 0.96f);
            }

            if (titleText == null) titleText = CreateText("Title", 14, new Vector2(8f, -8f), 40f);
            if (bodyText == null) bodyText = CreateText("Body", 12, new Vector2(8f, -50f), 180f);
            if (resultText == null) resultText = CreateText("Result", 12, new Vector2(8f, -236f), 24f);

            if (acceptButton == null)
            {
                acceptButton = CreateButton("Accept", "Accept", new Vector2(8f, 8f));
                acceptButton.onClick.AddListener(RequestAccept);
            }

            if (turnInButton == null)
            {
                turnInButton = CreateButton("TurnIn", "Complete", new Vector2(126f, 8f));
                turnInButton.onClick.AddListener(RequestTurnIn);
            }

            Hide();
        }

        private Text CreateText(string childName, int size, Vector2 position, float height)
        {
            var go = new GameObject(childName, typeof(RectTransform));
            go.transform.SetParent(transform, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(-16f, height);

            var text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.alignment = TextAnchor.UpperLeft;
            text.color = Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private Button CreateButton(string childName, string label, Vector2 position)
        {
            var go = new GameObject(childName, typeof(RectTransform));
            go.transform.SetParent(transform, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(110f, 28f);

            var image = go.AddComponent<Image>();
            image.color = new Color(0.24f, 0.28f, 0.34f, 1f);

            var button = go.AddComponent<Button>();

            var textGo = new GameObject("Label", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);
            var textRect = (RectTransform)textGo.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var text = textGo.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 13;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.text = label;
            text.raycastTarget = false;

            return button;
        }

        /// <summary>Draws a quest, or hides the panel when there is none.</summary>
        public void Show(QuestViewData quest)
        {
            Data = quest;

            if (!quest.IsValid)
            {
                Hide();
                return;
            }

            Title = FormatTitle(quest, Text);
            Body = FormatBody(quest, Text);
            IsVisible = true;

            if (titleText != null) titleText.text = Title;
            if (bodyText != null) bodyText.text = Body;

            if (acceptButton != null)
            {
                acceptButton.gameObject.SetActive(quest.Status == QuestStatusView.NotStarted
                    || (quest.IsCompleted && quest.Repeatable));
            }

            if (turnInButton != null) turnInButton.gameObject.SetActive(quest.IsReadyToComplete);
            if (root != null) root.SetActive(true);
        }

        /// <summary>Reports what the last request did. Purely a message.</summary>
        public void ShowResult(string message)
        {
            if (resultText != null) resultText.text = message ?? string.Empty;
        }

        public void Hide()
        {
            Data = QuestViewData.None;
            Title = string.Empty;
            Body = string.Empty;
            IsVisible = false;

            if (titleText != null) titleText.text = string.Empty;
            if (bodyText != null) bodyText.text = string.Empty;
            if (resultText != null) resultText.text = string.Empty;
            if (acceptButton != null) acceptButton.gameObject.SetActive(false);
            if (turnInButton != null) turnInButton.gameObject.SetActive(false);
            if (root != null) root.SetActive(false);
        }

        /// <summary>Raises <see cref="Accepted"/>. Changes nothing by itself.</summary>
        public void RequestAccept()
        {
            if (!IsVisible) return;

            var handler = Accepted;
            if (handler != null) handler();
        }

        /// <summary>Raises <see cref="TurnedIn"/>. Changes nothing by itself.</summary>
        public void RequestTurnIn()
        {
            if (!IsVisible) return;

            var handler = TurnedIn;
            if (handler != null) handler();
        }

        public static string FormatTitle(QuestViewData quest, ILocalizedTextSource text)
        {
            if (!quest.IsValid) return string.Empty;

            string name = quest.NameKey.IsValid
                ? LocalizedText.Resolve(text, quest.NameKey)
                : quest.QuestId.ToString();

            return quest.QuestType == QuestType.Normal ? name : name + " [" + quest.QuestType + "]";
        }

        /// <summary>
        /// The description, the objectives with progress, and the rewards.
        /// </summary>
        /// <remarks>Static and string-returning so the formatting rules test without a
        /// Canvas. Every figure arrived resolved.</remarks>
        public static string FormatBody(QuestViewData quest, ILocalizedTextSource text)
        {
            if (!quest.IsValid) return string.Empty;

            var builder = new StringBuilder();

            if (quest.DescriptionKey.IsValid)
            {
                builder.Append(LocalizedText.Resolve(text, quest.DescriptionKey));
            }

            if (quest.LevelRequirement > 0)
            {
                builder.Append("\nLevel ").Append(quest.LevelRequirement);
            }

            builder.Append("\nStatus: ").Append(quest.Status);

            if (quest.Objectives.Count > 0)
            {
                builder.Append("\nObjectives:");

                for (int i = 0; i < quest.Objectives.Count; i++)
                {
                    QuestObjectiveViewData objective = quest.Objectives[i];

                    builder.Append("\n  ").Append(objective.Type).Append(' ');

                    if (objective.TargetNameKey.IsValid)
                    {
                        builder.Append(LocalizedText.Resolve(text, objective.TargetNameKey))
                            .Append(' ');
                    }
                    else if (objective.Target.IsValid)
                    {
                        builder.Append(objective.Target).Append(' ');
                    }

                    builder.Append(objective.Current).Append('/').Append(objective.Required);
                    if (objective.IsComplete) builder.Append(" done");
                }
            }

            if (quest.Rewards.Count == 0) return builder.ToString();

            builder.Append("\nRewards:");

            for (int i = 0; i < quest.Rewards.Count; i++)
            {
                QuestRewardViewData reward = quest.Rewards[i];

                builder.Append("\n  ").Append(reward.Type).Append(' ');

                if (reward.TargetNameKey.IsValid)
                {
                    builder.Append(LocalizedText.Resolve(text, reward.TargetNameKey)).Append(' ');
                }
                else if (reward.Target.IsValid)
                {
                    builder.Append(reward.Target).Append(' ');
                }

                builder.Append('x').Append(reward.Amount);
            }

            return builder.ToString();
        }
    }
}
