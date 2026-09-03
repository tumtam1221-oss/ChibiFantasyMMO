using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>Equip, unequip and the stat seam. (STEPS 11-16)</summary>
    internal sealed class EquipmentServiceTests : ItemContainerTestBase
    {
        private ItemContainerState _bag;
        private CharacterEquipmentState _worn;

        private EquipmentService.Context Ctx(int level = 50,
            string characterClass = null, string characterJob = null)
        {
            return new EquipmentService.Context(Items, level,
                characterClass == null ? default : new DefinitionId(characterClass),
                characterJob == null ? default : new DefinitionId(characterJob));
        }

        private void Setup(int capacity = 6)
        {
            _bag = Container(capacity);
            _worn = new CharacterEquipmentState(new CharacterId("c1"));
        }

        // ---------------- equip ----------------

        [Test]
        public void Equipping_a_weapon_moves_it_from_the_bag_into_its_authored_slot()
        {
            Setup();
            var sword = Gear(Sword);
            _bag.Add(sword, Items);

            EquipResult result = EquipmentService.Equip(_bag, _worn, 0, Ctx());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.Slot, Is.EqualTo(EquipmentSlot.MainHand),
                "The slot comes from the definition, not from code.");
            Assert.That(_worn.IsOccupied(EquipmentSlot.MainHand), Is.True);
            Assert.That(_bag.GetSlot(0).IsEmpty, Is.True);
        }

        [Test]
        public void Equipping_armour_uses_its_own_slot()
        {
            Setup();
            _bag.Add(Gear(Helm), Items);

            EquipResult result = EquipmentService.Equip(_bag, _worn, 0, Ctx());

            Assert.That(result.Slot, Is.EqualTo(EquipmentSlot.Head));
            Assert.That(_worn.Count, Is.EqualTo(1));
        }

        [Test]
        public void A_non_equipment_item_is_refused()
        {
            Setup();
            _bag.Add(Stack(Potion, 5), Items);

            EquipResult result = EquipmentService.Equip(_bag, _worn, 0, Ctx());

            Assert.That(result.Reason, Is.EqualTo(EquipRejection.NotEquipment));
            Assert.That(_bag.CountOf(new DefinitionId(Potion)), Is.EqualTo(5));
            Assert.That(_worn.Count, Is.EqualTo(0));
        }

        [Test]
        public void An_empty_or_out_of_range_slot_is_refused()
        {
            Setup();

            Assert.That(EquipmentService.Equip(_bag, _worn, 0, Ctx()).Reason,
                Is.EqualTo(EquipRejection.SourceEmpty));
            Assert.That(EquipmentService.Equip(_bag, _worn, 99, Ctx()).Reason,
                Is.EqualTo(EquipRejection.SlotOutOfRange));
        }

        [Test]
        public void The_authored_level_requirement_is_enforced()
        {
            Setup();
            _bag.Add(Gear(Robe), Items);          // requires level 20

            Assert.That(EquipmentService.Equip(_bag, _worn, 0, Ctx(level: 19)).Reason,
                Is.EqualTo(EquipRejection.LevelTooLow));
            Assert.That(_worn.Count, Is.EqualTo(0));
            Assert.That(_bag.GetSlot(0).IsOccupied, Is.True, "Nothing left the bag.");

            Assert.That(EquipmentService.Equip(_bag, _worn, 0, Ctx(level: 20)).IsAccepted, Is.True);
        }

        [Test]
        public void The_authored_class_restriction_is_enforced()
        {
            Setup();
            _bag.Add(Gear(ClassOnly), Items);     // ClassA only

            Assert.That(EquipmentService.Equip(_bag, _worn, 0, Ctx(characterClass: ClassB)).Reason,
                Is.EqualTo(EquipRejection.ClassNotAllowed));
            Assert.That(_worn.Count, Is.EqualTo(0));

            Assert.That(EquipmentService.Equip(_bag, _worn, 0, Ctx(characterClass: ClassA)).IsAccepted,
                Is.True);
        }

        [Test]
        public void An_empty_allow_list_means_unrestricted()
        {
            Setup();
            _bag.Add(Gear(Sword), Items);

            Assert.That(EquipmentService.Equip(_bag, _worn, 0, Ctx(characterClass: ClassB)).IsAccepted,
                Is.True, "The sword authors no class list, so any class may hold it.");
        }

        [Test]
        public void Equipping_over_an_occupied_slot_returns_the_old_piece_to_the_bag()
        {
            Setup();
            var first = Gear(Sword);
            var second = Gear(Sword);
            _bag.Add(first, Items);
            EquipmentService.Equip(_bag, _worn, 0, Ctx());
            _bag.Add(second, Items);

            EquipResult result = EquipmentService.Equip(_bag, _worn, 0, Ctx());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.EquippedInstance, Is.EqualTo(second.InstanceId));
            Assert.That(result.ReturnedInstance, Is.EqualTo(first.InstanceId));
            Assert.That(_bag.IndexOf(first.InstanceId), Is.GreaterThanOrEqualTo(0),
                "The old sword is back in the bag, not destroyed.");
            Assert.That(_worn.Count, Is.EqualTo(1));
        }

        // ---------------- unequip ----------------

        [Test]
        public void Unequipping_puts_the_piece_back_in_the_bag()
        {
            Setup();
            var sword = Gear(Sword);
            _bag.Add(sword, Items);
            EquipmentService.Equip(_bag, _worn, 0, Ctx());

            EquipResult result = EquipmentService.Unequip(_bag, _worn, EquipmentSlot.MainHand, Ctx());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(_worn.IsOccupied(EquipmentSlot.MainHand), Is.False);
            Assert.That(_bag.IndexOf(sword.InstanceId), Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void Unequipping_an_empty_slot_is_refused()
        {
            Setup();

            Assert.That(EquipmentService.Unequip(_bag, _worn, EquipmentSlot.Head, Ctx()).Reason,
                Is.EqualTo(EquipRejection.SlotEmpty));
        }

        [Test]
        public void Unequipping_into_a_full_bag_is_refused_and_destroys_nothing()
        {
            Setup(capacity: 1);
            var sword = Gear(Sword);
            _bag.Add(sword, Items);
            EquipmentService.Equip(_bag, _worn, 0, Ctx());
            _bag.Add(Stack(Rock, 1), Items);          // fills the only slot

            Revision wornBefore = _worn.Revision;
            EquipResult result = EquipmentService.Unequip(_bag, _worn, EquipmentSlot.MainHand, Ctx());

            Assert.That(result.Reason, Is.EqualTo(EquipRejection.InventoryFull));
            Assert.That(_worn.IsOccupied(EquipmentSlot.MainHand), Is.True, "Still worn.");
            Assert.That(_worn.Revision, Is.EqualTo(wornBefore));
            Assert.That(_bag.IsFull, Is.True);
        }

        // ---------------- STEP 15/16: stats ----------------

        [Test]
        public void Equipped_gear_contributes_its_authored_modifiers()
        {
            Setup();
            _bag.Add(Gear(Sword), Items);          // +10 STR
            _bag.Add(Gear(Helm), Items);           // +5 STR
            EquipmentService.Equip(_bag, _worn, 0, Ctx());   // sword, slot 0
            EquipmentService.Equip(_bag, _worn, 1, Ctx());   // helm, slot 1

            List<StatModifier> modifiers = _worn.CollectModifiers(Items);

            Assert.That(modifiers.Count, Is.EqualTo(2));

            float total = 0f;
            for (int i = 0; i < modifiers.Count; i++) total += modifiers[i].Value;
            Assert.That(total, Is.EqualTo(15f), "10 from the sword, 5 from the helm.");
        }

        [Test]
        public void Nothing_equipped_contributes_nothing()
        {
            Setup();
            Assert.That(_worn.CollectModifiers(Items).Count, Is.EqualTo(0));
        }

        [Test]
        public void Collecting_repeatedly_never_drifts()
        {
            Setup();
            _bag.Add(Gear(Sword), Items);
            EquipmentService.Equip(_bag, _worn, 0, Ctx());

            var reused = new List<StatModifier>();

            for (int i = 0; i < 100; i++)
            {
                _worn.CollectModifiers(Items, reused);

                Assert.That(reused.Count, Is.EqualTo(1),
                    "Recalculating a hundred times must not accumulate bonuses.");
                Assert.That(reused[0].Value, Is.EqualTo(10f));
            }
        }

        [Test]
        public void Unequipping_removes_the_contribution()
        {
            Setup();
            _bag.Add(Gear(Sword), Items);
            EquipmentService.Equip(_bag, _worn, 0, Ctx());
            Assert.That(_worn.CollectModifiers(Items).Count, Is.EqualTo(1));

            EquipmentService.Unequip(_bag, _worn, EquipmentSlot.MainHand, Ctx());

            Assert.That(_worn.CollectModifiers(Items).Count, Is.EqualTo(0));
        }

        [Test]
        public void Equipping_the_same_slot_twice_does_not_double_the_bonus()
        {
            Setup();
            _bag.Add(Gear(Sword), Items);
            _bag.Add(Gear(Sword), Items);

            EquipmentService.Equip(_bag, _worn, 0, Ctx());
            EquipmentService.Equip(_bag, _worn, 0, Ctx());   // swaps, does not stack

            List<StatModifier> modifiers = _worn.CollectModifiers(Items);

            Assert.That(_worn.Count, Is.EqualTo(1), "One slot, one occupant.");
            Assert.That(modifiers.Count, Is.EqualTo(1));
            Assert.That(modifiers[0].Value, Is.EqualTo(10f));
        }

        [Test]
        public void Unequipping_twice_does_not_subtract_twice()
        {
            Setup();
            _bag.Add(Gear(Sword), Items);
            EquipmentService.Equip(_bag, _worn, 0, Ctx());

            Assert.That(EquipmentService.Unequip(_bag, _worn, EquipmentSlot.MainHand, Ctx()).IsAccepted,
                Is.True);
            Assert.That(EquipmentService.Unequip(_bag, _worn, EquipmentSlot.MainHand, Ctx()).Reason,
                Is.EqualTo(EquipRejection.SlotEmpty));
            Assert.That(_worn.CollectModifiers(Items).Count, Is.EqualTo(0));
        }
    }
}
