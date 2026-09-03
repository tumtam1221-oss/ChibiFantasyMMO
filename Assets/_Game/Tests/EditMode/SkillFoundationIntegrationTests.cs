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
    /// The whole Skill Foundation as one flow: authored definition, content validation,
    /// grants, learned state, learning, rank progression and availability.
    /// </summary>
    /// <remarks>
    /// The per-step suites each prove one layer. These prove the layers agree with each
    /// other, which is the thing no single-layer test can see: that content validation and
    /// the runtime rules accept the same content, that every rule reads the same
    /// authoritative level, class, job and learned state, and that nothing but a successful
    /// mutation moves a revision.
    ///
    /// Every class, job and skill here is a TEST FIXTURE with a deliberately generic name.
    /// No production content is authored.
    /// </remarks>
    internal sealed class SkillFoundationIntegrationTests : SkillUpgradeTestBase
    {
        private DefinitionRegistry<ClassDefinition> _classes;
        private DefinitionRegistry<JobDefinition> _jobs;
        private DefinitionRegistry<StatDefinition> _stats;
        private DefinitionRegistry<StatusEffectDefinition> _statusEffects;
        private List<UnityEngine.Object> _definitions;

        [SetUp]
        public void SetUpContent()
        {
            _classes = new DefinitionRegistry<ClassDefinition>();
            _jobs = new DefinitionRegistry<JobDefinition>();
            _stats = new DefinitionRegistry<StatDefinition>();
            _statusEffects = new DefinitionRegistry<StatusEffectDefinition>();
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
            SetPrivate(definition, "_startingSkills", ToIds(startingSkills));
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
            SetPrivate(definition, "_skills", ToIds(skills));
            _definitions.Add(definition);
            _jobs.Register(definition);
            return definition;
        }

        private static DefinitionId[] ToIds(string[] values)
        {
            var ids = new DefinitionId[values.Length];

            for (int i = 0; i < values.Length; i++)
            {
                ids[i] = new DefinitionId(values[i]);
            }

            return ids;
        }

        /// <summary>The content-validation half of the flow.</summary>
        private SkillContentValidator ContentValidator()
        {
            return new SkillContentValidator(Skills, _classes, _jobs, _stats, _statusEffects);
        }

        /// <summary>
        /// A skill authored completely enough to pass content validation.
        /// </summary>
        /// <remarks>The rule fixtures do not state a category or target, because the runtime
        /// rules never read them. Content validation does, and these tests run both halves,
        /// so a skill here is authored the way real content would be.</remarks>
        private SkillDefinition Authored(string id, int[] requiredLevels,
            string requiredClass = null, string requiredJob = null,
            SkillPrerequisite[] prerequisites = null)
        {
            SkillDefinition skill = AddRankedSkill(
                id, requiredLevels, requiredClass, requiredJob, prerequisites);
            return Categorise(skill);
        }

        private static SkillDefinition Categorise(SkillDefinition skill)
        {
            SetPrivate(skill, "_category", SkillCategory.Active);
            SetPrivate(skill, "_targetType", SkillTargetType.SingleEnemy);
            return skill;
        }

        private SkillAvailability Availability(CharacterSkillsState learned,
            CharacterClassState classState, int level, string skill)
        {
            return new SkillAvailabilityEvaluator()
                .Evaluate(learned, classState, level, Id(skill), Skills);
        }

        // ================= the whole flow =================

        [Test]
        public void AnAuthoredSkillPassesValidationThenIsLearnedRankedUpAndFinished()
        {
            AddClass(ClassA, "skill.ranked");
            Authored("skill.ranked", new[] { 1, 10, 20 }, requiredClass: ClassA);

            // 1. Content validation accepts it.
            ValidationReport content = ContentValidator().ValidateAll();
            Assert.IsTrue(content.IsValid,
                content.Messages.Count > 0 ? content.Messages[0].ToString() : string.Empty);

            CharacterSkillsState learned = NewSkills();
            CharacterClassState characterClass = NewCharacter(ClassA);

            // 2. Availability offers it, and offering it changes nothing.
            Assert.AreEqual(SkillAvailabilityStatus.Learnable,
                Availability(learned, characterClass, 1, "skill.ranked").Status);
            Assert.AreEqual(0, learned.Count);

            // 3. It is learned, at the id and rank the state was told.
            Assert.IsTrue(TryLearn(learned, characterClass, 1, "skill.ranked", out _));
            Assert.AreEqual(1, learned.Count);
            Assert.AreEqual(Id("skill.ranked"), learned.Skills[0].Skill);
            Assert.AreEqual(1, learned.Skills[0].Rank);

            // 4. Availability now reports it held, gated by the next rank's own level.
            SkillAvailability held = Availability(learned, characterClass, 1, "skill.ranked");
            Assert.IsTrue(held.IsLearned);
            Assert.AreEqual(SkillAvailabilityStatus.UpgradeBlocked, held.Status);
            Assert.AreEqual(10, held.Upgrade.RequiredLevel);

            // 5. Reaching that level makes it upgradeable, and the upgrade lands.
            Assert.AreEqual(SkillAvailabilityStatus.Upgradeable,
                Availability(learned, characterClass, 10, "skill.ranked").Status);
            Assert.IsTrue(TryUpgrade(learned, characterClass, 10, "skill.ranked", out _));
            Assert.AreEqual(2, learned.GetRankOrDefault(Id("skill.ranked"), -1));

            // 6. The last rank, then nothing further.
            Assert.IsTrue(TryUpgrade(learned, characterClass, 20, "skill.ranked", out _));
            Assert.AreEqual(SkillAvailabilityStatus.MaxRank,
                Availability(learned, characterClass, 99, "skill.ranked").Status);
            Assert.IsFalse(TryUpgrade(learned, characterClass, 99, "skill.ranked", out _));

            // 7. Three successful mutations, one entry, still valid content.
            Assert.AreEqual(3, learned.Revision.Value);
            Assert.AreEqual(1, learned.Count);
            Assert.IsTrue(new CharacterSkillsValidator().Validate(learned, Skills).IsValid);
        }

        [Test]
        public void ContentValidationAndRuntimeRulesAcceptTheSameContent()
        {
            // The consistency this step exists to check: a skill validation calls sound
            // must be usable, and one it rejects must not be silently usable anyway.
            AddClass(ClassA, "skill.good");
            Authored("skill.good", new[] { 1, 5 }, requiredClass: ClassA);

            Assert.IsTrue(ContentValidator().ValidateAll().IsValid);

            CharacterSkillsState learned = NewSkills();
            Assert.IsTrue(TryLearn(learned, NewCharacter(ClassA), 1, "skill.good", out _));
            Assert.IsTrue(TryUpgrade(learned, NewCharacter(ClassA), 5, "skill.good", out _));
        }

        [Test]
        public void ASkillClaimingRanksItDoesNotDescribeIsNowAContentFault()
        {
            // Regression for the gap this step closed. The runtime rule always refused to
            // rank such a skill up, with NextRankUnavailable, but content validation called
            // it sound, so the fault surfaced far from its cause and no designer was told.
            SkillDefinition gappy = Categorise(AddSkill("skill.gappy", maxLevel: 5));

            ValidationReport report = ContentValidator().Validate(gappy);

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(1, report.ErrorCount,
                "The missing level table must be the only thing wrong with it.");

            bool sawFault = false;
            foreach (ValidationMessage message in report.Messages)
            {
                if (message.Message.Contains("no level table is authored"))
                {
                    sawFault = true;
                }
            }

            Assert.IsTrue(sawFault, "Validation must report what the runtime rule refuses.");

            // And the runtime rule still refuses it, which is the defence in depth.
            Assert.AreEqual(SkillUpgradeRejection.NextRankUnavailable,
                EvaluateUpgrade(Holding("skill.gappy", 1), NewCharacter(ClassA), 99, "skill.gappy")
                    .Reason);
        }

        [Test]
        public void ASingleRankSkillStillNeedsNoLevelTable()
        {
            // The documented case must stay legal: one rank described by the skill's own
            // fields, no table required.
            SkillDefinition single = Categorise(AddSkill("skill.single", maxLevel: 1));

            Assert.IsTrue(ContentValidator().Validate(single).IsValid);
            Assert.AreEqual(SkillUpgradeRejection.AlreadyMaxRank,
                EvaluateUpgrade(Holding("skill.single", 1), NewCharacter(ClassA), 99, "skill.single")
                    .Reason);
        }

        // ================= one authoritative source =================

        [Test]
        public void EveryRuleReadsTheSameCharacterLevel()
        {
            AddRankedSkill("skill.ranked", new[] { 10, 20 });
            CharacterClassState characterClass = NewCharacter(ClassA);
            var progression = new CharacterProgressionState(CharacterId.New(), 10, 0L);

            // The level flows from progression into every rule as one value.
            Assert.IsTrue(Evaluate(NewSkills(), characterClass, progression.Level, "skill.ranked")
                .IsAllowed);
            Assert.AreEqual(SkillAvailabilityStatus.Learnable,
                Availability(NewSkills(), characterClass, progression.Level, "skill.ranked").Status);
            Assert.AreEqual(SkillUpgradeRejection.LevelTooLow,
                EvaluateUpgrade(Holding("skill.ranked", 1), characterClass, progression.Level,
                    "skill.ranked").Reason);
        }

        [Test]
        public void EveryRuleReadsTheSameClassAndJob()
        {
            AddRankedSkill("skill.jobbound", new[] { 1, 2 },
                requiredClass: ClassA, requiredJob: JobA);
            CharacterClassState characterClass = NewCharacter(ClassA);

            // Before the job change, every layer refuses for the same reason.
            Assert.AreEqual(SkillLearnRejection.JobRequirementNotMet,
                Evaluate(NewSkills(), characterClass, 99, "skill.jobbound").Reason);
            Assert.AreEqual(SkillUpgradeRejection.JobRequirementNotMet,
                EvaluateUpgrade(Holding("skill.jobbound", 1), characterClass, 99, "skill.jobbound")
                    .Reason);
            Assert.AreEqual(SkillAvailabilityStatus.Blocked,
                Availability(NewSkills(), characterClass, 99, "skill.jobbound").Status);

            // The one class/job source changes, and every layer follows it.
            characterClass.SetJob(Id(JobA));

            Assert.IsTrue(Evaluate(NewSkills(), characterClass, 99, "skill.jobbound").IsAllowed);
            Assert.IsTrue(EvaluateUpgrade(Holding("skill.jobbound", 1), characterClass, 99,
                "skill.jobbound").IsAllowed);
            Assert.AreEqual(SkillAvailabilityStatus.Learnable,
                Availability(NewSkills(), characterClass, 99, "skill.jobbound").Status);
        }

        [Test]
        public void EveryRuleReadsTheSameLearnedSkillsForPrerequisites()
        {
            AddRankedSkill("skill.base", new[] { 1, 1, 1 });
            AddRankedSkill("skill.advanced", new[] { 1, 2, 3 },
                prerequisites: new[] { Requires("skill.base", 3) });

            CharacterSkillsState learned = NewSkills();
            CharacterClassState characterClass = NewCharacter(ClassA);

            Assert.AreEqual(SkillLearnRejection.PrerequisiteNotLearned,
                Evaluate(learned, characterClass, 99, "skill.advanced").Reason);

            learned.SetRank(Id("skill.base"), 2);
            Assert.AreEqual(SkillLearnRejection.PrerequisiteRankTooLow,
                Evaluate(learned, characterClass, 99, "skill.advanced").Reason);

            // The same collection satisfies the prerequisite for learning and for ranking up.
            learned.SetRank(Id("skill.base"), 3);
            Assert.IsTrue(TryLearn(learned, characterClass, 99, "skill.advanced", out _));
            Assert.IsTrue(TryUpgrade(learned, characterClass, 99, "skill.advanced", out _));

            // Losing the prerequisite blocks further progression through the same state.
            learned.SetRank(Id("skill.base"), 1);
            Assert.AreEqual(SkillUpgradeRejection.PrerequisiteRankTooLow,
                EvaluateUpgrade(learned, characterClass, 99, "skill.advanced").Reason);
        }

        [Test]
        public void TheRankCeilingHasOneSourceAcrossRulesAndValidation()
        {
            AddRankedSkill("skill.ranked", new[] { 1, 1 });
            CharacterSkillsState learned = Holding("skill.ranked", 2);

            // The runtime rule says finished, and state validation says the rank is legal.
            Assert.AreEqual(SkillUpgradeRejection.AlreadyMaxRank,
                EvaluateUpgrade(learned, NewCharacter(ClassA), 99, "skill.ranked").Reason);
            Assert.IsTrue(new CharacterSkillsValidator().Validate(learned, Skills).IsValid);

            // A rank beyond that ceiling is rejected by state validation, from the same field.
            learned.SetRank(Id("skill.ranked"), 3);
            Assert.IsFalse(new CharacterSkillsValidator().Validate(learned, Skills).IsValid);
        }

        // ================= no phantom or duplicate state =================

        [Test]
        public void NoPathCanRecordAnUnknownSkill()
        {
            CharacterSkillsState learned = NewSkills();
            CharacterClassState characterClass = NewCharacter(ClassA);
            AddClass(ClassA, "skill.ghost");

            Assert.IsFalse(TryLearn(learned, characterClass, 99, "skill.ghost", out _));
            Assert.IsFalse(TryUpgrade(learned, characterClass, 99, "skill.ghost", out _));
            new SkillAvailabilityEvaluator()
                .EvaluateGranted(learned, characterClass, 99, Skills, _classes, _jobs);
            Availability(learned, characterClass, 99, "skill.ghost");

            Assert.AreEqual(0, learned.Count);
            Assert.AreEqual(Revision.Initial, learned.Revision);
        }

        [Test]
        public void GrantMetadataNeverBecomesLearnedState()
        {
            // A class starting skill and a job skill are statements about content, not about
            // a character. Reading them must not hand anything over; acquisition at creation
            // and on job change are later systems.
            AddRankedSkill("skill.start", new[] { 1, 2 });
            AddRankedSkill("skill.job", new[] { 1, 2 }, requiredJob: JobA);
            AddClass(ClassA, "skill.start");
            AddJobWithSkills(JobA, ClassA, "skill.job");

            CharacterSkillsState learned = NewSkills();
            CharacterClassState characterClass = NewCharacter(ClassA, JobA);
            Revision classBefore = characterClass.Revision;
            var evaluator = new SkillAvailabilityEvaluator();

            for (int i = 0; i < 3; i++)
            {
                IReadOnlyList<SkillAvailability> offered = evaluator.EvaluateGranted(
                    learned, characterClass, 99, Skills, _classes, _jobs);
                Assert.AreEqual(2, offered.Count);
            }

            Assert.AreEqual(0, learned.Count);
            Assert.AreEqual(Revision.Initial, learned.Revision);
            Assert.AreEqual(classBefore, characterClass.Revision);
        }

        [Test]
        public void RepeatedLearningAndUpgradingKeepExactlyOneEntryPerSkill()
        {
            AddRankedSkill("skill.one", new[] { 1, 1, 1 });
            AddRankedSkill("skill.two", new[] { 1, 1 });
            CharacterSkillsState learned = NewSkills();
            CharacterClassState characterClass = NewCharacter(ClassA);

            for (int i = 0; i < 4; i++)
            {
                TryLearn(learned, characterClass, 99, "skill.one", out _);
                TryLearn(learned, characterClass, 99, "skill.two", out _);
                TryUpgrade(learned, characterClass, 99, "skill.one", out _);
                TryUpgrade(learned, characterClass, 99, "skill.two", out _);
            }

            Assert.AreEqual(2, learned.Count);
            Assert.AreEqual(3, learned.GetRankOrDefault(Id("skill.one"), -1));
            Assert.AreEqual(2, learned.GetRankOrDefault(Id("skill.two"), -1));
            Assert.IsTrue(new CharacterSkillsValidator().Validate(learned, Skills).IsValid);
        }

        [Test]
        public void SkillsRemainIndependentOfOneAnother()
        {
            AddRankedSkill("skill.one", new[] { 1, 1 });
            AddRankedSkill("skill.two", new[] { 1, 1 });
            AddRankedSkill("skill.blocked", new[] { 90, 95 });

            CharacterSkillsState learned = NewSkills();
            CharacterClassState characterClass = NewCharacter(ClassA);

            TryLearn(learned, characterClass, 5, "skill.one", out _);
            TryLearn(learned, characterClass, 5, "skill.two", out _);
            TryLearn(learned, characterClass, 5, "skill.blocked", out _);
            TryUpgrade(learned, characterClass, 5, "skill.one", out _);

            Assert.AreEqual(2, learned.Count);
            Assert.AreEqual(2, learned.GetRankOrDefault(Id("skill.one"), -1));
            Assert.AreEqual(1, learned.GetRankOrDefault(Id("skill.two"), -1));
            Assert.IsFalse(learned.Knows(Id("skill.blocked")));
        }

        // ================= revision discipline =================

        [Test]
        public void OnlySuccessfulMutationsMoveTheRevision()
        {
            AddRankedSkill("skill.ranked", new[] { 1, 50 });
            AddRankedSkill("skill.gated", new[] { 90, 95 });
            CharacterSkillsState learned = NewSkills();
            CharacterClassState characterClass = NewCharacter(ClassA);

            int expected = 0;
            Assert.AreEqual(expected, learned.Revision.Value);

            Assert.IsTrue(TryLearn(learned, characterClass, 5, "skill.ranked", out _));
            Assert.AreEqual(++expected, learned.Revision.Value);

            // Everything below fails, and none of it may move the counter.
            TryLearn(learned, characterClass, 5, "skill.ranked", out _);   // already learned
            TryLearn(learned, characterClass, 5, "skill.gated", out _);    // level too low
            TryLearn(learned, characterClass, 5, "skill.ghost", out _);    // unknown
            TryUpgrade(learned, characterClass, 5, "skill.ranked", out _); // level too low
            TryUpgrade(learned, characterClass, 5, "skill.gated", out _);  // not learned
            TryUpgrade(learned, characterClass, 5, "skill.ghost", out _);  // unknown
            Availability(learned, characterClass, 5, "skill.ranked");      // read-only

            Assert.AreEqual(expected, learned.Revision.Value);

            Assert.IsTrue(TryUpgrade(learned, characterClass, 50, "skill.ranked", out _));
            Assert.AreEqual(++expected, learned.Revision.Value);
        }

        [Test]
        public void NoRuleTouchesTheCharactersOtherAggregates()
        {
            AddRankedSkill("skill.ranked", new[] { 1, 1 }, requiredClass: ClassA);
            CharacterClassState characterClass = NewCharacter(ClassA, JobA);
            var progression = new CharacterProgressionState(CharacterId.New(), 20, 100L);
            CharacterSkillsState learned = NewSkills();

            Revision classBefore = characterClass.Revision;
            Revision progressionBefore = progression.Revision;

            TryLearn(learned, characterClass, progression.Level, "skill.ranked", out _);
            TryUpgrade(learned, characterClass, progression.Level, "skill.ranked", out _);
            Availability(learned, characterClass, progression.Level, "skill.ranked");

            Assert.AreEqual(classBefore, characterClass.Revision);
            Assert.AreEqual(progressionBefore, progression.Revision);
            Assert.AreEqual(20, progression.Level);
        }

        // ================= layers stay separate =================

        [Test]
        public void ContentValidationNeverReadsOrWritesCharacterState()
        {
            AddClass(ClassA, "skill.ranked");
            AddRankedSkill("skill.ranked", new[] { 1, 2 }, requiredClass: ClassA);
            CharacterSkillsState learned = Holding("skill.ranked", 1);
            Revision before = learned.Revision;

            ContentValidator().ValidateAll();

            Assert.AreEqual(before, learned.Revision);
            Assert.AreEqual(1, learned.GetRankOrDefault(Id("skill.ranked"), -1));

            // The content validator takes only content; it has no way to reach a character.
            foreach (ParameterInfo parameter in typeof(SkillContentValidator)
                .GetConstructors()[0].GetParameters())
            {
                Assert.IsFalse(parameter.ParameterType.Name.Contains("Character"),
                    "Content validation must not depend on a character: " + parameter.Name);
            }
        }

        [Test]
        public void RuntimeRulesDoNotConsultContentValidation()
        {
            // The two layers answer different questions and must not be wired together: a
            // domain rule returns one reason, a validator accumulates every finding.
            foreach (Type type in new[]
            {
                typeof(SkillLearningEvaluator), typeof(SkillUpgradeEvaluator),
                typeof(SkillAvailabilityEvaluator)
            })
            {
                foreach (MethodInfo method in type.GetMethods(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    Assert.AreNotEqual(typeof(ValidationReport), method.ReturnType,
                        type.Name + "." + method.Name + " must not return a validation report.");

                    foreach (ParameterInfo parameter in method.GetParameters())
                    {
                        Assert.AreNotEqual(typeof(ValidationReport), parameter.ParameterType);
                    }
                }

                foreach (FieldInfo field in type.GetFields(
                    BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    Assert.AreNotEqual(typeof(SkillContentValidator), field.FieldType);
                    Assert.AreNotEqual(typeof(DefinitionValidator), field.FieldType);
                }
            }
        }

        [Test]
        public void LearnedStateSurvivesTheWholeFlowThroughSerialization()
        {
            AddRankedSkill("skill.one", new[] { 1, 1, 1 });
            AddRankedSkill("skill.two", new[] { 1, 1 });
            CharacterSkillsState learned = NewSkills();
            CharacterClassState characterClass = NewCharacter(ClassA);

            TryLearn(learned, characterClass, 99, "skill.one", out _);
            TryUpgrade(learned, characterClass, 99, "skill.one", out _);
            TryLearn(learned, characterClass, 99, "skill.two", out _);

            CharacterSkillsState restored =
                JsonUtility.FromJson<CharacterSkillsState>(JsonUtility.ToJson(learned));

            Assert.AreEqual(learned.CharacterId, restored.CharacterId);
            Assert.AreEqual(learned.Revision, restored.Revision);
            Assert.AreEqual(2, restored.GetRankOrDefault(Id("skill.one"), -1));
            Assert.AreEqual(1, restored.GetRankOrDefault(Id("skill.two"), -1));

            // Restored state is usable by the rules without any rebuilding step.
            Assert.IsTrue(TryUpgrade(restored, characterClass, 99, "skill.one", out _));
            Assert.AreEqual(3, restored.GetRankOrDefault(Id("skill.one"), -1));
        }

        [Test]
        public void RepeatedEvaluationOfTheWholeFlowIsIdentical()
        {
            AddClass(ClassA, "skill.one", "skill.two");
            AddRankedSkill("skill.one", new[] { 1, 40 });
            AddRankedSkill("skill.two", new[] { 90, 95 });
            CharacterSkillsState learned = Holding("skill.one", 1);
            CharacterClassState characterClass = NewCharacter(ClassA);
            var evaluator = new SkillAvailabilityEvaluator();

            IReadOnlyList<SkillAvailability> first = evaluator.EvaluateGranted(
                learned, characterClass, 10, Skills, _classes, _jobs);
            IReadOnlyList<SkillAvailability> second = evaluator.EvaluateGranted(
                learned, characterClass, 10, Skills, _classes, _jobs);

            Assert.AreEqual(first.Count, second.Count);

            for (int i = 0; i < first.Count; i++)
            {
                Assert.AreEqual(first[i].Skill, second[i].Skill, "index " + i);
                Assert.AreEqual(first[i].Status, second[i].Status, "index " + i);
                Assert.AreEqual(first[i].ToString(), second[i].ToString(), "index " + i);
            }

            Assert.AreEqual(SkillAvailabilityStatus.UpgradeBlocked, first[0].Status);
            Assert.AreEqual(SkillAvailabilityStatus.Blocked, first[1].Status);
        }

        [Test]
        public void ReturnedCollectionsCannotBeCastBackAndMutated()
        {
            AddClass(ClassA, "skill.one");
            AddRankedSkill("skill.one", new[] { 1, 2 });
            CharacterSkillsState learned = NewSkills();

            Assert.IsNotInstanceOf<List<CharacterSkillEntry>>(learned.Skills);

            IReadOnlyList<SkillAvailability> offered = new SkillAvailabilityEvaluator()
                .EvaluateGranted(learned, NewCharacter(ClassA), 1, Skills, _classes, _jobs);

            // The availability list is a fresh result each call, so a caller holding it
            // cannot alter what the next caller sees.
            IReadOnlyList<SkillAvailability> again = new SkillAvailabilityEvaluator()
                .EvaluateGranted(learned, NewCharacter(ClassA), 1, Skills, _classes, _jobs);
            Assert.AreNotSame(offered, again);
        }

        // ================= boundaries =================

        [Test]
        public void TheSkillDomainCarriesNoUnityOrNetworkDependency()
        {
            foreach (Type type in new[]
            {
                typeof(SkillLearningEvaluator), typeof(SkillUpgradeEvaluator),
                typeof(SkillAvailabilityEvaluator)
            })
            {
                Assert.IsFalse(typeof(UnityEngine.Object).IsAssignableFrom(type), type.Name);
                Assert.IsFalse(typeof(MonoBehaviour).IsAssignableFrom(type), type.Name);
            }

            foreach (AssemblyName referenced in
                typeof(SkillAvailabilityEvaluator).Assembly.GetReferencedAssemblies())
            {
                Assert.IsFalse(referenced.Name.StartsWith("FishNet", StringComparison.Ordinal));
                Assert.IsFalse(referenced.Name.StartsWith("UnityEditor", StringComparison.Ordinal));
                Assert.IsFalse(referenced.Name.StartsWith("ChibiFantasy.Network", StringComparison.Ordinal));
                Assert.IsFalse(referenced.Name.StartsWith("ChibiFantasy.Backend", StringComparison.Ordinal));
                Assert.IsFalse(referenced.Name.StartsWith("ChibiFantasy.UI", StringComparison.Ordinal));
            }

            // Persistent skill state is Data, and Data must not reach into Gameplay.
            foreach (AssemblyName referenced in
                typeof(CharacterSkillsState).Assembly.GetReferencedAssemblies())
            {
                Assert.IsFalse(referenced.Name.StartsWith("ChibiFantasy.Gameplay", StringComparison.Ordinal));
                Assert.IsFalse(referenced.Name.StartsWith("FishNet", StringComparison.Ordinal));
                Assert.IsFalse(referenced.Name.StartsWith("UnityEditor", StringComparison.Ordinal));
            }
        }

        [Test]
        public void PhaseSevenCanConsumeTheFoundationWithoutRewritingIt()
        {
            // The surface a future combat layer needs, asserted to exist so a later phase
            // does not discover it missing. Nothing here executes anything.
            Assert.IsNotNull(typeof(SkillDefinition).GetProperty("Levels"));
            Assert.IsNotNull(typeof(SkillDefinition).GetMethod("TryGetLevel"));
            Assert.IsNotNull(typeof(SkillLevelEntry).GetProperty("Effects"));
            Assert.IsNotNull(typeof(SkillLevelEntry).GetProperty("ResourceCost"));
            Assert.IsNotNull(typeof(SkillLevelEntry).GetProperty("CooldownSeconds"));
            Assert.IsNotNull(typeof(SkillEffect).GetProperty("Kind"));
            Assert.IsNotNull(typeof(CharacterSkillsState).GetMethod("TryGetRank"));

            // A rank a character holds indexes the authored table directly, which is what
            // lets combat ask "what does this skill do for this character" with two reads.
            AddRankedSkill("skill.ranked", new[] { 1, 5 });
            CharacterSkillsState learned = Holding("skill.ranked", 2);

            Assert.IsTrue(learned.TryGetRank(Id("skill.ranked"), out int rank));
            Assert.IsTrue(Skills.TryGet(Id("skill.ranked"), out SkillDefinition definition));
            Assert.IsTrue(definition.TryGetLevel(rank, out SkillLevelEntry entry));
            Assert.AreEqual(2, entry.Level);
            Assert.IsNotNull(entry.Effects);
        }

        [Test]
        public void NoCombatOrBackendTypeExistsInTheSkillFoundation()
        {
            // PHASE 07.3 moved this boundary. Skill execution now legitimately exists in
            // the Gameplay assembly (SkillExecutor), so its absence is no longer the
            // invariant. What still holds, and is what this guard now protects:
            //   - duplicate resolvers were never created; execution reuses
            //     BasicDamageFormula, TargetEvaluator and CharacterResourceState, so
            //     DamageResolver, HealResolver, TargetResolver and CooldownManager must
            //     still not exist anywhere;
            //   - backend and network types still belong to neither assembly.
            string[] forbidden =
            {
                "SkillCaster", "CombatResolver", "DamageResolver",
                "HealResolver", "CooldownManager", "TargetResolver", "SkillPoint",
                "SkillRepository", "SkillDatabase", "SkillApiClient", "SkillNetworkHandler"
            };

            foreach (Assembly assembly in new[]
            {
                typeof(SkillDefinition).Assembly, typeof(SkillAvailabilityEvaluator).Assembly
            })
            {
                foreach (Type type in assembly.GetTypes())
                {
                    foreach (string name in forbidden)
                    {
                        Assert.AreNotEqual(name, type.Name,
                            "Combat and backend belong to later phases.");
                    }
                }
            }
        }

        [Test]
        public void EvaluateAllRejectsBadArgumentsEvenForAnEmptyList()
        {
            // Regression: an empty candidate list used to skip argument checking entirely,
            // so the same call was rejected or accepted depending on the list's contents.
            var evaluator = new SkillAvailabilityEvaluator();
            CharacterSkillsState learned = NewSkills();
            CharacterClassState characterClass = NewCharacter(ClassA);
            var empty = new DefinitionId[0];

            Assert.Throws<ArgumentNullException>(
                () => evaluator.EvaluateAll(null, characterClass, 1, empty, Skills));
            Assert.Throws<ArgumentNullException>(
                () => evaluator.EvaluateAll(learned, null, 1, empty, Skills));
            Assert.Throws<ArgumentNullException>(
                () => evaluator.EvaluateAll(learned, characterClass, 1, empty, null));
        }
    }
}
