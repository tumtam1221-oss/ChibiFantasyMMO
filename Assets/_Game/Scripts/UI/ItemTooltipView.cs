using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace ChibiFantasy.UI
{
    /// <summary>
    /// Draws an <see cref="ItemTooltipData"/>.
    /// </summary>
    /// <remarks>
    /// <b>It formats, it does not compute.</b> Every number shown was authored and arrived
    /// in the snapshot. Nothing here adds a modifier to a stat or works out what a piece
    /// would do to a character -- that is <c>DerivedStatsCalculator</c>'s job and a second
    /// copy of it would drift.
    ///
    /// <b>Keys are shown, not translated.</b> No localisation table exists yet, so the view
    /// prints the <see cref="ChibiFantasy.Core.LocalizationKey"/> itself. Inventing English
    /// strings here would put content in code, which the project does not do.
    ///
    /// An invalid snapshot hides the tooltip rather than drawing an empty frame, so a stale
    /// selection simply shows nothing.
    /// </remarks>
    public sealed class ItemTooltipView : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private Text titleText;
        [SerializeField] private Text bodyText;

        /// <summary>Whether the tooltip is currently showing something.</summary>
        public bool IsVisible { get; private set; }

        /// <summary>The last snapshot passed to <see cref="Show"/>.</summary>
        public ItemTooltipData Data { get; private set; }

        /// <summary>The title line as last formatted. Exposed so the rule is testable.</summary>
        public string Title { get; private set; }

        /// <summary>The body block as last formatted.</summary>
        public string Body { get; private set; }

        /// <summary>Creates the child graphics when a prefab was not authored.</summary>
        public void EnsureVisuals()
        {
            if (root == null) root = gameObject;

            var image = GetComponent<Image>();
            if (image == null)
            {
                image = gameObject.AddComponent<Image>();
                image.color = new Color(0.08f, 0.09f, 0.11f, 0.92f);
                image.raycastTarget = false;
            }

            if (titleText == null) titleText = CreateText("Title", 14, TextAnchor.UpperLeft, 0.72f, 1f);
            if (bodyText == null) bodyText = CreateText("Body", 12, TextAnchor.UpperLeft, 0f, 0.7f);
        }

        private Text CreateText(string childName, int size, TextAnchor anchor,
            float anchorMinY, float anchorMaxY)
        {
            var go = new GameObject(childName, typeof(RectTransform));
            go.transform.SetParent(transform, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, anchorMinY);
            rect.anchorMax = new Vector2(1f, anchorMaxY);
            rect.offsetMin = new Vector2(6f, 4f);
            rect.offsetMax = new Vector2(-6f, -4f);

            var text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.alignment = anchor;
            text.color = Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        /// <summary>Shows a snapshot, or hides the tooltip when it is invalid.</summary>
        public void Show(ItemTooltipData data)
        {
            Data = data;

            if (!data.IsValid)
            {
                Hide();
                return;
            }

            Title = FormatTitle(data);
            Body = FormatBody(data);
            IsVisible = true;

            if (titleText != null) titleText.text = Title;
            if (bodyText != null) bodyText.text = Body;
            if (root != null) root.SetActive(true);
        }

        public void Hide()
        {
            Data = ItemTooltipData.None;
            Title = string.Empty;
            Body = string.Empty;
            IsVisible = false;

            if (titleText != null) titleText.text = string.Empty;
            if (bodyText != null) bodyText.text = string.Empty;
            if (root != null) root.SetActive(false);
        }

        /// <summary>The name line, with a stack count when there is more than one.</summary>
        public static string FormatTitle(ItemTooltipData data)
        {
            if (!data.IsValid) return string.Empty;

            string name = data.NameKey.IsValid ? data.NameKey.ToString() : data.DefinitionId.ToString();
            return data.Quantity > 1 ? name + " x" + data.Quantity : name;
        }

        /// <summary>
        /// The detail block: category, slot, requirements and authored modifiers.
        /// </summary>
        /// <remarks>Static and string-returning so the formatting rules can be tested with
        /// no GameObject, no Canvas and no scene.</remarks>
        public static string FormatBody(ItemTooltipData data)
        {
            if (!data.IsValid) return string.Empty;

            var builder = new StringBuilder();
            builder.Append(data.Category);

            if (data.IsEquipment) builder.Append(" / ").Append(data.Slot);

            if (data.DescriptionKey.IsValid)
            {
                builder.Append('\n').Append(data.DescriptionKey);
            }

            if (data.HasLevelRequirement)
            {
                builder.Append("\nLevel ").Append(data.LevelRequirement);
            }

            if (data.HasClassRestriction) AppendIds(builder, "\nClass: ", data.AllowedClasses);
            if (data.HasJobRestriction) AppendIds(builder, "\nJob: ", data.AllowedJobs);

            for (int i = 0; i < data.StatModifiers.Count; i++)
            {
                var modifier = data.StatModifiers[i];

                // The authored value verbatim, with the kind named rather than applied.
                builder.Append('\n')
                    .Append(modifier.Stat)
                    .Append(' ')
                    .Append(modifier.Value >= 0f ? "+" : string.Empty)
                    .Append(modifier.Value)
                    .Append(" (")
                    .Append(modifier.Kind)
                    .Append(')');
            }

            return builder.ToString();
        }

        private static void AppendIds(StringBuilder builder, string label,
            System.Collections.Generic.IReadOnlyList<ChibiFantasy.Core.DefinitionId> ids)
        {
            builder.Append(label);

            for (int i = 0; i < ids.Count; i++)
            {
                if (i > 0) builder.Append(", ");
                builder.Append(ids[i]);
            }
        }
    }
}
