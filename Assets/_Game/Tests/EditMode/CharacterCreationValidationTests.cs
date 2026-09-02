using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    internal sealed class CharacterCreationValidationTests : CharacterCreationTestBase
    {
        private bool TryCreate(CharacterCreationInput input, out Character created,
            out ValidationReport report)
        {
            return new CharacterCreationService().TryCreate(input, Content(), out created, out report);
        }

        [Test]
        public void InvalidOwnerIsRejected()
        {
            var input = new CharacterCreationInput(
                OwnerId.None, "Hero", CharacterGender.Male, new DefinitionId(Mage));

            Assert.IsFalse(TryCreate(input, out Character created, out ValidationReport report));
            Assert.IsNull(created);
            Assert.IsFalse(report.IsValid);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void MissingNameIsRejected(string name)
        {
            Assert.IsFalse(TryCreate(Input(Mage, CharacterGender.Male, name),
                out Character created, out ValidationReport report));
            Assert.IsNull(created);
            Assert.AreEqual(ValidationCode.InvalidConfiguration, report.Messages[0].Code);
        }

        [Test]
        public void UnspecifiedGenderIsRejected()
        {
            Assert.IsFalse(TryCreate(Input(Mage, CharacterGender.Unspecified),
                out Character created, out _));
            Assert.IsNull(created);
        }

        [Test]
        public void UnknownClassIsRejected()
        {
            Assert.IsFalse(TryCreate(Input("class.ghost"), out Character created,
                out ValidationReport report));
            Assert.IsNull(created);
            Assert.AreEqual(ValidationCode.MissingReference, report.Messages[0].Code);
        }

        [Test]
        public void MissingClassIsRejected()
        {
            var input = new CharacterCreationInput(
                new OwnerId("account:1"), "Hero", CharacterGender.Male, DefinitionId.None);

            Assert.IsFalse(TryCreate(input, out Character created, out ValidationReport report));
            Assert.IsNull(created);
            Assert.AreEqual(ValidationCode.MissingDefinitionId, report.Messages[0].Code);
        }

        [Test]
        public void GenderRestrictedClassRejectsTheWrongGender()
        {
            AddClass("class.valkyrie", GenderAvailability.FemaleOnly, 5, 5);

            Assert.IsFalse(TryCreate(Input("class.valkyrie", CharacterGender.Male),
                out Character rejected, out ValidationReport report));
            Assert.IsNull(rejected);
            Assert.AreEqual(ValidationCode.GenderIncompatible, report.Messages[0].Code);

            Assert.IsTrue(TryCreate(Input("class.valkyrie", CharacterGender.Female),
                out Character accepted, out _));
            Assert.IsNotNull(accepted);
        }

        [Test]
        public void GenderIncompatibleAppearanceIsRejected()
        {
            AddAppearance("hair_female", AppearanceSlot.Hair, GenderAvailability.FemaleOnly);

            var choices = new[]
            {
                new AppearanceChoice(AppearanceSlot.Hair, new DefinitionId("hair_female"))
            };

            Assert.IsFalse(TryCreate(Input(Mage, CharacterGender.Male, "Hero", "account:1", choices),
                out Character created, out ValidationReport report));
            Assert.IsNull(created);
            Assert.AreEqual(ValidationCode.GenderIncompatible, report.Messages[0].Code);
        }

        [Test]
        public void AppearanceOptionInTheWrongSlotIsRejected()
        {
            AddAppearance("hair_001", AppearanceSlot.Hair, GenderAvailability.Any);

            var choices = new[]
            {
                new AppearanceChoice(AppearanceSlot.Face, new DefinitionId("hair_001"))
            };

            Assert.IsFalse(TryCreate(Input(Mage, CharacterGender.Male, "Hero", "account:1", choices),
                out Character created, out ValidationReport report));
            Assert.IsNull(created);
            Assert.AreEqual(ValidationCode.SlotMismatch, report.Messages[0].Code);
        }

        [Test]
        public void UnknownAppearanceOptionIsRejected()
        {
            var choices = new[]
            {
                new AppearanceChoice(AppearanceSlot.Hair, new DefinitionId("hair.ghost"))
            };

            Assert.IsFalse(TryCreate(Input(Mage, CharacterGender.Male, "Hero", "account:1", choices),
                out Character created, out _));
            Assert.IsNull(created);
        }

        [Test]
        public void EveryFaultIsReportedNotJustTheFirst()
        {
            var input = new CharacterCreationInput(
                OwnerId.None, "  ", CharacterGender.Unspecified, DefinitionId.None);

            TryCreate(input, out _, out ValidationReport report);

            Assert.GreaterOrEqual(report.ErrorCount, 4,
                "A creation screen should see every problem at once.");
        }
    }
}
