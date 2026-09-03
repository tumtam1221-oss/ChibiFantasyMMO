using System.Collections.Generic;
using ChibiFantasy.Data;
using UnityEngine;
using UnityEngine.UI;

namespace ChibiFantasy.UI
{
    /// <summary>
    /// The paperdoll: one square per equipment position.
    /// </summary>
    /// <remarks>
    /// <b>Positions come from the existing enum.</b> The panel is configured with a list of
    /// <see cref="EquipmentSlot"/> values and draws exactly those; no slot type is defined
    /// here. Showing a subset is a layout decision, not a schema one.
    ///
    /// <b>It reuses <see cref="ItemSlotView"/>.</b> A worn helm and a helm in the bag are
    /// the same square with the same rules, so slot rendering exists once.
    /// <see cref="ItemSlotView"/> is keyed by an integer index, so this maps each position
    /// to its position in the configured list and translates back on click.
    ///
    /// Like the container panel, squares are built once and re-bound on refresh.
    /// </remarks>
    public sealed class EquipmentPanel : MonoBehaviour
    {
        private static readonly EquipmentSlot[] DefaultSlots =
        {
            EquipmentSlot.Head,
            EquipmentSlot.Body,
            EquipmentSlot.Legs,
            EquipmentSlot.Feet,
            EquipmentSlot.Hands,
            EquipmentSlot.MainHand,
            EquipmentSlot.OffHand,
            EquipmentSlot.Accessory,
            EquipmentSlot.Cape
        };

        [SerializeField] private RectTransform slotParent;
        [SerializeField] private float slotSize = 56f;
        [SerializeField] private float spacing = 4f;
        [SerializeField] private int columns = 3;

        [Tooltip("Which positions this paperdoll shows. Empty uses every authored slot.")]
        [SerializeField] private List<EquipmentSlot> displayedSlots = new List<EquipmentSlot>();

        private readonly List<ItemSlotView> _views = new List<ItemSlotView>();
        private readonly List<EquipmentSlot> _order = new List<EquipmentSlot>();

        /// <summary>Raised when a position is clicked, carrying the slot it represents.</summary>
        public event System.Action<EquipmentSlot> SlotClicked;

        public IReadOnlyList<EquipmentSlot> Order => _order;

        /// <summary>Creates one square per displayed position.</summary>
        public void Build()
        {
            EnsureParent();

            _order.Clear();

            if (displayedSlots != null && displayedSlots.Count > 0) _order.AddRange(displayedSlots);
            else _order.AddRange(DefaultSlots);

            for (int i = _views.Count; i < _order.Count; i++) _views.Add(CreateSlot(i));

            for (int i = 0; i < _views.Count; i++) _views[i].gameObject.SetActive(i < _order.Count);
        }

        /// <summary>Re-draws each position from a fresh snapshot.</summary>
        public void Refresh(IReadOnlyList<EquipmentSlotViewData> data, ItemSelection selection)
        {
            if (data == null) return;
            if (_order.Count == 0) Build();

            for (int i = 0; i < _order.Count && i < _views.Count; i++)
            {
                EquipmentSlotViewData match = EquipmentSlotViewData.Empty(_order[i]);

                for (int d = 0; d < data.Count; d++)
                {
                    if (data[d].Slot != _order[i]) continue;
                    match = data[d];
                    break;
                }

                // Reuse the item square by giving it this position's index.
                _views[i].Bind(ItemSlotViewData.ForEquipment(i, match), selection.Matches(match));
            }
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
            var go = new GameObject("Equip" + index, typeof(RectTransform));
            go.transform.SetParent(slotParent, false);

            var view = go.AddComponent<ItemSlotView>();
            view.EnsureVisuals();
            view.Bind(ItemSlotViewData.Empty(index), false);
            view.Clicked += OnSlotClicked;
            return view;
        }

        private void OnSlotClicked(int index)
        {
            if (index < 0 || index >= _order.Count) return;

            var handler = SlotClicked;
            if (handler != null) handler(_order[index]);
        }
    }
}
