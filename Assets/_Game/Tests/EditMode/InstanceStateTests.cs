using System;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    public sealed class InstanceStateTests
    {
        private static readonly InstanceId SampleInstance = new InstanceId("inst-1");
        private static readonly OwnerId SampleOwner = new OwnerId("character:42");

        [Test]
        public void ItemInstance_QuantityIsValidated()
        {
            var definition = new DefinitionId("item.potion");

            Assert.Throws<ArgumentOutOfRangeException>(
                () => new ItemInstance(SampleInstance, definition, SampleOwner, 0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new ItemInstance(SampleInstance, definition, SampleOwner, -5));

            var item = new ItemInstance(SampleInstance, definition, SampleOwner, 1);
            Assert.Throws<ArgumentOutOfRangeException>(() => item.SetQuantity(0));
            Assert.AreEqual(1, item.Quantity, "A rejected change must leave state untouched.");
        }

        [Test]
        public void ItemInstance_SetQuantityAdvancesRevision()
        {
            var item = new ItemInstance(SampleInstance, new DefinitionId("item.potion"), SampleOwner, 1);
            Revision before = item.Revision;

            item.SetQuantity(20);

            Assert.AreEqual(20, item.Quantity);
            Assert.IsTrue(item.Revision.IsNewerThan(before));
        }

        [Test]
        public void EquipmentInstance_EnhancementLevelIsInstanceState()
        {
            var equipment = new EquipmentInstance(
                SampleInstance, new DefinitionId("equip.sword"), SampleOwner);

            Assert.AreEqual(0, equipment.EnhancementLevel);

            Revision before = equipment.Revision;
            equipment.SetEnhancementLevel(7);

            Assert.AreEqual(7, equipment.EnhancementLevel);
            Assert.IsTrue(equipment.Revision.IsNewerThan(before));

            Assert.Throws<ArgumentOutOfRangeException>(() => equipment.SetEnhancementLevel(-1));
            Assert.AreEqual(7, equipment.EnhancementLevel);
        }

        [Test]
        public void EquipmentInstance_DoesNotInheritItemQuantity()
        {
            Assert.IsFalse(typeof(ItemInstance).IsAssignableFrom(typeof(EquipmentInstance)));
            Assert.IsTrue(typeof(GameInstance).IsAssignableFrom(typeof(EquipmentInstance)));
        }

        [Test]
        public void PetInstance_TracksLevelExperienceAndEvolution()
        {
            var pet = new PetInstance(SampleInstance, new DefinitionId("pet.cat"), SampleOwner);

            Assert.AreEqual(1, pet.Level);
            Assert.AreEqual(0, pet.Experience);
            Assert.AreEqual(0, pet.EvolutionStage);

            pet.SetLevel(12);
            pet.SetExperience(3400);
            pet.SetEvolutionStage(2);

            Assert.AreEqual(12, pet.Level);
            Assert.AreEqual(3400, pet.Experience);
            Assert.AreEqual(2, pet.EvolutionStage);
            Assert.AreEqual(3, pet.Revision.Value);
        }

        [Test]
        public void PetInstance_ValidatesBounds()
        {
            var pet = new PetInstance(SampleInstance, new DefinitionId("pet.cat"), SampleOwner);

            Assert.Throws<ArgumentOutOfRangeException>(() => pet.SetLevel(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => pet.SetExperience(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => pet.SetEvolutionStage(-1));
        }

        [Test]
        public void CardInstance_CarriesIdentityOnly()
        {
            var card = new CardInstance(SampleInstance, new DefinitionId("card.slime"), SampleOwner);

            Assert.AreEqual(SampleInstance, card.InstanceId);
            Assert.AreEqual(new DefinitionId("card.slime"), card.DefinitionId);
            Assert.AreEqual(SampleOwner, card.Owner);
            Assert.AreEqual(Revision.Initial, card.Revision);
        }

        [Test]
        public void DevilFruitInstance_DefaultsToOwnedAndTracksState()
        {
            var fruit = new DevilFruitInstance(
                SampleInstance, new DefinitionId("fruit.flame"), SampleOwner);

            Assert.AreEqual(DevilFruitState.Owned, fruit.State);

            Revision before = fruit.Revision;
            fruit.SetState(DevilFruitState.Consumed);

            Assert.AreEqual(DevilFruitState.Consumed, fruit.State);
            Assert.IsTrue(fruit.Revision.IsNewerThan(before));
        }
    }
}
