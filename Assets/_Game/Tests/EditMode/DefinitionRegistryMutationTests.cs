using System;
using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    public sealed class DefinitionRegistryMutationTests
    {
        private sealed class Fake : IDefinition
        {
            public Fake(string id)
            {
                Id = new DefinitionId(id);
            }

            public DefinitionId Id { get; }
        }

        private sealed class OtherFake : IDefinition
        {
            public OtherFake(string id)
            {
                Id = new DefinitionId(id);
            }

            public DefinitionId Id { get; }
        }

        [Test]
        public void EmptyRegistry_StartsEmpty()
        {
            var registry = new DefinitionRegistry<Fake>();

            Assert.AreEqual(0, registry.Count);
            Assert.AreEqual(0, registry.All.Count);
            Assert.IsFalse(registry.Contains(new DefinitionId("a")));
        }

        [Test]
        public void Register_AddsAndIsRetrievable()
        {
            var registry = new DefinitionRegistry<Fake>();
            var potion = new Fake("item.potion");

            registry.Register(potion);

            Assert.AreEqual(1, registry.Count);
            Assert.IsTrue(registry.Contains(new DefinitionId("item.potion")));
            Assert.IsTrue(registry.TryGet(new DefinitionId("item.potion"), out Fake found));
            Assert.AreSame(potion, found);
        }

        [Test]
        public void TryGet_MissingIdReturnsFalse()
        {
            var registry = new DefinitionRegistry<Fake>();
            registry.Register(new Fake("a"));

            Assert.IsFalse(registry.TryGet(new DefinitionId("b"), out Fake found));
            Assert.IsNull(found);
            Assert.IsFalse(registry.Contains(new DefinitionId("b")));
        }

        [Test]
        public void Register_RejectsDuplicateAndDoesNotOverwrite()
        {
            var registry = new DefinitionRegistry<Fake>();
            var first = new Fake("dup");
            var second = new Fake("dup");

            registry.Register(first);

            Assert.Throws<ArgumentException>(() => registry.Register(second));
            Assert.AreEqual(1, registry.Count);
            Assert.IsTrue(registry.TryGet(new DefinitionId("dup"), out Fake found));
            Assert.AreSame(first, found, "The original entry must survive a rejected duplicate.");
        }

        [Test]
        public void Register_RejectsNullAndInvalidId()
        {
            var registry = new DefinitionRegistry<Fake>();

            Assert.Throws<ArgumentException>(() => registry.Register(null));
            Assert.Throws<ArgumentException>(() => registry.Register(new Fake(null)));
            Assert.Throws<ArgumentException>(() => registry.Register(new Fake("   ")));
            Assert.AreEqual(0, registry.Count);
        }

        [Test]
        public void TryRegister_ReportsInsteadOfThrowing()
        {
            var registry = new DefinitionRegistry<Fake>();
            var first = new Fake("dup");

            Assert.IsTrue(registry.TryRegister(first));
            Assert.IsFalse(registry.TryRegister(new Fake("dup")));
            Assert.IsFalse(registry.TryRegister(null));
            Assert.IsFalse(registry.TryRegister(new Fake("")));

            Assert.AreEqual(1, registry.Count);
            registry.TryGet(new DefinitionId("dup"), out Fake found);
            Assert.AreSame(first, found);
        }

        [Test]
        public void Clear_EmptiesAndLeavesRegistryReusable()
        {
            var registry = new DefinitionRegistry<Fake>();
            registry.Register(new Fake("a"));
            registry.Register(new Fake("b"));

            registry.Clear();

            Assert.AreEqual(0, registry.Count);
            Assert.IsFalse(registry.Contains(new DefinitionId("a")));

            registry.Register(new Fake("a"));
            Assert.AreEqual(1, registry.Count);
        }

        [Test]
        public void All_IsReadOnlyAndInInsertionOrder()
        {
            var registry = new DefinitionRegistry<Fake>();
            registry.Register(new Fake("first"));
            registry.Register(new Fake("second"));
            registry.Register(new Fake("third"));

            IReadOnlyList<Fake> all = registry.All;

            Assert.AreEqual(3, all.Count);
            Assert.AreEqual(new DefinitionId("first"), all[0].Id);
            Assert.AreEqual(new DefinitionId("second"), all[1].Id);
            Assert.AreEqual(new DefinitionId("third"), all[2].Id);
            Assert.IsFalse(all is List<Fake>,
                "All must not hand out the backing list, which a caller could cast and mutate.");
        }

        [Test]
        public void IdentityScopeIsPerRegistryNotGlobal()
        {
            // The architecture scopes uniqueness to one registry: the caller picks the
            // scope by choosing T. Two differently typed registries may reuse an id.
            var items = new DefinitionRegistry<Fake>();
            var skills = new DefinitionRegistry<OtherFake>();

            items.Register(new Fake("fireball"));
            skills.Register(new OtherFake("fireball"));

            Assert.IsTrue(items.Contains(new DefinitionId("fireball")));
            Assert.IsTrue(skills.Contains(new DefinitionId("fireball")));
            Assert.AreEqual(1, items.Count);
            Assert.AreEqual(1, skills.Count);
        }

        [Test]
        public void ConstructorPathStillRejectsFaultySets()
        {
            Assert.Throws<ArgumentNullException>(() => new DefinitionRegistry<Fake>(null));
            Assert.Throws<ArgumentException>(
                () => new DefinitionRegistry<Fake>(new[] { new Fake("x"), new Fake("x") }));
        }
    }
}
