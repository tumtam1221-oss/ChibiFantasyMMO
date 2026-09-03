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
    /// UI can cause goes through a submit method here, and each of them calls an existing
    /// service -- <see cref="EquipmentService"/>, <see cref="ItemContainerTransfer"/>,
    /// <see cref="ItemContainerState"/>'s own move and split, or
    /// <see cref="ItemUseService"/>. No panel, view, dialog, menu or drag holds a
    /// container, so there is nowhere else a mutation could originate. The flow is one-way:
    /// state -> adapter -> view data -> panel -> input -> here -> service -> state.
    ///
    /// <b>The service's answer is the answer.</b> Nothing is pre-approved and no rejection
    /// is second-guessed. A refused command leaves the panels as they were, because a
    /// refused service call changed nothing to redraw. Nothing is applied optimistically:
    /// there is no rollback in the architecture, so there is nothing to roll back.
    ///
    /// <b>Drag is input.</b> It carries an <see cref="ItemDragPayload"/> -- a snapshot with
    /// an <see cref="InstanceId"/> in it -- and a drop turns into exactly one service call.
    /// Every drop re-checks that the dragged item is still where it was picked up
    /// (<see cref="ItemDragPayload.StillAt(ItemSlotViewData)"/>) and cancels if not.
    /// </remarks>
    public sealed class InventoryUiController : MonoBehaviour
    {
        [SerializeField] private ItemContainerPanel inventoryPanel;
        [SerializeField] private ItemContainerPanel storagePanel;
        [SerializeField] private EquipmentPanel equipmentPanel;
        [SerializeField] private ItemTooltipView tooltip;

        [Header("Interaction")]
        [SerializeField] private ItemDragVisual dragVisual;
        [SerializeField] private SplitStackDialog splitDialog;
        [SerializeField] private ItemContextMenu contextMenu;

        private readonly List<ItemSlotViewData> _inventoryView = new List<ItemSlotViewData>();
        private readonly List<ItemSlotViewData> _storageView = new List<ItemSlotViewData>();
        private readonly List<EquipmentSlotViewData> _equipmentView = new List<EquipmentSlotViewData>();
        private readonly List<ItemContextAction> _actions = new List<ItemContextAction>();
        private readonly List<ItemBuffGrant> _grantedBuffs = new List<ItemBuffGrant>();

        private ItemContainerState _inventory;
        private ItemContainerState _storage;
        private CharacterEquipmentState _equipment;
        private CharacterResourceState _resources;
        private IDefinitionRegistry<ItemDefinition> _items;
        private IDefinitionRegistry<MapDefinition> _maps;
        private IDefinitionRegistry<StatusEffectDefinition> _statusEffects;
        private ResourceLimits _limits;

        private int _characterLevel = 1;
        private DefinitionId _characterClass;
        private DefinitionId _characterJob;

        private Revision _lastInventoryRevision;
        private Revision _lastStorageRevision;
        private Revision _lastEquipmentRevision;
        private bool _bound;

        /// <summary>What the player has clicked. UI state; gameplay never sees it.</summary>
        public ItemSelection Selection { get; private set; }

        /// <summary>What is being dragged, if anything. UI state.</summary>
        public ItemDragPayload Drag { get; private set; }

        /// <summary>Icons for the panels. Optional; without one, slots draw placeholders.</summary>
        public IconResolver Icons { get; set; }

        /// <summary>Where keys are translated. Optional; without one, keys draw raw.</summary>
        public ILocalizedTextSource Text { get; set; }

        /// <summary>The answer to the last equip or unequip submitted.</summary>
        public EquipResult LastEquipResult { get; private set; }

        /// <summary>The answer to the last transfer, move or split submitted.</summary>
        public ItemContainerResult LastContainerResult { get; private set; }

        /// <summary>The answer to the last item use submitted.</summary>
        public ItemUseResult LastUseResult { get; private set; }

        /// <summary>
        /// Buffs the last accepted use granted.
        /// </summary>
        /// <remarks>Resolved from data, not applied to a character: see
        /// <see cref="ItemBuffGrant"/>. Exposed so a future status system can pick them up
        /// and so the resolution is observable now.</remarks>
        public IReadOnlyList<ItemBuffGrant> LastGrantedBuffs => _grantedBuffs;

        /// <summary>
        /// Where the last accepted use asked to send the character.
        /// </summary>
        /// <remarks>A validated destination, not a completed journey. Travel is a later
        /// system's and, in a served game, the server's.</remarks>
        public DefinitionId PendingWarpDestination { get; private set; }

        public bool IsStorageOpen { get; private set; }

        /// <summary>
        /// Points the UI at a character's containers.
        /// </summary>
        /// <remarks>
        /// The states are borrowed for reading and for handing to services; this class never
        /// assigns into one.
        ///
        /// The resource state, map registry and status registry are optional: they are what
        /// item use needs, and a screen with no consumables does not have to supply them.
        /// Item use is then refused with <see cref="ItemUseRejection.MissingContext"/>
        /// rather than half-working.
        /// </remarks>
        public void Bind(ItemContainerState inventory, ItemContainerState storage,
            CharacterEquipmentState equipment, IDefinitionRegistry<ItemDefinition> items,
            int characterLevel, DefinitionId characterClass = default,
            DefinitionId characterJob = default,
            CharacterResourceState resources = null, ResourceLimits limits = default,
            IDefinitionRegistry<MapDefinition> maps = null,
            IDefinitionRegistry<StatusEffectDefinition> statusEffects = null)
        {
            _inventory = inventory;
            _storage = storage;
            _equipment = equipment;
            _items = items;
            _characterLevel = characterLevel;
            _characterClass = characterClass;
            _characterJob = characterJob;
            _resources = resources;
            _limits = limits;
            _maps = maps;
            _statusEffects = statusEffects;

            Selection = ItemSelection.None;
            Drag = ItemDragPayload.None;
            PendingWarpDestination = DefinitionId.None;
            _grantedBuffs.Clear();

            HookPanels();
            HookInteraction();

            if (inventoryPanel != null && inventory != null) inventoryPanel.Build(inventory.Capacity);
            if (storagePanel != null && storage != null) storagePanel.Build(storage.Capacity);
            if (equipmentPanel != null) equipmentPanel.Build();
            if (tooltip != null) tooltip.Text = Text;

            _bound = true;
            Refresh();
        }

        private void HookPanels()
        {
            if (inventoryPanel != null)
            {
                inventoryPanel.SlotClicked -= OnInventoryClicked;
                inventoryPanel.SlotClicked += OnInventoryClicked;
                inventoryPanel.SlotRightClicked -= OnInventoryRightClicked;
                inventoryPanel.SlotRightClicked += OnInventoryRightClicked;
                inventoryPanel.SlotDragStarted -= OnInventoryDragStarted;
                inventoryPanel.SlotDragStarted += OnInventoryDragStarted;
                inventoryPanel.SlotDragging -= DragTo;
                inventoryPanel.SlotDragging += DragTo;
                inventoryPanel.SlotDragEnded -= OnDragEnded;
                inventoryPanel.SlotDragEnded += OnDragEnded;
                inventoryPanel.SlotDropped -= OnInventoryDropped;
                inventoryPanel.SlotDropped += OnInventoryDropped;
            }

            if (storagePanel != null)
            {
                storagePanel.SlotClicked -= OnStorageClicked;
                storagePanel.SlotClicked += OnStorageClicked;
                storagePanel.SlotRightClicked -= OnStorageRightClicked;
                storagePanel.SlotRightClicked += OnStorageRightClicked;
                storagePanel.SlotDragStarted -= OnStorageDragStarted;
                storagePanel.SlotDragStarted += OnStorageDragStarted;
                storagePanel.SlotDragging -= DragTo;
                storagePanel.SlotDragging += DragTo;
                storagePanel.SlotDragEnded -= OnDragEnded;
                storagePanel.SlotDragEnded += OnDragEnded;
                storagePanel.SlotDropped -= OnStorageDropped;
                storagePanel.SlotDropped += OnStorageDropped;
            }

            if (equipmentPanel == null) return;

            equipmentPanel.SlotClicked -= OnEquipmentClicked;
            equipmentPanel.SlotClicked += OnEquipmentClicked;
            equipmentPanel.SlotRightClicked -= OnEquipmentRightClicked;
            equipmentPanel.SlotRightClicked += OnEquipmentRightClicked;
            equipmentPanel.SlotDragStarted -= OnEquipmentDragStarted;
            equipmentPanel.SlotDragStarted += OnEquipmentDragStarted;
            equipmentPanel.SlotDragging -= DragTo;
            equipmentPanel.SlotDragging += DragTo;
            equipmentPanel.SlotDragEnded -= OnDragEnded;
            equipmentPanel.SlotDragEnded += OnDragEnded;
            equipmentPanel.SlotDropped -= OnEquipmentDropped;
            equipmentPanel.SlotDropped += OnEquipmentDropped;
        }

        private void HookInteraction()
        {
            if (splitDialog != null)
            {
                splitDialog.Confirmed -= OnSplitConfirmed;
                splitDialog.Confirmed += OnSplitConfirmed;
            }

            if (contextMenu == null) return;

            contextMenu.Picked -= OnContextActionPicked;
            contextMenu.Picked += OnContextActionPicked;
        }

        /// <summary>Shows or hides the storage panel.</summary>
        public void SetStorageOpen(bool open)
        {
            IsStorageOpen = open;

            if (storagePanel != null) storagePanel.gameObject.SetActive(open);

            // Anything pointing into a closed panel would act on something invisible.
            if (open) return;

            if (Selection.Source == ItemSelectionSource.Storage) ClearSelection();
            if (Drag.Source == ItemSelectionSource.Storage) CancelDrag();
            if (splitDialog != null && splitDialog.Source == ItemSelectionSource.Storage)
            {
                splitDialog.Cancel();
            }
        }

        // ---- refresh -------------------------------------------------------------------

        /// <summary>
        /// Redraws every panel from current gameplay state.
        /// </summary>
        /// <remarks>Called after a submitted command and when a container changes for any
        /// other reason. It re-binds existing slot views, so nothing is instantiated.</remarks>
        public void Refresh()
        {
            if (!_bound) return;

            RebuildViews();

            // A selection or a drag whose item is gone must stop being held, or the next
            // action would run against a slot that moved on.
            if (!SelectionStillValid()) Selection = ItemSelection.None;
            if (Drag.IsActive && !StillValid(Drag)) CancelDrag();

            RedrawPanels();
            RefreshTooltip();

            // A dialog describing a stack that no longer exists cannot be confirmed safely.
            if (splitDialog != null && splitDialog.IsOpen && !SplitTargetStillValid())
            {
                splitDialog.Cancel();
            }

            RecordRevisions();
        }

        /// <summary>
        /// Redraws only if a container actually changed.
        /// </summary>
        /// <remarks>Comparing four revision integers is not the same as rebuilding the
        /// inventory: the walk, the lookups, the icon resolution and the re-bind only happen
        /// on a real change. This is what catches state changed by something other than the
        /// UI -- loot picked up, an item consumed elsewhere -- without the UI subscribing to
        /// gameplay it cannot see.</remarks>
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

        /// <summary>
        /// Re-reads gameplay into the view lists, drawing nothing.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="Refresh"/> because a drop has to check the drag against
        /// <em>current</em> state before it decides anything. The cached lists are only as
        /// fresh as the last refresh, and gameplay can move between a pick-up and a
        /// release; validating against the cache is how a stale drag gets applied to
        /// whatever occupies the slot now.
        /// </remarks>
        private void RebuildViews()
        {
            InventoryViewAdapter.BuildContainer(_inventory, _items, _inventoryView);
            InventoryViewAdapter.BuildContainer(_storage, _items, _storageView);
            InventoryViewAdapter.BuildEquipment(_equipment, _items, _equipmentView);
        }

        private void RedrawPanels()
        {
            if (inventoryPanel != null) inventoryPanel.Refresh(_inventoryView, Selection, Icons);
            if (storagePanel != null) storagePanel.Refresh(_storageView, Selection, Icons);
            if (equipmentPanel != null) equipmentPanel.Refresh(_equipmentView, Selection, Icons);

            ApplyDropHints();
        }

        private void ApplyDropHints()
        {
            if (inventoryPanel != null) inventoryPanel.ApplyDropHints(Drag, ItemSelectionSource.Inventory);
            if (storagePanel != null) storagePanel.ApplyDropHints(Drag, ItemSelectionSource.Storage);
            if (equipmentPanel != null) equipmentPanel.ApplyDropHints(Drag);
        }

        // ---- validity ------------------------------------------------------------------

        private bool SelectionStillValid()
        {
            if (Selection.IsEmpty) return false;

            switch (Selection.Source)
            {
                case ItemSelectionSource.Inventory:
                    return MatchesAny(_inventoryView, Selection);
                case ItemSelectionSource.Storage:
                    return MatchesAny(_storageView, Selection);
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

        /// <summary>
        /// Whether a payload's item is still where it was picked up from.
        /// </summary>
        /// <remarks>Takes the payload rather than reading <see cref="Drag"/> so a drop can
        /// check a captured copy after the drag state has been cleared. Reads the view
        /// lists, so callers must have rebuilt them first.</remarks>
        private bool StillValid(ItemDragPayload payload)
        {
            switch (payload.Source)
            {
                case ItemSelectionSource.Inventory:
                    return payload.StillAt(At(_inventoryView, payload.SlotIndex));
                case ItemSelectionSource.Storage:
                    return payload.StillAt(At(_storageView, payload.SlotIndex));
                case ItemSelectionSource.Equipment:
                    for (int i = 0; i < _equipmentView.Count; i++)
                    {
                        if (payload.StillAt(_equipmentView[i])) return true;
                    }

                    return false;
                default:
                    return false;
            }
        }

        private bool SplitTargetStillValid()
        {
            List<ItemSlotViewData> view = ViewFor(splitDialog.Source);
            if (view == null) return false;

            ItemSlotViewData slot = At(view, splitDialog.SlotIndex);
            if (slot.IsEmpty) return false;

            // Same instance, and still enough of it to divide.
            return slot.InstanceId == splitDialog.Slot.InstanceId
                && SplitBounds.For(slot).IsSplittable;
        }

        private static bool MatchesAny(List<ItemSlotViewData> view, ItemSelection selection)
        {
            for (int i = 0; i < view.Count; i++)
            {
                if (selection.Matches(view[i])) return true;
            }

            return false;
        }

        private static ItemSlotViewData At(List<ItemSlotViewData> view, int index)
        {
            return index >= 0 && index < view.Count ? view[index] : ItemSlotViewData.Empty(index);
        }

        private List<ItemSlotViewData> ViewFor(ItemSelectionSource source)
        {
            switch (source)
            {
                case ItemSelectionSource.Inventory: return _inventoryView;
                case ItemSelectionSource.Storage: return _storageView;
                default: return null;
            }
        }

        private ItemContainerState ContainerFor(ItemSelectionSource source)
        {
            switch (source)
            {
                case ItemSelectionSource.Inventory: return _inventory;
                case ItemSelectionSource.Storage: return _storage;
                default: return null;
            }
        }

        // ---- click input ---------------------------------------------------------------

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
        /// <remarks>Storage: take it out. Inventory: stow it when storage is open, otherwise
        /// use it if it is usable, otherwise wear it if it can be worn. Consumables come
        /// before equipment because a usable item is used far more often than it is
        /// equipped, and nothing authored is both.</remarks>
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

            if (IsUsable(data.DefinitionId))
            {
                SubmitUse(slotIndex);
                return;
            }

            if (data.IsEquipment) SubmitEquip(slotIndex);
        }

        private void AfterSelectionChanged()
        {
            RedrawPanels();
            RefreshTooltip();
        }

        /// <summary>Drops the selection and the tooltip with it.</summary>
        public void ClearSelection()
        {
            Selection = ItemSelection.None;
            AfterSelectionChanged();
        }

        // ---- drag ----------------------------------------------------------------------

        /// <summary>Picks an inventory slot up.</summary>
        public void OnInventoryDragStarted(int slotIndex)
        {
            BeginContainerDrag(ItemSelectionSource.Inventory, slotIndex);
        }

        /// <summary>Picks a storage slot up.</summary>
        public void OnStorageDragStarted(int slotIndex)
        {
            BeginContainerDrag(ItemSelectionSource.Storage, slotIndex);
        }

        /// <summary>Picks a worn piece up.</summary>
        public void OnEquipmentDragStarted(EquipmentSlot slot)
        {
            EquipmentSlotViewData data = FindEquipment(slot);
            if (data.IsEmpty) return;

            Drag = ItemDragPayload.FromEquipment(data);
            AfterDragChanged();
        }

        private void BeginContainerDrag(ItemSelectionSource source, int slotIndex)
        {
            List<ItemSlotViewData> view = ViewFor(source);
            if (view == null) return;

            ItemSlotViewData data = At(view, slotIndex);
            if (data.IsEmpty) return;

            // Picking something up selects it, so the tooltip follows the drag and a
            // cancelled drag leaves the player where they expect to be.
            Drag = ItemDragPayload.FromContainer(source, data);
            Selection = new ItemSelection(source, slotIndex, data.InstanceId);
            AfterDragChanged();
        }

        private void AfterDragChanged()
        {
            if (dragVisual != null)
            {
                Sprite icon = Icons == null ? null : Icons.Resolve(Drag.Icon);
                dragVisual.Show(Drag, icon);
            }

            RedrawPanels();
            RefreshTooltip();
        }

        /// <summary>Moves the ghost. Pure presentation; nothing gameplay-visible happens.</summary>
        public void DragTo(Vector2 screenPosition)
        {
            if (!Drag.IsActive || dragVisual == null) return;
            dragVisual.MoveTo(screenPosition);
        }

        /// <summary>
        /// Ends a drag that nothing received.
        /// </summary>
        /// <remarks>Raised by the source slot on every release, and it arrives <em>after</em>
        /// a drop handler when a slot did receive one. So a still-active drag here means the
        /// pointer let go over nothing, which is a cancel.</remarks>
        public void OnDragEnded()
        {
            if (Drag.IsActive) CancelDrag();
        }

        /// <summary>
        /// Abandons a drag without touching gameplay.
        /// </summary>
        /// <remarks>Every path out of a drag ends here -- a release over nothing, Escape, a
        /// closed panel, a refresh that invalidated the source. There is no gameplay call in
        /// it, which is what makes "cancelling costs nothing" structural.</remarks>
        public void CancelDrag()
        {
            Drag = ItemDragPayload.None;

            if (dragVisual != null) dragVisual.Hide();

            if (inventoryPanel != null) inventoryPanel.ClearDropHints();
            if (storagePanel != null) storagePanel.ClearDropHints();
            if (equipmentPanel != null) equipmentPanel.ClearDropHints();
        }

        /// <summary>Drops onto an inventory slot.</summary>
        public void OnInventoryDropped(int slotIndex)
        {
            DropOnContainer(ItemSelectionSource.Inventory, slotIndex);
        }

        /// <summary>Drops onto a storage slot.</summary>
        public void OnStorageDropped(int slotIndex)
        {
            DropOnContainer(ItemSelectionSource.Storage, slotIndex);
        }

        /// <summary>
        /// Resolves a drop onto a container slot into exactly one gameplay command.
        /// </summary>
        /// <remarks>
        /// Same container: <c>Move</c>, which is already move-or-merge-or-swap, so no
        /// stacking rule is restated here. Across containers: the existing transfer, so
        /// atomicity stays where it was built. Off the paperdoll into the bag: unequip.
        /// </remarks>
        public void DropOnContainer(ItemSelectionSource targetSource, int targetSlotIndex)
        {
            if (!Drag.IsActive) return;

            ItemDragPayload drag = Drag;

            // Re-read gameplay first: the cached lists may be older than the container.
            RebuildViews();

            if (!StillValid(drag))
            {
                // The item moved on between pick-up and release. Acting would hit whatever
                // is there now.
                CancelDrag();
                Refresh();
                return;
            }

            if (ItemDropAdvice.ForContainerSlot(drag, targetSource, targetSlotIndex)
                != SlotDropHint.Valid)
            {
                CancelDrag();
                return;
            }

            if (drag.Source == ItemSelectionSource.Equipment)
            {
                CancelDrag();
                SubmitUnequip(drag.EquipmentSlot);
                return;
            }

            CancelDrag();

            if (drag.Source == targetSource)
            {
                SubmitMove(targetSource, drag.SlotIndex, targetSlotIndex);
                return;
            }

            if (targetSource == ItemSelectionSource.Storage)
            {
                SubmitDeposit(drag.SlotIndex, drag.Quantity);
                return;
            }

            SubmitWithdraw(drag.SlotIndex, drag.Quantity);
        }

        /// <summary>
        /// Resolves a drop onto a paperdoll position.
        /// </summary>
        /// <remarks>The position dropped on is not the position the piece lands in: which
        /// slot a piece occupies is authored on the definition and applied by
        /// <see cref="EquipmentService"/>. Letting the drop target decide would be the UI
        /// overruling content.</remarks>
        public void OnEquipmentDropped(EquipmentSlot targetSlot)
        {
            if (!Drag.IsActive) return;

            ItemDragPayload drag = Drag;

            RebuildViews();

            if (!StillValid(drag))
            {
                CancelDrag();
                Refresh();
                return;
            }

            if (ItemDropAdvice.ForEquipmentSlot(drag, targetSlot) != SlotDropHint.Valid)
            {
                CancelDrag();
                return;
            }

            CancelDrag();
            SubmitEquip(drag.SlotIndex);
        }

        // ---- context menu --------------------------------------------------------------

        /// <summary>Opens the context menu for an inventory slot.</summary>
        public void OnInventoryRightClicked(int slotIndex)
        {
            OpenContextMenu(ItemSelectionSource.Inventory, slotIndex);
        }

        /// <summary>Opens the context menu for a storage slot.</summary>
        public void OnStorageRightClicked(int slotIndex)
        {
            OpenContextMenu(ItemSelectionSource.Storage, slotIndex);
        }

        /// <summary>Opens the context menu for a paperdoll position.</summary>
        public void OnEquipmentRightClicked(EquipmentSlot slot)
        {
            EquipmentSlotViewData data = FindEquipment(slot);
            if (data.IsEmpty) return;

            Selection = new ItemSelection(ItemSelectionSource.Equipment, (int)slot, data.InstanceId);
            ItemContextActions.ForEquipmentSlot(data, _actions);
            AfterSelectionChanged();

            if (contextMenu != null)
            {
                contextMenu.Open(ItemSelectionSource.Equipment, (int)slot, _actions, PointerPosition());
            }
        }

        private void OpenContextMenu(ItemSelectionSource source, int slotIndex)
        {
            List<ItemSlotViewData> view = ViewFor(source);
            if (view == null) return;

            ItemSlotViewData data = At(view, slotIndex);

            if (data.IsEmpty)
            {
                if (contextMenu != null) contextMenu.Close();
                ClearSelection();
                return;
            }

            // Right-clicking selects, so the tooltip and the menu describe the same item.
            Selection = new ItemSelection(source, slotIndex, data.InstanceId);

            ItemContextActions.ForContainerSlot(source, data, IsUsable(data.DefinitionId),
                IsStorageOpen, _actions);

            AfterSelectionChanged();

            if (contextMenu != null)
            {
                contextMenu.Open(source, slotIndex, _actions, PointerPosition());
            }
        }

        /// <summary>The actions the last right click offered. Exposed so the rule is testable.</summary>
        public IReadOnlyList<ItemContextAction> OfferedActions => _actions;

        /// <summary>Runs a context action against the current selection.</summary>
        public void OnContextActionPicked(ItemContextAction action)
        {
            switch (action)
            {
                case ItemContextAction.Use:
                    if (Selection.Source == ItemSelectionSource.Inventory) SubmitUse(Selection.SlotIndex);
                    return;
                case ItemContextAction.Equip:
                    if (Selection.Source == ItemSelectionSource.Inventory) SubmitEquip(Selection.SlotIndex);
                    return;
                case ItemContextAction.Unequip:
                    if (Selection.Source == ItemSelectionSource.Equipment)
                    {
                        SubmitUnequip((EquipmentSlot)Selection.SlotIndex);
                    }

                    return;
                case ItemContextAction.Split:
                    OpenSplitDialog(Selection.Source, Selection.SlotIndex);
                    return;
                case ItemContextAction.MoveToStorage:
                    if (Selection.Source == ItemSelectionSource.Inventory)
                    {
                        SubmitDeposit(Selection.SlotIndex, At(_inventoryView, Selection.SlotIndex).Quantity);
                    }

                    return;
                case ItemContextAction.Withdraw:
                    if (Selection.Source == ItemSelectionSource.Storage)
                    {
                        SubmitWithdraw(Selection.SlotIndex, At(_storageView, Selection.SlotIndex).Quantity);
                    }

                    return;
            }
        }

        // ---- split ---------------------------------------------------------------------

        /// <summary>
        /// Opens the split dialog on a slot.
        /// </summary>
        /// <remarks>Returns false when the stack cannot be divided, rather than opening a
        /// dialog whose confirm is guaranteed to be refused.</remarks>
        public bool OpenSplitDialog(ItemSelectionSource source, int slotIndex)
        {
            List<ItemSlotViewData> view = ViewFor(source);
            if (view == null || splitDialog == null) return false;

            ItemSlotViewData data = At(view, slotIndex);
            if (!SplitBounds.For(data).IsSplittable) return false;

            Sprite icon = Icons == null ? null : Icons.Resolve(data.Icon);
            return splitDialog.Open(source, data,
                LocalizedText.ResolveOr(Text, data.NameKey, data.DefinitionId.ToString()), icon);
        }

        private void OnSplitConfirmed(ItemSelectionSource source, int slotIndex, int quantity)
        {
            SubmitSplit(source, slotIndex, quantity);
        }

        // ---- commands ------------------------------------------------------------------

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
            LastContainerResult = ItemContainerTransfer.Deposit(_inventory, _storage,
                inventorySlot, quantity, _items);

            if (LastContainerResult.IsAccepted) Selection = ItemSelection.None;

            Refresh();
            return LastContainerResult;
        }

        /// <summary>Asks gameplay to move a stack from storage into the inventory.</summary>
        public ItemContainerResult SubmitWithdraw(int storageSlot, int quantity)
        {
            LastContainerResult = ItemContainerTransfer.Withdraw(_storage, _inventory,
                storageSlot, quantity, _items);

            if (LastContainerResult.IsAccepted) Selection = ItemSelection.None;

            Refresh();
            return LastContainerResult;
        }

        /// <summary>
        /// Asks a container to rearrange two of its own slots.
        /// </summary>
        /// <remarks><c>Move</c> is already move-or-merge-or-swap. Choosing between those
        /// here would be a second copy of the stacking rules.</remarks>
        public ItemContainerResult SubmitMove(ItemSelectionSource source, int fromSlot, int toSlot)
        {
            ItemContainerState container = ContainerFor(source);

            if (container == null)
            {
                LastContainerResult = ItemContainerResult.Rejected(ItemContainerRejection.NoItem);
                return LastContainerResult;
            }

            LastContainerResult = container.Move(fromSlot, toSlot, _items);

            if (LastContainerResult.IsAccepted) Selection = ItemSelection.None;

            Refresh();
            return LastContainerResult;
        }

        /// <summary>
        /// Asks a container to split a stack into its first free slot.
        /// </summary>
        /// <remarks>The destination is the container's own
        /// <c>FirstEmptySlot</c>, so the UI does not choose where a split lands. No free
        /// slot is a refusal from the container, not a special case here.</remarks>
        public ItemContainerResult SubmitSplit(ItemSelectionSource source, int fromSlot, int quantity)
        {
            ItemContainerState container = ContainerFor(source);

            if (container == null)
            {
                LastContainerResult = ItemContainerResult.Rejected(ItemContainerRejection.NoItem);
                return LastContainerResult;
            }

            int destination = container.FirstEmptySlot();

            if (destination < 0)
            {
                LastContainerResult = ItemContainerResult.Rejected(ItemContainerRejection.ContainerFull);
                Refresh();
                return LastContainerResult;
            }

            LastContainerResult = container.Split(fromSlot, quantity, destination, _items);

            if (LastContainerResult.IsAccepted) Selection = ItemSelection.None;

            Refresh();
            return LastContainerResult;
        }

        /// <summary>
        /// Asks gameplay to use one of whatever is in an inventory slot.
        /// </summary>
        /// <remarks>
        /// Everything an item does comes from its authored configuration, resolved by
        /// <see cref="ItemUseService"/>. This method knows nothing about potions, food or
        /// scrolls, and compares no <see cref="DefinitionId"/> against anything.
        ///
        /// A granted warp is recorded in <see cref="PendingWarpDestination"/> and not acted
        /// on: travelling is a later system's job.
        /// </remarks>
        public ItemUseResult SubmitUse(int inventorySlot)
        {
            _grantedBuffs.Clear();

            var context = new ItemUseService.Context(_items, _resources, _limits,
                _statusEffects, _maps, default, _grantedBuffs);

            LastUseResult = ItemUseService.Use(_inventory, inventorySlot, context);

            if (LastUseResult.IsAccepted)
            {
                Selection = ItemSelection.None;
                if (LastUseResult.HasWarp) PendingWarpDestination = LastUseResult.WarpDestination;
            }
            else
            {
                _grantedBuffs.Clear();
            }

            Refresh();
            return LastUseResult;
        }

        /// <summary>Clears a recorded warp once whatever owns travel has taken it.</summary>
        public void ConsumePendingWarp()
        {
            PendingWarpDestination = DefinitionId.None;
        }

        // ---- cancel --------------------------------------------------------------------

        /// <summary>
        /// Backs out of whatever is in progress.
        /// </summary>
        /// <remarks>What Escape does, and what closing the window does. One thing at a time,
        /// innermost first, so a player who opened a dialog during a drag does not lose both
        /// at once. Nothing here touches gameplay.</remarks>
        public bool CancelActiveInteraction()
        {
            if (contextMenu != null && contextMenu.IsOpen)
            {
                contextMenu.Close();
                return true;
            }

            if (splitDialog != null && splitDialog.IsOpen)
            {
                splitDialog.Cancel();
                return true;
            }

            if (Drag.IsActive)
            {
                CancelDrag();
                return true;
            }

            if (!Selection.IsEmpty)
            {
                ClearSelection();
                return true;
            }

            return false;
        }

        // ---- tooltip -------------------------------------------------------------------

        private void RefreshTooltip()
        {
            if (tooltip == null) return;

            tooltip.Text = Text;
            tooltip.Show(BuildSelectionTooltip());
        }

        /// <summary>The tooltip for whatever is selected. Exposed so the rule is testable.</summary>
        public ItemTooltipData BuildSelectionTooltip()
        {
            switch (Selection.Source)
            {
                case ItemSelectionSource.Inventory:
                    return InventoryViewAdapter.BuildTooltip(_inventory, Selection.SlotIndex, _items, _maps);
                case ItemSelectionSource.Storage:
                    return InventoryViewAdapter.BuildTooltip(_storage, Selection.SlotIndex, _items, _maps);
                case ItemSelectionSource.Equipment:
                    return InventoryViewAdapter.BuildTooltip(_equipment,
                        (EquipmentSlot)Selection.SlotIndex, _items);
                default:
                    return ItemTooltipData.None;
            }
        }

        // ---- helpers -------------------------------------------------------------------

        /// <summary>
        /// Whether an item is authored as usable and configured.
        /// </summary>
        /// <remarks>Reads two authored flags. It does not evaluate whether the use would
        /// succeed -- a full-health character's potion is still offered, and
        /// <see cref="ItemUseService"/> refuses it with a reason.</remarks>
        private bool IsUsable(DefinitionId definitionId)
        {
            if (_items == null || !definitionId.IsValid) return false;

            ItemDefinition definition;
            if (!_items.TryGet(definitionId, out definition) || definition == null) return false;

            return definition.Usable
                && definition.UseType != ItemUseType.None
                && definition.UseEffects.Length > 0;
        }

        private EquipmentSlotViewData FindEquipment(EquipmentSlot slot)
        {
            for (int i = 0; i < _equipmentView.Count; i++)
            {
                if (_equipmentView[i].Slot == slot) return _equipmentView[i];
            }

            return EquipmentSlotViewData.Empty(slot);
        }

        private static Vector2 PointerPosition()
        {
            var current = UnityEngine.InputSystem.Mouse.current;
            return current == null ? Vector2.zero : current.position.ReadValue();
        }
    }
}
