using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ChibiFantasy.UI
{
    /// <summary>
    /// A grid of slots for one container.
    /// </summary>
    /// <remarks>
    /// <b>Capacity-driven.</b> The number of squares comes from whatever the container
    /// reports; no size is written here. <see cref="Build"/> is called once when the
    /// capacity is known and again only if it changes.
    ///
    /// <b>Inventory and storage share this.</b> They are the same container type in
    /// gameplay, so drawing them twice in two panels would be duplication for no reason.
    /// A panel is pointed at whichever container the controller hands it.
    ///
    /// <b>Slots are reused.</b> <see cref="Refresh"/> re-binds the existing views; nothing
    /// is instantiated or destroyed per refresh, and nothing polls. The controller calls
    /// it after a gameplay change, which is the only time the contents can differ.
    /// </remarks>
    public sealed class ItemContainerPanel : MonoBehaviour
    {
        [SerializeField] private RectTransform slotParent;
        [SerializeField] private int columns = 5;
        [SerializeField] private float slotSize = 56f;
        [SerializeField] private float spacing = 4f;

        private readonly List<ItemSlotView> _slots = new List<ItemSlotView>();

        /// <summary>Raised when a slot is clicked, carrying its index.</summary>
        public event System.Action<int> SlotClicked;

        /// <summary>Raised on a right click. What opens a context menu.</summary>
        public event System.Action<int> SlotRightClicked;

        /// <summary>Raised when a drag begins on a slot of this panel.</summary>
        public event System.Action<int> SlotDragStarted;

        /// <summary>Raised while dragging, carrying the pointer position.</summary>
        public event System.Action<Vector2> SlotDragging;

        /// <summary>Raised when a drag from this panel is released, wherever it landed.</summary>
        public event System.Action SlotDragEnded;

        /// <summary>Raised when a drag is released onto a slot of this panel.</summary>
        public event System.Action<int> SlotDropped;

        /// <summary>Raised when the pointer enters a slot of this panel.</summary>
        public event System.Action<int> SlotHovered;

        /// <summary>Raised when the pointer leaves a slot of this panel.</summary>
        public event System.Action<int> SlotUnhovered;

        public int SlotCount => _slots.Count;

        public IReadOnlyList<ItemSlotView> Slots => _slots;

        /// <summary>
        /// Creates exactly <paramref name="capacity"/> squares, reusing what already exists.
        /// </summary>
        /// <remarks>Called when a container is attached or its capacity changes, never per
        /// frame and never per refresh.</remarks>
        public void Build(int capacity)
        {
            if (capacity < 0) capacity = 0;

            EnsureParent();

            for (int i = _slots.Count; i < capacity; i++) _slots.Add(CreateSlot(i));

            // Surplus squares are hidden rather than destroyed, so shrinking and growing
            // again costs nothing.
            for (int i = 0; i < _slots.Count; i++)
            {
                _slots[i].gameObject.SetActive(i < capacity);
            }
        }

        /// <summary>Re-draws every visible slot from a fresh snapshot.</summary>
        public void Refresh(IReadOnlyList<ItemSlotViewData> data, ItemSelection selection)
        {
            Refresh(data, selection, null);
        }

        /// <summary>
        /// Re-draws every visible slot, resolving icons through <paramref name="icons"/>.
        /// </summary>
        /// <remarks>The resolver caches, so a refresh of a full bag costs no loads after
        /// the first. A null resolver is fine and draws placeholder colours.</remarks>
        public void Refresh(IReadOnlyList<ItemSlotViewData> data, ItemSelection selection,
            IconResolver icons)
        {
            if (data == null) return;

            if (data.Count != _slots.Count) Build(data.Count);

            for (int i = 0; i < data.Count && i < _slots.Count; i++)
            {
                Sprite icon = icons == null ? null : icons.Resolve(data[i].Icon);
                _slots[i].Bind(data[i], selection.Matches(data[i]), icon);
            }
        }

        /// <summary>
        /// Paints the advisory drop indicator across the panel for an active drag.
        /// </summary>
        /// <remarks>Recomputed for every slot rather than tracked incrementally, because a
        /// stale hint left on a slot the pointer already passed is the failure mode that
        /// actually happens. See <see cref="ItemDropAdvice"/>: this is advice, not
        /// permission.</remarks>
        public void ApplyDropHints(ItemDragPayload drag, ItemSelectionSource ownSource)
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                _slots[i].SetDropHint(drag.IsActive
                    ? ItemDropAdvice.ForContainerSlot(drag, ownSource, i)
                    : SlotDropHint.None);
            }
        }

        /// <summary>Clears every drop indicator. Called when a drag ends or is cancelled.</summary>
        public void ClearDropHints()
        {
            for (int i = 0; i < _slots.Count; i++) _slots[i].SetDropHint(SlotDropHint.None);
        }

        private void EnsureParent()
        {
            if (slotParent == null) slotParent = GetComponent<RectTransform>();
            if (slotParent == null) slotParent = gameObject.AddComponent<RectTransform>();

            var layout = slotParent.GetComponent<GridLayoutGroup>();
            if (layout == null) layout = slotParent.gameObject.AddComponent<GridLayoutGroup>();

            layout.cellSize = new Vector2(slotSize, slotSize);
            layout.spacing = new Vector2(spacing, spacing);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = columns < 1 ? 1 : columns;
        }

        private ItemSlotView CreateSlot(int index)
        {
            var go = new GameObject("Slot" + index, typeof(RectTransform));
            go.transform.SetParent(slotParent, false);

            var view = go.AddComponent<ItemSlotView>();
            view.EnsureVisuals();
            view.Bind(ItemSlotViewData.Empty(index), false);

            // Each slot's events are republished as the panel's, so the controller
            // subscribes once per panel instead of once per slot -- and keeps working
            // when the panel grows.
            view.Clicked += OnSlotClicked;
            view.RightClicked += OnSlotRightClicked;
            view.DragStarted += OnSlotDragStarted;
            view.Dragging += OnSlotDragging;
            view.DragEnded += OnSlotDragEnded;
            view.Dropped += OnSlotDropped;
            view.HoverEntered += OnSlotHovered;
            view.HoverExited += OnSlotUnhovered;
            return view;
        }

        private void OnSlotClicked(int index)
        {
            var handler = SlotClicked;
            if (handler != null) handler(index);
        }

        private void OnSlotRightClicked(int index)
        {
            var handler = SlotRightClicked;
            if (handler != null) handler(index);
        }

        private void OnSlotDragStarted(int index)
        {
            var handler = SlotDragStarted;
            if (handler != null) handler(index);
        }

        private void OnSlotDragging(Vector2 pointer)
        {
            var handler = SlotDragging;
            if (handler != null) handler(pointer);
        }

        private void OnSlotDragEnded()
        {
            var handler = SlotDragEnded;
            if (handler != null) handler();
        }

        private void OnSlotDropped(int index)
        {
            var handler = SlotDropped;
            if (handler != null) handler(index);
        }

        private void OnSlotHovered(int index)
        {
            var handler = SlotHovered;
            if (handler != null) handler(index);
        }

        private void OnSlotUnhovered(int index)
        {
            var handler = SlotUnhovered;
            if (handler != null) handler(index);
        }
    }
}
