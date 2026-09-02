using System;
using System.Reflection;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    internal sealed class SkillAvailabilityTests : SkillTestBase
    {
        [Test]
        public void ACommonSkillNeedsNoClassOrJob()
        {
            SkillDefinition skill = AddSkill("skill.common");

            Assert.IsTrue(Validate(skill).IsValid);
            Assert.IsFalse(skill.RequiredClass.IsValid);
            Assert.IsFalse(skill.RequiredJob.IsValid);
        }

        [Test]
        public void AClassSkillPasses()
        {
            SkillDefinition skill = AddSkill("skill.class", requiredClass: ClassA);

            Assert.IsTrue(Validate(skill).IsValid);
            Assert.AreEqual(new DefinitionId(ClassA), skill.RequiredClass);
        }

        [Test]
        public void AJobSkillPasses()
        {
            SkillDefinition skill = AddSkill("skill.job", requiredClass: ClassA, requiredJob: JobA);

            Assert.IsTrue(Validate(skill).IsValid);
            Assert.AreEqual(new DefinitionId(JobA), skill.RequiredJob);
        }

        [Test]
        public void AnUnknownClassIsRejected()
        {
            ValidationReport report = Validate(
                AddSkill("skill.ghostclass", requiredClass: "class.ghost"));

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.MissingReference, report.Messages[0].Code);
        }

        [Test]
        public void AnUnknownJobIsRejected()
        {
            ValidationReport report = Validate(
                AddSkill("skill.ghostjob", requiredJob: "job.ghost"));

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.MissingReference, report.Messages[0].Code);
        }

        [Test]
        public void AJobFromAnotherClassIsRejected()
        {
            ValidationReport report = Validate(
                AddSkill("skill.mismatch", requiredClass: ClassA, requiredJob: JobB));

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.InvalidConfiguration, report.Messages[0].Code);
            StringAssert.Contains("belongs to class", report.Messages[0].Message);
        }

        [Test]
        public void APrerequisiteAtAValidLevelPasses()
        {
            AddSkill("skill.base", 5);
            SkillDefinition advanced = AddSkill("skill.advanced",
                prerequisites: new[] { new SkillPrerequisite(new DefinitionId("skill.base"), 3) });

            Assert.IsTrue(Validate(advanced).IsValid);
            Assert.AreEqual(3, advanced.Prerequisites[0].Level);
        }

        [Test]
        public void AnUnknownPrerequisiteIsRejected()
        {
            ValidationReport report = Validate(AddSkill("skill.orphan",
                prerequisites: new[] { new SkillPrerequisite(new DefinitionId("skill.ghost"), 1) }));

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.MissingReference, report.Messages[0].Code);
        }

        [Test]
        public void APrerequisiteLevelBeyondThatSkillsMaximumIsRejected()
        {
            AddSkill("skill.base", 3);
            ValidationReport report = Validate(AddSkill("skill.tooHigh",
                prerequisites: new[] { new SkillPrerequisite(new DefinitionId("skill.base"), 9) }));

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.ValueOutOfRange, report.Messages[0].Code);
            StringAssert.Contains("only reaches", report.Messages[0].Message);
        }

        [Test]
        public void APrerequisiteLevelBelowOneIsRejected()
        {
            AddSkill("skill.base", 3);
            ValidationReport report = Validate(AddSkill("skill.zero",
                prerequisites: new[] { new SkillPrerequisite(new DefinitionId("skill.base"), 0) }));

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.ValueOutOfRange, report.Messages[0].Code);
        }

        [Test]
        public void ASelfPrerequisiteIsRejected()
        {
            SkillDefinition skill = AddSkill("skill.loop", 1,
                prerequisites: new[] { new SkillPrerequisite(new DefinitionId("skill.loop"), 1) });

            ValidationReport report = Validate(skill);

            Assert.IsFalse(report.IsValid);
            StringAssert.Contains("its own prerequisite", report.Messages[0].Message);
        }

        [Test]
        public void APrerequisiteWithNoSkillIsRejected()
        {
            ValidationReport report = Validate(AddSkill("skill.empty",
                prerequisites: new[] { new SkillPrerequisite(DefinitionId.None, 1) }));

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.MissingDefinitionId, report.Messages[0].Code);
        }

        [Test]
        public void ValidationIsDeterministic()
        {
            SkillDefinition skill = AddSkill("skill.messy", 2, -1f, -1f,
                requiredClass: "class.ghost",
                levels: new[] { Level(2, 0), Level(1, 1) });

            ValidationReport first = Validate(skill);
            ValidationReport second = Validate(skill);

            Assert.AreEqual(first.Messages.Count, second.Messages.Count);

            for (int i = 0; i < first.Messages.Count; i++)
            {
                Assert.AreEqual(first.Messages[i].Code, second.Messages[i].Code, "index " + i);
                Assert.AreEqual(first.Messages[i].Message, second.Messages[i].Message, "index " + i);
            }
        }

        [Test]
        public void NoPerClassSkillListExistsInCode()
        {
            string[] forbidden = { "MageSkills", "ArcherSkills", "SwordsmanSkills", "ClericSkills" };

            foreach (Type type in typeof(SkillDefinition).Assembly.GetTypes())
            {
                foreach (string name in forbidden)
                {
                    Assert.AreNotEqual(name, type.Name);
                }

                foreach (MemberInfo member in type.GetMembers())
                {
                    foreach (string name in forbidden)
                    {
                        Assert.AreNotEqual(name, member.Name,
                            "Skill availability is authored, never listed in code.");
                    }
                }
            }
        }

        [Test]
        public void NoExecutionSystemWasCreated()
        {
            string[] forbidden =
            {
                "SkillExecutor", "CombatSystem", "DamageSystem", "StatusSystem",
                "Cooldown Timer", "Projectile"
            };

            foreach (Type type in typeof(SkillDefinition).Assembly.GetTypes())
            {
                foreach (string name in forbidden)
                {
                    Assert.AreNotEqual(name.Replace(" ", string.Empty), type.Name,
                        "Execution belongs to a later step.");
                }
            }
        }

        [Test]
        public void NullRegistriesAreRejected()
        {
            Assert.Throws<ArgumentNullException>(
                () => new SkillValidationRule(null, Classes, Jobs));
            Assert.Throws<ArgumentNullException>(
                () => new SkillValidationRule(Skills, null, Jobs));
            Assert.Throws<ArgumentNullException>(
                () => new SkillValidationRule(Skills, Classes, null));
        }
    }
}
