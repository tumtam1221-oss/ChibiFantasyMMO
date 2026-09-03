using System.Reflection;
using ChibiFantasy.Client.UI;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.UI;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Drag and drop, split, and the interactions built on them.
    /// </summary>
    /// <remarks>
    /// Drag is an input method, so these tests drive the controller's drag entry points
    /// directly rather than simulating pointers -- what matters is that a drop turns into
    /// exactly one existing service call, and that a drag which has gone stale turns into
    /// none at all.
    ///
    /// The expensive bug in a drag layer is not a failed drop, it is a drop that acts on
    /// the wrong item. Several tests below exist only to hold that line.
    /// </remarks>
    internal sealed class InventoryUiDragTests : ItemContainerTestBase
    {
        private const string RedPotion = "item.red";

        private GameObject _host;
        private InventoryUiController _controller;
        private SplitStackDialog _split;
        private ItemContextMenu _menu;
        private ItemContainerState _inventory;
        private ItemContainerState _storage;
        private CharacterEquipmentState _equipment;
        private CharacterResourceState _resources;
        private ResourceLimits _limits;

        [SetUp]
        public void CreateController()
        {
            _host = new GameObject("DragTestHost");
            _controller = _host.AddComponent<InventoryUiController>();

            var splitGo = new GameObject("Split");
            splitGo.transform.SetParent(_host.transform, false);
            _split = splitGo.AddComponent<SplitStackDialog>();
            _split.EnsureVisuals();

            var menuGo = new GameObject("Menu");
            menuGo.transform.SetParent(_host.transform, false);
            _menu = menuGo.AddComponent<ItemContextMenu>();
            _menu.EnsureVisuals();

            // What the scene does through the inspector.
            Wire("splitDialog", _split);
            Wire("contextMenu", _menu);

            _inventory = Container(8);
            _storage = Container(8);
            _equipment = new CharacterEquipmentState(new CharacterId("char:test"));

            _limits = new ResourceLimits(1000, 400);
            _resources = new CharacterResourceState(new CharacterId("char:test"), _limits, 400, 100);

            AddUsable(RedPotion, ItemUseType.Recovery, new[]
            {
                new ItemUseEffect(ItemEffectKind.RestoreResource, ItemResource.Health, 500)
            });
        }

        [TearDown]
        public void DestroyController()
        {
            if (_host != null) Object.DestroyImmediate(_host);
        }

        private void Wire(string field, object value)
        {
            typeof(InventoryUiController)
                .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_controller, value);
        }

        private void Bind(int level = 1)
        {
            _controller.Bind(_inventory, _storage, _equipment, Items, level,
                default, default, _resources, _limits, Maps, StatusEffects);
        }

        // ---- inventory to inventory ----------------------------------------------------

        [Test]
        public void Dragging_onto_an_empty_slot_moves_the_item()
        {
            _inventory.Add(Stack(Potion, 5), Items);
            Bind();

            InstanceId moved = _inventory.GetSlot(0).InstanceId;

            _controller.OnInventoryDragStarted(0);
            Assert.That(_controller.Drag.IsActive, Is.True);
            Assert.That(_controller.Drag.InstanceId, Is.EqualTo(moved));

            _controller.DropOnContainer(ItemSelectionSource.Inventory, 4);

            Assert.That(_controller.LastContainerResult.IsAccepted, Is.True);
            Assert.That(_inventory.GetSlot(0).IsEmpty, Is.True);
            Assert.That(_inventory.GetSlot(4).InstanceId, Is.EqualTo(moved));
            Assert.That(_controller.Drag.IsActive, Is.False, "the drag ended with the drop");
        }

        [Test]
        public void Dragging_onto_a_matching_stack_merges_it()
        {
            // Ore's ceiling is 10, so 14 lands as 10 and 4; taking 5 off the first leaves
            // room for the second to pour in.
            _inventory.Add(Stack(Ore, 14), Items);
            _inventory.RemoveAt(0, 5);

            Assert.That(_inventory.GetSlot(0).Quantity, Is.EqualTo(5));
            Assert.That(_inventory.GetSlot(1).Quantity, Is.EqualTo(4));

            Bind();
            _controller.OnInventoryDragStarted(1);
            _controller.DropOnContainer(ItemSelectionSource.Inventory, 0);

            Assert.That(_controller.LastContainerResult.IsAccepted, Is.True);
            Assert.That(_inventory.GetSlot(0).Quantity, Is.EqualTo(9));
            Assert.That(_inventory.GetSlot(1).IsEmpty, Is.True);
            Assert.That(_inventory.CountOf(new DefinitionId(Ore)), Is.EqualTo(9),
                "no duplication and no loss");
        }

        [Test]
        public void A_merge_onto_a_full_stack_reports_the_containers_refusal_and_keeps_both()
        {
            // The UI does not know the ceiling and must not: it forwards the drop and
            // reports whatever the container decided.
            _inventory.Add(Stack(Ore, 12), Items);

            Assert.That(_inventory.GetSlot(0).Quantity, Is.EqualTo(10));
            Assert.That(_inventory.GetSlot(1).Quantity, Is.EqualTo(2));

            Bind();
            _controller.OnInventoryDragStarted(1);
            _controller.DropOnContainer(ItemSelectionSource.Inventory, 0);

            Assert.That(_controller.LastContainerResult.IsAccepted, Is.False);
            Assert.That(_controller.LastContainerResult.Reason,
                Is.EqualTo(ItemContainerRejection.NotStackable));
            Assert.That(_inventory.GetSlot(0).Quantity, Is.EqualTo(10));
            Assert.That(_inventory.GetSlot(1).Quantity, Is.EqualTo(2),
                "the overflow stayed where it was rather than being destroyed");
            Assert.That(_controller.Drag.IsActive, Is.False, "and the drag was cleaned up");
        }

        [Test]
        public void Dragging_onto_an_incompatible_item_swaps_them()
        {
            _inventory.Add(Stack(Potion, 5), Items);
            _inventory.Add(Stack(Rock, 1), Items);
            Bind();

            InstanceId potion = _inventory.GetSlot(0).InstanceId;
            InstanceId rock = _inventory.GetSlot(1).InstanceId;

            _controller.OnInventoryDragStarted(0);
            _controller.DropOnContainer(ItemSelectionSource.Inventory, 1);

            Assert.That(_inventory.GetSlot(0).InstanceId, Is.EqualTo(rock));
            Assert.That(_inventory.GetSlot(1).InstanceId, Is.EqualTo(potion),
                "swap is the container's answer, not a rule the UI chose");
        }

        [Test]
        public void Dropping_a_slot_onto_itself_does_nothing()
        {
            _inventory.Add(Stack(Potion, 5), Items);
            Bind();

            Revision before = _inventory.Revision;

            _controller.OnInventoryDragStarted(0);
            _controller.DropOnContainer(ItemSelectionSource.Inventory, 0);

            Assert.That(_inventory.Revision, Is.EqualTo(before));
            Assert.That(_inventory.GetSlot(0).Quantity, Is.EqualTo(5));
            Assert.That(_controller.Drag.IsActive, Is.False);
        }

        // ---- storage -------------------------------------------------------------------

        [Test]
        public void Dragging_within_storage_moves_within_storage()
        {
            _storage.Add(Stack(Potion, 5), Items);
            Bind();
            _controller.SetStorageOpen(true);

            InstanceId moved = _storage.GetSlot(0).InstanceId;

            _controller.OnStorageDragStarted(0);
            _controller.DropOnContainer(ItemSelectionSource.Storage, 3);

            Assert.That(_storage.GetSlot(3).InstanceId, Is.EqualTo(moved));
            Assert.That(_storage.GetSlot(0).IsEmpty, Is.True);
            Assert.That(_inventory.OccupiedSlots, Is.EqualTo(0), "the bag was not involved");
        }

        [Test]
        public void Dragging_from_the_bag_into_storage_deposits()
        {
            _inventory.Add(Stack(Potion, 5), Items);
            Bind();
            _controller.SetStorageOpen(true);

            _controller.OnInventoryDragStarted(0);
            _controller.DropOnContainer(ItemSelectionSource.Storage, 2);

            Assert.That(_controller.LastContainerResult.IsAccepted, Is.True);
            Assert.That(_inventory.CountOf(new DefinitionId(Potion)), Is.EqualTo(0));
            Assert.That(_storage.CountOf(new DefinitionId(Potion)), Is.EqualTo(5),
                "nothing was created or destroyed in transit");
        }

        [Test]
        public void Dragging_from_storage_into_the_bag_withdraws()
        {
            _storage.Add(Stack(Potion, 7), Items);
            Bind();
            _controller.SetStorageOpen(true);

            _controller.OnStorageDragStarted(0);
            _controller.DropOnContainer(ItemSelectionSource.Inventory, 5);

            Assert.That(_controller.LastContainerResult.IsAccepted, Is.True);
            Assert.That(_storage.CountOf(new DefinitionId(Potion)), Is.EqualTo(0));
            Assert.That(_inventory.CountOf(new DefinitionId(Potion)), Is.EqualTo(7));
        }

        // ---- equipment -----------------------------------------------------------------

        [Test]
        public void Dragging_gear_onto_the_paperdoll_equips_it_through_the_service()
        {
            _inventory.Add(Gear(Helm), Items);
            Bind();

            _controller.OnInventoryDragStarted(0);
            _controller.OnEquipmentDropped(EquipmentSlot.Head);

            Assert.That(_controller.LastEquipResult.IsAccepted, Is.True);
            Assert.That(_equipment.IsOccupied(EquipmentSlot.Head), Is.True);
            Assert.That(_inventory.GetSlot(0).IsEmpty, Is.True);
        }

        [Test]
        public void The_position_dropped_on_does_not_decide_where_the_piece_lands()
        {
            _inventory.Add(Gear(Helm), Items);
            Bind();

            // Dropped on the weapon hand; the definition says Head.
            _controller.OnInventoryDragStarted(0);
            _controller.OnEquipmentDropped(EquipmentSlot.MainHand);

            Assert.That(_controller.LastEquipResult.IsAccepted, Is.True);
            Assert.That(_controller.LastEquipResult.Slot, Is.EqualTo(EquipmentSlot.Head),
                "the authored slot wins; a drop target must not overrule content");
            Assert.That(_equipment.IsOccupied(EquipmentSlot.MainHand), Is.False);
        }

        [Test]
        public void Dragging_a_worn_piece_into_the_bag_unequips_it()
        {
            _inventory.Add(Gear(Helm), Items);
            Bind();
            _controller.SubmitEquip(0);

            _controller.OnEquipmentDragStarted(EquipmentSlot.Head);
            Assert.That(_controller.Drag.Source, Is.EqualTo(ItemSelectionSource.Equipment));

            _controller.DropOnContainer(ItemSelectionSource.Inventory, 0);

            Assert.That(_controller.LastEquipResult.IsAccepted, Is.True);
            Assert.That(_equipment.IsOccupied(EquipmentSlot.Head), Is.False);
            Assert.That(_inventory.GetSlot(0).DefinitionId, Is.EqualTo(new DefinitionId(Helm)));
        }

        [Test]
        public void An_unequip_with_no_room_is_refused_and_the_piece_stays_on()
        {
            _inventory = Container(1);
            _inventory.Add(Gear(Helm), Items);
            Bind();

            _controller.SubmitEquip(0);
            _inventory.Add(Stack(Rock, 1), Items);
            _controller.Refresh();

            _controller.OnEquipmentDragStarted(EquipmentSlot.Head);
            _controller.DropOnContainer(ItemSelectionSource.Inventory, 0);

            Assert.That(_controller.LastEquipResult.IsAccepted, Is.False);
            Assert.That(_controller.LastEquipResult.Reason, Is.EqualTo(EquipRejection.InventoryFull));
            Assert.That(_equipment.IsOccupied(EquipmentSlot.Head), Is.True,
                "the service refused, so the piece must not have been dropped anyway");
        }

        // ---- invalid targets and cancels -----------------------------------------------

        [Test]
        public void Dragging_something_unwearable_onto_the_paperdoll_changes_nothing()
        {
            _inventory.Add(Stack(Potion, 5), Items);
            Bind();

            Revision inventoryBefore = _inventory.Revision;
            Revision equipmentBefore = _equipment.Revision;

            _controller.OnInventoryDragStarted(0);
            _controller.OnEquipmentDropped(EquipmentSlot.Head);

            Assert.That(_inventory.Revision, Is.EqualTo(inventoryBefore));
            Assert.That(_equipment.Revision, Is.EqualTo(equipmentBefore));
            Assert.That(_controller.Drag.IsActive, Is.False, "and the drag was cleaned up");
        }

        [Test]
        public void Dragging_a_worn_piece_onto_storage_is_refused_without_touching_anything()
        {
            _inventory.Add(Gear(Helm), Items);
            Bind();
            _controller.SubmitEquip(0);
            _controller.SetStorageOpen(true);

            Revision storageBefore = _storage.Revision;
            Revision equipmentBefore = _equipment.Revision;

            _controller.OnEquipmentDragStarted(EquipmentSlot.Head);
            _controller.DropOnContainer(ItemSelectionSource.Storage, 0);

            Assert.That(_storage.Revision, Is.EqualTo(storageBefore));
            Assert.That(_equipment.Revision, Is.EqualTo(equipmentBefore),
                "unequip-then-deposit is two operations and nothing makes that pair atomic");
            Assert.That(_equipment.IsOccupied(EquipmentSlot.Head), Is.True);
        }

        [Test]
        public void Releasing_over_nothing_cancels_without_touching_gameplay()
        {
            _inventory.Add(Stack(Potion, 5), Items);
            Bind();

            Revision before = _inventory.Revision;

            _controller.OnInventoryDragStarted(0);
            _controller.OnDragEnded();

            Assert.That(_controller.Drag.IsActive, Is.False);
            Assert.That(_inventory.Revision, Is.EqualTo(before));
            Assert.That(_inventory.GetSlot(0).Quantity, Is.EqualTo(5));
        }

        [Test]
        public void Escape_cancels_a_drag_without_touching_gameplay()
        {
            _inventory.Add(Stack(Potion, 5), Items);
            Bind();

            Revision before = _inventory.Revision;

            _controller.OnInventoryDragStarted(0);
            Assert.That(_controller.CancelActiveInteraction(), Is.True);

            Assert.That(_controller.Drag.IsActive, Is.False);
            Assert.That(_inventory.Revision, Is.EqualTo(before));
        }

        [Test]
        public void Closing_storage_cancels_a_drag_that_started_there()
        {
            _storage.Add(Stack(Potion, 5), Items);
            Bind();
            _controller.SetStorageOpen(true);

            _controller.OnStorageDragStarted(0);
            Assert.That(_controller.Drag.IsActive, Is.True);

            _controller.SetStorageOpen(false);

            Assert.That(_controller.Drag.IsActive, Is.False);
            Assert.That(_storage.GetSlot(0).Quantity, Is.EqualTo(5));
        }

        [Test]
        public void A_drag_whose_item_vanished_is_cancelled_by_the_next_refresh()
        {
            _inventory.Add(Stack(Potion, 5), Items);
            Bind();

            _controller.OnInventoryDragStarted(0);

            // Something else consumed it: loot, a trade, another window.
            _inventory.RemoveAt(0, 5);
            _controller.Refresh();

            Assert.That(_controller.Drag.IsActive, Is.False);
            Assert.That(_controller.Selection.IsEmpty, Is.True);
        }

        [Test]
        public void A_drop_after_the_source_slot_was_refilled_acts_on_nothing()
        {
            // The precise bug instance identity exists to prevent.
            _inventory.Add(Stack(Potion, 5), Items);
            Bind();

            _controller.OnInventoryDragStarted(0);

            _inventory.RemoveAt(0, 5);
            _inventory.Add(Stack(Ore, 3), Items);   // a different item, same slot

            Revision afterRefill = _inventory.Revision;
            _controller.DropOnContainer(ItemSelectionSource.Inventory, 4);

            Assert.That(_inventory.Revision, Is.EqualTo(afterRefill),
                "the drag was stale, so the drop must not move the new occupant");
            Assert.That(_inventory.GetSlot(0).DefinitionId, Is.EqualTo(new DefinitionId(Ore)));
            Assert.That(_inventory.GetSlot(4).IsEmpty, Is.True);
            Assert.That(_controller.Drag.IsActive, Is.False);
        }

        [Test]
        public void Dragging_one_of_two_identical_stacks_moves_only_that_one()
        {
            // Ore's ceiling of 10 forces two slots to hold the same DefinitionId.
            _inventory.Add(Stack(Ore, 10), Items);
            _inventory.Add(Stack(Ore, 4), Items);
            Bind();

            InstanceId first = _inventory.GetSlot(0).InstanceId;
            InstanceId second = _inventory.GetSlot(1).InstanceId;
            Assert.That(first, Is.Not.EqualTo(second));

            _controller.OnInventoryDragStarted(1);
            Assert.That(_controller.Drag.InstanceId, Is.EqualTo(second),
                "the payload carries the instance, not just the definition");

            _controller.DropOnContainer(ItemSelectionSource.Inventory, 6);

            Assert.That(_inventory.GetSlot(6).InstanceId, Is.EqualTo(second));
            Assert.That(_inventory.GetSlot(0).InstanceId, Is.EqualTo(first),
                "the other stack never moved");
            Assert.That(_inventory.GetSlot(0).Quantity, Is.EqualTo(10));
        }

        [Test]
        public void Dragging_an_empty_slot_starts_nothing()
        {
            _inventory.Add(Stack(Potion, 5), Items);
            Bind();

            _controller.OnInventoryDragStarted(3);
            Assert.That(_controller.Drag.IsActive, Is.False);

            _controller.OnEquipmentDragStarted(EquipmentSlot.Head);
            Assert.That(_controller.Drag.IsActive, Is.False);
        }

        [Test]
        public void A_drop_with_no_drag_in_progress_does_nothing()
        {
            _inventory.Add(Stack(Potion, 5), Items);
            Bind();

            Revision before = _inventory.Revision;

            _controller.DropOnContainer(ItemSelectionSource.Inventory, 4);
            _controller.OnEquipmentDropped(EquipmentSlot.Head);

            Assert.That(_inventory.Revision, Is.EqualTo(before));
        }

        // ---- split ---------------------------------------------------------------------

        [Test]
        public void The_split_dialog_opens_with_bounds_taken_from_the_stack()
        {
            _inventory.Add(Stack(Potion, 10), Items);
            Bind();

            Assert.That(_controller.OpenSplitDialog(ItemSelectionSource.Inventory, 0), Is.True);
            Assert.That(_split.IsOpen, Is.True);
            Assert.That(_split.Bounds.Min, Is.EqualTo(1));
            Assert.That(_split.Bounds.Max, Is.EqualTo(9), "a split must leave something behind");
            Assert.That(_split.Quantity, Is.EqualTo(5), "half, rounded down");
        }

        [Test]
        public void Confirming_the_dialog_splits_through_the_container()
        {
            _inventory.Add(Stack(Potion, 10), Items);
            Bind();

            _controller.OpenSplitDialog(ItemSelectionSource.Inventory, 0);
            _split.SetQuantity(3);
            _split.Confirm();

            Assert.That(_controller.LastContainerResult.IsAccepted, Is.True);
            Assert.That(_inventory.GetSlot(0).Quantity, Is.EqualTo(7));
            Assert.That(_inventory.GetSlot(1).Quantity, Is.EqualTo(3));
            Assert.That(_inventory.CountOf(new DefinitionId(Potion)), Is.EqualTo(10),
                "no duplication and no loss");
            Assert.That(_split.IsOpen, Is.False);
        }

        [Test]
        public void Splitting_one_and_splitting_all_but_one_both_work()
        {
            _inventory.Add(Stack(Potion, 10), Items);
            Bind();

            Assert.That(_controller.SubmitSplit(ItemSelectionSource.Inventory, 0, 1).IsAccepted, Is.True);
            Assert.That(_inventory.GetSlot(0).Quantity, Is.EqualTo(9));
            Assert.That(_inventory.GetSlot(1).Quantity, Is.EqualTo(1));

            Assert.That(_controller.SubmitSplit(ItemSelectionSource.Inventory, 0, 8).IsAccepted, Is.True);
            Assert.That(_inventory.GetSlot(0).Quantity, Is.EqualTo(1));
            Assert.That(_inventory.CountOf(new DefinitionId(Potion)), Is.EqualTo(10));
        }

        [Test]
        public void Zero_negative_whole_and_oversized_splits_are_all_refused()
        {
            _inventory.Add(Stack(Potion, 10), Items);
            Bind();

            int[] bad = { 0, -1, -100, 10, 11, 1000 };

            foreach (int quantity in bad)
            {
                ItemContainerResult result =
                    _controller.SubmitSplit(ItemSelectionSource.Inventory, 0, quantity);

                Assert.That(result.IsAccepted, Is.False, "split of " + quantity + " was accepted");
                Assert.That(result.Reason, Is.EqualTo(ItemContainerRejection.InvalidQuantity));
                Assert.That(_inventory.GetSlot(0).Quantity, Is.EqualTo(10));
                Assert.That(_inventory.CountOf(new DefinitionId(Potion)), Is.EqualTo(10));
            }
        }

        [Test]
        public void A_split_with_no_free_slot_is_refused_and_nothing_is_lost()
        {
            _inventory = Container(1);
            _inventory.Add(Stack(Potion, 10), Items);
            Bind();

            ItemContainerResult result = _controller.SubmitSplit(ItemSelectionSource.Inventory, 0, 4);

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(result.Reason, Is.EqualTo(ItemContainerRejection.ContainerFull));
            Assert.That(_inventory.GetSlot(0).Quantity, Is.EqualTo(10));
        }

        [Test]
        public void A_stack_of_one_and_a_piece_of_equipment_cannot_be_split()
        {
            _inventory.Add(Stack(Rock, 1), Items);
            _inventory.Add(Gear(Helm), Items);
            Bind();

            Assert.That(_controller.OpenSplitDialog(ItemSelectionSource.Inventory, 0), Is.False);
            Assert.That(_controller.OpenSplitDialog(ItemSelectionSource.Inventory, 1), Is.False);
            Assert.That(_split.IsOpen, Is.False, "a dialog whose confirm cannot work must not open");
        }

        [Test]
        public void The_dialog_clamps_whatever_it_is_given()
        {
            _inventory.Add(Stack(Potion, 10), Items);
            Bind();
            _controller.OpenSplitDialog(ItemSelectionSource.Inventory, 0);

            _split.SetQuantity(99);
            Assert.That(_split.Quantity, Is.EqualTo(9));

            _split.SetQuantity(-5);
            Assert.That(_split.Quantity, Is.EqualTo(1));

            _split.Decrease();
            Assert.That(_split.Quantity, Is.EqualTo(1), "already at the floor");
        }

        [Test]
        public void Cancelling_the_dialog_changes_nothing()
        {
            _inventory.Add(Stack(Potion, 10), Items);
            Bind();

            Revision before = _inventory.Revision;

            _controller.OpenSplitDialog(ItemSelectionSource.Inventory, 0);
            _split.Cancel();

            Assert.That(_split.IsOpen, Is.False);
            Assert.That(_inventory.Revision, Is.EqualTo(before));
            Assert.That(_inventory.GetSlot(0).Quantity, Is.EqualTo(10));
        }

        [Test]
        public void An_open_dialog_closes_when_its_stack_disappears()
        {
            _inventory.Add(Stack(Potion, 10), Items);
            Bind();

            _controller.OpenSplitDialog(ItemSelectionSource.Inventory, 0);
            Assert.That(_split.IsOpen, Is.True);

            _inventory.RemoveAt(0, 10);
            _controller.Refresh();

            Assert.That(_split.IsOpen, Is.False,
                "confirming against a stack that no longer exists must be impossible");
        }

        [Test]
        public void Escape_closes_the_dialog_before_it_clears_a_drag()
        {
            _inventory.Add(Stack(Potion, 10), Items);
            Bind();

            _controller.OnInventoryDragStarted(0);
            _controller.OpenSplitDialog(ItemSelectionSource.Inventory, 0);

            Assert.That(_controller.CancelActiveInteraction(), Is.True);
            Assert.That(_split.IsOpen, Is.False);
            Assert.That(_controller.Drag.IsActive, Is.True, "one thing at a time, innermost first");

            Assert.That(_controller.CancelActiveInteraction(), Is.True);
            Assert.That(_controller.Drag.IsActive, Is.False);
        }

        // ---- context menu --------------------------------------------------------------

        [Test]
        public void The_menu_offers_what_suits_the_item_and_the_panel()
        {
            _inventory.Add(Stack(RedPotion, 5), Items);
            _inventory.Add(Gear(Helm), Items);
            _inventory.Add(Stack(Rock, 1), Items);
            Bind();

            _controller.OnInventoryRightClicked(0);
            Assert.That(_controller.OfferedActions, Contains.Item(ItemContextAction.Use));
            Assert.That(_controller.OfferedActions, Contains.Item(ItemContextAction.Split));
            Assert.That(_controller.OfferedActions, Has.No.Member(ItemContextAction.Equip));

            _controller.OnInventoryRightClicked(1);
            Assert.That(_controller.OfferedActions, Contains.Item(ItemContextAction.Equip));
            Assert.That(_controller.OfferedActions, Has.No.Member(ItemContextAction.Use));
            Assert.That(_controller.OfferedActions, Has.No.Member(ItemContextAction.Split),
                "equipment does not stack");

            _controller.OnInventoryRightClicked(2);
            Assert.That(_controller.OfferedActions, Is.Empty, "a single rock affords nothing yet");
        }

        [Test]
        public void Storage_offers_withdraw_and_the_paperdoll_offers_unequip()
        {
            _storage.Add(Stack(Potion, 5), Items);
            _inventory.Add(Gear(Helm), Items);
            Bind();
            _controller.SetStorageOpen(true);
            _controller.SubmitEquip(0);

            _controller.OnStorageRightClicked(0);
            Assert.That(_controller.OfferedActions, Contains.Item(ItemContextAction.Withdraw));
            Assert.That(_controller.OfferedActions, Has.No.Member(ItemContextAction.Equip));

            _controller.OnEquipmentRightClicked(EquipmentSlot.Head);
            Assert.That(_controller.OfferedActions, Contains.Item(ItemContextAction.Unequip));
        }

        [Test]
        public void Move_to_storage_is_offered_only_while_storage_is_open()
        {
            _inventory.Add(Stack(Potion, 5), Items);
            Bind();

            _controller.OnInventoryRightClicked(0);
            Assert.That(_controller.OfferedActions, Has.No.Member(ItemContextAction.MoveToStorage));

            _controller.SetStorageOpen(true);
            _controller.OnInventoryRightClicked(0);
            Assert.That(_controller.OfferedActions, Contains.Item(ItemContextAction.MoveToStorage));
        }

        [Test]
        public void Picking_a_menu_action_runs_the_matching_command()
        {
            _inventory.Add(Stack(RedPotion, 5), Items);
            Bind();

            _controller.OnInventoryRightClicked(0);
            _controller.OnContextActionPicked(ItemContextAction.Use);

            Assert.That(_controller.LastUseResult.IsAccepted, Is.True);
            Assert.That(_inventory.GetSlot(0).Quantity, Is.EqualTo(4));
            Assert.That(_resources.CurrentHealth, Is.EqualTo(900));
        }

        [Test]
        public void Picking_split_from_the_menu_opens_the_dialog_rather_than_splitting()
        {
            _inventory.Add(Stack(Potion, 8), Items);
            Bind();

            Revision before = _inventory.Revision;

            _controller.OnInventoryRightClicked(0);
            _controller.OnContextActionPicked(ItemContextAction.Split);

            Assert.That(_split.IsOpen, Is.True);
            Assert.That(_inventory.Revision, Is.EqualTo(before),
                "opening a dialog is not a gameplay operation");
        }

        [Test]
        public void Right_clicking_an_empty_slot_offers_nothing_and_closes_the_menu()
        {
            _inventory.Add(Stack(Potion, 5), Items);
            Bind();

            _controller.OnInventoryRightClicked(0);
            Assert.That(_menu.IsOpen, Is.True);

            _controller.OnInventoryRightClicked(4);

            Assert.That(_menu.IsOpen, Is.False);
            Assert.That(_controller.Selection.IsEmpty, Is.True);
        }

        [Test]
        public void An_action_that_does_not_match_the_current_selection_is_ignored()
        {
            _inventory.Add(Stack(Potion, 5), Items);
            Bind();

            _controller.OnInventoryRightClicked(0);

            Revision before = _inventory.Revision;

            // The selection is in the bag; Unequip belongs to the paperdoll.
            _controller.OnContextActionPicked(ItemContextAction.Unequip);
            _controller.OnContextActionPicked(ItemContextAction.Withdraw);

            Assert.That(_inventory.Revision, Is.EqualTo(before));
        }

        // ---- use through the controller ------------------------------------------------

        [Test]
        public void Double_clicking_a_usable_item_uses_it_rather_than_equipping_it()
        {
            _inventory.Add(Stack(RedPotion, 3), Items);
            Bind();

            _controller.OnInventoryClicked(0);
            _controller.OnInventoryClicked(0);

            Assert.That(_controller.LastUseResult.IsAccepted, Is.True);
            Assert.That(_inventory.GetSlot(0).Quantity, Is.EqualTo(2));
            Assert.That(_resources.CurrentHealth, Is.EqualTo(900));
        }

        [Test]
        public void A_refused_use_leaves_the_stack_alone_and_keeps_the_reason()
        {
            _resources.SetHealth(1000, _limits);
            _inventory.Add(Stack(RedPotion, 3), Items);
            Bind();

            ItemUseResult result = _controller.SubmitUse(0);

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(result.Reason, Is.EqualTo(ItemUseRejection.NoEffect));
            Assert.That(_inventory.GetSlot(0).Quantity, Is.EqualTo(3));
            Assert.That(_controller.LastGrantedBuffs, Is.Empty);
        }

        [Test]
        public void A_granted_warp_is_recorded_and_not_acted_on()
        {
            const string Scroll = "item.scroll";
            AddMap("map.town", MapCategory.Town, isTown: true);
            AddUsable(Scroll, ItemUseType.WarpTown, new[]
            {
                new ItemUseEffect(ItemEffectKind.WarpToMap,
                    destinationMap: new DefinitionId("map.town"))
            });

            _inventory.Add(Stack(Scroll, 2), Items);
            Bind();

            ItemUseResult result = _controller.SubmitUse(0);

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(_controller.PendingWarpDestination,
                Is.EqualTo(new DefinitionId("map.town")));
            Assert.That(_inventory.GetSlot(0).Quantity, Is.EqualTo(1));

            _controller.ConsumePendingWarp();
            Assert.That(_controller.PendingWarpDestination.IsValid, Is.False);
        }

        [Test]
        public void Buff_grants_from_the_last_use_are_reported_for_a_future_status_system()
        {
            const string Food = "item.food";
            AddStatusEffect("status.str", 600f);
            AddUsable(Food, ItemUseType.Buff, new[]
            {
                new ItemUseEffect(ItemEffectKind.ApplyStatusEffect,
                    statusEffect: new DefinitionId("status.str"))
            });

            _inventory.Add(Stack(Food, 1), Items);
            Bind();

            Assert.That(_controller.SubmitUse(0).IsAccepted, Is.True);
            Assert.That(_controller.LastGrantedBuffs.Count, Is.EqualTo(1));
            Assert.That(_controller.LastGrantedBuffs[0].DurationSeconds, Is.EqualTo(600f));
        }
    }
}
