using System;
using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    internal sealed class CharacterStatsStateTests : StatsTestBase
    {
        [Test]
        public void NewStatsAreEmptyAndPersistent()
        {
            CharacterStatsState stats = NewStats();

            Assert.IsInstanceOf<IPersistentState>(stats);
            Assert.IsInstanceOf<IVersionedState>(stats);
            Assert.IsNotInstanceOf<IRuntimeState>(stats);
            Assert.AreEqual(0, stats.Count);
            Assert.AreEqual(0, stats.Stats.Count);
            Assert.AreEqual(Revision.Initial, stats.Revision);
            Assert.IsTrue(stats.CharacterId.IsValid);
        }

        [Test]
        public void RequiresACharacter()
        {
            Assert.Throws<ArgumentException>(() => new CharacterStatsState(CharacterId.None));
        }

        [Test]
        public void EmptyLookupIsDeterministic()
        {
            CharacterStatsState stats = NewStats();

            Assert.IsFalse(stats.Contains(new DefinitionId("stat.str")));
            Assert.IsFalse(stats.TryGet(new DefinitionId("stat.str"), out int value));
            Assert.AreEqual(0, value);
            Assert.AreEqual(7, stats.GetOrDefault(new DefinitionId("stat.str"), 7));
        }

        [Test]
        public void SetsAndReadsAStat()
        {
            CharacterStatsState stats = NewStats();

            stats.Set(new DefinitionId("stat.str"), 12);

            Assert.IsTrue(stats.Contains(new DefinitionId("stat.str")));
            Assert.IsTrue(stats.TryGet(new DefinitionId("stat.str"), out int value));
            Assert.AreEqual(12, value);
            Assert.AreEqual(1, stats.Count);
        }

        [Test]
        public void AllSixCoreAttributesCoexist()
        {
            CharacterStatsState stats = NewStats();

            for (int i = 0; i < CoreStatIds.Length; i++)
            {
                stats.Set(new DefinitionId(CoreStatIds[i]), 10 + i);
            }

            Assert.AreEqual(6, stats.Count);

            for (int i = 0; i < CoreStatIds.Length; i++)
            {
                Assert.IsTrue(stats.TryGet(new DefinitionId(CoreStatIds[i]), out int value),
                    CoreStatIds[i] + " should be present.");
                Assert.AreEqual(10 + i, value, CoreStatIds[i]);
            }
        }

        [TestCase("stat.str")]
        [TestCase("stat.agi")]
        [TestCase("stat.vit")]
        [TestCase("stat.int")]
        [TestCase("stat.dex")]
        [TestCase("stat.luk")]
        public void EachCoreAttributeIsRepresentable(string statId)
        {
            CharacterStatsState stats = NewStats();

            stats.Set(new DefinitionId(statId), 42);

            Assert.IsTrue(stats.TryGet(new DefinitionId(statId), out int value));
            Assert.AreEqual(42, value);
        }

        [Test]
        public void SettingAnExistingStatReplacesItRatherThanDuplicating()
        {
            CharacterStatsState stats = NewStats();

            stats.Set(new DefinitionId("stat.str"), 10);
            stats.Set(new DefinitionId("stat.str"), 25);

            Assert.AreEqual(1, stats.Count, "A stat must never appear twice.");
            Assert.AreEqual(25, stats.GetOrDefault(new DefinitionId("stat.str"), 0));
        }

        [Test]
        public void SuccessfulSetAdvancesRevisionExactlyOnce()
        {
            CharacterStatsState stats = NewStats();

            stats.Set(new DefinitionId("stat.str"), 10);
            Assert.AreEqual(1, stats.Revision.Value);

            stats.Set(new DefinitionId("stat.agi"), 8);
            Assert.AreEqual(2, stats.Revision.Value);

            stats.Set(new DefinitionId("stat.str"), 11);
            Assert.AreEqual(3, stats.Revision.Value);
        }

        [Test]
        public void RejectedSetChangesNothing()
        {
            CharacterStatsState stats = NewStats();
            stats.Set(new DefinitionId("stat.str"), 10);
            Revision before = stats.Revision;

            Assert.Throws<ArgumentOutOfRangeException>(
                () => stats.Set(new DefinitionId("stat.str"), -1));
            Assert.Throws<ArgumentException>(() => stats.Set(DefinitionId.None, 5));
            Assert.Throws<ArgumentException>(() => stats.Set(new DefinitionId("  "), 5));

            Assert.AreEqual(before, stats.Revision);
            Assert.AreEqual(10, stats.GetOrDefault(new DefinitionId("stat.str"), 0));
            Assert.AreEqual(1, stats.Count);
        }

        [Test]
        public void ReadsDoNotAdvanceRevision()
        {
            CharacterStatsState stats = NewStats();
            stats.Set(new DefinitionId("stat.str"), 10);
            Revision after = stats.Revision;

            stats.Contains(new DefinitionId("stat.str"));
            stats.TryGet(new DefinitionId("stat.str"), out _);
            stats.GetOrDefault(new DefinitionId("stat.agi"), 0);
            int ignored = stats.Count;
            IReadOnlyList<CharacterStatEntry> list = stats.Stats;

            Assert.AreEqual(after, stats.Revision);
            Assert.AreEqual(1, ignored);
            Assert.AreEqual(1, list.Count);
        }

        [Test]
        public void RemoveAdvancesRevisionOnlyWhenSomethingWasRemoved()
        {
            CharacterStatsState stats = NewStats();
            stats.Set(new DefinitionId("stat.str"), 10);
            Revision afterSet = stats.Revision;

            Assert.IsFalse(stats.Remove(new DefinitionId("stat.agi")));
            Assert.AreEqual(afterSet, stats.Revision);

            Assert.IsTrue(stats.Remove(new DefinitionId("stat.str")));
            Assert.IsTrue(stats.Revision.IsNewerThan(afterSet));
            Assert.AreEqual(0, stats.Count);
        }

        [Test]
        public void StatsViewIsReadOnlyAndNotTheBackingList()
        {
            CharacterStatsState stats = NewStats();
            stats.Set(new DefinitionId("stat.str"), 10);

            IReadOnlyList<CharacterStatEntry> view = stats.Stats;

            Assert.IsFalse(view is List<CharacterStatEntry>,
                "The backing list must not escape where a caller could cast and mutate it.");
        }

        [Test]
        public void IdentityIsPreservedThroughMutation()
        {
            CharacterStatsState stats = NewStats();
            CharacterId id = stats.CharacterId;

            stats.Set(new DefinitionId("stat.str"), 10);
            stats.Set(new DefinitionId("stat.vit"), 20);
            stats.Remove(new DefinitionId("stat.str"));

            Assert.AreEqual(id, stats.CharacterId);
        }
    }
}
