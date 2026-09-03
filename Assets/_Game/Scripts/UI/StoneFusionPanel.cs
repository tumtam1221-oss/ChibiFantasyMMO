using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace ChibiFantasy.UI
{
    /// <summary>
    /// Shows one fusion recipe and offers to run it.
    /// </summary>
    /// <remarks>
    /// <b>It draws a snapshot and raises an event.</b> The panel holds a
    /// <see cref="FusionViewData"/> with the held-versus-required counts already resolved,
    /// so it never counts a container and never touches one. Pressing the button asks the
    /// controller; the controller asks gameplay.
    ///
    /// The button being interactable is advisory: <c>StoneFusionService</c> re-checks
    /// quantities, fusability, the output and the room for it, and remains the authority.
    /// </remarks>
    public sealed class StoneFusionPanel : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private Image resultIcon;
        [SerializeField] private Text titleText;
        [SerializeField] private Text detailText;
        [SerializeField] private Text resultText;
        [SerializeField] private Button fuseButton;

        [Tooltip("Drawn when the result has no authored icon.")]
        [SerializeField] private Color missingIconColor = new Color(0.65f, 0.55f, 0.25f, 1f);

        /// <summary>Raised when the player asks to run the recipe.</summary>
        public event System.Action Requested;

        public bool IsVisible { get; private set; }

        /// <summary>What the panel is currently describing.</summary>
        public FusionViewData Data { get; private set; }

        public string Title { get; private set; }

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

            if (resultIcon == null)
            {
                var go = new GameObject("ResultIcon", typeof(RectTransform));
                go.transform.SetParent(transform, false);
                var rect = (RectTransform)go.transform;
                rect.anchorMin = new Vector2(1f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(1f, 1f);
                rect.anchoredPosition = new Vector2(-8f, -8f);
                rect.sizeDelta = new Vector2(40f, 40f);
                resultIcon = go.AddComponent<Image>();
                resultIcon.raycastTarget = false;
            }

            if (titleText == null) titleText = CreateText("Title", 14, new Vector2(8f, -8f), 40f);
            if (detailText == null) detailText = CreateText("Detail", 12, new Vector2(8f, -50f), 130f);
            if (resultText == null) resultText = CreateText("Result", 12, new Vector2(8f, -186f), 24f);

            if (fuseButton == null)
            {
                fuseButton = CreateButton("Fuse", "Fuse", new Vector2(8f, 8f));
                fuseButton.onClick.AddListener(Request);
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
            rect.sizeDelta = new Vector2(-56f, height);

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
            image.color = new Color(0.24f, 0.26f, 0.34f, 1f);

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

        /// <summary>Draws a recipe, or hides the panel when there is none.</summary>
        public void Show(FusionViewData data, IconResolver icons)
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
            if (fuseButton != null) fuseButton.interactable = data.CanAttempt;

            if (resultIcon != null)
            {
                Sprite icon = icons == null ? null : icons.Resolve(data.ResultIcon);
                resultIcon.enabled = true;
                resultIcon.sprite = icon;
                resultIcon.color = icon != null ? Color.white : missingIconColor;
            }

            if (root != null) root.SetActive(true);
        }

        /// <summary>Reports what the last attempt did.</summary>
        public void ShowResult(string message)
        {
            if (resultText != null) resultText.text = message ?? string.Empty;
        }

        public void Hide()
        {
            Data = FusionViewData.None;
            Title = string.Empty;
            Detail = string.Empty;
            IsVisible = false;

            if (titleText != null) titleText.text = string.Empty;
            if (detailText != null) detailText.text = string.Empty;
            if (resultText != null) resultText.text = string.Empty;
            if (resultIcon != null) resultIcon.enabled = false;
            if (root != null) root.SetActive(false);
        }

        /// <summary>Raises <see cref="Requested"/>. Changes nothing by itself.</summary>
        public void Request()
        {
            if (!IsVisible) return;

            var handler = Requested;
            if (handler != null) handler();
        }

        /// <summary>What the recipe produces.</summary>
        public static string FormatTitle(FusionViewData data, ILocalizedTextSource text)
        {
            if (!data.IsValid) return string.Empty;

            string name = data.ResultNameKey.IsValid
                ? LocalizedText.Resolve(text, data.ResultNameKey)
                : data.Result.ToString();

            return data.ResultQuantity > 1 ? name + " x" + data.ResultQuantity : name;
        }

        /// <summary>
        /// The cost lines and the odds.
        /// </summary>
        /// <remarks>Static and string-returning so the formatting tests without a Canvas.
        /// Every count arrived resolved; nothing is counted here.</remarks>
        public static string FormatDetail(FusionViewData data, ILocalizedTextSource text)
        {
            if (!data.IsValid) return string.Empty;

            var builder = new StringBuilder();

            for (int i = 0; i < data.Inputs.Count; i++)
            {
                FusionIngredientViewData input = data.Inputs[i];

                if (i > 0) builder.Append('\n');

                builder.Append(input.NameKey.IsValid
                        ? LocalizedText.Resolve(text, input.NameKey)
                        : input.Item.ToString())
                    .Append(' ').Append(input.Held).Append('/').Append(input.Required);

                if (!input.IsSatisfied) builder.Append(" (short)");
            }

            if (data.CurrencyCost > 0)
            {
                builder.Append('\n').Append(data.CurrencyItem)
                    .Append(' ').Append(data.CurrencyHeld).Append('/').Append(data.CurrencyCost);
            }

            builder.Append("\nChance ").Append(data.SuccessChance <= 0f
                ? "certain"
                : Mathf.RoundToInt(Mathf.Clamp01(data.SuccessChance) * 100f) + "%");

            return builder.ToString();
        }
    }
}
