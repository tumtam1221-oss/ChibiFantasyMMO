using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace ChibiFantasy.UI
{
    /// <summary>
    /// The list of quests a player is carrying.
    /// </summary>
    /// <remarks>
    /// <b>Snapshots and a click event.</b> It holds <see cref="QuestViewData"/> copies, so
    /// there is no quest here to advance or complete. Selecting one raises an event; the
    /// controller decides what that means.
    ///
    /// <b>Rows are reused.</b> The pool grows to the longest list ever shown and is
    /// re-labelled after that, so a refresh instantiates nothing -- the same rule the
    /// container panel keeps.
    ///
    /// <b>It is told, not polled.</b> <see cref="Show"/> is called after a quest actually
    /// changed, which is what keeps quest tracking off the frame budget entirely.
    /// </remarks>
    public sealed class QuestTrackerView : MonoBehaviour
    {
        [SerializeField] private RectTransform rowParent;
        [SerializeField] private float rowHeight = 46f;
        [SerializeField] private float width = 240f;

        [SerializeField] private Color activeColor = new Color(0.16f, 0.17f, 0.20f, 0.92f);
        [SerializeField] private Color readyColor = new Color(0.18f, 0.30f, 0.18f, 0.95f);

        private readonly List<Button> _rows = new List<Button>();
        private readonly List<Text> _labels = new List<Text>();
        private readonly List<Image> _backgrounds = new List<Image>();
        private readonly List<QuestViewData> _quests = new List<QuestViewData>();

        /// <summary>Raised with the quest a player picked.</summary>
        public event System.Action<ChibiFantasy.Core.DefinitionId> QuestSelected;

        public int RowCount => _quests.Count;

        public IReadOnlyList<QuestViewData> Quests => _quests;

        /// <summary>Where keys are translated. Optional.</summary>
        public ILocalizedTextSource Text { get; set; }

        /// <summary>Creates the container when a prefab was not authored.</summary>
        public void EnsureVisuals()
        {
            if (rowParent == null) rowParent = GetComponent<RectTransform>();
            if (rowParent == null) rowParent = gameObject.AddComponent<RectTransform>();

            var layout = rowParent.GetComponent<VerticalLayoutGroup>();
            if (layout == null) layout = rowParent.gameObject.AddComponent<VerticalLayoutGroup>();

            layout.spacing = 2f;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.padding = new RectOffset(2, 2, 2, 2);
        }

        /// <summary>
        /// Draws a set of quests.
        /// </summary>
        /// <remarks>Completed quests are not filtered out here: which quests belong in a
        /// tracker is the controller's decision, and a view that filtered would be making
        /// one.</remarks>
        public void Show(IReadOnlyList<QuestViewData> quests)
        {
            EnsureVisuals();

            _quests.Clear();
            if (quests != null)
            {
                for (int i = 0; i < quests.Count; i++)
                {
                    if (quests[i].IsValid) _quests.Add(quests[i]);
                }
            }

            EnsurePool(_quests.Count);

            for (int i = 0; i < _rows.Count; i++)
            {
                bool used = i < _quests.Count;
                _rows[i].gameObject.SetActive(used);
                if (!used) continue;

                QuestViewData quest = _quests[i];

                _labels[i].text = FormatRow(quest, Text);
                _backgrounds[i].color = quest.IsReadyToComplete ? readyColor : activeColor;
            }
        }

        public void Clear()
        {
            _quests.Clear();
            for (int i = 0; i < _rows.Count; i++) _rows[i].gameObject.SetActive(false);
        }

        /// <summary>Picks by position. What a row click calls.</summary>
        public void Select(int index)
        {
            if (index < 0 || index >= _quests.Count) return;

            var handler = QuestSelected;
            if (handler != null) handler(_quests[index].QuestId);
        }

        /// <summary>
        /// One tracker line: the name, then each objective's progress.
        /// </summary>
        /// <remarks>Static and string-returning so the formatting rule tests without a
        /// Canvas. Every figure arrived resolved; nothing is counted here.</remarks>
        public static string FormatRow(QuestViewData quest, ILocalizedTextSource text)
        {
            if (!quest.IsValid) return string.Empty;

            var builder = new StringBuilder();

            builder.Append(quest.NameKey.IsValid
                ? LocalizedText.Resolve(text, quest.NameKey)
                : quest.QuestId.ToString());

            if (quest.IsReadyToComplete) builder.Append(" (complete)");

            for (int i = 0; i < quest.Objectives.Count; i++)
            {
                QuestObjectiveViewData objective = quest.Objectives[i];

                builder.Append("\n  ").Append(objective.Type).Append(' ');

                if (objective.TargetNameKey.IsValid)
                {
                    builder.Append(LocalizedText.Resolve(text, objective.TargetNameKey)).Append(' ');
                }
                else if (objective.Target.IsValid)
                {
                    builder.Append(objective.Target).Append(' ');
                }

                builder.Append(objective.Current).Append('/').Append(objective.Required);
            }

            return builder.ToString();
        }

        private void EnsurePool(int count)
        {
            for (int i = _rows.Count; i < count; i++)
            {
                int index = i;

                var go = new GameObject("Quest" + i, typeof(RectTransform));
                go.transform.SetParent(rowParent, false);

                var rect = (RectTransform)go.transform;
                rect.sizeDelta = new Vector2(width, rowHeight);

                var image = go.AddComponent<Image>();
                image.color = activeColor;

                var button = go.AddComponent<Button>();
                button.onClick.AddListener(() => Select(index));

                var textGo = new GameObject("Label", typeof(RectTransform));
                textGo.transform.SetParent(go.transform, false);
                var textRect = (RectTransform)textGo.transform;
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = new Vector2(6f, 2f);
                textRect.offsetMax = new Vector2(-6f, -2f);

                var label = textGo.AddComponent<Text>();
                label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                label.fontSize = 11;
                label.alignment = TextAnchor.UpperLeft;
                label.color = Color.white;
                label.raycastTarget = false;
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.verticalOverflow = VerticalWrapMode.Overflow;

                _rows.Add(button);
                _labels.Add(label);
                _backgrounds.Add(image);
            }
        }
    }
}
