using System;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Covers the two edges joining skill content to character content:
    /// <see cref="ClassDefinition.StartingSkills"/> and <see cref="JobDefinition.Skills"/>.
    /// </summary>
    /// <remarks>
    /// Every class, job and skill here is a TEST FIXTURE with a deliberately generic name.
    /// No production content is authored in this step.
    /// </remarks>
    internal sealed class SkillGrantValidationTests : SkillTestBase
    {
        private const string Grantor = "class.grantor";
        private const string GrantorJob = "job.grantor";

        private ValidationReport ValidateGrants(IDefinition definition)
        {
            var validator = new DefinitionValidator(
                new IDefinitionValidationRule[] { new SkillGrantValidationRule(Skills) });
            return validator.Validate(definition, Skills);
        }

        private static void AssertFirst(ValidationReport report, ValidationCode code, string contains)
        {
            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(code, report.Messages[0].Code);
            StringAssert.Contains(contains, report.Messages[0].Message);
        }

        // ---------- classes ----------

        [Test]
        public void AClassCanStartWithSkillsThatExistAndAllowIt()
        {
            ClassDefinition characterClass = AddClass(Grantor);
            AddSkill("skill.free");
            AddSkill("skill.mine", requiredClass: Grantor);
            SetStartingSkills(characterClass, "skill.free", "skill.mine");

            ValidationReport report = ValidateGrants(characterClass);

            Assert.IsTrue(report.IsValid,
                report.Messages.Count > 0 ? report.Messages[0].Message : string.Empty);
            Assert.AreEqual(2, characterClass.StartingSkills.Length);
        }

        [Test]
        public void AClassWithNoStartingSkillsIsLegal()
        {
            Assert.IsTrue(ValidateGrants(AddClass(Grantor)).IsValid);
        }

        [Test]
        public void AStartingSkillThatDoesNotExistIsRejected()
        {
            ClassDefinition characterClass = AddClass(Grantor);
            SetStartingSkills(characterClass, "skill.ghost");

            AssertFirst(ValidateGrants(characterClass),
                ValidationCode.MissingReference, "does not exist");
        }

        [Test]
        public void AnUnsetStartingSkillEntryIsRejected()
        {
            ClassDefinition characterClass = AddClass(Grantor);
            SetPrivate(characterClass, "_startingSkills", Ids((string)null));

            AssertFirst(ValidateGrants(characterClass),
                ValidationCode.MissingDefinitionId, "names no skill");
        }

        [Test]
        public void AStartingSkillListedTwiceIsRejected()
        {
            ClassDefinition characterClass = AddClass(Grantor);
            AddSkill("skill.free");
            SetStartingSkills(characterClass, "skill.free", "skill.free");

            AssertFirst(ValidateGrants(characterClass),
                ValidationCode.DuplicateDefinitionId, "listed more than once");
        }

        [Test]
        public void AClassCannotStartWithAnotherClassesSkill()
        {
            ClassDefinition characterClass = AddClass(Grantor);
            AddSkill("skill.foreign", requiredClass: ClassA);
            SetStartingSkills(characterClass, "skill.foreign");

            AssertFirst(ValidateGrants(characterClass),
                ValidationCode.InvalidConfiguration, "requires class '" + ClassA + "'");
        }

        [Test]
        public void AClassCannotStartWithAJobGatedSkill()
        {
            ClassDefinition characterClass = AddClass(Grantor);
            AddSkill("skill.jobbed", requiredClass: Grantor, requiredJob: JobA);
            SetStartingSkills(characterClass, "skill.jobbed");

            AssertFirst(ValidateGrants(characterClass),
                ValidationCode.InvalidConfiguration, "cannot yet hold");
        }

        [Test]
        public void AStartingSkillWhosePrerequisiteIsNotAlsoGrantedIsRejected()
        {
            ClassDefinition characterClass = AddClass(Grantor);
            AddSkill("skill.base", 3);
            AddSkill("skill.advanced",
                prerequisites: new[] { new SkillPrerequisite(new DefinitionId("skill.base"), 1) });
            SetStartingSkills(characterClass, "skill.advanced");

            AssertFirst(ValidateGrants(characterClass),
                ValidationCode.InvalidConfiguration, "is not also given");
        }

        [Test]
        public void AStartingSkillWhosePrerequisiteIsAlsoGrantedPasses()
        {
            ClassDefinition characterClass = AddClass(Grantor);
            AddSkill("skill.base", 3);
            AddSkill("skill.advanced",
                prerequisites: new[] { new SkillPrerequisite(new DefinitionId("skill.base"), 1) });
            SetStartingSkills(characterClass, "skill.base", "skill.advanced");

            Assert.IsTrue(ValidateGrants(characterClass).IsValid);
        }

        // ---------- jobs ----------

        [Test]
        public void AJobCanUnlockSkillsThatExistAndAllowIt()
        {
            AddClass(Grantor);
            JobDefinition job = AddJob(GrantorJob, Grantor);
            AddSkill("skill.free");
            AddSkill("skill.classbound", requiredClass: Grantor);
            AddSkill("skill.jobbound", requiredClass: Grantor, requiredJob: GrantorJob);
            SetJobSkills(job, "skill.free", "skill.classbound", "skill.jobbound");

            ValidationReport report = ValidateGrants(job);

            Assert.IsTrue(report.IsValid,
                report.Messages.Count > 0 ? report.Messages[0].Message : string.Empty);
            Assert.AreEqual(3, job.Skills.Length);
        }

        [Test]
        public void AJobWithNoSkillsIsLegal()
        {
            AddClass(Grantor);
            Assert.IsTrue(ValidateGrants(AddJob(GrantorJob, Grantor)).IsValid);
        }

        [Test]
        public void AJobSkillThatDoesNotExistIsRejected()
        {
            AddClass(Grantor);
            JobDefinition job = AddJob(GrantorJob, Grantor);
            SetJobSkills(job, "skill.ghost");

            AssertFirst(ValidateGrants(job), ValidationCode.MissingReference, "does not exist");
        }

        [Test]
        public void AJobSkillListedTwiceIsRejected()
        {
            AddClass(Grantor);
            JobDefinition job = AddJob(GrantorJob, Grantor);
            AddSkill("skill.free");
            SetJobSkills(job, "skill.free", "skill.free");

            AssertFirst(ValidateGrants(job),
                ValidationCode.DuplicateDefinitionId, "listed more than once");
        }

        [Test]
        public void AJobCannotUnlockAnotherJobsSkill()
        {
            AddClass(Grantor);
            JobDefinition job = AddJob(GrantorJob, Grantor);
            AddSkill("skill.foreign", requiredJob: JobA);
            SetJobSkills(job, "skill.foreign");

            AssertFirst(ValidateGrants(job),
                ValidationCode.InvalidConfiguration, "requires job '" + JobA + "'");
        }

        [Test]
        public void AJobCannotUnlockASkillBelongingToAnotherClass()
        {
            AddClass(Grantor);
            JobDefinition job = AddJob(GrantorJob, Grantor);
            AddSkill("skill.foreign", requiredClass: ClassA);
            SetJobSkills(job, "skill.foreign");

            AssertFirst(ValidateGrants(job),
                ValidationCode.InvalidConfiguration, "rather than this job's class");
        }

        // ---------- rule behaviour ----------

        [Test]
        public void TheRuleIgnoresDefinitionsThatGrantNothing()
        {
            Assert.IsTrue(ValidateGrants(AddSkill("skill.plain")).IsValid);
        }

        [Test]
        public void ValidationDoesNotMutateTheDefinition()
        {
            ClassDefinition characterClass = AddClass(Grantor);
            SetStartingSkills(characterClass, "skill.ghost", "skill.ghost", null);

            string before = JsonUtility.ToJson(characterClass);
            ValidateGrants(characterClass);

            Assert.AreEqual(before, JsonUtility.ToJson(characterClass));
        }

        [Test]
        public void ValidationIsDeterministic()
        {
            ClassDefinition characterClass = AddClass(Grantor);
            AddSkill("skill.foreign", requiredClass: ClassA, requiredJob: JobA);
            SetStartingSkills(characterClass, "skill.ghost", null, "skill.foreign", "skill.foreign");

            ValidationReport first = ValidateGrants(characterClass);
            ValidationReport second = ValidateGrants(characterClass);

            Assert.AreEqual(first.Messages.Count, second.Messages.Count);

            for (int i = 0; i < first.Messages.Count; i++)
            {
                Assert.AreEqual(first.Messages[i].Code, second.Messages[i].Code, "index " + i);
                Assert.AreEqual(first.Messages[i].Message, second.Messages[i].Message, "index " + i);
            }
        }

        [Test]
        public void EveryFaultInAListIsReportedRatherThanOnlyTheFirst()
        {
            ClassDefinition characterClass = AddClass(Grantor);
            AddSkill("skill.foreign", requiredClass: ClassA);
            SetStartingSkills(characterClass, "skill.ghost", null, "skill.foreign");

            ValidationReport report = ValidateGrants(characterClass);

            Assert.AreEqual(3, report.ErrorCount,
                "A content author fixing one entry at a time is a guessing game.");
        }

        [Test]
        public void NullRegistryIsRejected()
        {
            Assert.Throws<ArgumentNullException>(() => new SkillGrantValidationRule(null));
        }

        // ---------- authoring integration ----------

        [Test]
        public void ValidateAllCoversSkillsClassesAndJobsInOneCall()
        {
            var stats = new DefinitionRegistry<StatDefinition>();
            var statusEffects = new DefinitionRegistry<StatusEffectDefinition>();

            ClassDefinition characterClass = AddClass(Grantor);
            JobDefinition job = AddJob(GrantorJob, Grantor);

            // Skill fault: no category. Class fault: unknown grant. Job fault: unknown grant.
            SkillDefinition broken = AddSkill("skill.broken");
            SetPrivate(broken, "_category", SkillCategory.None);
            SetStartingSkills(characterClass, "skill.ghost");
            SetJobSkills(job, "skill.phantom");

            var validator = new SkillContentValidator(Skills, Classes, Jobs, stats, statusEffects);
            ValidationReport report = validator.ValidateAll();

            Assert.IsFalse(report.IsValid);

            bool sawSkill = false;
            bool sawClass = false;
            bool sawJob = false;

            foreach (ValidationMessage message in report.Messages)
            {
                if (message.Message.Contains("no category"))
                {
                    sawSkill = true;
                }

                if (message.Message.Contains("skill.ghost"))
                {
                    sawClass = true;
                }

                if (message.Message.Contains("skill.phantom"))
                {
                    sawJob = true;
                }
            }

            Assert.IsTrue(sawSkill, "Skills were not validated.");
            Assert.IsTrue(sawClass, "Class grants were not validated.");
            Assert.IsTrue(sawJob, "Job grants were not validated.");
        }

        [Test]
        public void ValidateAllPassesOnCoherentContent()
        {
            var stats = new DefinitionRegistry<StatDefinition>();
            var statusEffects = new DefinitionRegistry<StatusEffectDefinition>();

            ClassDefinition characterClass = AddClass(Grantor);
            JobDefinition job = AddJob(GrantorJob, Grantor);
            AddSkill("skill.free");
            AddSkill("skill.jobbound", requiredClass: Grantor, requiredJob: GrantorJob);
            SetStartingSkills(characterClass, "skill.free");
            SetJobSkills(job, "skill.jobbound");

            var validator = new SkillContentValidator(Skills, Classes, Jobs, stats, statusEffects);
            ValidationReport report = validator.ValidateAll();

            Assert.IsTrue(report.IsValid,
                report.Messages.Count > 0 ? report.Messages[0].ToString() : string.Empty);
        }

        [Test]
        public void ASkillIsUnaffectedByHowItIsGranted()
        {
            var stats = new DefinitionRegistry<StatDefinition>();
            var statusEffects = new DefinitionRegistry<StatusEffectDefinition>();

            ClassDefinition characterClass = AddClass(Grantor);
            SkillDefinition skill = AddSkill("skill.free");
            SetStartingSkills(characterClass, "skill.ghost");

            var validator = new SkillContentValidator(Skills, Classes, Jobs, stats, statusEffects);

            Assert.IsTrue(validator.Validate(skill).IsValid,
                "A broken grant elsewhere must not make the skill itself invalid.");
            Assert.IsFalse(validator.Validate(characterClass).IsValid);
        }
    }
}
