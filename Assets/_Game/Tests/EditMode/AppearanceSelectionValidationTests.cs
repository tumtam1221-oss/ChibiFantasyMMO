using System;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    internal sealed class AppearanceSelectionValidationTests : AppearanceTestBase
    {
        [Test]
        public void WrongCategorySelectionIsDetected()
        {
            AddNeutralSet();

            CharacterAppearanceState appearance = FullyDressed();
            // A hair option written into the face slot. Untyped ids cannot catch this at
            // compile time, so validation is where the guarantee actually lives.
            appearance.Select(AppearanceSlot.Face, new DefinitionId("hair_001"));

            ValidationReport report = new CharacterAppearanceValidator()
                .Validate(appearance, Options, CharacterGender.Male);

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.SlotMismatch, report.Messages[0].Code);
        }

        [Test]
        public void UnresolvableSelectionIsDetected()
        {
            AddNeutralSet();

            CharacterAppearanceState appearance = FullyDressed();
            appearance.Select(AppearanceSlot.Hair, new DefinitionId("hair_does_not_exist"));

            ValidationReport report = new CharacterAppearanceValidator()
                .Validate(appearance, Options, CharacterGender.Male);

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.MissingReference, report.Messages[0].Code);
        }

        [Test]
        public void MissingSelectionIsReportedOnlyWhenRequired()
        {
            AddNeutralSet();
            var appearance = new CharacterAppearanceState(CharacterId.New());

            ValidationReport strict = new CharacterAppearanceValidator(true)
                .Validate(appearance, Options, CharacterGender.Male);

            Assert.IsFalse(strict.IsValid);
            Assert.AreEqual(5, strict.ErrorCount, "Every unfilled slot should be reported.");

            ValidationReport lenient = new CharacterAppearanceValidator(false)
                .Validate(appearance, Options, CharacterGender.Male);

            Assert.IsTrue(lenient.IsValid, "Incomplete appearance is legitimate mid-creation.");
        }

        [Test]
        public void ValidationIsDeterministic()
        {
            AddNeutralSet();
            CharacterAppearanceState appearance = FullyDressed();
            appearance.Select(AppearanceSlot.Face, new DefinitionId("hair_001"));
            appearance.Select(AppearanceSlot.Eyes, new DefinitionId("missing"));

            var validator = new CharacterAppearanceValidator();
            ValidationReport first = validator.Validate(appearance, Options, CharacterGender.Male);
            ValidationReport second = validator.Validate(appearance, Options, CharacterGender.Male);

            Assert.AreEqual(first.Messages.Count, second.Messages.Count);

            for (int i = 0; i < first.Messages.Count; i++)
            {
                Assert.AreEqual(first.Messages[i].Code, second.Messages[i].Code, "index " + i);
                Assert.AreEqual(first.Messages[i].DefinitionId, second.Messages[i].DefinitionId, "index " + i);
            }
        }

        [Test]
        public void ValidatorDoesNotRepairSelections()
        {
            AddNeutralSet();
            CharacterAppearanceState appearance = FullyDressed();
            appearance.Select(AppearanceSlot.Hair, new DefinitionId("broken"));
            Revision before = appearance.Revision;

            new CharacterAppearanceValidator().Validate(appearance, Options, CharacterGender.Male);

            Assert.AreEqual(new DefinitionId("broken"), appearance.Hair,
                "A broken selection must be reported, never silently replaced.");
            Assert.AreEqual(before, appearance.Revision);
        }

        [Test]
        public void NullArgumentsThrow()
        {
            var validator = new CharacterAppearanceValidator();

            Assert.Throws<ArgumentNullException>(
                () => validator.Validate(null, Options, CharacterGender.Male));
            Assert.Throws<ArgumentNullException>(
                () => validator.Validate(FullyDressed(), null, CharacterGender.Male));
        }
    }
}
