using ChibiFantasy.Data;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Settings that are individually valid but contradict one another.
    /// </summary>
    internal sealed class SkillCombinationValidationTests : SkillTestBase
    {
        private SkillDefinition Configured(string id, SkillCategory category,
            SkillTargetType target, SkillResourceType resource, float cost = 0f, float castTime = 0f)
        {
            SkillDefinition skill = AddSkill(id, 1, cost, 0f, castTime);
            SetPrivate(skill, "_category", category);
            SetPrivate(skill, "_targetType", target);
            SetPrivate(skill, "_resourceType", resource);
            return skill;
        }

        [Test]
        public void AWellConfiguredActiveSkillPasses()
        {
            SkillDefinition skill = Configured("skill.ok", SkillCategory.Active,
                SkillTargetType.SingleEnemy, SkillResourceType.Mana, 10f);

            ValidationReport report = Validate(skill);

            Assert.IsTrue(report.IsValid,
                report.Messages.Count > 0 ? report.Messages[0].Message : string.Empty);
            Assert.AreEqual(0, report.WarningCount);
        }

        [Test]
        public void ASkillWithNoCategoryIsRejected()
        {
            SkillDefinition skill = Configured("skill.nocategory", SkillCategory.None,
                SkillTargetType.Self, SkillResourceType.None);

            ValidationReport report = Validate(skill);

            Assert.IsFalse(report.IsValid);
            StringAssert.Contains("no category", report.Messages[0].Message);
        }

        [TestCase(SkillCategory.Active)]
        [TestCase(SkillCategory.Toggle)]
        public void ACastSkillWithNoTargetTypeIsRejected(SkillCategory category)
        {
            SkillDefinition skill = Configured("skill.notarget." + category, category,
                SkillTargetType.None, SkillResourceType.None);

            ValidationReport report = Validate(skill);

            Assert.IsFalse(report.IsValid);
            StringAssert.Contains("nothing to cast it at", report.Messages[0].Message);
        }

        [Test]
        public void APassiveSkillNeedsNoTargetAndPasses()
        {
            SkillDefinition skill = Configured("skill.passive", SkillCategory.Passive,
                SkillTargetType.None, SkillResourceType.None);

            ValidationReport report = Validate(skill);

            Assert.IsTrue(report.IsValid);
            Assert.AreEqual(0, report.Messages.Count);
        }

        [Test]
        public void APassiveSkillWithATargetIsWarnedNotRejected()
        {
            SkillDefinition skill = Configured("skill.passivetarget", SkillCategory.Passive,
                SkillTargetType.SingleEnemy, SkillResourceType.None);

            ValidationReport report = Validate(skill);

            Assert.IsTrue(report.IsValid, "Suspicious content is usable, so this is a warning.");
            Assert.AreEqual(1, report.WarningCount);
            Assert.AreEqual(ValidationSeverity.Warning, report.Messages[0].Severity);
            StringAssert.Contains("nothing will read", report.Messages[0].Message);
        }

        [Test]
        public void APassiveSkillWithACastTimeIsWarnedNotRejected()
        {
            SkillDefinition skill = Configured("skill.passivecast", SkillCategory.Passive,
                SkillTargetType.None, SkillResourceType.None, 0f, 1.5f);

            ValidationReport report = Validate(skill);

            Assert.IsTrue(report.IsValid);
            Assert.AreEqual(1, report.WarningCount);
            StringAssert.Contains("never spend", report.Messages[0].Message);
        }

        [Test]
        public void ACostWithNoResourceTypeIsRejected()
        {
            SkillDefinition skill = Configured("skill.freecost", SkillCategory.Active,
                SkillTargetType.Self, SkillResourceType.None, 12f);

            ValidationReport report = Validate(skill);

            Assert.IsFalse(report.IsValid);
            StringAssert.Contains("names no resource type", report.Messages[0].Message);
        }

        [Test]
        public void APerLevelCostWithNoResourceTypeIsRejected()
        {
            SkillDefinition skill = AddSkill("skill.levelcost", 2,
                levels: new[] { Level(1, 1, 0f), Level(2, 5, 8f) });
            SetPrivate(skill, "_category", SkillCategory.Active);
            SetPrivate(skill, "_targetType", SkillTargetType.Self);
            SetPrivate(skill, "_resourceType", SkillResourceType.None);

            ValidationReport report = Validate(skill);

            Assert.IsFalse(report.IsValid);
            StringAssert.Contains("Level 2 costs", report.Messages[0].Message);
        }

        [Test]
        public void AFreeSkillWithNoResourceTypeIsFine()
        {
            SkillDefinition skill = Configured("skill.free", SkillCategory.Active,
                SkillTargetType.Self, SkillResourceType.None);

            Assert.IsTrue(Validate(skill).IsValid);
        }

        [Test]
        public void CombinationValidationIsDeterministic()
        {
            SkillDefinition skill = Configured("skill.messy", SkillCategory.None,
                SkillTargetType.None, SkillResourceType.None, 5f);

            ValidationReport first = Validate(skill);
            ValidationReport second = Validate(skill);

            Assert.AreEqual(first.Messages.Count, second.Messages.Count);

            for (int i = 0; i < first.Messages.Count; i++)
            {
                Assert.AreEqual(first.Messages[i].Code, second.Messages[i].Code, "index " + i);
                Assert.AreEqual(first.Messages[i].Message, second.Messages[i].Message, "index " + i);
            }
        }
    }
}
