using System;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    internal sealed class ProgressionDefinitionTests : ProgressionTestBase
    {
        private static ValidationReport Validate(CharacterProgressionDefinition definition,
            IDefinitionLookup lookup)
        {
            var validator = new DefinitionValidator(
                new IDefinitionValidationRule[] { new CharacterProgressionValidationRule() });
            return validator.Validate(definition, lookup);
        }

        [Test]
        public void IsADefinitionWithAStableId()
        {
            CharacterProgressionDefinition curve = StandardCurve();

            Assert.IsInstanceOf<GameDefinition>(curve);
            Assert.IsInstanceOf<IDefinition>(curve);
            Assert.AreEqual(new DefinitionId("progression_test"), curve.Id);
            Assert.IsTrue(curve.Id.IsValid);
        }

        [Test]
        public void ExposesCostsPerLevel()
        {
            CharacterProgressionDefinition curve = StandardCurve();

            Assert.AreEqual(1, curve.MinLevel);
            Assert.AreEqual(4, curve.MaxLevel);
            Assert.AreEqual(3, curve.TransitionCount);
            Assert.AreEqual(100L, curve.GetExperienceToNextLevel(1));
            Assert.AreEqual(200L, curve.GetExperienceToNextLevel(2));
            Assert.AreEqual(300L, curve.GetExperienceToNextLevel(3));
        }

        [Test]
        public void RejectsQueriesOutsideTheLevellingRange()
        {
            CharacterProgressionDefinition curve = StandardCurve();

            Assert.Throws<ArgumentOutOfRangeException>(() => curve.GetExperienceToNextLevel(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => curve.GetExperienceToNextLevel(4),
                "The maximum level has no next level to reach.");
            Assert.Throws<ArgumentOutOfRangeException>(() => curve.GetExperienceToNextLevel(99));

            Assert.IsTrue(curve.IsLevelInRange(1));
            Assert.IsTrue(curve.IsLevelInRange(4));
            Assert.IsFalse(curve.IsLevelInRange(5));
        }

        [Test]
        public void ValidCurvePassesValidation()
        {
            ValidationReport report = Validate(StandardCurve(), new DefinitionRegistry<GameDefinition>());

            Assert.IsTrue(report.IsValid);
            Assert.AreEqual(0, report.Messages.Count);
        }

        [Test]
        public void MaximumBelowMinimumIsRejected()
        {
            ValidationReport report = Validate(
                Curve("bad_range", 5, 2), new DefinitionRegistry<GameDefinition>());

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.InvalidConfiguration, report.Messages[0].Code);
        }

        [Test]
        public void MinimumBelowOneIsRejected()
        {
            ValidationReport report = Validate(
                Curve("bad_min", 0, 2, 100), new DefinitionRegistry<GameDefinition>());

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.InvalidConfiguration, report.Messages[0].Code);
        }

        [Test]
        public void IncompleteTableIsRejected()
        {
            ValidationReport report = Validate(
                Curve("short_table", 1, 5, 100, 200), new DefinitionRegistry<GameDefinition>());

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.InvalidConfiguration, report.Messages[0].Code);
            StringAssert.Contains("level costs", report.Messages[0].Message);
        }

        [TestCase(0L)]
        [TestCase(-50L)]
        public void NonPositiveLevelCostIsRejected(long badCost)
        {
            ValidationReport report = Validate(
                Curve("bad_cost", 1, 3, 100, badCost), new DefinitionRegistry<GameDefinition>());

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.InvalidConfiguration, report.Messages[0].Code);
        }

        [Test]
        public void DecreasingCostsAreAllowed()
        {
            // Per-level costs may fall as well as rise. Forbidding that would rule out a
            // curve the game has not decided against.
            ValidationReport report = Validate(
                Curve("decreasing", 1, 4, 500, 200, 100), new DefinitionRegistry<GameDefinition>());

            Assert.IsTrue(report.IsValid);
        }

        [Test]
        public void CumulativeOverflowIsRejected()
        {
            ValidationReport report = Validate(
                Curve("overflow", 1, 4, long.MaxValue, long.MaxValue, long.MaxValue),
                new DefinitionRegistry<GameDefinition>());

            Assert.IsFalse(report.IsValid);
            StringAssert.Contains("exceeds the range", report.Messages[0].Message);
        }

        [Test]
        public void MissingIdIsCaughtByTheExistingValidator()
        {
            CharacterProgressionDefinition curve = Curve("", 1, 2, 100);

            ValidationReport report = Validate(curve, new DefinitionRegistry<GameDefinition>());

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.MissingDefinitionId, report.Messages[0].Code,
                "The generic id check runs before any specialised rule.");
        }

        [Test]
        public void ValidationIsDeterministic()
        {
            CharacterProgressionDefinition curve = Curve("bad", 1, 4, 100, 0, -5);
            var lookup = new DefinitionRegistry<GameDefinition>();

            ValidationReport first = Validate(curve, lookup);
            ValidationReport second = Validate(curve, lookup);

            Assert.AreEqual(first.Messages.Count, second.Messages.Count);

            for (int i = 0; i < first.Messages.Count; i++)
            {
                Assert.AreEqual(first.Messages[i].Code, second.Messages[i].Code, "index " + i);
                Assert.AreEqual(first.Messages[i].Message, second.Messages[i].Message, "index " + i);
            }
        }

        [Test]
        public void WorksWithTheExistingRegistry()
        {
            var registry = new DefinitionRegistry<CharacterProgressionDefinition>();
            CharacterProgressionDefinition first = StandardCurve();

            registry.Register(first);

            Assert.AreEqual(1, registry.Count);
            Assert.IsTrue(registry.TryGet(new DefinitionId("progression_test"),
                out CharacterProgressionDefinition found));
            Assert.AreSame(first, found);

            CharacterProgressionDefinition duplicate = Curve("progression_test", 1, 2, 100);
            Assert.Throws<ArgumentException>(() => registry.Register(duplicate));
            Assert.IsFalse(registry.TryRegister(duplicate));
            Assert.AreEqual(1, registry.Count);
        }
    }
}
