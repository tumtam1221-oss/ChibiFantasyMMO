using System;
using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Where a character stands with a skill: class, job, level and prerequisite gates
    /// resolved into one status.
    /// </summary>
    internal sealed class SkillAvailabilityStatusTests : SkillUpgradeTestBase
    {
        private SkillAvailability Availability(CharacterSkillsState learned,
            CharacterClassState classState, int level, string skill)
        {
            return new SkillAvailabilityEvaluator()
                .Evaluate(learned, classState, level, Id(skill), Skills);
        }

        // ---------- class ----------

        [Test]
        public void ASkillForTheCharactersClassIsLearnable()
        {
            AddRankedSkill("skill.classbound", new[] { 1, 2 }, requiredClass: ClassA);

            SkillAvailability result =
                Availability(NewSkills(), NewCharacter(ClassA), 1, "skill.classbound");

            Assert.AreEqual(SkillAvailabilityStatus.Learnable, result.Status);
            Assert.IsTrue(result.IsActionable);
            Assert.IsFalse(result.IsLearned);
        }

        [Test]
        public void ASkillForAnotherClassIsBlocked()
        {
            AddRankedSkill("skill.classbound", new[] { 1, 2 }, requiredClass: ClassA);

            SkillAvailability result =
                Availability(NewSkills(), NewCharacter(ClassB), 99, "skill.classbound");

            Assert.AreEqual(SkillAvailabilityStatus.Blocked, result.Status);
            Assert.AreEqual(SkillLearnRejection.ClassRequirementNotMet, result.Learn.Reason);
            Assert.IsFalse(result.IsActionable);
        }

        [Test]
        public void ASkillWithNoClassRestrictionIsOpenToAnyClass()
        {
            AddRankedSkill("skill.free", new[] { 1, 2 });

            Assert.AreEqual(SkillAvailabilityStatus.Learnable,
                Availability(NewSkills(), NewCharacter(ClassA), 1, "skill.free").Status);
            Assert.AreEqual(SkillAvailabilityStatus.Learnable,
                Availability(NewSkills(), NewCharacter(ClassB), 1, "skill.free").Status);
        }

        // ---------- job ----------

        [Test]
        public void ASkillForTheHeldJobIsLearnable()
        {
            AddRankedSkill("skill.jobbound", new[] { 1, 2 },
                requiredClass: ClassA, requiredJob: JobA);

            Assert.AreEqual(SkillAvailabilityStatus.Learnable,
                Availability(NewSkills(), NewCharacter(ClassA, JobA), 1, "skill.jobbound").Status);
        }

        [Test]
        public void ASkillForAnotherJobIsBlocked()
        {
            AddRankedSkill("skill.jobbound", new[] { 1, 2 }, requiredJob: JobA);

            SkillAvailability result =
                Availability(NewSkills(), NewCharacter(ClassA, JobB), 99, "skill.jobbound");

            Assert.AreEqual(SkillAvailabilityStatus.Blocked, result.Status);
            Assert.AreEqual(SkillLearnRejection.JobRequirementNotMet, result.Learn.Reason);
        }

        [Test]
        public void AJobSkillIsBlockedBeforeAnyJobIsHeld()
        {
            AddRankedSkill("skill.jobbound", new[] { 1, 2 }, requiredJob: JobA);

            SkillAvailability result =
                Availability(NewSkills(), NewCharacter(ClassA), 99, "skill.jobbound");

            Assert.AreEqual(SkillAvailabilityStatus.Blocked, result.Status);
            Assert.AreEqual(SkillLearnRejection.JobRequirementNotMet, result.Learn.Reason);
        }

        [Test]
        public void TheExistingClassJobRelationshipIsRespected()
        {
            // The skill states a class and a job; the character must satisfy both, and
            // JobDefinition.BaseClass remains the only place their relationship is authored.
            AddRankedSkill("skill.jobbound", new[] { 1, 2 },
                requiredClass: ClassA, requiredJob: JobA);

            Assert.AreEqual(SkillAvailabilityStatus.Learnable,
                Availability(NewSkills(), NewCharacter(ClassA, JobA), 1, "skill.jobbound").Status);
            Assert.AreEqual(SkillLearnRejection.ClassRequirementNotMet,
                Availability(NewSkills(), NewCharacter(ClassB, JobA), 1, "skill.jobbound")
                    .Learn.Reason);
        }

        // ---------- level ----------

        [Test]
        public void ASkillAboveTheCharactersLevelIsBlocked()
        {
            AddRankedSkill("skill.gated", new[] { 20, 30 });

            SkillAvailability result =
                Availability(NewSkills(), NewCharacter(ClassA), 19, "skill.gated");

            Assert.AreEqual(SkillAvailabilityStatus.Blocked, result.Status);
            Assert.AreEqual(SkillLearnRejection.LevelTooLow, result.Learn.Reason);
            Assert.AreEqual(20, result.Learn.RequiredLevel);
        }

        [Test]
        public void ASkillBecomesLearnableOnceTheLevelIsReached()
        {
            AddRankedSkill("skill.gated", new[] { 20, 30 });

            Assert.AreEqual(SkillAvailabilityStatus.Learnable,
                Availability(NewSkills(), NewCharacter(ClassA), 20, "skill.gated").Status);
        }

        // ---------- prerequisites ----------

        [Test]
        public void AMissingPrerequisiteBlocksAndIsNamed()
        {
            AddRankedSkill("skill.base", new[] { 1, 1 });
            AddRankedSkill("skill.advanced", new[] { 1, 2 },
                prerequisites: new[] { Requires("skill.base", 2) });

            SkillAvailability result =
                Availability(NewSkills(), NewCharacter(ClassA), 99, "skill.advanced");

            Assert.AreEqual(SkillAvailabilityStatus.Blocked, result.Status);
            Assert.AreEqual(SkillLearnRejection.PrerequisiteNotLearned, result.Learn.Reason);
            Assert.AreEqual(Id("skill.base"), result.Learn.BlockingPrerequisite);
        }

        [Test]
        public void AvailabilityRespectsPrerequisiteRank()
        {
            AddRankedSkill("skill.base", new[] { 1, 1, 1 });
            AddRankedSkill("skill.advanced", new[] { 1, 2 },
                prerequisites: new[] { Requires("skill.base", 3) });

            CharacterSkillsState learned = Holding("skill.base", 2);
            Assert.AreEqual(SkillLearnRejection.PrerequisiteRankTooLow,
                Availability(learned, NewCharacter(ClassA), 99, "skill.advanced").Learn.Reason);

            learned.SetRank(Id("skill.base"), 3);
            Assert.AreEqual(SkillAvailabilityStatus.Learnable,
                Availability(learned, NewCharacter(ClassA), 99, "skill.advanced").Status);
        }

        // ---------- learned, upgradeable, max ----------

        [Test]
        public void AKnownSkillWithARankLeftIsUpgradeable()
        {
            AddRankedSkill("skill.ranked", new[] { 1, 10, 20 });

            SkillAvailability result =
                Availability(Holding("skill.ranked", 1), NewCharacter(ClassA), 10, "skill.ranked");

            Assert.AreEqual(SkillAvailabilityStatus.Upgradeable, result.Status);
            Assert.IsTrue(result.IsLearned);
            Assert.IsTrue(result.IsActionable);
            Assert.AreEqual(1, result.CurrentRank);
            Assert.AreEqual(2, result.Upgrade.NextRank);
        }

        [Test]
        public void AKnownSkillWhoseNextRankIsGatedIsUpgradeBlocked()
        {
            AddRankedSkill("skill.ranked", new[] { 1, 30 });

            SkillAvailability result =
                Availability(Holding("skill.ranked", 1), NewCharacter(ClassA), 29, "skill.ranked");

            Assert.AreEqual(SkillAvailabilityStatus.UpgradeBlocked, result.Status);
            Assert.IsTrue(result.IsLearned);
            Assert.IsFalse(result.IsActionable);
            Assert.AreEqual(SkillUpgradeRejection.LevelTooLow, result.Upgrade.Reason);
            Assert.AreEqual(30, result.Upgrade.RequiredLevel);
        }

        [Test]
        public void AKnownSkillAtItsCeilingIsMaxRank()
        {
            AddRankedSkill("skill.ranked", new[] { 1, 5 });

            SkillAvailability result =
                Availability(Holding("skill.ranked", 2), NewCharacter(ClassA), 99, "skill.ranked");

            Assert.AreEqual(SkillAvailabilityStatus.MaxRank, result.Status);
            Assert.IsTrue(result.IsLearned);
            Assert.IsFalse(result.IsActionable);
            Assert.AreEqual(2, result.CurrentRank);
        }

        [Test]
        public void AKnownSkillIsNeverReportedAsLearnable()
        {
            AddRankedSkill("skill.ranked", new[] { 1, 5 });

            SkillAvailability result =
                Availability(Holding("skill.ranked", 1), NewCharacter(ClassA), 99, "skill.ranked");

            Assert.AreNotEqual(SkillAvailabilityStatus.Learnable, result.Status);
            Assert.AreEqual(SkillLearnRejection.AlreadyLearned, result.Learn.Reason,
                "The learn answer stays meaningful for a known skill.");
        }

        [Test]
        public void AnUnknownSkillIsReportedAsUnknown()
        {
            SkillAvailability result =
                Availability(NewSkills(), NewCharacter(ClassA), 99, "skill.ghost");

            Assert.AreEqual(SkillAvailabilityStatus.Unknown, result.Status);
            Assert.IsFalse(result.IsLearned);
            Assert.IsFalse(result.IsActionable);
        }

        [Test]
        public void AnUnlearnedSkillsUpgradeAnswerStaysMeaningful()
        {
            AddRankedSkill("skill.ranked", new[] { 1, 5 });

            SkillAvailability result =
                Availability(NewSkills(), NewCharacter(ClassA), 99, "skill.ranked");

            Assert.AreEqual(SkillUpgradeRejection.NotLearned, result.Upgrade.Reason,
                "Neither answer is left at a default a caller could misread.");
        }

        // ---------- read-only and deterministic ----------

        [Test]
        public void AvailabilityDoesNotMutateSkillState()
        {
            AddRankedSkill("skill.ranked", new[] { 1, 5 });
            CharacterSkillsState learned = Holding("skill.ranked", 1);
            string before = UnityEngine.JsonUtility.ToJson(learned);

            for (int i = 0; i < 5; i++)
            {
                Availability(learned, NewCharacter(ClassA), 99, "skill.ranked");
            }

            Assert.AreEqual(before, UnityEngine.JsonUtility.ToJson(learned));
            Assert.AreEqual(1, learned.Count);
            Assert.AreEqual(1, learned.GetRankOrDefault(Id("skill.ranked"), -1));
        }

        [Test]
        public void AvailabilityDoesNotChangeAnyRevision()
        {
            AddRankedSkill("skill.ranked", new[] { 1, 5 });
            CharacterSkillsState learned = Holding("skill.ranked", 1);
            CharacterClassState characterClass = NewCharacter(ClassA, JobA);
            Revision skillsBefore = learned.Revision;
            Revision classBefore = characterClass.Revision;

            Availability(learned, characterClass, 99, "skill.ranked");
            Availability(learned, characterClass, 99, "skill.ghost");

            Assert.AreEqual(skillsBefore, learned.Revision);
            Assert.AreEqual(classBefore, characterClass.Revision);
        }

        [Test]
        public void RepeatedQueriesGiveIdenticalResults()
        {
            AddRankedSkill("skill.base", new[] { 1, 1 });
            AddRankedSkill("skill.advanced", new[] { 1, 40 },
                prerequisites: new[] { Requires("skill.base", 2) });
            CharacterSkillsState learned = Holding("skill.advanced", 1);
            CharacterClassState characterClass = NewCharacter(ClassA);

            SkillAvailability first = Availability(learned, characterClass, 5, "skill.advanced");
            SkillAvailability second = Availability(learned, characterClass, 5, "skill.advanced");

            Assert.AreEqual(first.Status, second.Status);
            Assert.AreEqual(first.CurrentRank, second.CurrentRank);
            Assert.AreEqual(first.Upgrade.Reason, second.Upgrade.Reason);
            Assert.AreEqual(first.ToString(), second.ToString());
        }

        [Test]
        public void StatusAgreesWithTheRulesThatGateTheMutation()
        {
            // The status must never say a skill is actionable when the mutating rule would
            // refuse it, which is the whole reason status is derived rather than decided.
            AddRankedSkill("skill.base", new[] { 1, 1 });
            AddRankedSkill("skill.ranked", new[] { 5, 30 }, requiredClass: ClassA,
                prerequisites: new[] { Requires("skill.base", 1) });

            var learning = new SkillLearningEvaluator();
            var upgrading = new SkillUpgradeEvaluator();
            CharacterClassState characterClass = NewCharacter(ClassA);

            foreach (int level in new[] { 1, 5, 29, 30 })
            {
                foreach (int baseRank in new[] { 0, 1 })
                {
                    CharacterSkillsState learned = NewSkills();

                    if (baseRank > 0)
                    {
                        learned.SetRank(Id("skill.base"), baseRank);
                    }

                    SkillAvailability status =
                        Availability(learned, characterClass, level, "skill.ranked");
                    bool canLearn = learning
                        .Evaluate(learned, characterClass, level, Id("skill.ranked"), Skills)
                        .IsAllowed;

                    Assert.AreEqual(canLearn,
                        status.Status == SkillAvailabilityStatus.Learnable,
                        "level " + level + ", base rank " + baseRank);

                    learned.SetRank(Id("skill.ranked"), 1);
                    SkillAvailability held =
                        Availability(learned, characterClass, level, "skill.ranked");
                    bool canUpgrade = upgrading
                        .Evaluate(learned, characterClass, level, Id("skill.ranked"), Skills)
                        .IsAllowed;

                    Assert.AreEqual(canUpgrade,
                        held.Status == SkillAvailabilityStatus.Upgradeable,
                        "level " + level + ", base rank " + baseRank);
                }
            }
        }

        [Test]
        public void NullArgumentsAreRejected()
        {
            var evaluator = new SkillAvailabilityEvaluator();
            CharacterSkillsState learned = NewSkills();
            CharacterClassState characterClass = NewCharacter(ClassA);

            Assert.Throws<ArgumentNullException>(
                () => evaluator.Evaluate(null, characterClass, 1, Id("skill.x"), Skills));
            Assert.Throws<ArgumentNullException>(
                () => evaluator.Evaluate(learned, null, 1, Id("skill.x"), Skills));
            Assert.Throws<ArgumentNullException>(
                () => evaluator.Evaluate(learned, characterClass, 1, Id("skill.x"), null));
            Assert.Throws<ArgumentNullException>(() => evaluator.EvaluateAll(
                learned, characterClass, 1, (IEnumerable<DefinitionId>)null, Skills));
        }
    }
}
