using System;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    internal sealed class CharacterStatsValidationTests : StatsTestBase
    {
        [Test]
        public void ValidStatsPass()
        {
            AddCoreStats();
            CharacterStatsState stats = NewStats();
            stats.Set(new DefinitionId("stat.str"), 10);
            stats.Set(new DefinitionId("stat.vit"), 20);

            ValidationReport report = new CharacterStatsValidator().Validate(stats, Definitions);

            Assert.IsTrue(report.IsValid);
            Assert.AreEqual(0, report.Messages.Count);
        }

        [Test]
        public void EmptyStatsAreValid()
        {
            AddCoreStats();

            ValidationReport report = new CharacterStatsValidator().Validate(NewStats(), Definitions);

            Assert.IsTrue(report.IsValid);
        }

        [Test]
        public void ValueAboveTheStatCeilingIsRejected()
        {
            AddStat("stat.str", 0, 100);
            CharacterStatsState stats = NewStats();
            stats.Set(new DefinitionId("stat.str"), 101);

            ValidationReport report = new CharacterStatsValidator().Validate(stats, Definitions);

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.ValueOutOfRange, report.Messages[0].Code);
            Assert.AreEqual(new DefinitionId("stat.str"), report.Messages[0].DefinitionId);
        }

        [Test]
        public void ValueBelowTheStatFloorIsRejected()
        {
            AddStat("stat.str", 5, 100);
            CharacterStatsState stats = NewStats();
            stats.Set(new DefinitionId("stat.str"), 1);

            ValidationReport report = new CharacterStatsValidator().Validate(stats, Definitions);

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.ValueOutOfRange, report.Messages[0].Code);
        }

        [Test]
        public void ValueExactlyOnTheBoundsIsAccepted()
        {
            AddStat("stat.str", 5, 100);
            CharacterStatsState stats = NewStats();

            stats.Set(new DefinitionId("stat.str"), 5);
            Assert.IsTrue(new CharacterStatsValidator().Validate(stats, Definitions).IsValid);

            stats.Set(new DefinitionId("stat.str"), 100);
            Assert.IsTrue(new CharacterStatsValidator().Validate(stats, Definitions).IsValid);
        }

        [Test]
        public void OrphanedStatIsReportedNotDeleted()
        {
            AddCoreStats();
            CharacterStatsState stats = NewStats();
            stats.Set(new DefinitionId("stat.removed_by_patch"), 10);

            ValidationReport report = new CharacterStatsValidator().Validate(stats, Definitions);

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.MissingReference, report.Messages[0].Code);
            Assert.AreEqual(10, stats.GetOrDefault(new DefinitionId("stat.removed_by_patch"), 0),
                "Migration tooling must still be able to see the orphaned value.");
        }

        [Test]
        public void StatsWithoutACharacterAreReported()
        {
            AddCoreStats();
            var orphan = new CharacterStatsState();

            ValidationReport report = new CharacterStatsValidator().Validate(orphan, Definitions);

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.MissingDefinitionId, report.Messages[0].Code);
        }

        [Test]
        public void ValidationIsDeterministic()
        {
            AddStat("stat.str", 0, 10);
            CharacterStatsState stats = NewStats();
            stats.Set(new DefinitionId("stat.str"), 50);
            stats.Set(new DefinitionId("stat.ghost"), 1);

            var validator = new CharacterStatsValidator();
            ValidationReport first = validator.Validate(stats, Definitions);
            ValidationReport second = validator.Validate(stats, Definitions);

            Assert.AreEqual(first.Messages.Count, second.Messages.Count);

            for (int i = 0; i < first.Messages.Count; i++)
            {
                Assert.AreEqual(first.Messages[i].Code, second.Messages[i].Code, "index " + i);
                Assert.AreEqual(first.Messages[i].DefinitionId, second.Messages[i].DefinitionId, "index " + i);
            }
        }

        [Test]
        public void ValidatorDoesNotRepair()
        {
            AddStat("stat.str", 0, 10);
            CharacterStatsState stats = NewStats();
            stats.Set(new DefinitionId("stat.str"), 999);
            Revision before = stats.Revision;

            new CharacterStatsValidator().Validate(stats, Definitions);

            Assert.AreEqual(999, stats.GetOrDefault(new DefinitionId("stat.str"), 0));
            Assert.AreEqual(before, stats.Revision);
        }

        [Test]
        public void NullArgumentsThrow()
        {
            var validator = new CharacterStatsValidator();

            Assert.Throws<ArgumentNullException>(() => validator.Validate(null, Definitions));
            Assert.Throws<ArgumentNullException>(() => validator.Validate(NewStats(), null));
        }

        [Test]
        public void StatDefinitionWithInvertedBoundsIsRejected()
        {
            StatDefinition bad = AddStat("stat.bad", 100, 10);

            var validator = new DefinitionValidator(
                new IDefinitionValidationRule[] { new StatDefinitionValidationRule() });
            ValidationReport report = validator.Validate(bad, Definitions);

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.InvalidConfiguration, report.Messages[0].Code);
        }

        [Test]
        public void StatDefinitionWithNegativeFloorIsRejected()
        {
            StatDefinition bad = AddStat("stat.negative", -5, 10);

            var validator = new DefinitionValidator(
                new IDefinitionValidationRule[] { new StatDefinitionValidationRule() });
            ValidationReport report = validator.Validate(bad, Definitions);

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.InvalidConfiguration, report.Messages[0].Code);
        }

        [Test]
        public void WellFormedStatDefinitionPasses()
        {
            StatDefinition good = AddStat("stat.ok", 0, 999);

            var validator = new DefinitionValidator(
                new IDefinitionValidationRule[] { new StatDefinitionValidationRule() });

            Assert.IsTrue(validator.Validate(good, Definitions).IsValid);
        }

        [Test]
        public void StatDefinitionsWorkWithTheExistingRegistry()
        {
            AddStat("stat.str", 0, 999);

            Assert.AreEqual(1, Definitions.Count);
            Assert.IsTrue(Definitions.Contains(new DefinitionId("stat.str")));
            Assert.IsTrue(Definitions.TryGet(new DefinitionId("stat.str"), out StatDefinition found));
            Assert.AreEqual(999, found.MaxValue);

            Assert.Throws<ArgumentException>(() => AddStat("stat.str", 0, 10));
        }
    }
}
