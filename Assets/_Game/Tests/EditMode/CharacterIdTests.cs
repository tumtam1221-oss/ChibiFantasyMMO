using System;
using System.Collections.Generic;
using ChibiFantasy.Core;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    public sealed class CharacterIdTests
    {
        [Test]
        public void New_ProducesValidUniqueIds()
        {
            CharacterId first = CharacterId.New();
            CharacterId second = CharacterId.New();

            Assert.IsTrue(first.IsValid);
            Assert.AreNotEqual(first, second);
            Assert.AreEqual(32, first.Value.Length, "Expected a 32 character GUID in N form.");
        }

        [Test]
        public void NoneAndBlankAreInvalid()
        {
            Assert.IsFalse(CharacterId.None.IsValid);
            Assert.IsFalse(new CharacterId(null).IsValid);
            Assert.IsFalse(new CharacterId("").IsValid);
            Assert.IsFalse(new CharacterId("   ").IsValid);
            Assert.AreEqual(string.Empty, CharacterId.None.ToString());
        }

        [Test]
        public void Equality()
        {
            Assert.AreEqual(new CharacterId("abc"), new CharacterId("abc"));
            Assert.IsTrue(new CharacterId("abc") == new CharacterId("abc"));
            Assert.IsTrue(new CharacterId("abc") != new CharacterId("def"));
            Assert.AreNotEqual(new CharacterId("Abc"), new CharacterId("abc"));
        }

        [Test]
        public void HashIsDeterministic()
        {
            Assert.AreEqual(new CharacterId("x").GetHashCode(), new CharacterId("x").GetHashCode());
            // Same pinned FNV-1a value as the other identity types, proving one shared implementation.
            Assert.AreEqual(unchecked((int)0xE40C292C), new CharacterId("a").GetHashCode());
        }

        [Test]
        public void AllIdentityTypesShareTheSameDeterministicHash()
        {
            int expected = unchecked((int)0xE40C292C);

            Assert.AreEqual(expected, new CharacterId("a").GetHashCode());
            Assert.AreEqual(expected, new InstanceId("a").GetHashCode());
            Assert.AreEqual(expected, new OwnerId("a").GetHashCode());
            Assert.AreEqual(expected, new DefinitionId("a").GetHashCode());
        }

        [Test]
        public void WorksAsDictionaryKey()
        {
            CharacterId id = CharacterId.New();
            var map = new Dictionary<CharacterId, string> { { id, "hero" } };

            Assert.IsTrue(map.ContainsKey(new CharacterId(id.Value)));
            Assert.AreEqual("hero", map[id]);
            Assert.IsFalse(map.ContainsKey(CharacterId.New()));
        }

        [Test]
        public void SurvivesSerializationRoundTrip()
        {
            var holder = new CharacterIdHolder { Id = CharacterId.New() };

            string json = JsonUtility.ToJson(holder);
            CharacterIdHolder restored = JsonUtility.FromJson<CharacterIdHolder>(json);

            Assert.AreEqual(holder.Id, restored.Id);
            Assert.IsTrue(restored.Id.IsValid);
        }

        [Test]
        public void IsADistinctTypeFromTheOtherIdentities()
        {
            Assert.AreNotEqual(typeof(CharacterId), typeof(InstanceId));
            Assert.AreNotEqual(typeof(CharacterId), typeof(OwnerId));
            Assert.AreNotEqual(typeof(CharacterId), typeof(DefinitionId));
        }
    }

    [Serializable]
    internal sealed class CharacterIdHolder
    {
        public CharacterId Id;
    }
}
