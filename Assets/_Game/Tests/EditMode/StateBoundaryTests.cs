using System;
using System.Reflection;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    public sealed class StateBoundaryTests
    {
        [Test]
        public void PersistentAndRuntimeStateAreDistinctClassifications()
        {
            var persistent = new TestPersistentState(
                InstanceId.New(), new DefinitionId("item.potion"), new OwnerId("character:1"), 5);
            var runtime = new TestRuntimeState(5);

            Assert.IsInstanceOf<IPersistentState>(persistent);
            Assert.IsNotInstanceOf<IRuntimeState>(persistent);

            Assert.IsInstanceOf<IRuntimeState>(runtime);
            Assert.IsNotInstanceOf<IPersistentState>(runtime);

            Assert.IsInstanceOf<IVersionedState>(persistent);
            Assert.IsInstanceOf<IVersionedState>(runtime);
        }

        [Test]
        public void OwnedInstancesAreClassifiedAsPersistentState()
        {
            var item = new ItemInstance(
                InstanceId.New(), new DefinitionId("item.potion"), new OwnerId("character:1"), 1);

            Assert.IsInstanceOf<IPersistentState>(item);
            Assert.IsTrue(typeof(IPersistentState).IsAssignableFrom(typeof(IGameInstance)));
            Assert.IsTrue(typeof(IPersistentState).IsAssignableFrom(typeof(EquipmentInstance)));
            Assert.IsTrue(typeof(IPersistentState).IsAssignableFrom(typeof(PetInstance)));
            Assert.IsTrue(typeof(IPersistentState).IsAssignableFrom(typeof(CardInstance)));
            Assert.IsTrue(typeof(IPersistentState).IsAssignableFrom(typeof(DevilFruitInstance)));
        }

        [Test]
        public void PersistentStateSurvivesSerializationWithIdentityAndRevisionIntact()
        {
            var original = new TestPersistentState(
                InstanceId.New(), new DefinitionId("item.potion"), new OwnerId("character:9"), 1);
            original.SetValue(77);

            string json = JsonUtility.ToJson(original);
            TestPersistentState restored = JsonUtility.FromJson<TestPersistentState>(json);

            Assert.AreEqual(original.InstanceId, restored.InstanceId);
            Assert.AreEqual(original.DefinitionId, restored.DefinitionId);
            Assert.AreEqual(original.Owner, restored.Owner);
            Assert.AreEqual(original.Revision, restored.Revision, "Serialization must not alter the revision.");
            Assert.AreEqual(77, restored.Value);
        }

        [Test]
        public void IdentityStaysStableWhileStateMutates()
        {
            var state = new TestPersistentState(
                InstanceId.New(), new DefinitionId("item.potion"), new OwnerId("character:3"), 0);

            InstanceId instanceId = state.InstanceId;
            DefinitionId definitionId = state.DefinitionId;
            OwnerId owner = state.Owner;

            state.SetValue(1);
            state.SetValue(2);
            state.SetValue(3);

            Assert.AreEqual(instanceId, state.InstanceId);
            Assert.AreEqual(definitionId, state.DefinitionId);
            Assert.AreEqual(owner, state.Owner);
            Assert.AreEqual(3, state.Revision.Value);
        }

        [Test]
        public void ImmutableStateCannotBeChangedOutsideTheBoundary()
        {
            Type type = typeof(ImmutableTestState);

            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.IsFalse(property.CanWrite, property.Name + " must not be settable.");
            }

            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.IsTrue(field.IsInitOnly, field.Name + " must not be publicly writable.");
            }
        }

        [Test]
        public void StateContainerIsNotAUnityObjectAndCarriesNoForbiddenDependency()
        {
            Assembly core = typeof(StateContainer<ImmutableTestState>).Assembly;

            Assert.AreEqual("ChibiFantasy.Core", core.GetName().Name);

            foreach (AssemblyName referenced in core.GetReferencedAssemblies())
            {
                Assert.IsFalse(referenced.Name.StartsWith("FishNet", StringComparison.Ordinal),
                    "Core must not reference FishNet.");
                Assert.IsFalse(referenced.Name.StartsWith("UnityEditor", StringComparison.Ordinal),
                    "Core must not reference UnityEditor.");
                Assert.IsFalse(referenced.Name.StartsWith("ChibiFantasy.", StringComparison.Ordinal),
                    "Core must stay free of project dependencies, found " + referenced.Name);
            }
        }

        [Test]
        public void RuntimeStateIsNotRequiredToBeSerializable()
        {
            // The classification is the point: runtime state carries no persistence
            // obligation, so it need not be marked serializable at all.
            Assert.IsNull(typeof(TestRuntimeState).GetCustomAttribute<SerializableAttribute>(false),
                "Runtime state should not need a serialization contract.");
            Assert.IsNotNull(typeof(TestPersistentState).GetCustomAttribute<SerializableAttribute>(false),
                "Persistent state must remain serializable.");
        }

        [Test]
        public void NoSecondRevisionTypeWasIntroduced()
        {
            Assembly core = typeof(Revision).Assembly;
            int revisionLikeTypes = 0;

            foreach (Type type in core.GetTypes())
            {
                if (!type.IsPublic)
                {
                    continue;
                }

                string name = type.Name;
                if (name == "Revision" || name == "StateVersion" || name == "DataVersion" || name == "Version")
                {
                    revisionLikeTypes++;
                }
            }

            Assert.AreEqual(1, revisionLikeTypes, "Revision must not have a competing sibling.");
        }
    }
}
