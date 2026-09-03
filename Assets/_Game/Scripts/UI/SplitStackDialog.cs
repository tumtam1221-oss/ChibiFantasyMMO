using UnityEngine;
using UnityEngine.UI;

namespace ChibiFantasy.UI
{
    /// <summary>
    /// Asks how much of a stack to split off.
    /// </summary>
    /// <remarks>
    /// <b>It collects a number and raises an event.</b> It never touches a quantity: the
    /// stack it is describing is an <see cref="ItemSlotViewData"/> snapshot, and confirming
    /// hands the figure to the controller, which asks gameplay to do the split.
    ///
    /// <b>Its validation is for the player's benefit only.</b> <see cref="SplitBounds"/>
    /// clamps the field and disables confirm so a doomed request is not sent, and
    /// <c>ItemContainerState.Split</c> re-checks everything regardless. If the two ever
    /// disagree, the container wins.
    ///
    /// Buttons rather than a slider: the project has no slider convention yet, and
    /// inventing one here would be a UI framework decision smuggled into a dialog.
    /// </remarks>
    public sealed class SplitStackDialog : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private Image iconImage;
        [SerializeField] private Text titleText;
        [SerializeField] private Text quantityText;
        [SerializeField] private Button decreaseButton;
        [SerializeField] private Button increaseButton;
        [SerializeField] private Button confirmButton;
        [SerializeField] private Button cancelButton;

        [Tooltip("Drawn when the item has no authored icon.")]
        [SerializeField] private Color missingIconColor = new Color(0.65f, 0.55f, 0.25f, 1f);

        /// <summary>
        /// Raised with the panel, the slot and the chosen quantity.
        /// </summary>
        /// <remarks>All three travel with the event because the dialog closes before raising
        /// it -- a subscriber reading <see cref="Source"/> afterwards would find it already
        /// cleared, and holding a copy in the controller would be a second place for the
        /// same fact to go stale.</remarks>
        public event System.Action<ItemSelectionSource, int, int> Confirmed;

        /// <summary>Raised when the dialog closes without a request.</summary>
        public event System.Action Cancelled;

        public bool IsOpen { get; private set; }

        /// <summary>Which panel and slot the open dialog refers to.</summary>
        public ItemSelectionSource Source { get; private set; }

        public int SlotIndex { get; private set; }

        /// <summary>The stack being split, as it was when the dialog opened.</summary>
        public ItemSlotViewData Slot { get; private set; }

        public SplitBounds Bounds { get; private set; }

        /// <summary>The currently chosen quantity. Always inside <see cref="Bounds"/>.</summary>
        public int Quantity { get; private set; }

        /// <summary>The title as last formatted. Exposed so the rule is testable.</summary>
        public string Title { get; private set; }

        /// <summary>Creates the child graphics when a prefab was not authored.</summary>
        public void EnsureVisuals()
        {
            if (root == null) root = gameObject;

            var background = GetComponent<Image>();
            if (background == null)
            {
                background = gameObject.AddComponent<Image>();
                background.color = new Color(0.08f, 0.09f, 0.11f, 0.97f);
            }

            if (iconImage == null)
            {
                var go = new GameObject("Icon", typeof(RectTransform));
                go.transform.SetParent(transform, false);
                var rect = (RectTransform)go.transform;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(10f, -10f);
                rect.sizeDelta = new Vector2(40f, 40f);
                iconImage = go.AddComponent<Image>();
                iconImage.raycastTarget = false;
            }

            if (titleText == null) titleText = CreateText("Title", 13, new Vector2(58f, -12f), 180f);
            if (quantityText == null) quantityText = CreateText("Quantity", 16, new Vector2(58f, -34f), 180f);

            if (decreaseButton == null) decreaseButton = CreateButton("Minus", "-", new Vector2(12f, 12f));
            if (increaseButton == null) increaseButton = CreateButton("Plus", "+", new Vector2(56f, 12f));
            if (confirmButton == null) confirmButton = CreateButton("Confirm", "OK", new Vector2(110f, 12f));
            if (cancelButton == null) cancelButton = CreateButton("Cancel", "X", new Vector2(164f, 12f));

            decreaseButton.onClick.AddListener(Decrease);
            increaseButton.onClick.AddListener(Increase);
            confirmButton.onClick.AddListener(Confirm);
            cancelButton.onClick.AddListener(Cancel);

            Close();
        }

        private Text CreateText(string childName, int size, Vector2 position, float width)
        {
            var go = new GameObject(childName, typeof(RectTransform));
            go.transform.SetParent(transform, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(width, 22f);

            var text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = Color.white;
            text.raycastTarget = false;
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
            rect.sizeDelta = new Vector2(40f, 26f);

            var image = go.AddComponent<Image>();
            image.color = new Color(0.24f, 0.26f, 0.32f, 1f);

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

        /// <summary>
        /// Opens on a stack.
        /// </summary>
        /// <remarks>Refuses to open on something unsplittable rather than opening a dialog
        /// whose confirm can never work.</remarks>
        public bool Open(ItemSelectionSource source, ItemSlotViewData slot, string displayName,
            Sprite icon)
        {
            Bounds = SplitBounds.For(slot);

            if (!Bounds.IsSplittable)
            {
                Close();
                return false;
            }

            Source = source;
            SlotIndex = slot.SlotIndex;
            Slot = slot;
            Quantity = Bounds.DefaultQuantity;
            Title = displayName;
            IsOpen = true;

            if (iconImage != null)
            {
                iconImage.sprite = icon;
                iconImage.color = icon != null ? Color.white : missingIconColor;
            }

            if (titleText != null) titleText.text = displayName;

            if (root != null) root.SetActive(true);
            Redraw();
            return true;
        }

        /// <summary>Closes without raising <see cref="Confirmed"/>.</summary>
        public void Close()
        {
            IsOpen = false;
            Source = ItemSelectionSource.None;
            SlotIndex = -1;
            Slot = default;
            Bounds = SplitBounds.None;
            Quantity = 0;
            Title = string.Empty;

            if (root != null) root.SetActive(false);
        }

        /// <summary>Sets the quantity, clamped into range.</summary>
        public void SetQuantity(int quantity)
        {
            if (!IsOpen) return;

            Quantity = Bounds.Clamp(quantity);
            Redraw();
        }

        public void Increase()
        {
            SetQuantity(Quantity + 1);
        }

        public void Decrease()
        {
            SetQuantity(Quantity - 1);
        }

        /// <summary>Raises <see cref="Confirmed"/> and closes.</summary>
        public void Confirm()
        {
            if (!IsOpen || !Bounds.Allows(Quantity))
            {
                Cancel();
                return;
            }

            int requested = Quantity;
            ItemSelectionSource source = Source;
            int slot = SlotIndex;
            Close();

            var handler = Confirmed;
            if (handler != null) handler(source, slot, requested);
        }

        /// <summary>Raises <see cref="Cancelled"/> and closes.</summary>
        public void Cancel()
        {
            bool wasOpen = IsOpen;
            Close();

            if (!wasOpen) return;

            var handler = Cancelled;
            if (handler != null) handler();
        }

        private void Redraw()
        {
            if (quantityText != null)
            {
                quantityText.text = Quantity + " / " + Bounds.StackQuantity;
            }

            if (decreaseButton != null) decreaseButton.interactable = Quantity > Bounds.Min;
            if (increaseButton != null) increaseButton.interactable = Quantity < Bounds.Max;
            if (confirmButton != null) confirmButton.interactable = Bounds.Allows(Quantity);
        }
    }
}
