using System;
using System.Collections.Generic;
using ChibiFantasy.Core;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    public sealed class InstanceIdentityTests
    {
        [Test]
        public void InstanceId_SameValue_AreEqual()
        {
            var a = new InstanceId("abc123");
            var b = new InstanceId("abc123");

            Assert.AreEqual(a, b);
            Assert.IsTrue(a == b);
            Assert.IsFalse(a != b);
        }

        [Test]
        public void InstanceId_DifferentValue_AreNotEqual()
        {
            Assert.AreNotEqual(new InstanceId("abc"), new InstanceId("def"));
            Assert.IsTrue(new InstanceId("abc") != new InstanceId("def"));
        }

        [Test]
        public void InstanceId_NoneIsInvalid()
        {
            Assert.IsFalse(InstanceId.None.IsValid);
            Assert.IsFalse(new InstanceId(null).IsValid);
            Assert.IsFalse(new InstanceId("").IsValid);
            Assert.IsFalse(new InstanceId("   ").IsValid);
            Assert.AreEqual(string.Empty, InstanceId.None.ToString());
        }

        [Test]
        public void InstanceId_New_ProducesValidUniqueIds()
        {
            var first = InstanceId.New();
            var second = InstanceId.New();

            Assert.IsTrue(first.IsValid);
            Assert.IsTrue(second.IsValid);
            Assert.AreNotEqual(first, second);
            Assert.AreEqual(32, first.Value.Length, "Expected a 32 character GUID in N format.");
        }

        [Test]
        public void InstanceId_HashIsDeterministic()
        {
            Assert.AreEqual(new InstanceId("stable").GetHashCode(), new InstanceId("stable").GetHashCode());
            // FNV-1a 32-bit of "a", pinned so the hash cannot silently become runtime-randomised.
            Assert.AreEqual(unchecked((int)0xE40C292C), new InstanceId("a").GetHashCode());
        }

        [Test]
        public void InstanceId_WorksAsDictionaryKey()
        {
            var id = InstanceId.New();
            var map = new Dictionary<InstanceId, string> { { id, "held" } };

            Assert.IsTrue(map.ContainsKey(new InstanceId(id.Value)));
            Assert.AreEqual("held", map[id]);
            Assert.IsFalse(map.ContainsKey(InstanceId.New()));
        }

        [Test]
        public void InstanceId_SurvivesSerializationRoundTrip()
        {
            var original = new IdentityHolder
            {
                Instance = InstanceId.New(),
                Owner = new OwnerId("account:4711"),
                Definition = new DefinitionId("item.potion"),
                Revision = new Revision(7)
            };

            string json = JsonUtility.ToJson(original);
            IdentityHolder restored = JsonUtility.FromJson<IdentityHolder>(json);

            Assert.AreEqual(original.Instance, restored.Instance);
            Assert.AreEqual(original.Owner, restored.Owner);
            Assert.AreEqual(original.Definition, restored.Definition);
            Assert.AreEqual(original.Revision, restored.Revision);
        }

        [Test]
        public void InstanceId_IsDistinctTypeFromDefinitionIdAndOwnerId()
        {
            Assert.AreNotEqual(typeof(InstanceId), typeof(DefinitionId));
            Assert.AreNotEqual(typeof(InstanceId), typeof(OwnerId));
            Assert.AreNotEqual(typeof(OwnerId), typeof(DefinitionId));
        }

        [Test]
        public void OwnerId_EqualityValidityAndHashing()
        {
            Assert.AreEqual(new OwnerId("char:9"), new OwnerId("char:9"));
            Assert.AreNotEqual(new OwnerId("char:9"), new OwnerId("char:10"));
            Assert.IsFalse(OwnerId.None.IsValid);
            Assert.IsFalse(new OwnerId("  ").IsValid);
            Assert.IsTrue(new OwnerId("guild:7").IsValid);
            Assert.AreEqual(new OwnerId("guild:7").GetHashCode(), new OwnerId("guild:7").GetHashCode());
        }

        [Test]
        public void OwnerId_WorksAsDictionaryKey()
        {
            var map = new Dictionary<OwnerId, int> { { new OwnerId("account:1"), 3 } };

            Assert.IsTrue(map.ContainsKey(new OwnerId("account:1")));
            Assert.IsFalse(map.ContainsKey(new OwnerId("account:2")));
        }

        [Test]
        public void Revision_StartsAtInitialAndAdvances()
        {
            Revision initial = Revision.Initial;

            Assert.AreEqual(0, initial.Value);

            Revision next = initial.Next();

            Assert.AreEqual(1, next.Value);
            Assert.AreEqual(0, initial.Value, "Next must not mutate the original.");
            Assert.IsTrue(next.IsNewerThan(initial));
            Assert.IsFalse(initial.IsNewerThan(next));
        }

        [Test]
        public void Revision_EqualityAndComparison()
        {
            Assert.AreEqual(new Revision(4), new Revision(4));
            Assert.IsTrue(new Revision(4) == new Revision(4));
            Assert.IsTrue(new Revision(4) != new Revision(5));
            Assert.Less(new Revision(4).CompareTo(new Revision(5)), 0);
            Assert.Greater(new Revision(6).CompareTo(new Revision(5)), 0);
            Assert.AreEqual(0, new Revision(5).CompareTo(new Revision(5)));
        }
    }

    [Serializable]
    internal sealed class IdentityHolder
    {
        public InstanceId Instance;
        public OwnerId Owner;
        public DefinitionId Definition;
        public Revision Revision;
    }
}
