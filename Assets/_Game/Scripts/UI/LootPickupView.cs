using System.Collections.Generic;
using ChibiFantasy.Core;
using UnityEngine;
using UnityEngine.UI;

namespace ChibiFantasy.UI
{
    /// <summary>
    /// The contents of a loot pile, and a way to take them.
    /// </summary>
    /// <remarks>
    /// <b>It reuses <see cref="ItemSlotView"/>.</b> A loot line is a square holding an
    /// item, with the same missing-art behaviour as every other square in the project. A
    /// second renderer here would be a second thing to keep in step.
    ///
    /// <b>Snapshots and a click event.</b> It holds <see cref="LootEntryViewData"/> copies
    /// and never a <c>LootObjectState</c>, so a panel cannot take loot by drawing it.
    /// Clicking reports which entry; <c>LootPickupService</c> decides whether it may be
    /// taken, and by whom.
    ///
    /// Taken entries are shown greyed rather than removed, so the list does not jump under
    /// a player's cursor mid-click.
    /// </remarks>
    public sealed class LootPickupView : MonoBehaviour
    {
        [SerializeField] private RectTransform entryParent;
        [SerializeField] private Text titleText;
        [SerializeField] private float slotSize = 44f;
        [SerializeField] private float spacing = 4f;
        [SerializeField] private int columns = 4;

        private readonly List<ItemSlotView> _slots = new List<ItemSlotView>();
        private readonly List<LootEntryViewData> _entries = new List<LootEntryViewData>();

        /// <summary>Raised with the entry index a player clicked.</summary>
        public event System.Action<int> EntryClicked;

        public bool IsVisible { get; private set; }

        public int EntryCount => _entries.Count;

        public IReadOnlyList<LootEntryViewData> Entries => _entries;

        /// <summary>Where keys are translated. Optional.</summary>
        public ILocalizedTextSource Text { get; set; }

        /// <summary>Creates the container when a prefab was not authored.</summary>
        public void EnsureVisuals()
        {
            if (entryParent == null) entryParent = GetComponent<RectTransform>();
            if (entryParent == null) entryParent = gameObject.AddComponent<RectTransform>();

            var background = GetComponent<Image>();
            if (background == null)
            {
                background = gameObject.AddComponent<Image>();
                background.color = new Color(0.08f, 0.09f, 0.11f, 0.94f);
            }

            var layout = entryParent.GetComponent<GridLayoutGroup>();
            if (layout == null) layout = entryParent.gameObject.AddComponent<GridLayoutGroup>();

            layout.cellSize = new Vector2(slotSize, slotSize);
            layout.spacing = new Vector2(spacing, spacing);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = columns < 1 ? 1 : columns;
            layout.padding = new RectOffset(4, 4, 22, 4);
        }

        /// <summary>
        /// Draws a pile.
        /// </summary>
        /// <remarks>Squares are created only when the list grows and reused after that, so
        /// a refresh instantiates nothing.</remarks>
        public void Show(IReadOnlyList<LootEntryViewData> entries, string title,
            IconResolver icons)
        {
            EnsureVisuals();

            _entries.Clear();
            if (entries != null)
            {
                for (int i = 0; i < entries.Count; i++) _entries.Add(entries[i]);
            }

            for (int i = _slots.Count; i < _entries.Count; i++) _slots.Add(CreateSlot(i));

            for (int i = 0; i < _slots.Count; i++)
            {
                bool used = i < _entries.Count;
                _slots[i].gameObject.SetActive(used);
                if (!used) continue;

                LootEntryViewData entry = _entries[i];

                // A taken entry draws as an empty square: still there, visibly gone.
                ItemSlotViewData square = entry.IsTaken
                    ? ItemSlotViewData.Empty(entry.Index)
                    : ItemSlotViewData.From(entry.Index, entry.Item, InstanceId.None,
                        entry.Quantity, null);

                Sprite icon = entry.IsTaken || icons == null ? null : icons.Resolve(entry.Icon);
                _slots[i].Bind(square, false, icon);
            }

            IsVisible = _entries.Count > 0;

            if (titleText != null) titleText.text = title ?? string.Empty;
            gameObject.SetActive(IsVisible);
        }

        public void Hide()
        {
            _entries.Clear();
            IsVisible = false;

            for (int i = 0; i < _slots.Count; i++) _slots[i].gameObject.SetActive(false);
            if (titleText != null) titleText.text = string.Empty;

            gameObject.SetActive(false);
        }

        private ItemSlotView CreateSlot(int index)
        {
            var go = new GameObject("Loot" + index, typeof(RectTransform));
            go.transform.SetParent(entryParent, false);

            var view = go.AddComponent<ItemSlotView>();
            view.EnsureVisuals();
            view.Bind(ItemSlotViewData.Empty(index), false);
            view.Clicked += OnEntryClicked;
            return view;
        }

        private void OnEntryClicked(int index)
        {
            var handler = EntryClicked;
            if (handler != null) handler(index);
        }
    }
}
