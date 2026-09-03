using System.Collections.Generic;
using ChibiFantasy.Client.UI;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.UI;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The presentation rules of PHASE 08.2.
    /// </summary>
    /// <remarks>
    /// These are the rules a player sees and the ones most likely to be quietly broken by
    /// a later change: what an empty square shows, when a stack count appears, whether two
    /// identical stacks can be told apart, and what happens when art or content is missing.
    /// They run without a Canvas, a scene or a prefab, because the rules live in the view
    /// data rather than in the components that draw it.
    /// </remarks>
    internal sealed class InventoryUiViewDataTests : ItemContainerTestBase
    {
        private const string NoArt = "item.noart";

        [Test]
        public void An_empty_slot_shows_nothing_and_keeps_its_position()
        {
            ItemSlotViewData slot = ItemSlotViewData.Empty(4);

            Assert.That(slot.IsEmpty, Is.True);
            Assert.That(slot.IsOccupied, Is.False);
            Assert.That(slot.SlotIndex, Is.EqualTo(4), "an empty square still has a position");
            Assert.That(slot.Quantity, Is.EqualTo(0));
            Assert.That(slot.ShowQuantity, Is.False);
            Assert.That(slot.HasIcon, Is.False);
            Assert.That(slot.DefinitionId.IsValid, Is.False);
            Assert.That(slot.InstanceId.IsValid, Is.False);
        }

        [Test]
        public void An_occupied_slot_carries_what_the_definition_authored()
        {
            ItemDefinition definition;
            Items.TryGet(new DefinitionId(Potion), out definition);

            ItemInstance stack = Stack(Potion, 5);
            ItemSlotViewData slot = ItemSlotViewData.From(2, stack.DefinitionId,
                stack.InstanceId, stack.Quantity, definition);

            Assert.That(slot.IsOccupied, Is.True);
            Assert.That(slot.SlotIndex, Is.EqualTo(2));
            Assert.That(slot.DefinitionId, Is.EqualTo(new DefinitionId(Potion)));
            Assert.That(slot.InstanceId, Is.EqualTo(stack.InstanceId));
            Assert.That(slot.Quantity, Is.EqualTo(5));
            Assert.That(slot.Category, Is.EqualTo(definition.Category),
                "the category is the authored one, never re-derived by the UI");
            Assert.That(slot.IsEquipment, Is.False);
        }

        [Test]
        public void A_single_item_shows_no_number_and_a_stack_does()
        {
            ItemDefinition potion;
            Items.TryGet(new DefinitionId(Potion), out potion);

            ItemSlotViewData one = ItemSlotViewData.From(0, new DefinitionId(Potion),
                InstanceId.New(), 1, potion);
            ItemSlotViewData many = ItemSlotViewData.From(1, new DefinitionId(Potion),
                InstanceId.New(), 2, potion);

            Assert.That(one.ShowQuantity, Is.False, "a lone item labelled 1 is noise");
            Assert.That(many.ShowQuantity, Is.True);
            Assert.That(ItemSlotViewData.Empty(2).ShowQuantity, Is.False);
        }

        [Test]
        public void Two_stacks_of_the_same_item_are_still_different_things()
        {
            ItemDefinition potion;
            Items.TryGet(new DefinitionId(Potion), out potion);

            ItemInstance first = Stack(Potion, 5);
            ItemInstance second = Stack(Potion, 5);

            ItemSlotViewData a = ItemSlotViewData.From(0, first.DefinitionId,
                first.InstanceId, 5, potion);
            ItemSlotViewData b = ItemSlotViewData.From(1, second.DefinitionId,
                second.InstanceId, 5, potion);

            Assert.That(a.DefinitionId, Is.EqualTo(b.DefinitionId));
            Assert.That(a.InstanceId, Is.Not.EqualTo(b.InstanceId));

            // Selecting one must not light up or act on the other.
            var selection = new ItemSelection(ItemSelectionSource.Inventory, 0, first.InstanceId);
            Assert.That(selection.Matches(a), Is.True);
            Assert.That(selection.Matches(b), Is.False);
        }

        [Test]
        public void An_equipment_slot_keeps_the_position_it_was_asked_for()
        {
            ItemDefinition helm;
            Items.TryGet(new DefinitionId(Helm), out helm);

            EquipmentInstance worn = Gear(Helm);
            EquipmentSlotViewData data = EquipmentSlotViewData.From(EquipmentSlot.Head,
                worn.DefinitionId, worn.InstanceId, helm);

            Assert.That(data.Slot, Is.EqualTo(EquipmentSlot.Head));
            Assert.That(data.InstanceId, Is.EqualTo(worn.InstanceId));
            Assert.That(data.IsOccupied, Is.True);

            EquipmentSlotViewData empty = EquipmentSlotViewData.Empty(EquipmentSlot.OffHand);
            Assert.That(empty.Slot, Is.EqualTo(EquipmentSlot.OffHand),
                "an empty paperdoll position must stay on screen");
            Assert.That(empty.IsEmpty, Is.True);
        }

        [Test]
        public void A_worn_piece_reuses_the_item_square_without_showing_a_count()
        {
            ItemDefinition helm;
            Items.TryGet(new DefinitionId(Helm), out helm);

            EquipmentInstance worn = Gear(Helm);
            EquipmentSlotViewData equipped = EquipmentSlotViewData.From(EquipmentSlot.Head,
                worn.DefinitionId, worn.InstanceId, helm);

            ItemSlotViewData square = ItemSlotViewData.ForEquipment(3, equipped);

            Assert.That(square.SlotIndex, Is.EqualTo(3), "the index is the layout position");
            Assert.That(square.InstanceId, Is.EqualTo(worn.InstanceId));
            Assert.That(square.Quantity, Is.EqualTo(1));
            Assert.That(square.ShowQuantity, Is.False, "equipment never stacks");
            Assert.That(square.IsEquipment, Is.True);

            Assert.That(ItemSlotViewData.ForEquipment(3,
                EquipmentSlotViewData.Empty(EquipmentSlot.Head)).IsEmpty, Is.True);
        }

        [Test]
        public void A_tooltip_repeats_authored_values_and_computes_nothing()
        {
            ItemDefinition sword;
            Items.TryGet(new DefinitionId(Sword), out sword);

            ItemTooltipData tooltip = ItemTooltipData.From(new DefinitionId(Sword), 1, sword);

            Assert.That(tooltip.IsValid, Is.True);
            Assert.That(tooltip.IsEquipment, Is.True);
            Assert.That(tooltip.Slot, Is.EqualTo(EquipmentSlot.MainHand));
            Assert.That(tooltip.HasStatModifiers, Is.True);
            Assert.That(tooltip.StatModifiers.Count, Is.EqualTo(1));
            Assert.That(tooltip.StatModifiers[0].Stat, Is.EqualTo(new DefinitionId(Str)));
            Assert.That(tooltip.StatModifiers[0].Value, Is.EqualTo(10f),
                "the authored value verbatim, never a computed effective stat");
            Assert.That(tooltip.HasLevelRequirement, Is.False, "the sword authors no level gate");
        }

        [Test]
        public void A_tooltip_shows_a_level_gate_and_a_class_restriction_when_authored()
        {
            ItemDefinition robe;
            ItemDefinition restricted;
            Items.TryGet(new DefinitionId(Robe), out robe);
            Items.TryGet(new DefinitionId(ClassOnly), out restricted);

            ItemTooltipData robeTip = ItemTooltipData.From(new DefinitionId(Robe), 1, robe);
            ItemTooltipData classTip = ItemTooltipData.From(new DefinitionId(ClassOnly), 1, restricted);

            Assert.That(robeTip.HasLevelRequirement, Is.True);
            Assert.That(robeTip.LevelRequirement, Is.EqualTo(20));
            Assert.That(robeTip.HasClassRestriction, Is.False);

            Assert.That(classTip.HasClassRestriction, Is.True);
            Assert.That(classTip.AllowedClasses[0], Is.EqualTo(new DefinitionId(ClassA)));

            string body = ItemTooltipView.FormatBody(classTip);
            Assert.That(body, Does.Contain(ClassA), "the restriction has to be visible to a player");
        }

        [Test]
        public void A_tooltip_for_nothing_is_none_rather_than_an_exception()
        {
            Assert.That(ItemTooltipData.None.IsValid, Is.False);
            Assert.That(ItemTooltipData.From(new DefinitionId(Potion), 1, null).IsValid, Is.False);
            Assert.That(ItemTooltipData.From(DefinitionId.None, 1, null).IsValid, Is.False);
            Assert.That(ItemTooltipView.FormatTitle(ItemTooltipData.None), Is.Empty);
            Assert.That(ItemTooltipView.FormatBody(ItemTooltipData.None), Is.Empty);
        }

        [Test]
        public void A_stack_count_appears_in_the_tooltip_title_only_above_one()
        {
            ItemDefinition potion;
            Items.TryGet(new DefinitionId(Potion), out potion);

            ItemTooltipData one = ItemTooltipData.From(new DefinitionId(Potion), 1, potion);
            ItemTooltipData many = ItemTooltipData.From(new DefinitionId(Potion), 7, potion);

            Assert.That(ItemTooltipView.FormatTitle(one), Does.Not.Contain("x"));
            Assert.That(ItemTooltipView.FormatTitle(many), Does.Contain("x7"));
        }

        [Test]
        public void An_item_with_no_authored_icon_is_drawable_not_an_error()
        {
            AddItem(NoArt, stackable: false, maxStack: 1);

            ItemDefinition definition;
            Items.TryGet(new DefinitionId(NoArt), out definition);

            ItemSlotViewData slot = ItemSlotViewData.From(0, new DefinitionId(NoArt),
                InstanceId.New(), 1, definition);

            Assert.That(slot.IsOccupied, Is.True, "no art does not make the slot empty");
            Assert.That(slot.HasIcon, Is.False, "so a view draws a placeholder instead");
            Assert.That(slot.Icon.IsValid, Is.False);
        }

        [Test]
        public void An_item_whose_content_is_gone_shows_as_occupied_but_unnamed()
        {
            // A save can outlive a definition removed by a patch. Losing the row silently
            // would hide that; an unnamed square shows it without crashing the bag.
            ItemSlotViewData slot = ItemSlotViewData.From(0, new DefinitionId("item.deleted"),
                InstanceId.New(), 3, null);

            Assert.That(slot.IsOccupied, Is.True);
            Assert.That(slot.Quantity, Is.EqualTo(3));
            Assert.That(slot.HasIcon, Is.False);
            Assert.That(slot.NameKey.IsValid, Is.False);
        }

        [Test]
        public void A_selection_stops_matching_once_its_item_has_moved_on()
        {
            ItemDefinition potion;
            Items.TryGet(new DefinitionId(Potion), out potion);

            ItemInstance original = Stack(Potion, 5);
            var selection = new ItemSelection(ItemSelectionSource.Inventory, 0, original.InstanceId);

            ItemSlotViewData sameSlotDifferentItem = ItemSlotViewData.From(0,
                new DefinitionId(Potion), InstanceId.New(), 5, potion);

            Assert.That(selection.Matches(sameSlotDifferentItem), Is.False,
                "the slot was refilled; acting on it would hit the wrong item");
            Assert.That(selection.Matches(ItemSlotViewData.Empty(0)), Is.False);

            ItemSlotViewData sameItemMoved = ItemSlotViewData.From(3,
                new DefinitionId(Potion), original.InstanceId, 5, potion);

            Assert.That(selection.Matches(sameItemMoved), Is.False,
                "the item moved; the recorded position is stale");
        }

        [Test]
        public void An_empty_selection_matches_nothing_at_all()
        {
            Assert.That(ItemSelection.None.IsEmpty, Is.True);
            Assert.That(ItemSelection.None.Matches(ItemSlotViewData.Empty(0)), Is.False);
            Assert.That(ItemSelection.None.Matches(
                EquipmentSlotViewData.Empty(EquipmentSlot.Head)), Is.False);
        }

        [Test]
        public void An_inventory_selection_never_matches_an_equipment_square()
        {
            ItemDefinition helm;
            Items.TryGet(new DefinitionId(Helm), out helm);

            EquipmentInstance worn = Gear(Helm);
            EquipmentSlotViewData equipped = EquipmentSlotViewData.From(EquipmentSlot.Head,
                worn.DefinitionId, worn.InstanceId, helm);

            var fromBag = new ItemSelection(ItemSelectionSource.Inventory, 0, worn.InstanceId);
            var fromPaperdoll = new ItemSelection(ItemSelectionSource.Equipment,
                (int)EquipmentSlot.Head, worn.InstanceId);

            Assert.That(fromBag.Matches(equipped), Is.False,
                "the two panels number their slots differently and must not be crossed");
            Assert.That(fromPaperdoll.Matches(equipped), Is.True);
        }

        [Test]
        public void The_adapter_produces_one_square_per_slot_including_the_empty_ones()
        {
            ItemContainerState container = Container(6);
            container.Add(Stack(Potion, 5), Items);
            container.Add(Gear(Sword), Items);

            var view = new List<ItemSlotViewData>();
            InventoryViewAdapter.BuildContainer(container, Items, view);

            Assert.That(view.Count, Is.EqualTo(6), "the grid shape comes from capacity");
            Assert.That(view[0].DefinitionId, Is.EqualTo(new DefinitionId(Potion)));
            Assert.That(view[0].Quantity, Is.EqualTo(5));
            Assert.That(view[1].IsEquipment, Is.True);
            Assert.That(view[2].IsEmpty, Is.True);

            for (int i = 0; i < view.Count; i++)
            {
                Assert.That(view[i].SlotIndex, Is.EqualTo(i), "index must be the position");
            }
        }

        [Test]
        public void The_adapter_refreshes_to_whatever_gameplay_now_says()
        {
            ItemContainerState container = Container(4);
            container.Add(Stack(Potion, 5), Items);

            var view = new List<ItemSlotViewData>();
            InventoryViewAdapter.BuildContainer(container, Items, view);
            Assert.That(view[0].Quantity, Is.EqualTo(5));

            // Gameplay changes; the UI was never told, and asking again is all it takes.
            container.RemoveAt(0, 2);
            InventoryViewAdapter.BuildContainer(container, Items, view);

            Assert.That(view[0].Quantity, Is.EqualTo(3));

            container.RemoveAt(0, 3);
            InventoryViewAdapter.BuildContainer(container, Items, view);

            Assert.That(view[0].IsEmpty, Is.True, "an emptied slot must go back to empty");
            Assert.That(view.Count, Is.EqualTo(4));
        }

        [Test]
        public void The_adapter_reports_only_the_positions_that_are_actually_worn()
        {
            ItemContainerState inventory = Container(4);
            var equipment = new CharacterEquipmentState(new CharacterId("char:test"));
            inventory.Add(Gear(Helm), Items);

            EquipmentService.Equip(inventory, equipment, 0,
                new EquipmentService.Context(Items, 1));

            var view = new List<EquipmentSlotViewData>();
            InventoryViewAdapter.BuildEquipment(equipment, Items, view);

            Assert.That(view.Count, Is.EqualTo(1));
            Assert.That(view[0].Slot, Is.EqualTo(EquipmentSlot.Head));
            Assert.That(view[0].DefinitionId, Is.EqualTo(new DefinitionId(Helm)));
        }

        [Test]
        public void The_adapter_survives_a_missing_registry_and_a_missing_container()
        {
            var view = new List<ItemSlotViewData>();

            Assert.DoesNotThrow(() => InventoryViewAdapter.BuildContainer(null, Items, view));
            Assert.That(view.Count, Is.EqualTo(0));

            ItemContainerState container = Container(2);
            container.Add(Stack(Potion, 1), Items);

            Assert.DoesNotThrow(() => InventoryViewAdapter.BuildContainer(container, null, view));
            Assert.That(view.Count, Is.EqualTo(2));
            Assert.That(view[0].IsOccupied, Is.True, "the row still exists without its art");
            Assert.That(view[0].HasIcon, Is.False);
        }
    }
}
