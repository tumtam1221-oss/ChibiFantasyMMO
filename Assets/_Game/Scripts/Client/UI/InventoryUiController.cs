using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.UI;
using UnityEngine;

namespace ChibiFantasy.Client.UI
{
    /// <summary>
    /// Wires the inventory, storage and equipment panels to gameplay.
    /// </summary>
    /// <remarks>
    /// <b>This is the whole command boundary.</b> Every change to gameplay state that the
    /// UI can cause goes through one of the four submit methods here, and each of them
    /// calls an existing Phase 08.1 service -- <see cref="EquipmentService"/> or
    /// <see cref="ItemContainerTransfer"/>. No panel and no view holds a container, so
    /// there is nowhere else a mutation could originate. The flow is one-way:
    /// state -> adapter -> view data -> panel -> click -> here -> service -> state.
    ///
    /// <b>The service's answer is the answer.</b> Nothing is pre-approved here and no
    /// rejection reason is second-guessed. A refused equip leaves the panels exactly as
    /// they were, because a refused service call changed nothing to redraw.
    ///
    /// <b>Click-based, no drag.</b> A first click selects; a second click on the same slot
    /// performs the obvious action for that panel. Drag and drop would need a canvas-wide
    /// drag layer, ghost rendering and drop targets, which is a UI framework change rather
    /// than a UI feature -- deliberately left to 08.3 rather than destabilising this.
    /// </remarks>
    public sealed class InventoryUiController : MonoBehaviour
    {
        [SerializeField] private ItemContainerPanel inventoryPanel;
        [SerializeField] private ItemContainerPanel storagePanel;
        [SerializeField] private EquipmentPanel equipmentPanel;
        [SerializeField] private ItemTooltipView tooltip;

        private readonly List<ItemSlotViewData> _inventoryView = new List<ItemSlotViewData>();
        private readonly List<ItemSlotViewData> _storageView = new List<ItemSlotViewData>();
        private readonly List<EquipmentSlotViewData> _equipmentView = new List<EquipmentSlotViewData>();

        private ItemContainerState _inventory;
        private ItemContainerState _storage;
        private CharacterEquipmentState _equipment;
        private IDefinitionRegistry<ItemDefinition> _items;

        private int _characterLevel = 1;
        private DefinitionId _characterClass;
        private DefinitionId _characterJob;

        private Revision _lastInventoryRevision;
        private Revision _lastStorageRevision;
        private Revision _lastEquipmentRevision;
        private bool _bound;

        /// <summary>What the player has clicked. UI state; gameplay never sees it.</summary>
        public ItemSelection Selection { get; private set; }

        /// <summary>The answer to the last equip or unequip submitted, for reporting.</summary>
        public EquipResult LastEquipResult { get; private set; }

        /// <summary>The answer to the last deposit or withdraw submitted.</summary>
        public ItemContainerResult LastTransferResult { get; private set; }

        public bool IsStorageOpen { get; private set; }

        /// <summary>
        /// Points the UI at a character's containers.
        /// </summary>
        /// <remarks>The states are borrowed for reading and for handing to services; this
        /// class never assigns into one.</remarks>
        public void Bind(ItemContainerState inventory, ItemContainerState storage,
            CharacterEquipmentState equipment, IDefinitionRegistry<ItemDefinition> items,
            int characterLevel, DefinitionId characterClass = default,
            DefinitionId characterJob = default)
        {
            _inventory = inventory;
            _storage = storage;
            _equipment = equipment;
            _items = items;
            _characterLevel = characterLevel;
            _characterClass = characterClass;
            _characterJob = characterJob;

            Selection = ItemSelection.None;

            HookPanels();

            if (inventoryPanel != null && inventory != null) inventoryPanel.Build(inventory.Capacity);
            if (storagePanel != null && storage != null) storagePanel.Build(storage.Capacity);
            if (equipmentPanel != null) equipmentPanel.Build();

            _bound = true;
            Refresh();
        }

        private void HookPanels()
        {
            if (inventoryPanel != null)
            {
                inventoryPanel.SlotClicked -= OnInventoryClicked;
                inventoryPanel.SlotClicked += OnInventoryClicked;
            }

            if (storagePanel != null)
            {
                storagePanel.SlotClicked -= OnStorageClicked;
                storagePanel.SlotClicked += OnStorageClicked;
            }

            if (equipmentPanel != null)
            {
                equipmentPanel.SlotClicked -= OnEquipmentClicked;
                equipmentPanel.SlotClicked += OnEquipmentClicked;
            }
        }

