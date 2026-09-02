using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    internal sealed class AppearanceGenderValidationTests : AppearanceTestBase
    {
        [Test]
        public void FullyResolvedNeutralAppearancePasses()
        {
            AddNeutralSet();

            ValidationReport report = new CharacterAppearanceValidator()
                .Validate(FullyDressed(), Options, CharacterGender.Female);

            Assert.IsTrue(report.IsValid);
            Assert.AreEqual(0, report.Messages.Count);
        }

        [Test]
        public void MaleCharacterMayUseMaleOnlyContent()
        {
            AddNeutralSet();
            Add("hair_male", AppearanceSlot.Hair, GenderAvailability.MaleOnly);

            CharacterAppearanceState appearance = FullyDressed();
            appearance.Select(AppearanceSlot.Hair, new DefinitionId("hair_male"));

            ValidationReport report = new CharacterAppearanceValidator()
                .Validate(appearance, Options, CharacterGender.Male);

            Assert.IsTrue(report.IsValid);
        }

        [Test]
        public void FemaleCharacterMayUseFemaleOnlyContent()
        {
            AddNeutralSet();
            Add("face_female", AppearanceSlot.Face, GenderAvailability.FemaleOnly);

            CharacterAppearanceState appearance = FullyDressed();
            appearance.Select(AppearanceSlot.Face, new DefinitionId("face_female"));

            ValidationReport report = new CharacterAppearanceValidator()
                .Validate(appearance, Options, CharacterGender.Female);

            Assert.IsTrue(report.IsValid);
        }

        [Test]
        public void IncompatibleGenderIsRejected()
        {
            AddNeutralSet();
            Add("hair_female", AppearanceSlot.Hair, GenderAvailability.FemaleOnly);

            CharacterAppearanceState appearance = FullyDressed();
            appearance.Select(AppearanceSlot.Hair, new DefinitionId("hair_female"));

            ValidationReport report = new CharacterAppearanceValidator()
                .Validate(appearance, Options, CharacterGender.Male);

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.GenderIncompatible, report.Messages[0].Code);
            Assert.AreEqual(new DefinitionId("hair_female"), report.Messages[0].DefinitionId);
        }

        [Test]
        public void AnyAvailabilitySuitsBothGenders()
        {
            Assert.IsTrue(CharacterAppearanceValidator.IsAllowedFor(
                GenderAvailability.Any, CharacterGender.Male));
            Assert.IsTrue(CharacterAppearanceValidator.IsAllowedFor(
                GenderAvailability.Any, CharacterGender.Female));
            Assert.IsFalse(CharacterAppearanceValidator.IsAllowedFor(
                GenderAvailability.MaleOnly, CharacterGender.Female));
            Assert.IsFalse(CharacterAppearanceValidator.IsAllowedFor(
                GenderAvailability.FemaleOnly, CharacterGender.Male));
            Assert.IsFalse(CharacterAppearanceValidator.IsAllowedFor(
                GenderAvailability.MaleOnly, CharacterGender.Unspecified));
        }
    }
}
