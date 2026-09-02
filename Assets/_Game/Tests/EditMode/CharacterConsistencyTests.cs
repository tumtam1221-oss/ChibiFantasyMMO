using System;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    internal sealed class CharacterConsistencyTests : ConsistencyTestBase
    {
        [Test]
        public void AFreshlyCreatedCharacterIsConsistent()
        {
            ValidationReport report = Check(Create());

            Assert.IsTrue(report.IsValid,
                report.Messages.Count > 0 ? report.Messages[0].Message : string.Empty);
            Assert.AreEqual(0, report.Messages.Count);
        }

        [Test]
        public void ARootStateWithNoJobIsConsistent()
        {
            Character character = Create();

            Assert.IsFalse(character.Class.HasChangedJob);
            Assert.IsTrue(Check(character).IsValid);
        }

        [Test]
        public void AValidJobAtOrAboveItsRequirementIsConsistent()
        {
            AddJob("job.sword.first", Swordsman, 1);
            Character character = Create();
            character.Class.SetJob(new DefinitionId("job.sword.first"));

            Assert.IsTrue(Check(character).IsValid);
        }

        [Test]
        public void AJobFromAnotherClassIsDetected()
        {
            AddJob("job.mage.first", Mage, 1);
            Character character = Create(Swordsman);
            character.Class.SetJob(new DefinitionId("job.mage.first"));

            ValidationReport report = Check(character);

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.InvalidConfiguration, report.Messages[0].Code);
        }

        [Test]
        public void AJobAboveTheCharacterLevelIsDetected()
        {
            AddJob("job.sword.third", Swordsman, 60);
            Character character = Create();
            character.Class.SetJob(new DefinitionId("job.sword.third"));

            ValidationReport report = Check(character);

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.ValueOutOfRange, report.Messages[0].Code);
        }

        [Test]
        public void AJobThatNoLongerExistsIsDetected()
        {
            Character character = Create();
            character.Class.SetJob(new DefinitionId("job.removed_by_patch"));

            ValidationReport report = Check(character);

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.MissingReference, report.Messages[0].Code);
        }

        [Test]
        public void AMissingClassIsDetected()
        {
            Character character = Create();
            Character orphan = With(character, new CharacterClassState(
                character.Identity.CharacterId, new DefinitionId("class.ghost")));

            ValidationReport report = Check(orphan);

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.MissingReference, report.Messages[0].Code);
        }

        [Test]
        public void StateBelongingToAnotherCharacterIsDetected()
        {
            Character first = Create();
            Character second = Create();

            // Only an aggregate-level check can catch this; each part is valid alone.
            Character mismatched = With(first, second.Class);

            ValidationReport report = Check(mismatched);

            Assert.IsFalse(report.IsValid);
            StringAssert.Contains("different character", report.Messages[0].Message);
        }

        [Test]
        public void AGenderIncompatibleClassIsDetected()
        {
            AddClass("class.valkyrie", GenderAvailability.FemaleOnly, 5, 5);
            Character character = Create();

            Character mismatched = With(character, new CharacterClassState(
                character.Identity.CharacterId, new DefinitionId("class.valkyrie")));

            ValidationReport report = Check(mismatched);

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.GenderIncompatible, report.Messages[0].Code);
        }

        [Test]
        public void AnOrphanedStatIsDetected()
        {
            Character character = Create();
            character.Stats.Set(new DefinitionId("stat.removed_by_patch"), 5);

            ValidationReport report = Check(character);

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.MissingReference, report.Messages[0].Code);
        }

        [Test]
        public void NullArgumentsThrow()
        {
            var validator = new CharacterConsistencyValidator();
            Character character = Create();

            Assert.Throws<ArgumentNullException>(() => validator.Validate(null, Content(), Jobs));
            Assert.Throws<ArgumentNullException>(() => validator.Validate(character, null, Jobs));
            Assert.Throws<ArgumentNullException>(() => validator.Validate(character, Content(), null));
        }
    }
}
