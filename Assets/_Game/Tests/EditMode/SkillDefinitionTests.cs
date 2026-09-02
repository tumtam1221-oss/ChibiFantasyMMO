using System;
using System.Reflection;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    internal sealed class SkillDefinitionTests : SkillTestBase
    {
        [Test]
        public void AWellFormedSkillPasses()
        {
            SkillDefinition skill = AddSkill("skill.basic", 3, 5f, 2f, 0.5f, 4f, ClassA,
                levels: new[] { Level(1, 1, 5f, 2f), Level(2, 5, 7f, 2f), Level(3, 10, 9f, 1.5f) });

            ValidationReport report = Validate(skill);

            Assert.IsTrue(report.IsValid,
                report.Messages.Count > 0 ? report.Messages[0].Message : string.Empty);
        }

        [Test]
        public void ASkillWithNoLevelTableIsASingleRankSkill()
        {
            SkillDefinition skill = AddSkill("skill.simple");

            Assert.IsTrue(Validate(skill).IsValid);
            Assert.AreEqual(0, skill.Levels.Length);
        }

        [Test]
        public void MissingIdIsCaughtByTheExistingValidator()
        {
            SkillDefinition skill = AddSkill("", register: false);

            ValidationReport report = Validate(skill);

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.MissingDefinitionId, report.Messages[0].Code);
        }

        [Test]
        public void MaximumLevelBelowOneIsRejected()
        {
            ValidationReport report = Validate(AddSkill("skill.bad", 0));

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.InvalidConfiguration, report.Messages[0].Code);
        }

        [TestCase(-1f, 0f, 0f, 0f)]
        [TestCase(0f, -1f, 0f, 0f)]
        [TestCase(0f, 0f, -1f, 0f)]
        [TestCase(0f, 0f, 0f, -1f)]
        public void NegativeScalarsAreRejected(float cost, float cooldown, float castTime, float range)
        {
            ValidationReport report = Validate(
                AddSkill("skill.negative", 1, cost, cooldown, castTime, range));

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.ValueOutOfRange, report.Messages[0].Code);
        }

        [Test]
        public void TableLengthMustMatchMaximumLevel()
        {
            ValidationReport report = Validate(AddSkill("skill.mismatch", 3,
                levels: new[] { Level(1, 1), Level(2, 5) }));

            Assert.IsFalse(report.IsValid);
            StringAssert.Contains("table holds", report.Messages[0].Message);
        }

        [Test]
        public void OutOfOrderLevelsAreRejected()
        {
            ValidationReport report = Validate(AddSkill("skill.unordered", 3,
                levels: new[] { Level(1, 1), Level(3, 5), Level(2, 10) }));

            Assert.IsFalse(report.IsValid);
            StringAssert.Contains("one upward", report.Messages[0].Message);
        }

        [Test]
        public void DuplicateLevelsAreRejected()
        {
            ValidationReport report = Validate(AddSkill("skill.duplicate", 3,
                levels: new[] { Level(1, 1), Level(2, 5), Level(2, 10) }));

            Assert.IsFalse(report.IsValid);
            StringAssert.Contains("one upward", report.Messages[0].Message);
        }

        [Test]
        public void NegativePerLevelValuesAreRejected()
        {
            ValidationReport costFault = Validate(AddSkill("skill.cost", 1,
                levels: new[] { Level(1, 1, -5f) }));
            Assert.IsFalse(costFault.IsValid);

            ValidationReport cooldownFault = Validate(AddSkill("skill.cooldown", 1,
                levels: new[] { Level(1, 1, 0f, -2f) }));
            Assert.IsFalse(cooldownFault.IsValid);

            ValidationReport levelFault = Validate(AddSkill("skill.charlevel", 1,
                levels: new[] { Level(1, 0) }));
            Assert.IsFalse(levelFault.IsValid);
        }

        [Test]
        public void TryGetLevelFindsRanksAndReportsMissingOnes()
        {
            SkillDefinition skill = AddSkill("skill.ranked", 2,
                levels: new[] { Level(1, 1, 5f), Level(2, 8, 9f) });

            Assert.IsTrue(skill.TryGetLevel(2, out SkillLevelEntry second));
            Assert.AreEqual(8, second.RequiredCharacterLevel);
            Assert.AreEqual(9f, second.ResourceCost);
            Assert.IsFalse(skill.TryGetLevel(3, out _));
        }

        [Test]
        public void PresentationStaysAssetRefAndNeverAUnityObject()
        {
            foreach (FieldInfo field in typeof(SkillDefinition).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                Assert.IsFalse(typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType),
                    field.Name + " must be an address, not a direct asset reference.");
            }

            SkillDefinition skill = AddSkill("skill.presentation");

            Assert.AreEqual(typeof(AssetRef), skill.Icon.GetType());
            Assert.AreEqual(typeof(AssetRef), skill.Animation.GetType());
            Assert.AreEqual(typeof(AssetRef), skill.VisualEffect.GetType());
            Assert.AreEqual(typeof(AssetRef), skill.SoundEffect.GetType());
        }

        [Test]
        public void IdentityUsesTheExistingDefinitionId()
        {
            SkillDefinition skill = AddSkill("skill.identity");

            Assert.IsInstanceOf<GameDefinition>(skill);
            Assert.AreEqual(new DefinitionId("skill.identity"), skill.Id);
            Assert.AreEqual(typeof(DefinitionId), skill.Id.GetType());

            foreach (Type type in typeof(SkillDefinition).Assembly.GetTypes())
            {
                Assert.AreNotEqual("SkillId", type.Name, "DefinitionId already identifies skills.");
                Assert.AreNotEqual("SkillGuid", type.Name);
            }
        }

        [Test]
        public void SerializationRoundTripsDeterministically()
        {
            SkillDefinition skill = AddSkill("skill.serial", 2, 3f, 1f, 0.25f, 6f, ClassA,
                levels: new[] { Level(1, 1, 3f, 1f), Level(2, 6, 4f, 1f) });

            string json = JsonUtility.ToJson(skill);
            var restored = ScriptableObject.CreateInstance<SkillDefinition>();
            try
            {
                JsonUtility.FromJsonOverwrite(json, restored);

                Assert.AreEqual(skill.Id, restored.Id);
                Assert.AreEqual(skill.MaxLevel, restored.MaxLevel);
                Assert.AreEqual(skill.Levels.Length, restored.Levels.Length);
                Assert.AreEqual(skill.Levels[1].RequiredCharacterLevel,
                    restored.Levels[1].RequiredCharacterLevel);
                Assert.AreEqual(json, JsonUtility.ToJson(restored));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(restored);
            }
        }

        [Test]
        public void ValidationDoesNotMutateTheDefinition()
        {
            SkillDefinition skill = AddSkill("skill.stable", 2, 3f,
                levels: new[] { Level(1, 1), Level(2, 5) });

            string before = JsonUtility.ToJson(skill);

            Validate(skill);
            Validate(skill);

            Assert.AreEqual(before, JsonUtility.ToJson(skill));
        }
    }
}
