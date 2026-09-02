using System;
using System.Collections.Generic;
using ChibiFantasy.Core;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    public sealed class DefinitionIdTests
    {
        [Test]
        public void SameValue_AreEqual()
        {
            var a = new DefinitionId("item.potion.small");
            var b = new DefinitionId("item.potion.small");

            Assert.IsTrue(a.Equals(b));
            Assert.IsTrue(a == b);
            Assert.IsFalse(a != b);
            Assert.AreEqual(a, b);
        }

        [Test]
        public void DifferentValue_AreNotEqual()
        {
            var a = new DefinitionId("item.potion.small");
            var b = new DefinitionId("item.potion.large");

            Assert.IsFalse(a == b);
            Assert.IsTrue(a != b);
        }

        [Test]
        public void Equality_IsCaseSensitive()
        {
            Assert.AreNotEqual(new DefinitionId("Item.Potion"), new DefinitionId("item.potion"));
        }

        [Test]
        public void None_IsNotValid()
        {
            Assert.IsFalse(DefinitionId.None.IsValid);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void NullEmptyOrWhitespace_IsNotValid(string value)
        {
            Assert.IsFalse(new DefinitionId(value).IsValid);
        }

        [Test]
        public void NonEmptyValue_IsValid()
        {
            Assert.IsTrue(new DefinitionId("skill.fireball").IsValid);
        }

        [Test]
        public void SameValue_ProducesSameHashCode()
        {
            Assert.AreEqual(
                new DefinitionId("monster.slime").GetHashCode(),
                new DefinitionId("monster.slime").GetHashCode());
        }

        [Test]
        public void HashCode_IsDeterministicKnownValue()
        {
            // FNV-1a 32-bit of "a" is 0xE40C292C. Pinning it proves the hash does not
            // depend on the runtime's randomised string hashing.
            Assert.AreEqual(unchecked((int)0xE40C292C), new DefinitionId("a").GetHashCode());
        }

        [Test]
        public void WorksAsDictionaryKey()
        {
            var map = new Dictionary<DefinitionId, string>
            {
                { new DefinitionId("quest.intro"), "intro" }
            };

            Assert.IsTrue(map.ContainsKey(new DefinitionId("quest.intro")));
            Assert.AreEqual("intro", map[new DefinitionId("quest.intro")]);
            Assert.IsFalse(map.ContainsKey(new DefinitionId("quest.other")));
        }

        [Test]
        public void ToString_ReturnsValue()
        {
            Assert.AreEqual("map.town.start", new DefinitionId("map.town.start").ToString());
            Assert.AreEqual(string.Empty, DefinitionId.None.ToString());
        }

        [Test]
        public void SurvivesUnitySerializationRoundTrip()
        {
            var original = new DefinitionIdHolder { Id = new DefinitionId("pet.cat") };

            string json = JsonUtility.ToJson(original);
            DefinitionIdHolder restored = JsonUtility.FromJson<DefinitionIdHolder>(json);

            Assert.AreEqual(original.Id, restored.Id);
            Assert.AreEqual("pet.cat", restored.Id.Value);
        }
    }

    [Serializable]
    internal sealed class DefinitionIdHolder
    {
        public DefinitionId Id;
    }
}
