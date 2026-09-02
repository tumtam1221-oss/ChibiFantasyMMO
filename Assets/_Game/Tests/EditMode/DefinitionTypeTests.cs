using System;
using System.Collections.Generic;
using System.Reflection;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Schema-level tests that apply to every concrete definition, including ones added
    /// later, by discovering GameDefinition subclasses through reflection.
    /// </summary>
    public sealed class DefinitionTypeTests
    {
        private static IEnumerable<Type> ConcreteDefinitionTypes()
        {
            Assembly dataAssembly = typeof(GameDefinition).Assembly;
            foreach (Type type in dataAssembly.GetTypes())
            {
                if (!type.IsAbstract && typeof(GameDefinition).IsAssignableFrom(type))
                {
                    yield return type;
                }
            }
        }

        [Test]
        public void EveryConcreteDefinition_CanBeInstantiated()
        {
            int count = 0;
            foreach (Type type in ConcreteDefinitionTypes())
            {
                var instance = ScriptableObject.CreateInstance(type);
                try
                {
                    Assert.IsNotNull(instance, "Failed to instantiate " + type.Name);
                    Assert.IsInstanceOf<GameDefinition>(instance);
                    Assert.IsInstanceOf<IDefinition>(instance);
                    count++;
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }

            Assert.GreaterOrEqual(count, 15, "Expected at least the 15 Step 04.4 definition types.");
        }

        [Test]
        public void EveryConcreteDefinition_HasInvalidIdBeforeAuthoring()
        {
            foreach (Type type in ConcreteDefinitionTypes())
            {
                var instance = (GameDefinition)ScriptableObject.CreateInstance(type);
                try
                {
                    Assert.IsFalse(instance.Id.IsValid, type.Name + " should start with no id.");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }
        }

        [Test]
        public void EveryConcreteDefinition_AcceptsSerializedId()
        {
            foreach (Type type in ConcreteDefinitionTypes())
            {
                var instance = (GameDefinition)ScriptableObject.CreateInstance(type);
                try
                {
                    JsonUtility.FromJsonOverwrite("{\"_id\":{\"_value\":\"authored.id\"}}", instance);

                    Assert.IsTrue(instance.Id.IsValid, type.Name + " did not accept a serialized id.");
                    Assert.AreEqual(new DefinitionId("authored.id"), instance.Id, type.Name);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }
        }

        [Test]
        public void EveryConcreteDefinition_SerializesArrayFieldsAsEmptyNotNull()
        {
            foreach (Type type in ConcreteDefinitionTypes())
            {
                var instance = (GameDefinition)ScriptableObject.CreateInstance(type);
                try
                {
                    foreach (FieldInfo field in type.GetFields(
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                    {
                        if (!field.FieldType.IsArray)
                        {
                            continue;
                        }

                        Assert.IsNotNull(
                            field.GetValue(instance),
                            type.Name + "." + field.Name + " should default to an empty array, not null.");
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }
        }

        [TestCase(typeof(ItemDefinition))]
        [TestCase(typeof(EquipmentDefinition))]
        [TestCase(typeof(SkillDefinition))]
        [TestCase(typeof(MonsterDefinition))]
        [TestCase(typeof(NPCDefinition))]
        [TestCase(typeof(QuestDefinition))]
        [TestCase(typeof(ClassDefinition))]
        [TestCase(typeof(JobDefinition))]
        [TestCase(typeof(MapDefinition))]
        [TestCase(typeof(PetDefinition))]
        [TestCase(typeof(CardDefinition))]
        [TestCase(typeof(DevilFruitDefinition))]
        [TestCase(typeof(StatusEffectDefinition))]
        [TestCase(typeof(EnhancementDefinition))]
        [TestCase(typeof(StatDefinition))]
        [TestCase(typeof(RarityDefinition))]
        public void ExpectedDefinitionType_ExistsAndDerivesFromGameDefinition(Type type)
        {
            Assert.IsTrue(typeof(GameDefinition).IsAssignableFrom(type), type.Name);
            Assert.AreEqual(typeof(GameDefinition).Assembly, type.Assembly,
                type.Name + " must live in the Data assembly.");
        }

        [Test]
        public void EquipmentDefinition_ExtendsItemDefinition()
        {
            Assert.IsTrue(typeof(ItemDefinition).IsAssignableFrom(typeof(EquipmentDefinition)));

            var equipment = ScriptableObject.CreateInstance<EquipmentDefinition>();
            try
            {
                // Inherited item surface is reachable without casting away the equipment type.
                Assert.AreEqual(ItemCategory.Misc, equipment.Category);
                Assert.AreEqual(EquipmentSlot.None, equipment.Slot);
                Assert.IsNotNull(equipment.BaseStatModifiers);
                Assert.IsNotNull(equipment.AllowedClasses);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(equipment);
            }
        }
    }
}
