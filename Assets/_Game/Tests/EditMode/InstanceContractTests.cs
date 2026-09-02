using System;
using System.Collections.Generic;
using System.Reflection;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    public sealed class InstanceContractTests
    {
        private static readonly InstanceId SampleInstance = new InstanceId("inst-1");
        private static readonly DefinitionId SampleDefinition = new DefinitionId("item.potion.small");
        private static readonly OwnerId SampleOwner = new OwnerId("character:42");

        private static IEnumerable<Type> ConcreteInstanceTypes()
        {
            Assembly data = typeof(GameInstance).Assembly;
            foreach (Type type in data.GetTypes())
            {
                if (!type.IsAbstract && typeof(GameInstance).IsAssignableFrom(type))
                {
                    yield return type;
                }
            }
        }

        [TestCase(typeof(ItemInstance))]
        [TestCase(typeof(EquipmentInstance))]
        [TestCase(typeof(PetInstance))]
        [TestCase(typeof(CardInstance))]
        [TestCase(typeof(DevilFruitInstance))]
        public void ExpectedInstanceType_DerivesFromGameInstance(Type type)
        {
            Assert.IsTrue(typeof(GameInstance).IsAssignableFrom(type), type.Name);
            Assert.IsTrue(typeof(IGameInstance).IsAssignableFrom(type), type.Name);
            Assert.AreEqual(typeof(GameInstance).Assembly, type.Assembly, type.Name);
        }

        [Test]
        public void NoInstanceIsAUnityObjectOrNetworkBehaviour()
        {
            int checkedTypes = 0;
            foreach (Type type in ConcreteInstanceTypes())
            {
                Assert.IsFalse(typeof(UnityEngine.Object).IsAssignableFrom(type),
                    type.Name + " must not derive from UnityEngine.Object.");

                for (Type current = type; current != null; current = current.BaseType)
                {
                    Assert.AreNotEqual("MonoBehaviour", current.Name, type.Name);
                    Assert.AreNotEqual("NetworkBehaviour", current.Name, type.Name);
                    Assert.AreNotEqual("ScriptableObject", current.Name, type.Name);

                    bool fishNetType = current.Namespace != null
                        && current.Namespace.StartsWith("FishNet", StringComparison.Ordinal);
                    Assert.IsFalse(fishNetType, type.Name + " must not inherit a FishNet type.");
                }

                checkedTypes++;
            }

            Assert.GreaterOrEqual(checkedTypes, 5);
        }

        [Test]
        public void Instance_CarriesIdentityAndStartsAtInitialRevision()
        {
            var item = new ItemInstance(SampleInstance, SampleDefinition, SampleOwner, 3);

            Assert.AreEqual(SampleInstance, item.InstanceId);
            Assert.AreEqual(SampleDefinition, item.DefinitionId);
            Assert.AreEqual(SampleOwner, item.Owner);
            Assert.AreEqual(Revision.Initial, item.Revision);
        }

        [Test]
        public void Instance_RequiresValidIdentityAndDefinition()
        {
            Assert.Throws<ArgumentException>(
                () => new ItemInstance(InstanceId.None, SampleDefinition, SampleOwner, 1));
            Assert.Throws<ArgumentException>(
                () => new ItemInstance(SampleInstance, DefinitionId.None, SampleOwner, 1));
        }

        [Test]
        public void Instance_AllowsNoOwnerForUnassignedState()
        {
            var item = new ItemInstance(SampleInstance, SampleDefinition, OwnerId.None, 1);

            Assert.IsFalse(item.Owner.IsValid);
        }

        [Test]
        public void SetOwner_ReassignsAndAdvancesRevision()
        {
            var item = new ItemInstance(SampleInstance, SampleDefinition, SampleOwner, 1);
            Revision before = item.Revision;

            item.SetOwner(new OwnerId("character:99"));

            Assert.AreEqual(new OwnerId("character:99"), item.Owner);
            Assert.IsTrue(item.Revision.IsNewerThan(before));
        }

        [Test]
        public void DefinitionIsReferencedByIdNotByObject()
        {
            FieldInfo definitionField = typeof(GameInstance).GetField(
                "_definitionId", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(definitionField);
            Assert.AreEqual(typeof(DefinitionId), definitionField.FieldType);
        }
    }
}
