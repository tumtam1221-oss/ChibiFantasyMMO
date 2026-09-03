using System.Text;
using ChibiFantasy.Core;
using UnityEngine;
using UnityEngine.UI;

namespace ChibiFantasy.UI
{
    /// <summary>
    /// Shows one enhancement attempt and offers it.
    /// </summary>
    /// <remarks>
    /// <b>It draws a snapshot and raises an event.</b> The panel holds an
    /// <see cref="EnhancementViewData"/>, which is a copy with the preview figures already
    /// in it, so there is nothing here to mutate and nothing here that computes what an
    /// upgrade is worth. Pressing the button asks the controller; the controller asks
    /// gameplay.
    ///
    /// <b>Showing the preview is not attempting it.</b> <see cref="Show"/> is called
    /// whenever the selection changes and costs nothing but formatting -- the figures were
    /// resolved by a pure function against a level the piece is not at.
    ///
    /// The button being interactable is advisory, exactly like a drop hint:
    /// <c>EnhancementService</c> re-checks everything.
    /// </remarks>
    public sealed class EnhancementPanel : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private Text titleText;
        [SerializeField] private Text detailText;
        [SerializeField] private Text resultText;
        [SerializeField] private Button enhanceButton;

        /// <summary>Raised when the player asks for an attempt.</summary>
        public event System.Action Requested;

        public bool IsVisible { get; private set; }

        /// <summary>What the panel is currently describing.</summary>
        public EnhancementViewData Data { get; private set; }

        /// <summary>The title as last formatted. Exposed so the rule is testable.</summary>
        public string Title { get; private set; }

        /// <summary>The detail block as last formatted.</summary>
        public string Detail { get; private set; }

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
            if (detailText == null) detailText = CreateText("Detail", 12, new Vector2(8f, -30f), 150f);
            if (resultText == null) resultText = CreateText("Result", 12, new Vector2(8f, -186f), 24f);

            if (enhanceButton == null)
            {
                enhanceButton = CreateButton("Enhance", "Enhance", new Vector2(8f, 8f));
                enhanceButton.onClick.AddListener(Request);
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
            image.color = new Color(0.24f, 0.30f, 0.24f, 1f);

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

        /// <summary>Draws a snapshot, or hides the panel when there is nothing to show.</summary>
        public void Show(EnhancementViewData data)
        {
            Data = data;

            if (!data.IsValid)
            {
                Hide();
                return;
            }

            Title = FormatTitle(data, Text);
            Detail = FormatDetail(data, Text);
            IsVisible = true;

            if (titleText != null) titleText.text = Title;
            if (detailText != null) detailText.text = Detail;
            if (enhanceButton != null) enhanceButton.interactable = CanOffer(data);
            if (root != null) root.SetActive(true);
        }

        /// <summary>Reports what the last attempt did. Purely a message.</summary>
        public void ShowResult(string message)
        {
            if (resultText != null) resultText.text = message ?? string.Empty;
        }

        public void Hide()
        {
            Data = EnhancementViewData.None;
            Title = string.Empty;
            Detail = string.Empty;
            IsVisible = false;

            if (titleText != null) titleText.text = string.Empty;
            if (detailText != null) detailText.text = string.Empty;
            if (resultText != null) resultText.text = string.Empty;
            if (root != null) root.SetActive(false);
        }

        /// <summary>Raises <see cref="Requested"/>. Changes nothing by itself.</summary>
        public void Request()
        {
            if (!IsVisible) return;

            var handler = Requested;
            if (handler != null) handler();
        }

        /// <summary>
        /// Whether the button should be pressable.
        /// </summary>
        /// <remarks>Advisory: it reflects the coarse facts the snapshot carries. A player
        /// who presses anyway gets a typed rejection, which is the honest outcome.</remarks>
        public static bool CanOffer(EnhancementViewData data)
        {
            return data.IsValid && data.CanAttempt
                && data.HasEnoughMaterial && data.HasEnoughCurrency;
        }

        /// <summary>The headline: what is being enhanced, and to what.</summary>
        public static string FormatTitle(EnhancementViewData data, ILocalizedTextSource text)
        {
            if (!data.IsValid) return string.Empty;

            string name = data.NameKey.IsValid
                ? LocalizedText.Resolve(text, data.NameKey)
                : data.DefinitionId.ToString();

            if (data.IsAtCeiling) return name + " +" + data.CurrentLevel + " (max)";

            return name + " +" + data.CurrentLevel + " -> +" + (data.CurrentLevel + 1);
        }

        /// <summary>
        /// The detail block: odds, cost, and what the upgrade is worth.
        /// </summary>
        /// <remarks>Static and string-returning so the formatting rules test without a
        /// Canvas. Every figure was resolved elsewhere; nothing is computed here.</remarks>
        public static string FormatDetail(EnhancementViewData data, ILocalizedTextSource text)
        {
            if (!data.IsValid) return string.Empty;

            var builder = new StringBuilder();

            if (data.IsAtCeiling)
            {
                builder.Append("At the maximum level.");
                AppendCurrent(builder, data);
                return builder.ToString();
            }

            builder.Append("Chance ").Append(FormatChance(data.SuccessChance));
            builder.Append("\nOn failure: ").Append(data.FailureBehavior);

            if (data.MaterialAmount > 0)
            {
                builder.Append('\n').Append(Name(text, data.MaterialNameKey, data.MaterialItem))
                    .Append(' ').Append(data.MaterialHeld).Append('/').Append(data.MaterialAmount);
            }

            if (data.CurrencyCost > 0)
            {
                builder.Append('\n').Append(Name(text, data.CurrencyNameKey, data.CurrencyItem))
                    .Append(' ').Append(data.CurrencyHeld).Append('/').Append(data.CurrencyCost);
            }

            AppendCurrent(builder, data);

            if (data.PreviewModifiers.Count > 0)
            {
                builder.Append("\nAfter:");
                AppendModifiers(builder, data.PreviewModifiers);
            }

            return builder.ToString();
        }

        private static void AppendCurrent(StringBuilder builder, EnhancementViewData data)
        {
            if (data.CurrentModifiers.Count == 0) return;

            builder.Append("\nNow:");
            AppendModifiers(builder, data.CurrentModifiers);
        }

        private static void AppendModifiers(StringBuilder builder,
            System.Collections.Generic.IReadOnlyList<StatModifier> modifiers)
        {
            for (int i = 0; i < modifiers.Count; i++)
            {
                StatModifier modifier = modifiers[i];

                builder.Append("\n  ").Append(modifier.Stat).Append(' ')
                    .Append(modifier.Value >= 0f ? "+" : string.Empty).Append(modifier.Value);
            }
        }

        /// <summary>Zero or less reads as certain, matching what the services do with it.</summary>
        private static string FormatChance(float chance)
        {
            if (chance <= 0f) return "certain";
            return Mathf.RoundToInt(Mathf.Clamp01(chance) * 100f) + "%";
        }

        /// <summary>The authored name, falling back to the id when none was authored.</summary>
        private static string Name(ILocalizedTextSource text, LocalizationKey nameKey,
            DefinitionId item)
        {
            if (nameKey.IsValid) return LocalizedText.Resolve(text, nameKey);
            return item.IsValid ? item.ToString() : string.Empty;
        }
    }
}
