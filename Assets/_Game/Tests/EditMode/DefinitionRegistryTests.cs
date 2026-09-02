using System;
using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    public sealed class DefinitionRegistryTests
    {
        private sealed class FakeDefinition : IDefinition
        {
            public FakeDefinition(string id)
            {
                Id = new DefinitionId(id);
            }

            public DefinitionId Id { get; }
        }

        private static DefinitionRegistry<FakeDefinition> Build(params string[] ids)
        {
            var items = new List<FakeDefinition>();
            foreach (string id in ids)
            {
                items.Add(new FakeDefinition(id));
            }

            return new DefinitionRegistry<FakeDefinition>(items);
        }

        [Test]
        public void TryGet_ExistingId_ReturnsTrueAndDefinition()
        {
            DefinitionRegistry<FakeDefinition> registry = Build("a", "b");

            bool found = registry.TryGet(new DefinitionId("b"), out FakeDefinition definition);

            Assert.IsTrue(found);
            Assert.IsNotNull(definition);
            Assert.AreEqual(new DefinitionId("b"), definition.Id);
        }

        [Test]
        public void TryGet_MissingId_ReturnsFalse()
        {
            DefinitionRegistry<FakeDefinition> registry = Build("a");

            bool found = registry.TryGet(new DefinitionId("missing"), out FakeDefinition definition);

            Assert.IsFalse(found);
            Assert.IsNull(definition);
        }

        [Test]
        public void Contains_ReflectsMembership()
        {
            DefinitionRegistry<FakeDefinition> registry = Build("a", "b");

            Assert.IsTrue(registry.Contains(new DefinitionId("a")));
            Assert.IsFalse(registry.Contains(new DefinitionId("c")));
            Assert.IsFalse(registry.Contains(DefinitionId.None));
        }

        [Test]
        public void All_ReturnsEveryDefinitionInConstructionOrder()
        {
            DefinitionRegistry<FakeDefinition> registry = Build("first", "second", "third");

            Assert.AreEqual(3, registry.All.Count);
            Assert.AreEqual(new DefinitionId("first"), registry.All[0].Id);
            Assert.AreEqual(new DefinitionId("second"), registry.All[1].Id);
            Assert.AreEqual(new DefinitionId("third"), registry.All[2].Id);
        }

        [Test]
        public void EmptyRegistry_IsValidAndEmpty()
        {
            DefinitionRegistry<FakeDefinition> registry = Build();

            Assert.AreEqual(0, registry.All.Count);
            Assert.IsFalse(registry.Contains(new DefinitionId("a")));
        }

        [Test]
        public void DuplicateId_Throws()
        {
            Assert.Throws<ArgumentException>(() => Build("dup", "dup"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void InvalidId_Throws(string id)
        {
            Assert.Throws<ArgumentException>(() => Build(id));
        }

        [Test]
        public void NullDefinition_Throws()
        {
            var items = new List<FakeDefinition> { null };

            Assert.Throws<ArgumentException>(
                () => new DefinitionRegistry<FakeDefinition>(items));
        }

        [Test]
        public void NullSource_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => new DefinitionRegistry<FakeDefinition>(null));
        }
    }
}
