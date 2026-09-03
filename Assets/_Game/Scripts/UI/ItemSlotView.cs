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
    public sealed class ItemSlotView : MonoBehaviour, IPointerClickHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler,
        IPointerEnterHandler, IPointerExitHandler
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

        [Header("Interaction feedback")]
        [SerializeField] private Color hoverTint = new Color(0.36f, 0.39f, 0.46f, 1f);
        [SerializeField] private Color validDropColor = new Color(0.25f, 0.52f, 0.28f, 1f);
        [SerializeField] private Color invalidDropColor = new Color(0.52f, 0.24f, 0.24f, 1f);
        [SerializeField] private Color dragSourceColor = new Color(0.18f, 0.19f, 0.23f, 1f);

        /// <summary>Raised on left click, carrying this slot's index.</summary>
        public event System.Action<int> Clicked;

        /// <summary>Raised on right click. What opens a context menu.</summary>
        public event System.Action<int> RightClicked;

        /// <summary>Raised when a drag starts on this slot.</summary>
        public event System.Action<int> DragStarted;

        /// <summary>Raised every frame of a drag, carrying the pointer position.</summary>
        public event System.Action<Vector2> Dragging;

        /// <summary>
        /// Raised when a drag that started here is released.
        /// </summary>
        /// <remarks>Always raised, whether or not a slot received the drop, so the drag
        /// state and the ghost are cleaned up even when the pointer let go over nothing.</remarks>
        public event System.Action DragEnded;

        /// <summary>Raised when a drag is released <em>onto</em> this slot.</summary>
        public event System.Action<int> Dropped;

        /// <summary>Raised when the pointer enters or leaves, carrying the slot index.</summary>
        public event System.Action<int> HoverEntered;

        public event System.Action<int> HoverExited;

        /// <summary>The snapshot currently drawn.</summary>
        public ItemSlotViewData Data { get; private set; }

        public bool IsSelected { get; private set; }

        public bool IsHovered { get; private set; }

        /// <summary>The advisory drop indicator currently drawn.</summary>
        public SlotDropHint DropHint { get; private set; }

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
            Bind(data, selected, null);
        }

        /// <summary>
        /// Draws a slot with a resolved icon.
        /// </summary>
        /// <remarks>
        /// The sprite is passed in rather than looked up: resolving an
        /// <see cref="ChibiFantasy.Core.AssetRef"/> is <see cref="IconResolver"/>'s job, and
        /// a slot that loaded its own art would load once per slot per refresh. A null
        /// sprite is ordinary -- either nothing is authored, or what is authored is not
        /// there yet -- and draws the placeholder colour rather than logging.
        /// </remarks>
        public void Bind(ItemSlotViewData data, bool selected, Sprite icon)
        {
            Data = data;
            IsSelected = selected;
            DropHint = SlotDropHint.None;

            if (iconImage != null)
            {
                iconImage.enabled = data.IsOccupied;
                iconImage.sprite = icon;
                iconImage.color = icon != null
                    ? Color.white
                    : (data.HasIcon ? Color.white : missingIconColor);
            }

            if (quantityText != null)
            {
                quantityText.enabled = data.ShowQuantity;
                quantityText.text = data.ShowQuantity ? data.Quantity.ToString() : string.Empty;
            }

            if (selectionOutline != null) selectionOutline.enabled = selected;

            RedrawBackground();
        }

        public void SetSelected(bool selected)
        {
            IsSelected = selected;
            if (selectionOutline != null) selectionOutline.enabled = selected;
        }

        /// <summary>Sets the advisory drop indicator. See <see cref="ItemDropAdvice"/>.</summary>
        public void SetDropHint(SlotDropHint hint)
        {
            DropHint = hint;
            RedrawBackground();
        }

        /// <summary>
        /// Picks the background colour from every state at once.
        /// </summary>
        /// <remarks>One place rather than a colour assignment in each handler, so hovering
        /// during a drag cannot leave a slot showing the wrong thing after the drag ends.
        /// Drop hints outrank hover, because during a drag that is the information the
        /// player needs.</remarks>
        private void RedrawBackground()
        {
            if (backgroundImage == null) return;

            switch (DropHint)
            {
                case SlotDropHint.Valid:
                    backgroundImage.color = validDropColor;
                    return;
                case SlotDropHint.Invalid:
                    backgroundImage.color = invalidDropColor;
                    return;
                case SlotDropHint.Source:
                    backgroundImage.color = dragSourceColor;
                    return;
            }

            if (IsHovered && Data.IsOccupied)
            {
                backgroundImage.color = hoverTint;
                return;
            }

            backgroundImage.color = Data.IsOccupied ? occupiedColor : emptyColor;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData != null && eventData.button == PointerEventData.InputButton.Right)
            {
                var right = RightClicked;
                if (right != null) right(Data.SlotIndex);
                return;
            }

            var handler = Clicked;
            if (handler != null) handler(Data.SlotIndex);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            // An empty slot has nothing to pick up. Reporting it anyway would make the
            // controller responsible for a check the view can answer.
            if (Data.IsEmpty) return;

            var handler = DragStarted;
            if (handler != null) handler(Data.SlotIndex);

            OnDrag(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            var handler = Dragging;
            if (handler != null) handler(eventData == null ? Vector2.zero : eventData.position);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            var handler = DragEnded;
            if (handler != null) handler();
        }

        public void OnDrop(PointerEventData eventData)
        {
            var handler = Dropped;
            if (handler != null) handler(Data.SlotIndex);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            IsHovered = true;
            RedrawBackground();

            var handler = HoverEntered;
            if (handler != null) handler(Data.SlotIndex);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            IsHovered = false;
            RedrawBackground();

            var handler = HoverExited;
            if (handler != null) handler(Data.SlotIndex);
        }
    }
}
