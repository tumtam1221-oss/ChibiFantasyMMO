using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>Inventory and storage containers. (STEPS 3-10, 17-20)</summary>
    internal sealed class ItemContainerTests : ItemContainerTestBase
    {
        // ---------------- capacity and slots ----------------

        [Test]
        public void A_new_container_is_empty_with_the_capacity_it_was_given()
        {
            ItemContainerState bag = Container(40);

            Assert.That(bag.Capacity, Is.EqualTo(40));
            Assert.That(bag.FreeSlots, Is.EqualTo(40));
            Assert.That(bag.OccupiedSlots, Is.EqualTo(0));
            Assert.That(bag.IsFull, Is.False);
            Assert.That(bag.Slots.Count, Is.EqualTo(40));
        }

        [Test]
        public void Capacity_is_configurable_and_not_a_constant()
        {
            Assert.That(Container(1).Capacity, Is.EqualTo(1));
            Assert.That(Container(8).Capacity, Is.EqualTo(8));
            Assert.That(Container(200).Capacity, Is.EqualTo(200));
        }

        [Test]
        public void Slot_indices_outside_the_container_are_invalid()
        {
            ItemContainerState bag = Container(40);

            Assert.That(bag.IsValidIndex(-1), Is.False);
            Assert.That(bag.IsValidIndex(0), Is.True);
            Assert.That(bag.IsValidIndex(39), Is.True);
            Assert.That(bag.IsValidIndex(40), Is.False);

            Assert.That(bag.RemoveAt(-1, 1).Reason, Is.EqualTo(ItemContainerRejection.SlotOutOfRange));
            Assert.That(bag.RemoveAt(40, 1).Reason, Is.EqualTo(ItemContainerRejection.SlotOutOfRange));
        }

        [Test]
        public void A_negative_capacity_is_refused_at_construction()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => new ItemContainerState(Owner, -1));
        }

        // ---------------- add ----------------

        [Test]
        public void Adding_to_an_empty_container_uses_the_lowest_slot()
        {
            ItemContainerState bag = Container(5);

            ItemContainerResult result = bag.Add(Stack(Potion, 10), Items);

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.AffectedQuantity, Is.EqualTo(10));
            Assert.That(result.Remainder, Is.EqualTo(0));
            Assert.That(Describe(bag), Is.EqualTo("item.potionx10|-|-|-|-"));
        }

        [Test]
        public void Adding_fills_existing_stacks_before_taking_a_new_slot()
        {
            ItemContainerState bag = Container(5);
            bag.Add(Stack(Potion, 80), Items);

            ItemContainerResult result = bag.Add(Stack(Potion, 30), Items);

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.AffectedQuantity, Is.EqualTo(30));
            Assert.That(Describe(bag), Is.EqualTo("item.potionx99|item.potionx11|-|-|-"),
                "80 tops up to 99, the remaining 11 opens a new stack.");
        }

        [Test]
        public void Adding_spreads_across_several_stacks_without_exceeding_the_maximum()
        {
            ItemContainerState bag = Container(4);

            bag.Add(Stack(Ore, 25), Items);   // max 10

            Assert.That(Describe(bag), Is.EqualTo("item.orex10|item.orex10|item.orex5|-"));
            Assert.That(bag.CountOf(new DefinitionId(Ore)), Is.EqualTo(25));
        }

        [Test]
        public void Placement_is_deterministic()
        {
            for (int run = 0; run < 20; run++)
            {
                ItemContainerState bag = Container(6);
                bag.Add(Stack(Ore, 12), Items);
                bag.Add(Stack(Potion, 5), Items);
                bag.Add(Stack(Ore, 3), Items);

                Assert.That(Describe(bag),
                    Is.EqualTo("item.orex10|item.orex5|item.potionx5|-|-|-"));
            }
        }

        [Test]
        public void A_non_stackable_item_takes_a_whole_slot_each_time()
        {
            ItemContainerState bag = Container(3);

            bag.Add(Stack(Rock, 1), Items);
            bag.Add(Stack(Rock, 1), Items);

            Assert.That(Describe(bag), Is.EqualTo("item.rockx1|item.rockx1|-"));
        }

        [Test]
        public void A_full_container_reports_the_remainder_and_destroys_nothing()
        {
            ItemContainerState bag = Container(1);
            bag.Add(Stack(Potion, 90), Items);

            var incoming = Stack(Potion, 30);
            ItemContainerResult result = bag.Add(incoming, Items);

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.IsPartial, Is.True);
            Assert.That(result.AffectedQuantity, Is.EqualTo(9));
            Assert.That(result.Remainder, Is.EqualTo(21));
            Assert.That(bag.CountOf(new DefinitionId(Potion)), Is.EqualTo(99));
            Assert.That(incoming.Quantity, Is.EqualTo(21),
                "The caller is left holding exactly what did not fit.");
        }

        [Test]
        public void Adding_to_a_completely_full_container_is_refused()
        {
            ItemContainerState bag = Container(1);
            bag.Add(Stack(Rock, 1), Items);

            ItemContainerResult result = bag.Add(Stack(Rock, 1), Items);

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(result.Reason, Is.EqualTo(ItemContainerRejection.ContainerFull));
            Assert.That(Describe(bag), Is.EqualTo("item.rockx1"));
        }

        [Test]
        public void A_null_item_or_unknown_definition_is_refused()
        {
            ItemContainerState bag = Container(4);

            Assert.That(bag.Add(null, Items).Reason, Is.EqualTo(ItemContainerRejection.NoItem));

            var unknown = new ItemInstance(InstanceId.New(), new DefinitionId("item.nope"), Owner, 1);
            Assert.That(bag.Add(unknown, Items).Reason,
                Is.EqualTo(ItemContainerRejection.UnknownDefinition));
            Assert.That(bag.OccupiedSlots, Is.EqualTo(0));
        }

        [Test]
        public void The_same_instance_cannot_be_added_twice()
        {
            ItemContainerState bag = Container(4);
            var stack = Stack(Rock, 1);

            Assert.That(bag.Add(stack, Items).IsAccepted, Is.True);
            Assert.That(bag.Add(stack, Items).Reason,
                Is.EqualTo(ItemContainerRejection.DuplicateInstance));
            Assert.That(bag.OccupiedSlots, Is.EqualTo(1));
        }

        [Test]
        public void An_item_instance_cannot_hold_zero_or_a_negative_quantity()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => Stack(Potion, 0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => Stack(Potion, -5));
        }

        // ---------------- remove ----------------

        [Test]
        public void Removing_part_of_a_stack_leaves_the_rest()
        {
            ItemContainerState bag = Container(3);
            bag.Add(Stack(Potion, 20), Items);

            ItemContainerResult result = bag.RemoveAt(0, 5);

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(Describe(bag), Is.EqualTo("item.potionx15|-|-"));
        }

        [Test]
        public void Removing_the_whole_stack_clears_the_slot()
        {
            ItemContainerState bag = Container(3);
            bag.Add(Stack(Potion, 15), Items);

            bag.RemoveAt(0, 15);

            Assert.That(Describe(bag), Is.EqualTo("-|-|-"));
        }

        [Test]
        public void Removing_more_than_is_held_changes_nothing()
        {
            ItemContainerState bag = Container(3);
            bag.Add(Stack(Potion, 15), Items);
            Revision before = bag.Revision;

            ItemContainerResult result = bag.RemoveAt(0, 20);

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(result.Reason, Is.EqualTo(ItemContainerRejection.InsufficientQuantity));
            Assert.That(Describe(bag), Is.EqualTo("item.potionx15|-|-"));
            Assert.That(bag.Revision, Is.EqualTo(before), "A refused removal is not a change.");
        }

        [Test]
        public void Removing_by_definition_spans_stacks_and_is_all_or_nothing()
        {
            ItemContainerState bag = Container(4);
            bag.Add(Stack(Ore, 25), Items);        // 10 | 10 | 5

            Assert.That(bag.RemoveByDefinition(new DefinitionId(Ore), 12).IsAccepted, Is.True);
            Assert.That(bag.CountOf(new DefinitionId(Ore)), Is.EqualTo(13));

            ItemContainerResult tooMany = bag.RemoveByDefinition(new DefinitionId(Ore), 99);
            Assert.That(tooMany.Reason, Is.EqualTo(ItemContainerRejection.InsufficientQuantity));
            Assert.That(bag.CountOf(new DefinitionId(Ore)), Is.EqualTo(13),
                "Asking for too much removes nothing at all.");
        }

        [Test]
        public void Removing_a_zero_or_negative_quantity_is_refused()
        {
            ItemContainerState bag = Container(2);
            bag.Add(Stack(Potion, 5), Items);

            Assert.That(bag.RemoveAt(0, 0).Reason, Is.EqualTo(ItemContainerRejection.InvalidQuantity));
            Assert.That(bag.RemoveAt(0, -3).Reason, Is.EqualTo(ItemContainerRejection.InvalidQuantity));
            Assert.That(bag.CountOf(new DefinitionId(Potion)), Is.EqualTo(5));
        }

        // ---------------- move ----------------

        [Test]
        public void Moving_onto_an_empty_slot_relocates_the_contents()
        {
            ItemContainerState bag = Container(4);
            bag.Add(Stack(Potion, 7), Items);

            bag.Move(0, 3, Items);

            Assert.That(Describe(bag), Is.EqualTo("-|-|-|item.potionx7"));
        }

        [Test]
        public void Moving_onto_a_compatible_stack_merges()
        {
            ItemContainerState bag = Container(4);
            bag.Add(Stack(Potion, 50), Items);
            bag.Split(0, 20, 2, Items);

            bag.Move(2, 0, Items);

            Assert.That(Describe(bag), Is.EqualTo("item.potionx50|-|-|-"));
        }

        [Test]
        public void Moving_onto_an_incompatible_slot_swaps()
        {
            ItemContainerState bag = Container(3);
            bag.Add(Stack(Potion, 5), Items);
            bag.Add(Stack(Rock, 1), Items);

            bag.Move(0, 1, Items);

            Assert.That(Describe(bag), Is.EqualTo("item.rockx1|item.potionx5|-"));
        }

        [Test]
        public void Moving_from_empty_or_to_itself_is_refused()
        {
            ItemContainerState bag = Container(3);
            bag.Add(Stack(Potion, 5), Items);

            Assert.That(bag.Move(1, 2, Items).Reason, Is.EqualTo(ItemContainerRejection.SourceEmpty));
            Assert.That(bag.Move(0, 0, Items).Reason, Is.EqualTo(ItemContainerRejection.SameSlot));
            Assert.That(bag.Move(0, 9, Items).Reason, Is.EqualTo(ItemContainerRejection.SlotOutOfRange));
            Assert.That(Describe(bag), Is.EqualTo("item.potionx5|-|-"));
        }

        // ---------------- split ----------------

        [Test]
        public void Splitting_moves_part_of_a_stack_into_an_empty_slot()
        {
            ItemContainerState bag = Container(4);
            bag.Add(Stack(Potion, 50), Items);

            ItemContainerResult result = bag.Split(0, 20, 1, Items);

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(Describe(bag), Is.EqualTo("item.potionx30|item.potionx20|-|-"));
            Assert.That(bag.CountOf(new DefinitionId(Potion)), Is.EqualTo(50), "Nothing created, nothing lost.");
        }

        [Test]
        public void Splitting_creates_a_distinct_instance_identity()
        {
            ItemContainerState bag = Container(4);
            bag.Add(Stack(Potion, 50), Items);
            bag.Split(0, 20, 1, Items);

            Assert.That(bag.GetSlot(0).InstanceId, Is.Not.EqualTo(bag.GetSlot(1).InstanceId),
                "Two stacks of the same item are still two owned things.");
        }

        [Test]
        public void An_invalid_split_changes_nothing()
        {
            ItemContainerState bag = Container(4);
            bag.Add(Stack(Potion, 50), Items);
            bag.Add(Stack(Rock, 1), Items);
            Revision before = bag.Revision;

            Assert.That(bag.Split(0, 0, 2, Items).Reason, Is.EqualTo(ItemContainerRejection.InvalidQuantity));
            Assert.That(bag.Split(0, 50, 2, Items).Reason, Is.EqualTo(ItemContainerRejection.InvalidQuantity),
                "Splitting the whole stack is a move, not a split.");
            Assert.That(bag.Split(0, 60, 2, Items).Reason, Is.EqualTo(ItemContainerRejection.InvalidQuantity));
            Assert.That(bag.Split(0, 10, 1, Items).Reason, Is.EqualTo(ItemContainerRejection.DestinationOccupied));
            Assert.That(bag.Split(1, 1, 2, Items).Reason, Is.EqualTo(ItemContainerRejection.NotStackable));

            Assert.That(Describe(bag), Is.EqualTo("item.potionx50|item.rockx1|-|-"));
            Assert.That(bag.Revision, Is.EqualTo(before));
        }

        // ---------------- merge ----------------

        [Test]
        public void Merging_pours_one_stack_into_another()
        {
            ItemContainerState bag = Container(3);
            bag.Add(Stack(Potion, 50), Items);
            bag.Split(0, 30, 1, Items);      // 20 | 30

            ItemContainerResult result = bag.Merge(1, 0, Items);

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.Remainder, Is.EqualTo(0));
            Assert.That(Describe(bag), Is.EqualTo("item.potionx50|-|-"));
        }

        [Test]
        public void Merging_past_the_maximum_leaves_the_overflow_behind()
        {
            // Reach 80 | 50 through the public API: 149 fills 99 | 50, then trim the first.
            ItemContainerState bag = Container(3);
            bag.Add(Stack(Potion, 149), Items);
            bag.RemoveAt(0, 19);
            Assert.That(Describe(bag), Is.EqualTo("item.potionx80|item.potionx50|-"));

            ItemContainerResult result = bag.Merge(1, 0, Items);

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.AffectedQuantity, Is.EqualTo(19));
            Assert.That(result.Remainder, Is.EqualTo(31));
            Assert.That(Describe(bag), Is.EqualTo("item.potionx99|item.potionx31|-"),
                "Never above the maximum, and the overflow is never destroyed.");
        }

        [Test]
        public void Non_stackable_slots_never_merge_even_with_the_same_definition()
        {
            ItemContainerState bag = Container(3);
            bag.Add(Stack(Rock, 1), Items);
            bag.Add(Stack(Rock, 1), Items);

            Assert.That(bag.CanStackTogether(0, 1, Items), Is.False);
            Assert.That(bag.Merge(0, 1, Items).Reason, Is.EqualTo(ItemContainerRejection.NotStackable));
            Assert.That(Describe(bag), Is.EqualTo("item.rockx1|item.rockx1|-"));
        }

        // ---------------- transfers ----------------

        [Test]
        public void Depositing_moves_items_between_containers()
        {
            ItemContainerState bag = Container(4);
            ItemContainerState vault = Container(4);
            bag.Add(Stack(Potion, 30), Items);

            ItemContainerResult result = ItemContainerTransfer.Deposit(bag, vault, 0, 10, Items);

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(bag.CountOf(new DefinitionId(Potion)), Is.EqualTo(20));
            Assert.That(vault.CountOf(new DefinitionId(Potion)), Is.EqualTo(10));
        }

        [Test]
        public void Withdrawing_is_the_same_operation_in_the_other_direction()
        {
            ItemContainerState bag = Container(4);
            ItemContainerState vault = Container(4);
            vault.Add(Stack(Potion, 30), Items);

            ItemContainerTransfer.Withdraw(vault, bag, 0, 25, Items);

            Assert.That(vault.CountOf(new DefinitionId(Potion)), Is.EqualTo(5));
            Assert.That(bag.CountOf(new DefinitionId(Potion)), Is.EqualTo(25));
        }

        [Test]
        public void A_transfer_into_a_full_container_changes_neither_side()
        {
            ItemContainerState bag = Container(2);
            ItemContainerState vault = Container(1);
            bag.Add(Stack(Potion, 30), Items);
            vault.Add(Stack(Rock, 1), Items);

            Revision bagBefore = bag.Revision;
            Revision vaultBefore = vault.Revision;

            ItemContainerResult result = ItemContainerTransfer.Deposit(bag, vault, 0, 10, Items);

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(result.Reason, Is.EqualTo(ItemContainerRejection.ContainerFull));
            Assert.That(bag.CountOf(new DefinitionId(Potion)), Is.EqualTo(30));
            Assert.That(vault.CountOf(new DefinitionId(Potion)), Is.EqualTo(0));
            Assert.That(bag.Revision, Is.EqualTo(bagBefore));
            Assert.That(vault.Revision, Is.EqualTo(vaultBefore));
        }

        [Test]
        public void Transferring_equipment_keeps_the_very_same_instance()
        {
            ItemContainerState bag = Container(4);
            ItemContainerState vault = Container(4);
            var sword = Gear(Sword);
            bag.Add(sword, Items);

            ItemContainerTransfer.Deposit(bag, vault, 0, 1, Items);

            Assert.That(bag.OccupiedSlots, Is.EqualTo(0));
            Assert.That(vault.GetSlot(0).InstanceId, Is.EqualTo(sword.InstanceId),
                "A sword keeps its identity, and with it its enhancement level.");
        }

        [Test]
        public void Invalid_transfers_are_refused_without_touching_anything()
        {
            ItemContainerState bag = Container(4);
            ItemContainerState vault = Container(4);
            bag.Add(Stack(Potion, 10), Items);

            Assert.That(ItemContainerTransfer.Deposit(bag, vault, 3, 1, Items).Reason,
                Is.EqualTo(ItemContainerRejection.SourceEmpty));
            Assert.That(ItemContainerTransfer.Deposit(bag, vault, 0, 0, Items).Reason,
                Is.EqualTo(ItemContainerRejection.InvalidQuantity));
            Assert.That(ItemContainerTransfer.Deposit(bag, vault, 0, 99, Items).Reason,
                Is.EqualTo(ItemContainerRejection.InsufficientQuantity));
            Assert.That(ItemContainerTransfer.Deposit(bag, bag, 0, 1, Items).Reason,
                Is.EqualTo(ItemContainerRejection.SameSlot));

            Assert.That(bag.CountOf(new DefinitionId(Potion)), Is.EqualTo(10));
            Assert.That(vault.OccupiedSlots, Is.EqualTo(0));
        }

        [Test]
        public void The_revision_advances_only_on_a_real_change()
        {
            ItemContainerState bag = Container(3);
            Revision start = bag.Revision;

            bag.Add(Stack(Potion, 5), Items);
            Assert.That(bag.Revision.IsNewerThan(start), Is.True);

            Revision afterAdd = bag.Revision;
            bag.RemoveAt(2, 1);      // empty slot, refused
            Assert.That(bag.Revision, Is.EqualTo(afterAdd));
        }
    }
}
