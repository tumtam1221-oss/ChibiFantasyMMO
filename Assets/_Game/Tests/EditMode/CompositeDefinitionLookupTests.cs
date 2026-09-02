using System;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    internal sealed class CompositeDefinitionLookupTests
    {
        private DefinitionRegistry<StatDefinition> _stats;
        private DefinitionRegistry<StatusEffectDefinition> _statusEffects;
        private StatDefinition _stat;
        private StatusEffectDefinition _status;

        [SetUp]
        public void SetUp()
        {
            _stats = new DefinitionRegistry<StatDefinition>();
            _statusEffects = new DefinitionRegistry<StatusEffectDefinition>();

            _stat = ScriptableObject.CreateInstance<StatDefinition>();
            JsonUtility.FromJsonOverwrite("{\"_id\":{\"_value\":\"stat.str\"}}", _stat);
            _stats.Register(_stat);

            _status = ScriptableObject.CreateInstance<StatusEffectDefinition>();
            JsonUtility.FromJsonOverwrite("{\"_id\":{\"_value\":\"status.burning\"}}", _status);
            _statusEffects.Register(_status);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_stat);
            UnityEngine.Object.DestroyImmediate(_status);
        }

        [Test]
        public void ResolvesAcrossEveryRegistry()
        {
            var lookup = new CompositeDefinitionLookup(_stats, _statusEffects);

            Assert.AreEqual(2, lookup.Count);
            Assert.IsTrue(lookup.Contains(new DefinitionId("stat.str")));
            Assert.IsTrue(lookup.Contains(new DefinitionId("status.burning")));
            Assert.IsFalse(lookup.Contains(new DefinitionId("nothing.here")));
        }

        [Test]
        public void IsItselfADefinitionLookup()
        {
            IDefinitionLookup lookup = new CompositeDefinitionLookup(_stats);

            Assert.IsInstanceOf<IDefinitionLookup>(lookup);
            Assert.IsTrue(lookup.Contains(new DefinitionId("stat.str")));
        }

        [Test]
        public void AnEmptyCompositeResolvesNothingRatherThanFailing()
        {
            var lookup = new CompositeDefinitionLookup();

            Assert.AreEqual(0, lookup.Count);
            Assert.IsFalse(lookup.Contains(new DefinitionId("stat.str")));
        }

        [Test]
        public void NullEntriesAreSkippedNotThrown()
        {
            var lookup = new CompositeDefinitionLookup(_stats, null, _statusEffects);

            Assert.AreEqual(2, lookup.Count);
            Assert.IsTrue(lookup.Contains(new DefinitionId("stat.str")));
        }

        [Test]
        public void NullArrayIsRejected()
        {
            Assert.Throws<ArgumentNullException>(
                () => new CompositeDefinitionLookup((IDefinitionLookup[])null));
        }

        [Test]
        public void ItReadsThroughToLaterRegistrations()
        {
            var lookup = new CompositeDefinitionLookup(_stats);

            Assert.IsFalse(lookup.Contains(new DefinitionId("stat.agi")));

            var added = ScriptableObject.CreateInstance<StatDefinition>();
            try
            {
                JsonUtility.FromJsonOverwrite("{\"_id\":{\"_value\":\"stat.agi\"}}", added);
                _stats.Register(added);

                Assert.IsTrue(lookup.Contains(new DefinitionId("stat.agi")),
                    "The composite holds registries, not a copy of their contents.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(added);
            }
        }
    }
}
