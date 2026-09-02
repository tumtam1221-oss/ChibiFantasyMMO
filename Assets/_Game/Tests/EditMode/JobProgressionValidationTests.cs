using System;
using System.Reflection;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    internal sealed class JobProgressionValidationTests : ClassJobTestBase
    {
        private ValidationReport Validate(JobDefinition job)
        {
            var validator = new DefinitionValidator(
                new IDefinitionValidationRule[] { new JobProgressionValidationRule(Jobs, Classes) });
            return validator.Validate(job, Jobs);
        }

        [Test]
        public void AWellFormedTreePasses()
        {
            AddSwordsmanTree();

            foreach (JobDefinition job in Jobs.All)
            {
                ValidationReport report = Validate(job);
                Assert.IsTrue(report.IsValid, job.Id + ": "
                    + (report.Messages.Count > 0 ? report.Messages[0].Message : string.Empty));
            }
        }

        [Test]
        public void DirectCycleIsDetected()
        {
            AddClass(Mage, 15, "job.a");
            AddJob("job.a", Mage, 1, 15, null, "job.b");
            AddJob("job.b", Mage, 2, 35, "job.a", "job.a");

            ValidationReport report = Validate(Jobs.All[0]);

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.InvalidConfiguration, report.Messages[0].Code);
            StringAssert.Contains("leads back", report.Messages[0].Message);
        }

        [Test]
        public void LongerCycleIsDetected()
        {
            AddClass(Mage, 15, "job.a");
            AddJob("job.a", Mage, 1, 15, null, "job.b");
            AddJob("job.b", Mage, 2, 35, "job.a", "job.c");
            AddJob("job.c", Mage, 3, 60, "job.b", "job.a");

            ValidationReport report = Validate(Jobs.All[0]);

            Assert.IsFalse(report.IsValid);
            StringAssert.Contains("leads back", report.Messages[0].Message);
        }

        [Test]
        public void SelfReferenceInNextJobsIsRejected()
        {
            AddClass(Mage, 15, "job.self");
            AddJob("job.self", Mage, 1, 15, null, "job.self");

            ValidationReport report = Validate(Jobs.All[0]);

            Assert.IsFalse(report.IsValid);
            StringAssert.Contains("itself", report.Messages[0].Message);
        }

        [Test]
        public void SelfPrerequisiteIsRejected()
        {
            AddClass(Mage, 15, "job.loop");
            AddJob("job.loop", Mage, 1, 15, "job.loop");

            ValidationReport report = Validate(Jobs.All[0]);

            Assert.IsFalse(report.IsValid);
            StringAssert.Contains("own prerequisite", report.Messages[0].Message);
        }

        [Test]
        public void UnknownClassIsRejected()
        {
            AddJob("job.orphan", "class.ghost", 1, 15, null);

            ValidationReport report = Validate(Jobs.All[0]);

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.MissingReference, report.Messages[0].Code);
        }

        [Test]
        public void UnknownNextJobIsRejected()
        {
            AddClass(Mage, 15, "job.a");
            AddJob("job.a", Mage, 1, 15, null, "job.missing");

            ValidationReport report = Validate(Jobs.All[0]);

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.MissingReference, report.Messages[0].Code);
        }

        [Test]
        public void NextJobFromAnotherClassIsRejected()
        {
            AddClass(Mage, 15, "job.m");
            AddClass(Archer, 15, "job.ar");
            AddJob("job.m", Mage, 1, 15, null, "job.ar");
            AddJob("job.ar", Archer, 1, 15, null);

            ValidationReport report = Validate(Jobs.All[0]);

            Assert.IsFalse(report.IsValid);
            StringAssert.Contains("different class", report.Messages[0].Message);
        }

        [TestCase(0, 15)]
        [TestCase(1, -5)]
        public void InvalidTierOrLevelIsRejected(int tier, int levelRequirement)
        {
            AddClass(Mage, 15, "job.bad");
            AddJob("job.bad", Mage, tier, levelRequirement, null);

            ValidationReport report = Validate(Jobs.All[0]);

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.InvalidConfiguration, report.Messages[0].Code);
        }

        [Test]
        public void ValidationIsDeterministic()
        {
            AddClass(Mage, 15, "job.a");
            AddJob("job.a", Mage, 0, -1, "job.a", "job.missing");

            ValidationReport first = Validate(Jobs.All[0]);
            ValidationReport second = Validate(Jobs.All[0]);

            Assert.AreEqual(first.Messages.Count, second.Messages.Count);

            for (int i = 0; i < first.Messages.Count; i++)
            {
                Assert.AreEqual(first.Messages[i].Code, second.Messages[i].Code, "index " + i);
                Assert.AreEqual(first.Messages[i].Message, second.Messages[i].Message, "index " + i);
            }
        }

        [Test]
        public void NoDuplicateClassOrJobIdentitySystemExists()
        {
            Assembly data = typeof(ClassDefinition).Assembly;
            int classTypes = 0;
            int jobTypes = 0;

            foreach (Type type in data.GetTypes())
            {
                if (!type.IsPublic)
                {
                    continue;
                }

                if (type.Name == "ClassDefinition")
                {
                    classTypes++;
                }

                if (type.Name == "JobDefinition")
                {
                    jobTypes++;
                }

                Assert.AreNotEqual("ClassId", type.Name, "DefinitionId already identifies classes.");
                Assert.AreNotEqual("JobId", type.Name, "DefinitionId already identifies jobs.");
            }

            Assert.AreEqual(1, classTypes);
            Assert.AreEqual(1, jobTypes);
        }

        [Test]
        public void NoSkillCombatQuestOrNpcSystemWasCreated()
        {
            string[] forbidden =
            {
                "SkillTree", "SkillPoint", "Combat", "Damage", "Quest", "Dialogue", "Npc"
            };

            Type[] introduced =
            {
                typeof(CharacterClassState), typeof(JobChangeEvaluator),
                typeof(JobProgressionValidationRule), typeof(JobChangeEligibility)
            };

            foreach (Type type in introduced)
            {
                foreach (MemberInfo member in type.GetMembers())
                {
                    foreach (string name in forbidden)
                    {
                        Assert.IsFalse(member.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0,
                            type.Name + "." + member.Name + " belongs to a later system.");
                    }
                }
            }
        }
    }
}
