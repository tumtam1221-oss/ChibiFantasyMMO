using System;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Proves the JobChangeLevel / LevelRequirement duplication reported in 05.7 can no
    /// longer go unnoticed.
    /// </summary>
    internal sealed class ClassProgressionValidationTests : ClassJobTestBase
    {
        private ValidationReport Validate(ClassDefinition definition)
        {
            var validator = new DefinitionValidator(
                new IDefinitionValidationRule[] { new ClassProgressionValidationRule(Jobs) });
            return validator.Validate(definition, Classes);
        }

        [Test]
        public void AgreeingLevelsPass()
        {
            AddSwordsmanTree();

            Classes.TryGet(new DefinitionId(Swordsman), out ClassDefinition swordsman);

            Assert.IsTrue(Validate(swordsman).IsValid,
                "The class advertises 15 and its first job requires 15.");
        }

        [Test]
        public void DisagreeingLevelsAreReported()
        {
            // The class advertises 20 while its only first job requires 15.
            AddClass(Mage, 20, "job.mage.first");
            AddJob("job.mage.first", Mage, 1, 15, null);

            Classes.TryGet(new DefinitionId(Mage), out ClassDefinition mage);
            ValidationReport report = Validate(mage);

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.InvalidConfiguration, report.Messages[0].Code);
            StringAssert.Contains("authoritative", report.Messages[0].Message);
        }

        [Test]
        public void TheLowestRequirementAmongOfferedJobsIsUsed()
        {
            AddClass(Archer, 12, "job.archer.a", "job.archer.b");
            AddJob("job.archer.a", Archer, 1, 12, null);
            AddJob("job.archer.b", Archer, 1, 18, null);

            Classes.TryGet(new DefinitionId(Archer), out ClassDefinition archer);

            Assert.IsTrue(Validate(archer).IsValid,
                "Advertising the earliest reachable job is correct.");
        }

        [Test]
        public void AClassWithNoAdvancementIsLegal()
        {
            AddClass(Cleric, 0);

            Classes.TryGet(new DefinitionId(Cleric), out ClassDefinition cleric);

            Assert.IsTrue(Validate(cleric).IsValid, "A class that never changes job is allowed.");
        }

        [Test]
        public void UnknownFirstJobIsReported()
        {
            AddClass(Mage, 15, "job.missing");

            Classes.TryGet(new DefinitionId(Mage), out ClassDefinition mage);
            ValidationReport report = Validate(mage);

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.MissingReference, report.Messages[0].Code);
        }

        [Test]
        public void FirstJobFromAnotherClassIsReported()
        {
            AddClass(Mage, 15, "job.archer.first");
            AddClass(Archer, 15, "job.archer.first");
            AddJob("job.archer.first", Archer, 1, 15, null);

            Classes.TryGet(new DefinitionId(Mage), out ClassDefinition mage);
            ValidationReport report = Validate(mage);

            Assert.IsFalse(report.IsValid);
            StringAssert.Contains("another class", report.Messages[0].Message);
        }

        [Test]
        public void AFirstJobDemandingAPredecessorIsReported()
        {
            AddClass(Mage, 15, "job.mage.second");
            AddJob("job.mage.second", Mage, 2, 15, "job.mage.first");

            Classes.TryGet(new DefinitionId(Mage), out ClassDefinition mage);
            ValidationReport report = Validate(mage);

            Assert.IsFalse(report.IsValid);
            StringAssert.Contains("previous job", report.Messages[0].Message);
        }

        [Test]
        public void ValidationIsDeterministic()
        {
            AddClass(Mage, 99, "job.mage.first", "job.missing");
            AddJob("job.mage.first", Mage, 1, 15, null);

            Classes.TryGet(new DefinitionId(Mage), out ClassDefinition mage);

            ValidationReport first = Validate(mage);
            ValidationReport second = Validate(mage);

            Assert.AreEqual(first.Messages.Count, second.Messages.Count);

            for (int i = 0; i < first.Messages.Count; i++)
            {
                Assert.AreEqual(first.Messages[i].Code, second.Messages[i].Code, "index " + i);
                Assert.AreEqual(first.Messages[i].Message, second.Messages[i].Message, "index " + i);
            }
        }

        [Test]
        public void NullJobRegistryIsRejected()
        {
            Assert.Throws<ArgumentNullException>(() => new ClassProgressionValidationRule(null));
        }
    }
}
