using UnityEngine;
using UnityEngine.UI;

namespace ChibiFantasy.UI
{
    /// <summary>
    /// The ghost that follows the pointer during a drag.
    /// </summary>
    /// <remarks>
    /// <b>One object, shown and hidden.</b> Not an <see cref="ItemSlotView"/> clone and not
    /// instantiated per drag: a duplicated slot would be a second clickable, raycastable
    /// slot in the hierarchy that could receive its own drop. This is a single decoration
    /// with raycasting off, reused for every drag and hidden between them.
    ///
    /// <b>It cannot affect anything.</b> It draws an <see cref="ItemDragPayload"/>, which is
    /// a copy, and holds no gameplay reference. Destroying or forgetting it can never
    /// change an item.
    /// </remarks>
    public sealed class ItemDragVisual : MonoBehaviour
    {
        [SerializeField] private Image iconImage;
        [SerializeField] private Text quantityText;
        [SerializeField] private CanvasGroup group;

        [Tooltip("Drawn when the dragged item has no authored icon.")]
        [SerializeField] private Color missingIconColor = new Color(0.65f, 0.55f, 0.25f, 1f);

        [Tooltip("Offset from the pointer so the ghost does not sit under the cursor tip.")]
        [SerializeField] private Vector2 pointerOffset = new Vector2(14f, -14f);

        private RectTransform _rect;

        public bool IsVisible { get; private set; }

        /// <summary>What the ghost is currently drawing.</summary>
        public ItemDragPayload Payload { get; private set; }

        /// <summary>
        /// Builds itself and starts hidden.
        /// </summary>
        /// <remarks>Self-initialising, unlike the slot views: nothing else owns the ghost, so
        /// nothing else would build it. Hiding here is what keeps an empty ghost from sitting
        /// in the middle of the screen from the moment a scene loads.</remarks>
        private void Awake()
        {
            EnsureVisuals();
            Hide();
        }

        /// <summary>Creates the child graphics when a prefab was not authored.</summary>
        public void EnsureVisuals()
        {
            _rect = GetComponent<RectTransform>();
            if (_rect == null) _rect = gameObject.AddComponent<RectTransform>();
            _rect.sizeDelta = new Vector2(48f, 48f);

            if (group == null) group = gameObject.AddComponent<CanvasGroup>();

            // Never a raycast target: the ghost sits under the pointer, and if it could be
            // hit it would block every drop target it passes over.
            group.blocksRaycasts = false;
            group.interactable = false;
            group.alpha = 0.85f;

            if (iconImage == null)
            {
                var go = new GameObject("Icon", typeof(RectTransform));
                go.transform.SetParent(transform, false);
                var rect = (RectTransform)go.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;

                iconImage = go.AddComponent<Image>();
                iconImage.raycastTarget = false;
            }

            if (quantityText != null) return;

            var textGo = new GameObject("Quantity", typeof(RectTransform));
            textGo.transform.SetParent(transform, false);
            var textRect = (RectTransform)textGo.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(2f, 2f);
            textRect.offsetMax = new Vector2(-3f, -2f);

            quantityText = textGo.AddComponent<Text>();
            quantityText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            quantityText.fontSize = 12;
            quantityText.alignment = TextAnchor.LowerRight;
            quantityText.color = Color.white;
            quantityText.raycastTarget = false;
        }

        /// <summary>Starts drawing a payload.</summary>
        public void Show(ItemDragPayload payload, Sprite icon)
        {
            if (!payload.IsActive)
            {
                Hide();
                return;
            }

            Payload = payload;
            IsVisible = true;

            if (iconImage != null)
            {
                iconImage.enabled = true;
                iconImage.sprite = icon;

                // Missing art is ordinary: a swatch, never an error.
                iconImage.color = icon != null ? Color.white : missingIconColor;
            }

            if (quantityText != null)
            {
                bool show = payload.Quantity > 1;
                quantityText.enabled = show;
                quantityText.text = show ? payload.Quantity.ToString() : string.Empty;
            }

            gameObject.SetActive(true);
        }

        /// <summary>Moves the ghost to a screen position.</summary>
        public void MoveTo(Vector2 screenPosition)
        {
            if (_rect == null) _rect = GetComponent<RectTransform>();
            if (_rect == null) return;

            _rect.position = new Vector3(screenPosition.x + pointerOffset.x,
                screenPosition.y + pointerOffset.y, _rect.position.z);
        }

        /// <summary>Stops drawing. Called on drop and on every kind of cancel.</summary>
        public void Hide()
        {
            Payload = ItemDragPayload.None;
            IsVisible = false;

            if (quantityText != null) quantityText.text = string.Empty;
            if (iconImage != null) iconImage.sprite = null;

            gameObject.SetActive(false);
        }
    }
}
