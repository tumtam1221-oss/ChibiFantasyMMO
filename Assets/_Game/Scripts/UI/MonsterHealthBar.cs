using UnityEngine;
using UnityEngine.UI;

namespace ChibiFantasy.UI
{
    /// <summary>
    /// Draws a monster's health.
    /// </summary>
    /// <remarks>
    /// <b>Two numbers and a name.</b> It holds a <see cref="MonsterHealthViewData"/>, which
    /// is a copy, so a bar cannot damage what it describes.
    ///
    /// <b>It is told, not polled.</b> <see cref="Show"/> is called when the monster's
    /// health actually changed -- its revision moves on a real change and not otherwise --
    /// so a screen full of monsters costs nothing while nothing is happening.
    ///
    /// Hidden for a dead or invalid monster rather than drawn empty, so a corpse does not
    /// leave a bar behind.
    /// </remarks>
    public sealed class MonsterHealthBar : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private Image fillImage;
        [SerializeField] private Image backgroundImage;
        [SerializeField] private Text labelText;

        [SerializeField] private Color normalColor = new Color(0.72f, 0.22f, 0.20f, 1f);
        [SerializeField] private Color bossColor = new Color(0.80f, 0.45f, 0.10f, 1f);

        public bool IsVisible { get; private set; }

        /// <summary>What the bar currently describes.</summary>
        public MonsterHealthViewData Data { get; private set; }

        /// <summary>The label as last formatted. Exposed so the rule is testable.</summary>
        public string Label { get; private set; }

        /// <summary>Where keys are translated. Optional.</summary>
        public ILocalizedTextSource Text { get; set; }

        /// <summary>Creates the child graphics when a prefab was not authored.</summary>
        public void EnsureVisuals()
        {
            if (root == null) root = gameObject;

            if (backgroundImage == null) backgroundImage = GetComponent<Image>();
            if (backgroundImage == null)
            {
                backgroundImage = gameObject.AddComponent<Image>();
                backgroundImage.color = new Color(0.10f, 0.10f, 0.12f, 0.85f);
                backgroundImage.raycastTarget = false;
            }

            if (fillImage == null)
            {
                var go = new GameObject("Fill", typeof(RectTransform));
                go.transform.SetParent(transform, false);

                var rect = (RectTransform)go.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = new Vector2(1f, 1f);
                rect.offsetMin = new Vector2(1f, 1f);
                rect.offsetMax = new Vector2(-1f, -1f);
                rect.pivot = new Vector2(0f, 0.5f);

                fillImage = go.AddComponent<Image>();
                fillImage.color = normalColor;
                fillImage.raycastTarget = false;
                fillImage.type = Image.Type.Filled;
                fillImage.fillMethod = Image.FillMethod.Horizontal;
            }

            if (labelText != null) return;

            var textGo = new GameObject("Label", typeof(RectTransform));
            textGo.transform.SetParent(transform, false);

            var textRect = (RectTransform)textGo.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(4f, 0f);
            textRect.offsetMax = new Vector2(-4f, 0f);

            labelText = textGo.AddComponent<Text>();
            labelText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            labelText.fontSize = 11;
            labelText.alignment = TextAnchor.MiddleCenter;
            labelText.color = Color.white;
            labelText.raycastTarget = false;
        }

        /// <summary>Draws a snapshot, or hides the bar when there is nothing to draw.</summary>
        public void Show(MonsterHealthViewData data)
        {
            Data = data;

            if (!data.IsValid || !data.IsAlive)
            {
                Hide();
                return;
            }

            Label = FormatLabel(data, Text);
            IsVisible = true;

            if (fillImage != null)
            {
                fillImage.fillAmount = Mathf.Clamp01(data.Fraction);
                fillImage.color = data.IsBoss ? bossColor : normalColor;
            }

            if (labelText != null) labelText.text = Label;
            if (root != null) root.SetActive(true);
        }

        public void Hide()
        {
            Data = MonsterHealthViewData.None;
            Label = string.Empty;
            IsVisible = false;

            if (labelText != null) labelText.text = string.Empty;
            if (root != null) root.SetActive(false);
        }

        /// <summary>
        /// The name, level and figures.
        /// </summary>
        /// <remarks>Static and string-returning so the formatting rule tests without a
        /// Canvas. Falls back to the definition id when no name was authored, which keeps
        /// content removed by a patch visible instead of blank.</remarks>
        public static string FormatLabel(MonsterHealthViewData data, ILocalizedTextSource text)
        {
            if (!data.IsValid) return string.Empty;

            string name = data.NameKey.IsValid
                ? LocalizedText.Resolve(text, data.NameKey)
                : data.DefinitionId.ToString();

            return "Lv" + data.Level + " " + name + "  "
                + data.CurrentHealth + "/" + data.MaxHealth;
        }
    }
}
