using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ChibiFantasy.UI
{
    /// <summary>
    /// Shows a piece's sockets and offers to fill one.
    /// </summary>
    /// <remarks>
    /// <b>It reuses <see cref="ItemSlotView"/>.</b> A socket is a square holding an item,
    /// with the same missing-art and selection behaviour as every other square in the
    /// project. A second slot renderer here would be a second thing to keep in step.
    ///
    /// <b>It draws snapshots and raises events.</b> The panel holds
    /// <see cref="EnchantSlotViewData"/> copies; it never touches an enchant list. Clicking
    /// a socket reports which one, and asking to apply reports nothing more than the
    /// intent -- the controller supplies the stone and gameplay decides.
    ///
    /// Sockets are built once per capacity change and re-bound after that, like the
    /// container panel.
    /// </remarks>
    public sealed class EnchantPanel : MonoBehaviour
    {
        [SerializeField] private RectTransform socketParent;
        [SerializeField] private Text titleText;
        [SerializeField] private float slotSize = 44f;
        [SerializeField] private float spacing = 4f;
        [SerializeField] private int columns = 4;

        private readonly List<ItemSlotView> _sockets = new List<ItemSlotView>();
        private readonly List<EnchantSlotViewData> _data = new List<EnchantSlotViewData>();

        /// <summary>Raised with the socket index a player clicked.</summary>
        public event System.Action<int> SocketClicked;

        public int SocketCount => _data.Count;

        public IReadOnlyList<EnchantSlotViewData> Sockets => _data;

        /// <summary>Where keys are translated. Optional.</summary>
        public ILocalizedTextSource Text { get; set; }

        /// <summary>Creates the container when a prefab was not authored.</summary>
        public void EnsureVisuals()
        {
            if (socketParent == null) socketParent = GetComponent<RectTransform>();
            if (socketParent == null) socketParent = gameObject.AddComponent<RectTransform>();

            var layout = socketParent.GetComponent<GridLayoutGroup>();
            if (layout == null) layout = socketParent.gameObject.AddComponent<GridLayoutGroup>();

            layout.cellSize = new Vector2(slotSize, slotSize);
            layout.spacing = new Vector2(spacing, spacing);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = columns < 1 ? 1 : columns;
            layout.padding = new RectOffset(4, 4, 24, 4);
        }

        /// <summary>
        /// Draws a piece's sockets.
        /// </summary>
        /// <remarks>Squares are created only when the count grows and reused after that, so
        /// a refresh instantiates nothing.</remarks>
        public void Show(IReadOnlyList<EnchantSlotViewData> sockets, string title,
            IconResolver icons)
        {
            EnsureVisuals();

            _data.Clear();
            if (sockets != null)
            {
                for (int i = 0; i < sockets.Count; i++) _data.Add(sockets[i]);
            }

            for (int i = _sockets.Count; i < _data.Count; i++) _sockets.Add(CreateSocket(i));

            for (int i = 0; i < _sockets.Count; i++)
            {
                bool used = i < _data.Count;
                _sockets[i].gameObject.SetActive(used);
                if (!used) continue;

                EnchantSlotViewData socket = _data[i];

                // The shared square is keyed by an integer, which is exactly what a socket
                // index is, so no translation is needed here.
                ItemSlotViewData square = socket.IsEmpty
                    ? ItemSlotViewData.Empty(socket.SocketIndex)
                    : ItemSlotViewData.From(socket.SocketIndex, socket.Stone,
                        ChibiFantasy.Core.InstanceId.None, 1, null);

                Sprite icon = icons == null ? null : icons.Resolve(socket.Icon);
                _sockets[i].Bind(square, false, icon);
            }

            if (titleText != null) titleText.text = title ?? string.Empty;
        }

        /// <summary>Empties the panel.</summary>
        public void Clear()
        {
            _data.Clear();

            for (int i = 0; i < _sockets.Count; i++) _sockets[i].gameObject.SetActive(false);
            if (titleText != null) titleText.text = string.Empty;
        }

        private ItemSlotView CreateSocket(int index)
        {
            var go = new GameObject("Socket" + index, typeof(RectTransform));
            go.transform.SetParent(socketParent, false);

            var view = go.AddComponent<ItemSlotView>();
            view.EnsureVisuals();
            view.Bind(ItemSlotViewData.Empty(index), false);
            view.Clicked += OnSocketClicked;
            return view;
        }

        private void OnSocketClicked(int socketIndex)
        {
            var handler = SocketClicked;
            if (handler != null) handler(socketIndex);
        }
    }
}