        /// <summary>Shows or hides the storage panel.</summary>
        public void SetStorageOpen(bool open)
        {
            IsStorageOpen = open;

            if (storagePanel != null) storagePanel.gameObject.SetActive(open);

            // A selection pointing into a closed panel would act on something invisible.
            if (!open && Selection.Source == ItemSelectionSource.Storage) ClearSelection();
        }

        /// <summary>
        /// Redraws every panel from current gameplay state.
        /// </summary>
        /// <remarks>Called after a submitted command and when a container changes for any
        /// other reason. It re-binds existing slot views, so nothing is instantiated.</remarks>
        public void Refresh()
        {
            if (!_bound) return;

            InventoryViewAdapter.BuildContainer(_inventory, _items, _inventoryView);
            InventoryViewAdapter.BuildContainer(_storage, _items, _storageView);
            InventoryViewAdapter.BuildEquipment(_equipment, _items, _equipmentView);

            // A selection whose item is gone stops matching, so it must also stop being
            // held -- otherwise the next action would run against a slot that moved on.
            if (!SelectionStillValid()) Selection = ItemSelection.None;

            if (inventoryPanel != null) inventoryPanel.Refresh(_inventoryView, Selection);
            if (storagePanel != null) storagePanel.Refresh(_storageView, Selection);
            if (equipmentPanel != null) equipmentPanel.Refresh(_equipmentView, Selection);

            RefreshTooltip();
            RecordRevisions();
        }

        /// <summary>
        /// Redraws only if a container actually changed.
        /// </summary>
        /// <remarks>Comparing three revision integers is not the same as rebuilding the
        /// inventory: the walk, the lookups and the re-bind only happen on a real change.
        /// This is what catches state changed by something other than the UI -- loot picked
        /// up, an item consumed -- without the UI subscribing to gameplay it cannot see.</remarks>
        public bool RefreshIfChanged()
        {
            if (!_bound) return false;
            if (!HasChanged()) return false;

            Refresh();
            return true;
        }

        private bool HasChanged()
        {
            if (_inventory != null && _inventory.Revision != _lastInventoryRevision) return true;
            if (_storage != null && _storage.Revision != _lastStorageRevision) return true;
            if (_equipment != null && _equipment.Revision != _lastEquipmentRevision) return true;
            return false;
        }

        private void RecordRevisions()
        {
            if (_inventory != null) _lastInventoryRevision = _inventory.Revision;
            if (_storage != null) _lastStorageRevision = _storage.Revision;
            if (_equipment != null) _lastEquipmentRevision = _equipment.Revision;
        }

        private bool SelectionStillValid()
        {
            if (Selection.IsEmpty) return false;

            switch (Selection.Source)
            {
                case ItemSelectionSource.Inventory:
                    return MatchesAny(_inventoryView);
                case ItemSelectionSource.Storage:
                    return MatchesAny(_storageView);
                case ItemSelectionSource.Equipment:
                    for (int i = 0; i < _equipmentView.Count; i++)
                    {
                        if (Selection.Matches(_equipmentView[i])) return true;
                    }

                    return false;
                default:
                    return false;
            }
        }

        private bool MatchesAny(List<ItemSlotViewData> view)
        {
            for (int i = 0; i < view.Count; i++)
            {
                if (Selection.Matches(view[i])) return true;
            }

            return false;
        }

        // ---- input -------------------------------------------------------------------

        /// <summary>Selects an inventory slot, or acts on it when it is already selected.</summary>
        public void OnInventoryClicked(int slotIndex)
        {
            HandleContainerClick(ItemSelectionSource.Inventory, _inventoryView, slotIndex);
        }

        /// <summary>Selects a storage slot, or acts on it when it is already selected.</summary>
        public void OnStorageClicked(int slotIndex)
        {
            HandleContainerClick(ItemSelectionSource.Storage, _storageView, slotIndex);
        }

        /// <summary>Selects a worn piece, or takes it off when it is already selected.</summary>
        public void OnEquipmentClicked(EquipmentSlot slot)
        {
            EquipmentSlotViewData data = FindEquipment(slot);

            if (data.IsEmpty)
            {
                ClearSelection();
                return;
            }

            if (Selection.Matches(data))
            {
                SubmitUnequip(slot);
                return;
            }

            Selection = new ItemSelection(ItemSelectionSource.Equipment, (int)slot, data.InstanceId);
            AfterSelectionChanged();
        }

        private void HandleContainerClick(ItemSelectionSource source,
            List<ItemSlotViewData> view, int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= view.Count)
            {
                ClearSelection();
                return;
            }

            ItemSlotViewData data = view[slotIndex];

            if (data.IsEmpty)
            {
                ClearSelection();
                return;
            }

