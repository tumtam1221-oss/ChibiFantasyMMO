using System;
using System.Collections.Generic;
using System.Reflection;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Evaluating a whole set of skills, including the ones a character's class and job
    /// actually offer.
    /// </summary>
    /// <remarks>
    /// Every class, job and skill here is a TEST FIXTURE with a deliberately generic name.
    /// </remarks>
    internal sealed class SkillAvailabilityListTests : SkillUpgradeTestBase
    {
        private DefinitionRegistry<ClassDefinition> _classes;
        private DefinitionRegistry<JobDefinition> _jobs;
        private List<UnityEngine.Object> _definitions;

        [SetUp]
        public void SetUpContent()
        {
            _classes = new DefinitionRegistry<ClassDefinition>();
            _jobs = new DefinitionRegistry<JobDefinition>();
            _definitions = new List<UnityEngine.Object>();
        }

        [TearDown]
        public void TearDownContent()
        {
            foreach (UnityEngine.Object created in _definitions)
            {
                UnityEngine.Object.DestroyImmediate(created);
            }
        }

        private ClassDefinition AddClass(string id, params string[] startingSkills)
        {
            var definition = ScriptableObject.CreateInstance<ClassDefinition>();
            JsonUtility.FromJsonOverwrite("{\"_id\":{\"_value\":\"" + id + "\"}}", definition);
            SetPrivate(definition, "_startingSkills", Ids(startingSkills));
            _definitions.Add(definition);
            _classes.Register(definition);
            return definition;
        }

        private JobDefinition AddJobWithSkills(string id, string baseClass, params string[] skills)
        {
            var definition = ScriptableObject.CreateInstance<JobDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_baseClass\":{\"_value\":\"" + baseClass
                + "\"},\"_tier\":1,\"_levelRequirement\":1}", definition);
            SetPrivate(definition, "_skills", Ids(skills));
            _definitions.Add(definition);
            _jobs.Register(definition);
            return definition;
        }

        private static DefinitionId[] Ids(string[] values)
        {
            var ids = new DefinitionId[values.Length];

            for (int i = 0; i < values.Length; i++)
            {
                ids[i] = new DefinitionId(values[i]);
            }

            return ids;
        }

        private IReadOnlyList<SkillAvailability> Granted(CharacterSkillsState learned,
            CharacterClassState classState, int level)
        {
            return new SkillAvailabilityEvaluator()
                .EvaluateGranted(learned, classState, level, Skills, _classes, _jobs);
        }

        // ---------- collections ----------

        [Test]
        public void SeveralSkillsProduceOneResultEachInTheOrderAsked()
        {
            AddRankedSkill("skill.one", new[] { 1, 2 });
            AddRankedSkill("skill.two", new[] { 50, 60 });
            AddRankedSkill("skill.three", new[] { 1, 2 });

            // Level 10 clears rank two of skill.three but not rank one of skill.two.
            IReadOnlyList<SkillAvailability> results = new SkillAvailabilityEvaluator().EvaluateAll(
                Holding("skill.three", 1), NewCharacter(ClassA), 10,
                Ids(new[] { "skill.one", "skill.two", "skill.three", "skill.ghost" }), Skills);

            Assert.AreEqual(4, results.Count);
            Assert.AreEqual(Id("skill.one"), results[0].Skill);
            Assert.AreEqual(SkillAvailabilityStatus.Learnable, results[0].Status);
            Assert.AreEqual(SkillAvailabilityStatus.Blocked, results[1].Status);
            Assert.AreEqual(SkillAvailabilityStatus.Upgradeable, results[2].Status);
            Assert.AreEqual(SkillAvailabilityStatus.Unknown, results[3].Status);
        }

        [Test]
        public void TheSameQueryAlwaysReturnsTheSameOrderAndResults()
        {
            AddRankedSkill("skill.one", new[] { 1, 2 });
            AddRankedSkill("skill.two", new[] { 1, 2 });
            AddRankedSkill("skill.three", new[] { 99, 99 });

            DefinitionId[] candidates = Ids(new[] { "skill.three", "skill.one", "skill.two" });
            var evaluator = new SkillAvailabilityEvaluator();
            CharacterClassState characterClass = NewCharacter(ClassA);

            IReadOnlyList<SkillAvailability> first =
                evaluator.EvaluateAll(NewSkills(), characterClass, 1, candidates, Skills);
            IReadOnlyList<SkillAvailability> second =
                evaluator.EvaluateAll(NewSkills(), characterClass, 1, candidates, Skills);

            Assert.AreEqual(first.Count, second.Count);

            for (int i = 0; i < first.Count; i++)
            {
                Assert.AreEqual(first[i].Skill, second[i].Skill, "index " + i);
                Assert.AreEqual(first[i].Status, second[i].Status, "index " + i);
            }
        }

        [Test]
        public void AnEmptyQueryReturnsNothing()
        {
            Assert.AreEqual(0, new SkillAvailabilityEvaluator().EvaluateAll(
                NewSkills(), NewCharacter(ClassA), 1, new DefinitionId[0], Skills).Count);
        }

        // ---------- class starting skills ----------

        [Test]
        public void AClassesStartingSkillsAreOfferedToItsCharacters()
        {
            AddRankedSkill("skill.start_a", new[] { 1, 2 });
            AddRankedSkill("skill.start_b", new[] { 1, 2 });
            AddClass(ClassA, "skill.start_a", "skill.start_b");

            IReadOnlyList<SkillAvailability> results =
                Granted(NewSkills(), NewCharacter(ClassA), 1);

            Assert.AreEqual(2, results.Count);
            Assert.AreEqual(Id("skill.start_a"), results[0].Skill);
            Assert.AreEqual(Id("skill.start_b"), results[1].Skill);
            Assert.AreEqual(SkillAvailabilityStatus.Learnable, results[0].Status);
        }

        [Test]
        public void StartingSkillInformationGrantsNothing()
        {
            // Knowing a class offers a skill is not the same as holding it. Acquisition at
            // creation is a later step, and this query must not quietly perform it.
            AddRankedSkill("skill.start", new[] { 1, 2 });
            AddClass(ClassA, "skill.start");
            CharacterSkillsState learned = NewSkills();

            Granted(learned, NewCharacter(ClassA), 1);
            Granted(learned, NewCharacter(ClassA), 1);

            Assert.AreEqual(0, learned.Count, "Availability must never learn a skill.");
            Assert.AreEqual(Revision.Initial, learned.Revision);
        }

        [Test]
        public void AStartingSkillAlreadyHeldIsReportedAsHeldNotOfferedTwice()
        {
            AddRankedSkill("skill.start", new[] { 1, 2 });
            AddClass(ClassA, "skill.start");

            IReadOnlyList<SkillAvailability> results =
                Granted(Holding("skill.start", 1), NewCharacter(ClassA), 99);

            Assert.AreEqual(1, results.Count);
            Assert.IsTrue(results[0].IsLearned);
            Assert.AreEqual(SkillAvailabilityStatus.Upgradeable, results[0].Status);
        }

        // ---------- job skills ----------

        [Test]
        public void TheHeldJobsSkillsAreOfferedAfterTheClassesOwn()
        {
            AddRankedSkill("skill.start", new[] { 1, 2 });
            AddRankedSkill("skill.job", new[] { 1, 2 }, requiredJob: JobA);
            AddClass(ClassA, "skill.start");
            AddJobWithSkills(JobA, ClassA, "skill.job");

            IReadOnlyList<SkillAvailability> results =
                Granted(NewSkills(), NewCharacter(ClassA, JobA), 1);

            Assert.AreEqual(2, results.Count);
            Assert.AreEqual(Id("skill.start"), results[0].Skill);
            Assert.AreEqual(Id("skill.job"), results[1].Skill);
            Assert.AreEqual(SkillAvailabilityStatus.Learnable, results[1].Status);
        }

        [Test]
        public void AJobsSkillsAreNotOfferedBeforeTheJobIsHeld()
        {
            AddRankedSkill("skill.start", new[] { 1, 2 });
            AddRankedSkill("skill.job", new[] { 1, 2 }, requiredJob: JobA);
            AddClass(ClassA, "skill.start");
            AddJobWithSkills(JobA, ClassA, "skill.job");

            IReadOnlyList<SkillAvailability> results =
                Granted(NewSkills(), NewCharacter(ClassA), 1);

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(Id("skill.start"), results[0].Skill);
        }

        [Test]
        public void AnotherJobsSkillsAreNotOffered()
        {
            AddRankedSkill("skill.a", new[] { 1, 2 }, requiredJob: JobA);
            AddRankedSkill("skill.b", new[] { 1, 2 }, requiredJob: JobB);
            AddClass(ClassA);
            AddJobWithSkills(JobA, ClassA, "skill.a");
            AddJobWithSkills(JobB, ClassA, "skill.b");

            IReadOnlyList<SkillAvailability> results =
                Granted(NewSkills(), NewCharacter(ClassA, JobB), 1);

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(Id("skill.b"), results[0].Skill);
        }

        [Test]
        public void JobSkillInformationGrantsNothing()
        {
            AddRankedSkill("skill.job", new[] { 1, 2 }, requiredJob: JobA);
            AddClass(ClassA);
            AddJobWithSkills(JobA, ClassA, "skill.job");
            CharacterSkillsState learned = NewSkills();

            Granted(learned, NewCharacter(ClassA, JobA), 99);
            Granted(learned, NewCharacter(ClassA, JobA), 99);

            Assert.AreEqual(0, learned.Count, "Availability must never learn a skill.");
            Assert.AreEqual(Revision.Initial, learned.Revision);
        }

        [Test]
        public void ASkillOfferedByBothClassAndJobIsAnsweredOnce()
        {
            AddRankedSkill("skill.shared", new[] { 1, 2 });
            AddClass(ClassA, "skill.shared");
            AddJobWithSkills(JobA, ClassA, "skill.shared");

            IReadOnlyList<SkillAvailability> results =
                Granted(NewSkills(), NewCharacter(ClassA, JobA), 1);

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(Id("skill.shared"), results[0].Skill);
        }

        [Test]
        public void GrantedSkillsStillObeyEveryRequirement()
        {
            // Being offered by a class is not a bypass; the level gate still applies.
            AddRankedSkill("skill.early", new[] { 1, 2 });
            AddRankedSkill("skill.late", new[] { 40, 50 });
            AddClass(ClassA, "skill.early", "skill.late");

            IReadOnlyList<SkillAvailability> results =
                Granted(NewSkills(), NewCharacter(ClassA), 10);

            Assert.AreEqual(SkillAvailabilityStatus.Learnable, results[0].Status);
            Assert.AreEqual(SkillAvailabilityStatus.Blocked, results[1].Status);
            Assert.AreEqual(SkillLearnRejection.LevelTooLow, results[1].Learn.Reason);
        }

        [Test]
        public void AnUnresolvableClassOrJobContributesNothingRatherThanThrowing()
        {
            // An orphaned reference is a content fault for validation to report; a
            // character must not become unreadable because of one.
            AddRankedSkill("skill.one", new[] { 1, 2 });

            IReadOnlyList<SkillAvailability> results =
                Granted(NewSkills(), NewCharacter("class.missing", "job.missing"), 1);

            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void AGrantedSkillThatNoLongerExistsIsReportedAsUnknown()
        {
            AddClass(ClassA, "skill.removedbyapatch");

            IReadOnlyList<SkillAvailability> results =
                Granted(NewSkills(), NewCharacter(ClassA), 1);

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(SkillAvailabilityStatus.Unknown, results[0].Status);
        }

        [Test]
        public void AClassOfferingNothingReturnsNothing()
        {
            AddClass(ClassA);

            Assert.AreEqual(0, Granted(NewSkills(), NewCharacter(ClassA), 1).Count);
        }

        [Test]
        public void NullArgumentsAreRejected()
        {
            var evaluator = new SkillAvailabilityEvaluator();
            CharacterSkillsState learned = NewSkills();
            CharacterClassState characterClass = NewCharacter(ClassA);

            Assert.Throws<ArgumentNullException>(() => evaluator.EvaluateGranted(
                learned, null, 1, Skills, _classes, _jobs));
            Assert.Throws<ArgumentNullException>(() => evaluator.EvaluateGranted(
                learned, characterClass, 1, Skills, null, _jobs));
            Assert.Throws<ArgumentNullException>(() => evaluator.EvaluateGranted(
                learned, characterClass, 1, Skills, _classes, null));
        }

        // ---------- boundaries ----------

        [Test]
        public void TheAvailabilityLayerHoldsOnlyTheRuleEvaluators()
        {
            // It composes 06.6 and 06.7 and owns no state of its own, so no level, class,
            // job or learned skill can be cached here and drift from its source.
            FieldInfo[] fields = typeof(SkillAvailabilityEvaluator).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            Assert.AreEqual(2, fields.Length);

            foreach (FieldInfo field in fields)
            {
                Assert.IsTrue(
                    field.FieldType == typeof(SkillLearningEvaluator)
                    || field.FieldType == typeof(SkillUpgradeEvaluator),
                    "Unexpected state on the availability layer: " + field.Name);
            }
        }

        [Test]
        public void NoSecondRuleImplementationWasIntroduced()
        {
            string[] forbidden =
            {
                "SkillAvailabilityRules", "SkillRequirementEvaluator", "SkillLevelChecker",
                "SkillClassChecker", "SkillJobChecker", "SkillPrerequisiteGraph",
                "LearnedSkillCache", "SkillRegistry"
            };

            foreach (Type type in typeof(SkillAvailabilityEvaluator).Assembly.GetTypes())
            {
                foreach (string name in forbidden)
                {
                    Assert.AreNotEqual(name, type.Name,
                        "Availability composes the existing rules; it must not restate them.");
                }
            }
        }

        [Test]
        public void CarriesNoForbiddenDependency()
        {
            foreach (AssemblyName referenced in
                typeof(SkillAvailabilityEvaluator).Assembly.GetReferencedAssemblies())
            {
                Assert.IsFalse(referenced.Name.StartsWith("FishNet", StringComparison.Ordinal));
                Assert.IsFalse(referenced.Name.StartsWith("UnityEditor", StringComparison.Ordinal));
                Assert.IsFalse(referenced.Name.StartsWith("ChibiFantasy.Backend", StringComparison.Ordinal));
                Assert.IsFalse(referenced.Name.StartsWith("ChibiFantasy.UI", StringComparison.Ordinal));
                Assert.IsFalse(referenced.Name.StartsWith("ChibiFantasy.Network", StringComparison.Ordinal));
            }
        }
    }
}
