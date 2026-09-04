using System.Collections.Generic;
using ChibiFantasy.Data;
using ChibiFantasy.Network;
using ChibiFantasy.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ChibiFantasy.Client.UI
{
    /// <summary>
    /// The bag and the paperdoll, drawn from the server's snapshot.
    /// </summary>
    /// <remarks>
    /// <b>Nothing here is authoritative and nothing here is optimistic.</b> A click sends a
    /// request; the squares change when the next snapshot arrives. That is a frame or two of
    /// delay and it is the honest one -- an item that moved and then moved back because the
    /// server disagreed is worse than an item that took a moment to move.
    ///
    /// <b>Sockets show as a count.</b> The snapshot carries how many stones and cards a piece
    /// has, not which -- a deliberate 18.4 limitation -- so the panel says "2 sockets" and
    /// does not pretend to know what is in them.
    /// </remarks>
    public sealed class InventoryScreen : MonoBehaviour
    {
        private NetworkInventoryPresenter _presenter;

        private RectTransform _panel;
        private RectTransform _bagGrid;
        private RectTransform _paperdoll;
        private TextMeshProUGUI _status;
        private TextMeshProUGUI _detail;

        private readonly List<GameObject> _squares = new List<GameObject>();

        private bool _built;

        /// <summary>Which bag slot is selected, or -1. Presentation only.</summary>
        public int SelectedBagSlot { get; private set; } = -1;

        /// <summary>Which worn slot is selected, or None. Presentation only.</summary>
        public EquipmentSlot SelectedEquipmentSlot { get; private set; } = EquipmentSlot.None;

        public bool IsOpen => _panel != null && _panel.gameObject.activeSelf;

        /// <summary>What the panel is currently drawing, for a test to read.</summary>
        public NetworkInventoryPresenter Presenter => _presenter;

        /// <summary>The last message shown, so it can be read without a renderer.</summary>
        public string StatusMessage { get; private set; } = string.Empty;

        /// <summary>Binds the local player's character and starts listening for snapshots.</summary>
        public bool Bind(CharacterNetworkEntity entity,
            IDefinitionRegistry<ItemDefinition> items)
        {
            EnsureBuilt();

            Unbind();

            _presenter = new NetworkInventoryPresenter(items);
            _presenter.Changed += Repaint;

            bool bound = _presenter.Bind(entity);

            Repaint();

            return bound;
        }

        public void Unbind()
        {
            if (_presenter != null)
            {
                _presenter.Changed -= Repaint;
                _presenter.Unbind();
            }

            _presenter = null;

            ClearSquares();
        }

        public void SetOpen(bool open)
        {
            EnsureBuilt();

            if (_panel != null) _panel.gameObject.SetActive(open);

            if (open) Repaint();
        }

        public void Toggle()
        {
            SetOpen(!IsOpen);
        }

        // ---- what a click does ---------------------------------------------------------------

        /// <summary>Selects a bag square. Selecting changes nothing on the server.</summary>
        public void SelectBagSlot(int slot)
        {
            SelectedBagSlot = slot;
            SelectedEquipmentSlot = EquipmentSlot.None;

            Describe();
        }

        public void SelectEquipmentSlot(EquipmentSlot slot)
        {
            SelectedEquipmentSlot = slot;
            SelectedBagSlot = -1;

            Describe();
        }

        /// <summary>Asks the server to wear the selected item.</summary>
        /// <remarks>Returns whether a request was sent. Whether it worked is the next
        /// snapshot's answer, not this method's.</remarks>
        public bool Equip()
        {
            if (_presenter == null || SelectedBagSlot < 0) return false;

            return _presenter.RequestEquip(SelectedBagSlot);
        }

        /// <summary>Asks the server to take off the selected piece.</summary>
        public bool Unequip()
        {
            if (_presenter == null || SelectedEquipmentSlot == EquipmentSlot.None) return false;

            return _presenter.RequestUnequip(SelectedEquipmentSlot);
        }

        /// <summary>Asks the server to move the selected item to a slot.</summary>
        public bool MoveTo(int slot)
        {
            if (_presenter == null || SelectedBagSlot < 0) return false;

            return _presenter.RequestMove(SelectedBagSlot, slot);
        }

        /// <summary>Asks the server to split the selected stack into a slot.</summary>
        public bool SplitTo(int slot, int quantity)
        {
            if (_presenter == null || SelectedBagSlot < 0) return false;

            return _presenter.RequestSplit(SelectedBagSlot, slot, quantity);
        }

        // ---- drawing --------------------------------------------------------------------------

        private void Repaint()
        {
            ClearSquares();

            if (_presenter == null || !_presenter.HasSnapshot)
            {
                // Not "empty bag". Nothing has arrived yet, and a grid of empty squares
                // would be a claim about the character that the client cannot make.
                SetStatus("Waiting for server state");

                return;
            }

            SetStatus(string.Empty);

            IReadOnlyList<ItemSlotViewData> bag = _presenter.Bag;

            for (int i = 0; i < bag.Count; i++)
            {
                int slot = i;

                AddSquare(_bagGrid, bag[i].IsOccupied ? Label(bag[i]) : string.Empty,
                    () => SelectBagSlot(slot));
            }

            IReadOnlyList<EquipmentSlotViewData> worn = _presenter.Worn;

            for (int i = 0; i < worn.Count; i++)
            {
                EquipmentSlot slot = worn[i].Slot;

                AddSquare(_paperdoll,
                    worn[i].IsOccupied ? slot + ": worn" : slot.ToString(),
                    () => SelectEquipmentSlot(slot));
            }

            Describe();
        }

        private void SetStatus(string message)
        {
            StatusMessage = message ?? string.Empty;

            if (_status != null) _status.text = StatusMessage;
        }

        /// <summary>What a square says. The definition's name when there is one.</summary>
        private static string Label(in ItemSlotViewData item)
        {
            string name = string.IsNullOrEmpty(item.NameKey.Key)
                ? item.DefinitionId.Value
                : item.NameKey.Key;

            return item.ShowQuantity ? name + "  x" + item.Quantity : name;
        }

        /// <summary>Writes the detail line for whatever is selected.</summary>
        private void Describe()
        {
            if (_detail == null || _presenter == null) return;

            if (SelectedBagSlot >= 0 && SelectedBagSlot < _presenter.Bag.Count)
            {
                ItemSlotViewData item = _presenter.Bag[SelectedBagSlot];

                _detail.text = item.IsEmpty
                    ? "Empty slot " + SelectedBagSlot
                    : Label(item) + (item.IsEquipment ? "  (equipment)" : string.Empty);

                return;
            }

            if (SelectedEquipmentSlot != EquipmentSlot.None)
            {
                _detail.text = SelectedEquipmentSlot.ToString();

                return;
            }

            _detail.text = string.Empty;
        }

        private void AddSquare(RectTransform parent, string text, System.Action onPicked)
        {
            Button button = UiFactory.CreateButton("Slot", parent, text,
                out TextMeshProUGUI label);

            button.GetComponent<Image>().color = UiFactory.Slot;
            label.fontSize = 14f;

            var element = button.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = 96f;
            element.preferredHeight = 48f;

            if (onPicked != null) button.onClick.AddListener(() => onPicked());

            _squares.Add(button.gameObject);
        }

        private void ClearSquares()
        {
            for (int i = 0; i < _squares.Count; i++)
            {
                UiFactory.DestroyWidget(_squares[i]);
            }

            _squares.Clear();
        }

        private void Awake()
        {
            EnsureBuilt();
        }

        /// <summary>Builds this screen's widgets, once.</summary>
        /// <remarks>Unity only sends <c>Awake</c> while the player loop is running, so a
        /// screen added and bound in the same breath would otherwise have no widgets at all.
        /// Building on first use makes both orders identical, and the flag makes a second
        /// call after <c>Awake</c> harmless rather than a second canvas.</remarks>
        private void EnsureBuilt()
        {
            if (_built) return;

            _built = true;

            Build();
        }

        private void Build()
        {
            Canvas canvas = UiFactory.CreateCanvas("Inventory Canvas", gameObject);

            RectTransform root = UiFactory.CreateStretched("Root", canvas.transform);

            _panel = UiFactory.CreateAnchored("Panel", root, new Vector2(0.5f, 0.5f),
                new Vector2(760f, 560f));

            UiFactory.CreatePanel("Frame", _panel, UiFactory.Panel).rectTransform
                .SetAsFirstSibling();

            var frame = (RectTransform)_panel.GetChild(0);
            frame.anchorMin = Vector2.zero;
            frame.anchorMax = Vector2.one;
            frame.offsetMin = Vector2.zero;
            frame.offsetMax = Vector2.zero;

            UiFactory.CreateLabel("Title", _panel, "Inventory", 28f,
                TextAlignmentOptions.Center).rectTransform.anchoredPosition =
                new Vector2(0f, -16f);

            _bagGrid = Grid("Bag", new Vector2(0f, 1f), new Vector2(420f, 400f),
                new Vector2(20f, -60f));

            _paperdoll = Grid("Equipment", new Vector2(1f, 1f), new Vector2(280f, 400f),
                new Vector2(-20f, -60f));

            _detail = UiFactory.CreateLabel("Detail", _panel, string.Empty, 18f);
            _detail.rectTransform.anchorMin = new Vector2(0f, 0f);
            _detail.rectTransform.anchorMax = new Vector2(1f, 0f);
            _detail.rectTransform.pivot = new Vector2(0.5f, 0f);
            _detail.rectTransform.sizeDelta = new Vector2(-40f, 30f);
            _detail.rectTransform.anchoredPosition = new Vector2(0f, 58f);

            _status = UiFactory.CreateLabel("Status", _panel, string.Empty, 18f,
                TextAlignmentOptions.Center);
            _status.color = UiFactory.Muted;
            _status.rectTransform.anchorMin = new Vector2(0f, 0f);
            _status.rectTransform.anchorMax = new Vector2(1f, 0f);
            _status.rectTransform.pivot = new Vector2(0.5f, 0f);
            _status.rectTransform.sizeDelta = new Vector2(-40f, 30f);
            _status.rectTransform.anchoredPosition = new Vector2(0f, 24f);

            Button equip = UiFactory.CreateButton("Equip", _panel, "Equip",
                out TextMeshProUGUI _);
            Action(equip.GetComponent<RectTransform>(), new Vector2(20f, 96f));
            equip.onClick.AddListener(() => Equip());

            Button unequip = UiFactory.CreateButton("Unequip", _panel, "Unequip",
                out TextMeshProUGUI _);
            Action(unequip.GetComponent<RectTransform>(), new Vector2(190f, 96f));
            unequip.onClick.AddListener(() => Unequip());

            Button close = UiFactory.CreateButton("Close", _panel, "Close",
                out TextMeshProUGUI _);
            close.GetComponent<Image>().color = UiFactory.Slot;
            Action(close.GetComponent<RectTransform>(), new Vector2(-20f, 96f));
            close.GetComponent<RectTransform>().anchorMin = new Vector2(1f, 0f);
            close.GetComponent<RectTransform>().anchorMax = new Vector2(1f, 0f);
            close.GetComponent<RectTransform>().pivot = new Vector2(1f, 0f);
            close.onClick.AddListener(() => SetOpen(false));

            _panel.gameObject.SetActive(false);
        }

        private RectTransform Grid(string name, Vector2 anchor, Vector2 size, Vector2 offset)
        {
            RectTransform rect = UiFactory.CreateAnchored(name, _panel, anchor, size, offset);

            var layout = rect.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(96f, 48f);
            layout.spacing = new Vector2(6f, 6f);

            return rect;
        }

        private static void Action(RectTransform rect, Vector2 offset)
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.sizeDelta = new Vector2(160f, 44f);
            rect.anchoredPosition = offset;
        }
    }
}