            if (Selection.Source == source && Selection.Matches(data))
            {
                Activate(source, slotIndex, data);
                return;
            }

            Selection = new ItemSelection(source, slotIndex, data.InstanceId);
            AfterSelectionChanged();
        }

        /// <summary>
        /// The obvious action for a slot the player clicked twice.
        /// </summary>
        /// <remarks>Inventory: wear it, or stow it when storage is open. Storage: take it
        /// out. A non-equippable item with storage closed has no action yet -- using
        /// consumables is not part of this phase, and inventing an effect for them would be
        /// a gameplay change hidden inside a UI one.</remarks>
        private void Activate(ItemSelectionSource source, int slotIndex, ItemSlotViewData data)
        {
            if (source == ItemSelectionSource.Storage)
            {
                SubmitWithdraw(slotIndex, data.Quantity);
                return;
            }

            if (IsStorageOpen)
            {
                SubmitDeposit(slotIndex, data.Quantity);
                return;
            }

            if (data.IsEquipment) SubmitEquip(slotIndex);
        }

        private void AfterSelectionChanged()
        {
            if (inventoryPanel != null) inventoryPanel.Refresh(_inventoryView, Selection);
            if (storagePanel != null) storagePanel.Refresh(_storageView, Selection);
            if (equipmentPanel != null) equipmentPanel.Refresh(_equipmentView, Selection);
            RefreshTooltip();
        }

        /// <summary>Drops the selection and the tooltip with it.</summary>
        public void ClearSelection()
        {
            Selection = ItemSelection.None;
            AfterSelectionChanged();
        }

        // ---- commands ----------------------------------------------------------------

        /// <summary>Asks gameplay to wear the piece in an inventory slot.</summary>
        public EquipResult SubmitEquip(int inventorySlot)
        {
            var context = new EquipmentService.Context(_items, _characterLevel,
                _characterClass, _characterJob);

            LastEquipResult = EquipmentService.Equip(_inventory, _equipment, inventorySlot, context);

            if (LastEquipResult.IsAccepted) Selection = ItemSelection.None;

            Refresh();
            return LastEquipResult;
        }

        /// <summary>Asks gameplay to take a worn piece off.</summary>
        public EquipResult SubmitUnequip(EquipmentSlot slot)
        {
            var context = new EquipmentService.Context(_items, _characterLevel,
                _characterClass, _characterJob);

            LastEquipResult = EquipmentService.Unequip(_inventory, _equipment, slot, context);

            if (LastEquipResult.IsAccepted) Selection = ItemSelection.None;

            Refresh();
            return LastEquipResult;
        }

        /// <summary>Asks gameplay to move a stack from the inventory into storage.</summary>
        public ItemContainerResult SubmitDeposit(int inventorySlot, int quantity)
        {
            LastTransferResult = ItemContainerTransfer.Deposit(_inventory, _storage,
                inventorySlot, quantity, _items);

            if (LastTransferResult.IsAccepted) Selection = ItemSelection.None;

            Refresh();
            return LastTransferResult;
        }

        /// <summary>Asks gameplay to move a stack from storage into the inventory.</summary>
        public ItemContainerResult SubmitWithdraw(int storageSlot, int quantity)
        {
            LastTransferResult = ItemContainerTransfer.Withdraw(_storage, _inventory,
                storageSlot, quantity, _items);

            if (LastTransferResult.IsAccepted) Selection = ItemSelection.None;

            Refresh();
            return LastTransferResult;
        }

        // ---- tooltip -----------------------------------------------------------------

        private void RefreshTooltip()
        {
            if (tooltip == null) return;

            tooltip.Show(BuildSelectionTooltip());
        }

        /// <summary>The tooltip for whatever is selected. Exposed so the rule is testable.</summary>
        public ItemTooltipData BuildSelectionTooltip()
        {
            switch (Selection.Source)
            {
                case ItemSelectionSource.Inventory:
                    return InventoryViewAdapter.BuildTooltip(_inventory, Selection.SlotIndex, _items);
                case ItemSelectionSource.Storage:
                    return InventoryViewAdapter.BuildTooltip(_storage, Selection.SlotIndex, _items);
                case ItemSelectionSource.Equipment:
                    return InventoryViewAdapter.BuildTooltip(_equipment,
                        (EquipmentSlot)Selection.SlotIndex, _items);
                default:
                    return ItemTooltipData.None;
            }
        }

        private EquipmentSlotViewData FindEquipment(EquipmentSlot slot)
        {
            for (int i = 0; i < _equipmentView.Count; i++)
            {
                if (_equipmentView[i].Slot == slot) return _equipmentView[i];
            }

            return EquipmentSlotViewData.Empty(slot);
        }
    }
}
