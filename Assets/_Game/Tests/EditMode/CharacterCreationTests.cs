using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    internal sealed class CharacterCreationTests : CharacterCreationTestBase
    {
        private bool TryCreate(CharacterCreationInput input, out NewCharacter created,
            out ValidationReport report)
        {
            return new CharacterCreationService().TryCreate(input, Content(), out created, out report);
        }

        [TestCase(Swordsman)]
        [TestCase(Cleric)]
        [TestCase(Mage)]
        [TestCase(Archer)]
        public void EveryStartingClassCreatesThroughTheSamePath(string startingClass)
        {
            bool created = TryCreate(Input(startingClass), out NewCharacter character, out ValidationReport report);

            Assert.IsTrue(created, report.Messages.Count > 0 ? report.Messages[0].Message : string.Empty);
            Assert.IsTrue(report.IsValid);
            Assert.AreEqual(new DefinitionId(startingClass), character.Class.BaseClass);
        }

        [TestCase(CharacterGender.Male)]
        [TestCase(CharacterGender.Female)]
        public void BothGendersAreAccepted(CharacterGender gender)
        {
            bool created = TryCreate(Input(Cleric, gender), out NewCharacter character, out _);

            Assert.IsTrue(created);
            Assert.AreEqual(gender, character.Identity.Gender);
        }

        [Test]
        public void IdentityIsMintedAndSharedByEveryAggregate()
        {
            TryCreate(Input(Mage), out NewCharacter character, out _);

            CharacterId id = character.Identity.CharacterId;

            Assert.IsTrue(id.IsValid);
            Assert.AreEqual(id, character.Class.CharacterId);
            Assert.AreEqual(id, character.Appearance.CharacterId);
            Assert.AreEqual(id, character.Progression.CharacterId);
            Assert.AreEqual(id, character.Stats.CharacterId);
            Assert.AreEqual(id, character.Resources.CharacterId);
        }

        [Test]
        public void EachCreationMintsADistinctIdentity()
        {
            TryCreate(Input(Mage), out NewCharacter first, out _);
            TryCreate(Input(Mage), out NewCharacter second, out _);

            Assert.AreNotEqual(first.Identity.CharacterId, second.Identity.CharacterId);
        }

        [Test]
        public void OwnerAndNameArePreserved()
        {
            TryCreate(Input(Archer, CharacterGender.Female, "Robin", "account:77"),
                out NewCharacter character, out _);

            Assert.AreEqual(new OwnerId("account:77"), character.Identity.Owner);
            Assert.AreEqual("Robin", character.Identity.Name);
        }

        [Test]
        public void ProgressionStartsAtTheCurveMinimumWithNoExperience()
        {
            TryCreate(Input(Swordsman), out NewCharacter character, out _);

            Assert.AreEqual(1, character.Progression.Level);
            Assert.AreEqual(0L, character.Progression.Experience);
            Assert.AreEqual(Revision.Initial, character.Progression.Revision);
        }

        [Test]
        public void BaseStatsComeFromTheClassAsset()
        {
            // Swordsman fixture authors STR 10, VIT 8.
            TryCreate(Input(Swordsman), out NewCharacter character, out _);

            Assert.AreEqual(10, character.Stats.GetOrDefault(new DefinitionId(Str), -1));
            Assert.AreEqual(8, character.Stats.GetOrDefault(new DefinitionId(Vit), -1));
        }

        [Test]
        public void DifferentClassesYieldDifferentStartingStats()
        {
            TryCreate(Input(Swordsman), out NewCharacter warrior, out _);
            TryCreate(Input(Mage), out NewCharacter mage, out _);

            Assert.AreEqual(10, warrior.Stats.GetOrDefault(new DefinitionId(Str), -1));
            Assert.AreEqual(3, mage.Stats.GetOrDefault(new DefinitionId(Str), -1));
        }

        [Test]
        public void ResourcesStartFullFromTheCalculatedMaxima()
        {
            // Swordsman: VIT 8 so MaxHP = 50 + 80 = 130; STR 10 so MaxMP = 10 + 20 = 30.
            TryCreate(Input(Swordsman), out NewCharacter character, out _);

            Assert.AreEqual(130, character.Resources.CurrentHealth);
            Assert.AreEqual(30, character.Resources.CurrentMana);
        }

        [Test]
        public void NoJobIsAssignedAtCreation()
        {
            TryCreate(Input(Swordsman), out NewCharacter character, out _);

            Assert.IsFalse(character.Class.HasChangedJob);
            Assert.AreEqual(DefinitionId.None, character.Class.CurrentJob);
        }

        [Test]
        public void AppearanceIsInitialisedAndCarriesSuppliedChoices()
        {
            AddAppearance("hair_001", AppearanceSlot.Hair, GenderAvailability.Any);
            AddAppearance("face_001", AppearanceSlot.Face, GenderAvailability.Any);

            var choices = new[]
            {
                new AppearanceChoice(AppearanceSlot.Hair, new DefinitionId("hair_001")),
                new AppearanceChoice(AppearanceSlot.Face, new DefinitionId("face_001"))
            };

            TryCreate(Input(Cleric, CharacterGender.Female, "Nun", "account:1", choices),
                out NewCharacter character, out _);

            Assert.AreEqual(new DefinitionId("hair_001"), character.Appearance.Hair);
            Assert.AreEqual(new DefinitionId("face_001"), character.Appearance.Face);
        }

        [Test]
        public void AllAggregatesKeepTheirOwnStateClassification()
        {
            TryCreate(Input(Mage), out NewCharacter character, out _);

            Assert.IsInstanceOf<IPersistentState>(character.Identity);
            Assert.IsInstanceOf<IPersistentState>(character.Class);
            Assert.IsInstanceOf<IPersistentState>(character.Appearance);
            Assert.IsInstanceOf<IPersistentState>(character.Progression);
            Assert.IsInstanceOf<IPersistentState>(character.Stats);

            Assert.IsInstanceOf<IRuntimeState>(character.Resources);
            Assert.IsNotInstanceOf<IPersistentState>(character.Resources);
        }
    }
}
