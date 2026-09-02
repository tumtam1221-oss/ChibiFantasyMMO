using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Proves persisted identity and state survive a serialization round trip, which is
    /// what patch safety and future database mapping depend on.
    /// </summary>
    public sealed class InstanceSerializationTests
    {
        [Test]
        public void ItemInstance_SurvivesRoundTrip()
        {
            var original = new ItemInstance(
                InstanceId.New(), new DefinitionId("item.potion.small"), new OwnerId("character:42"), 9);
            original.SetQuantity(11);

            string json = JsonUtility.ToJson(original);
            ItemInstance restored = JsonUtility.FromJson<ItemInstance>(json);

            Assert.AreEqual(original.InstanceId, restored.InstanceId);
            Assert.AreEqual(original.DefinitionId, restored.DefinitionId);
            Assert.AreEqual(original.Owner, restored.Owner);
            Assert.AreEqual(original.Revision, restored.Revision);
            Assert.AreEqual(11, restored.Quantity);
        }

        [Test]
        public void EquipmentInstance_SurvivesRoundTrip()
        {
            var original = new EquipmentInstance(
                InstanceId.New(), new DefinitionId("equip.sword.iron"), new OwnerId("character:7"), 4);

            string json = JsonUtility.ToJson(original);
            EquipmentInstance restored = JsonUtility.FromJson<EquipmentInstance>(json);

            Assert.AreEqual(original.InstanceId, restored.InstanceId);
            Assert.AreEqual(original.DefinitionId, restored.DefinitionId);
            Assert.AreEqual(original.Owner, restored.Owner);
            Assert.AreEqual(4, restored.EnhancementLevel);
        }

        [Test]
        public void PetInstance_SurvivesRoundTrip()
        {
            var original = new PetInstance(
                InstanceId.New(), new DefinitionId("pet.cat"), new OwnerId("account:3"), 15, 900, 2);

            string json = JsonUtility.ToJson(original);
            PetInstance restored = JsonUtility.FromJson<PetInstance>(json);

            Assert.AreEqual(original.InstanceId, restored.InstanceId);
            Assert.AreEqual(15, restored.Level);
            Assert.AreEqual(900, restored.Experience);
            Assert.AreEqual(2, restored.EvolutionStage);
        }

        [Test]
        public void CardInstance_SurvivesRoundTrip()
        {
            var original = new CardInstance(
                InstanceId.New(), new DefinitionId("card.slime"), new OwnerId("character:1"));

            string json = JsonUtility.ToJson(original);
            CardInstance restored = JsonUtility.FromJson<CardInstance>(json);

            Assert.AreEqual(original.InstanceId, restored.InstanceId);
            Assert.AreEqual(original.DefinitionId, restored.DefinitionId);
            Assert.AreEqual(original.Owner, restored.Owner);
        }

        [Test]
        public void DevilFruitInstance_SurvivesRoundTrip()
        {
            var original = new DevilFruitInstance(
                InstanceId.New(), new DefinitionId("fruit.flame"), new OwnerId("character:1"),
                DevilFruitState.Equipped);

            string json = JsonUtility.ToJson(original);
            DevilFruitInstance restored = JsonUtility.FromJson<DevilFruitInstance>(json);

            Assert.AreEqual(original.InstanceId, restored.InstanceId);
            Assert.AreEqual(DevilFruitState.Equipped, restored.State);
        }

        [Test]
        public void RoundTripDoesNotDeriveIdentityFromRuntimeState()
        {
            var original = new ItemInstance(
                InstanceId.New(), new DefinitionId("item.potion"), new OwnerId("character:5"), 1);

            string json = JsonUtility.ToJson(original);
            ItemInstance first = JsonUtility.FromJson<ItemInstance>(json);
            ItemInstance second = JsonUtility.FromJson<ItemInstance>(json);

            Assert.AreEqual(original.InstanceId, first.InstanceId);
            Assert.AreEqual(first.InstanceId, second.InstanceId);
            Assert.AreNotSame(first, second);
        }
    }
}
