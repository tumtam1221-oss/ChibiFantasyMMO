using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ChibiFantasy.UI
{
    /// <summary>
    /// One clickable square.
    /// </summary>
    /// <remarks>
    /// <b>It draws a snapshot and reports clicks.</b> It holds an
    /// <see cref="ItemSlotViewData"/>, which is a copy, so there is nothing here for a view
    /// to mutate. Clicking raises an event carrying the slot index; deciding what a click
    /// means is the controller's job, and performing it is gameplay's.
    ///
    /// <b>Every visual reference is optional.</b> A slot with no icon Image, no quantity
    /// Text or no background still works and still reports clicks. Missing art is an
    /// ordinary state, so a slot renders a placeholder colour rather than logging an
    /// error every frame.
    ///
    /// <b>Reused, never rebuilt.</b> <see cref="Bind"/> updates the same object, so a
    /// refresh costs no instantiation. See <see cref="ItemContainerPanel"/>.
    /// </remarks>
    public sealed class ItemSlotView : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private Image iconImage;
        [SerializeField] private Image backgroundImage;
        [SerializeField] private Text quantityText;
        [SerializeField] private Image selectionOutline;

        [Header("Colours")]
        [SerializeField] private Color emptyColor = new Color(0.16f, 0.17f, 0.20f, 1f);
        [SerializeField] private Color occupiedColor = new Color(0.28f, 0.30f, 0.36f, 1f);

        [Tooltip("Drawn when an item has no authored icon. Missing art is not an error.")]
        [SerializeField] private Color missingIconColor = new Color(0.65f, 0.55f, 0.25f, 1f);

        /// <summary>Raised on click, carrying this slot's index.</summary>
        public event System.Action<int> Clicked;

        /// <summary>The snapshot currently drawn.</summary>
        public ItemSlotViewData Data { get; private set; }

        public bool IsSelected { get; private set; }

        /// <summary>Creates the child graphics when a prefab was not authored.</summary>
        public void EnsureVisuals()
        {
            if (backgroundImage == null) backgroundImage = GetComponent<Image>();
            if (backgroundImage == null) backgroundImage = gameObject.AddComponent<Image>();

            if (iconImage == null) iconImage = CreateChild("Icon", 0.62f);
            if (selectionOutline == null)
            {
                selectionOutline = CreateChild("Selection", 1f);
                selectionOutline.color = new Color(1f, 0.85f, 0.3f, 0.35f);
                selectionOutline.raycastTarget = false;
                selectionOutline.enabled = false;
            }

            if (quantityText != null) return;

            var go = new GameObject("Quantity", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(2f, 2f);
            rect.offsetMax = new Vector2(-3f, -2f);

            quantityText = go.AddComponent<Text>();
            quantityText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            quantityText.fontSize = 12;
            quantityText.alignment = TextAnchor.LowerRight;
            quantityText.color = Color.white;
            quantityText.raycastTarget = false;
        }

        private Image CreateChild(string childName, float scale)
        {
            var go = new GameObject(childName, typeof(RectTransform));
            go.transform.SetParent(transform, false);

            var rect = (RectTransform)go.transform;
            float inset = (1f - scale) * 0.5f;
            rect.anchorMin = new Vector2(inset, inset);
            rect.anchorMax = new Vector2(1f - inset, 1f - inset);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = go.AddComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        /// <summary>
        /// Draws a slot.
        /// </summary>
        /// <remarks>Safe against every optional reference being null, which is what lets a
        /// panel build slots procedurally and a test construct one without a prefab.</remarks>
        public void Bind(ItemSlotViewData data, bool selected)
        {
            Data = data;
            IsSelected = selected;

            if (backgroundImage != null)
            {
                backgroundImage.color = data.IsOccupied ? occupiedColor : emptyColor;
            }

            if (iconImage != null)
            {
                // No icon loader exists yet, so an authored address shows as a filled
                // swatch and an unauthored one as the placeholder colour. Neither throws.
                iconImage.enabled = data.IsOccupied;
                iconImage.color = data.HasIcon ? Color.white : missingIconColor;
            }

            if (quantityText != null)
            {
                quantityText.enabled = data.ShowQuantity;
                quantityText.text = data.ShowQuantity ? data.Quantity.ToString() : string.Empty;
            }

            if (selectionOutline != null) selectionOutline.enabled = selected;
        }

        public void SetSelected(bool selected)
        {
            IsSelected = selected;
            if (selectionOutline != null) selectionOutline.enabled = selected;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            var handler = Clicked;
            if (handler != null) handler(Data.SlotIndex);
        }
    }
}
