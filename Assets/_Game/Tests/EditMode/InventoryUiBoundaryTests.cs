using System;
using System.Linq;
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
    /// The rule PHASE 08.2 exists to protect: the UI is a view, not an owner.
    /// </summary>
    /// <remarks>
    /// A panel that quietly changed a quantity would still look correct on screen, so this
    /// cannot be checked by looking at the game. It is checked structurally -- the UI
    /// assembly cannot see gameplay at all -- and behaviourally: every change the player
    /// can cause goes through an existing Phase 08.1 service and the service's answer is
    /// what happens.
    /// </remarks>
    internal sealed class InventoryUiBoundaryTests : ItemContainerTestBase
    {
        private GameObject _host;
        private InventoryUiController _controller;
        private ItemContainerState _inventory;
        private ItemContainerState _storage;
        private CharacterEquipmentState _equipment;

        [SetUp]
        public void CreateController()
        {
            _host = new GameObject("InventoryUiTestHost");
            _controller = _host.AddComponent<InventoryUiController>();

            _inventory = Container(8);
            _storage = Container(8);
            _equipment = new CharacterEquipmentState(new CharacterId("char:test"));
        }

        [TearDown]
        public void DestroyController()
        {
            if (_host != null) UnityEngine.Object.DestroyImmediate(_host);
        }

        private void Bind(int level = 1)
        {
            _controller.Bind(_inventory, _storage, _equipment, Items, level);
        }

        // ---- structural ---------------------------------------------------------------

        [Test]
        public void The_ui_assembly_cannot_see_gameplay_at_all()
        {
            Assembly ui = typeof(ItemSlotViewData).Assembly;

            Assert.That(ui.GetName().Name, Is.EqualTo("ChibiFantasy.UI"));

            string[] referenced = ui.GetReferencedAssemblies().Select(a => a.Name).ToArray();

            Assert.That(referenced, Does.Not.Contain("ChibiFantasy.Gameplay"),
                "the UI must not be able to reach a container, so it must not compile against one");
            Assert.That(referenced, Does.Not.Contain("ChibiFantasy.Backend"));
            Assert.That(referenced, Does.Not.Contain("ChibiFantasy.Network"));
        }

        [Test]
        public void No_ui_type_holds_a_container_or_an_instance()
        {
            Assembly ui = typeof(ItemSlotViewData).Assembly;

            foreach (Type type in ui.GetTypes())
            {
                FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Static
                    | BindingFlags.Public | BindingFlags.NonPublic);

                foreach (FieldInfo field in fields)
                {
                    string held = field.FieldType.FullName ?? string.Empty;

                    Assert.That(held, Does.Not.Contain("ChibiFantasy.Gameplay"),
                        type.Name + "." + field.Name + " holds gameplay state; a view must hold copies");
                    Assert.That(held, Does.Not.Contain("ItemInstance"),
                        type.Name + "." + field.Name + " holds an owned item rather than a snapshot");
                }
            }
        }

        [Test]
        public void No_second_inventory_or_equipment_state_was_introduced()
        {
            string[] forbidden =
            {
                "InventoryState", "InventoryModel", "UiInventory", "UiInventoryState",
                "EquipmentModel", "UiEquipmentState", "StorageState", "ItemDatabase"
            };

            Type[] types = typeof(ItemSlotViewData).Assembly.GetTypes()
                .Concat(typeof(InventoryUiController).Assembly.GetTypes()).ToArray();

            foreach (Type type in types)
            {
                Assert.That(forbidden, Does.Not.Contain(type.Name),
                    type.FullName + " duplicates state Phase 08.1 already owns");
            }
        }

        // ---- behavioural --------------------------------------------------------------

        [Test]
        public void Clicking_selects_and_never_changes_anything()
        {
            _inventory.Add(Stack(Potion, 5), Items);
            Bind();

            Revision before = _inventory.Revision;
            _controller.OnInventoryClicked(0);

            Assert.That(_controller.Selection.Source, Is.EqualTo(ItemSelectionSource.Inventory));
            Assert.That(_controller.Selection.SlotIndex, Is.EqualTo(0));
            Assert.That(_inventory.Revision, Is.EqualTo(before),
                "selecting is UI state; gameplay must not have moved");
            Assert.That(_inventory.GetSlot(0).Quantity, Is.EqualTo(5));
        }

        [Test]
        public void Clicking_an_empty_slot_drops_the_selection()
        {
            _inventory.Add(Stack(Potion, 5), Items);
            Bind();

            _controller.OnInventoryClicked(0);
            Assert.That(_controller.Selection.IsEmpty, Is.False);

            _controller.OnInventoryClicked(3);
            Assert.That(_controller.Selection.IsEmpty, Is.True);
        }

        [Test]
        public void A_second_click_equips_through_the_service_and_the_state_moves()
        {
            _inventory.Add(Gear(Helm), Items);
            Bind();

            _controller.OnInventoryClicked(0);
            _controller.OnInventoryClicked(0);

            Assert.That(_controller.LastEquipResult.IsAccepted, Is.True);
            Assert.That(_equipment.IsOccupied(EquipmentSlot.Head), Is.True,
                "the service moved it, not the panel");
            Assert.That(_inventory.GetSlot(0).IsEmpty, Is.True);
            Assert.That(_controller.Selection.IsEmpty, Is.True,
                "the selected item is no longer where it was");
        }

        [Test]
        public void A_refused_equip_changes_nothing_and_keeps_the_service_reason()
        {
            _inventory.Add(Gear(Robe), Items);   // level 20 gear
            Bind(level: 5);

            Revision inventoryBefore = _inventory.Revision;
            Revision equipmentBefore = _equipment.Revision;

            _controller.OnInventoryClicked(0);
            _controller.OnInventoryClicked(0);

            Assert.That(_controller.LastEquipResult.IsAccepted, Is.False);
            Assert.That(_controller.LastEquipResult.Reason,
                Is.EqualTo(EquipRejection.LevelTooLow),
                "the UI reports the service's reason rather than inventing one");
            Assert.That(_inventory.Revision, Is.EqualTo(inventoryBefore));
            Assert.That(_equipment.Revision, Is.EqualTo(equipmentBefore));
            Assert.That(_inventory.GetSlot(0).IsOccupied, Is.True);
        }

        [Test]
        public void A_non_equippable_item_clicked_twice_simply_does_nothing()
        {
            _inventory.Add(Stack(Potion, 3), Items);
            Bind();

            Revision before = _inventory.Revision;

            _controller.OnInventoryClicked(0);
            _controller.OnInventoryClicked(0);

            Assert.That(_inventory.Revision, Is.EqualTo(before),
                "the fixture potion is not authored usable, so there is nothing to do");
            Assert.That(_inventory.GetSlot(0).Quantity, Is.EqualTo(3));
        }

        [Test]
        public void Clicking_a_worn_piece_twice_takes_it_off_through_the_service()
        {
            _inventory.Add(Gear(Helm), Items);
            Bind();

            _controller.SubmitEquip(0);
            Assert.That(_equipment.IsOccupied(EquipmentSlot.Head), Is.True);

            _controller.OnEquipmentClicked(EquipmentSlot.Head);
            Assert.That(_controller.Selection.Source, Is.EqualTo(ItemSelectionSource.Equipment));

            _controller.OnEquipmentClicked(EquipmentSlot.Head);

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
            _inventory.Add(Stack(Rock, 1), Items);   // the only slot is taken again

            EquipResult result = _controller.SubmitUnequip(EquipmentSlot.Head);

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(result.Reason, Is.EqualTo(EquipRejection.InventoryFull));
            Assert.That(_equipment.IsOccupied(EquipmentSlot.Head), Is.True,
                "refusing is the service's call; the UI must not drop the piece anyway");
        }

        [Test]
        public void Storage_deposits_and_withdraws_go_through_the_transfer_service()
        {
            _inventory.Add(Stack(Potion, 5), Items);
            Bind();
            _controller.SetStorageOpen(true);

            _controller.OnInventoryClicked(0);
            _controller.OnInventoryClicked(0);

            Assert.That(_controller.LastContainerResult.IsAccepted, Is.True);
            Assert.That(_inventory.GetSlot(0).IsEmpty, Is.True);
            Assert.That(_storage.CountOf(new DefinitionId(Potion)), Is.EqualTo(5));

            _controller.OnStorageClicked(0);
            _controller.OnStorageClicked(0);

            Assert.That(_storage.GetSlot(0).IsEmpty, Is.True);
            Assert.That(_inventory.CountOf(new DefinitionId(Potion)), Is.EqualTo(5),
                "nothing was created or destroyed on the way");
        }

        [Test]
        public void Closing_storage_drops_a_selection_that_pointed_into_it()
        {
            _storage.Add(Stack(Potion, 2), Items);
            Bind();
            _controller.SetStorageOpen(true);

            _controller.OnStorageClicked(0);
            Assert.That(_controller.Selection.Source, Is.EqualTo(ItemSelectionSource.Storage));

            _controller.SetStorageOpen(false);

            Assert.That(_controller.Selection.IsEmpty, Is.True,
                "acting on an invisible panel would surprise the player");
        }

        [Test]
        public void A_refresh_shows_a_change_the_ui_did_not_make()
        {
            _inventory.Add(Stack(Potion, 5), Items);
            Bind();

            // Loot, a consumed potion, anything outside this window.
            _inventory.Add(Stack(Ore, 4), Items);

            Assert.That(_controller.RefreshIfChanged(), Is.True);

            ItemTooltipData none = _controller.BuildSelectionTooltip();
            Assert.That(none.IsValid, Is.False);

            _controller.OnInventoryClicked(1);
            ItemTooltipData tooltip = _controller.BuildSelectionTooltip();

            Assert.That(tooltip.IsValid, Is.True);
            Assert.That(tooltip.DefinitionId, Is.EqualTo(new DefinitionId(Ore)));
            Assert.That(tooltip.Quantity, Is.EqualTo(4));
        }

        [Test]
        public void An_unchanged_container_causes_no_refresh_work()
        {
            _inventory.Add(Stack(Potion, 5), Items);
            Bind();

            Assert.That(_controller.RefreshIfChanged(), Is.False,
                "the panel must not rebuild itself while nothing is happening");
            Assert.That(_controller.RefreshIfChanged(), Is.False);
        }

        [Test]
        public void A_selection_whose_item_vanished_is_dropped_on_the_next_refresh()
        {
            _inventory.Add(Stack(Potion, 5), Items);
            Bind();

            _controller.OnInventoryClicked(0);
            Assert.That(_controller.Selection.IsEmpty, Is.False);

            _inventory.RemoveAt(0, 5);
            _controller.Refresh();

            Assert.That(_controller.Selection.IsEmpty, Is.True);
            Assert.That(_controller.BuildSelectionTooltip().IsValid, Is.False);
        }

        [Test]
        public void An_unbound_controller_does_nothing_rather_than_throwing()
        {
            Assert.DoesNotThrow(() => _controller.Refresh());
            Assert.DoesNotThrow(() => _controller.OnInventoryClicked(0));
            Assert.DoesNotThrow(() => _controller.OnEquipmentClicked(EquipmentSlot.Head));
            Assert.That(_controller.RefreshIfChanged(), Is.False);
            Assert.That(_controller.Selection.IsEmpty, Is.True);
        }
    }
}
