using ChibiFantasy.Core;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    public sealed class GameDefinitionTests
    {
        [Test]
        public void NewDefinition_HasInvalidIdUntilAuthored()
        {
            var definition = ScriptableObject.CreateInstance<TestGameDefinition>();
            try
            {
                Assert.IsFalse(definition.Id.IsValid);
            }
            finally
            {
                Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void SerializedId_IsExposedThroughIdProperty()
        {
            var definition = ScriptableObject.CreateInstance<TestGameDefinition>();
            try
            {
                JsonUtility.FromJsonOverwrite("{\"_id\":{\"_value\":\"item.sword.basic\"}}", definition);

                Assert.IsTrue(definition.Id.IsValid);
                Assert.AreEqual(new DefinitionId("item.sword.basic"), definition.Id);
            }
            finally
            {
                Object.DestroyImmediate(definition);
            }
        }
    }
}
