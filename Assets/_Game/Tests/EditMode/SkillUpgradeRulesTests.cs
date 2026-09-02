using System;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The rank-progression rules: which requirements gate the next rank, and which reason
    /// each failure gives.
    /// </summary>
    internal sealed class SkillUpgradeRulesTests : SkillUpgradeTestBase
    {
        // ---------- allowed ----------

        [Test]
        public void RankOneCanAdvanceToRankTwoWhenRequirementsPass()
        {
            AddRankedSkill("skill.ranked", new[] { 1, 10, 20 });

            SkillUpgradeEligibility result =
                EvaluateUpgrade(Holding("skill.ranked", 1), NewCharacter(ClassA), 10, "skill.ranked");

            Assert.IsTrue(result.IsAllowed, result.ToString());
            Assert.AreEqual(1, result.CurrentRank);
            Assert.AreEqual(2, result.NextRank);
            Assert.AreEqual(10, result.RequiredLevel);
        }

        [Test]
        public void EachRankUsesItsOwnLevelRequirementNotRankOnes()
        {
            AddRankedSkill("skill.ranked", new[] { 1, 10, 20 });
            CharacterClassState characterClass = NewCharacter(ClassA);

            // Rank one demanded level 1; that must not be the gate for rank two or three.
            Assert.AreEqual(10,
                EvaluateUpgrade(Holding("skill.ranked", 1), characterClass, 10, "skill.ranked")
                    .RequiredLevel);
            Assert.AreEqual(20,
                EvaluateUpgrade(Holding("skill.ranked", 2), characterClass, 20, "skill.ranked")
                    .RequiredLevel);

            Assert.IsFalse(
                EvaluateUpgrade(Holding("skill.ranked", 2), characterClass, 19, "skill.ranked")
                    .IsAllowed,
                "Rank three demands level 20, not rank one's level 1.");
        }

        [Test]
        public void ExactlyMeetingTheNextRanksLevelIsEnough()
        {
            AddRankedSkill("skill.ranked", new[] { 1, 15 });

            Assert.IsTrue(
                EvaluateUpgrade(Holding("skill.ranked", 1), NewCharacter(ClassA), 15, "skill.ranked")
                    .IsAllowed);
        }

        [Test]
        public void AllSkillWideRequirementsMetIsAllowed()
        {
            AddRankedSkill("skill.base", new[] { 1, 1, 1 });
            AddRankedSkill("skill.ranked", new[] { 1, 25 },
                requiredClass: ClassA, requiredJob: JobA,
                prerequisites: new[] { Requires("skill.base", 3) });

            CharacterSkillsState learned = Holding("skill.ranked", 1);
            learned.SetRank(Id("skill.base"), 3);

            SkillUpgradeEligibility result =
                EvaluateUpgrade(learned, NewCharacter(ClassA, JobA), 25, "skill.ranked");

            Assert.IsTrue(result.IsAllowed, result.ToString());
        }

        // ---------- refused ----------

        [Test]
        public void AnUnknownSkillCannotBeUpgraded()
        {
            SkillUpgradeEligibility result =
                EvaluateUpgrade(NewSkills(), NewCharacter(ClassA), 99, "skill.ghost");

            Assert.IsFalse(result.IsAllowed);
            Assert.AreEqual(SkillUpgradeRejection.UnknownSkill, result.Reason);
            Assert.AreEqual(0, result.NextRank);
        }

        [Test]
        public void AnUnsetSkillIdCannotBeUpgraded()
        {
            SkillUpgradeEligibility result = new SkillUpgradeEvaluator().Evaluate(
                NewSkills(), NewCharacter(ClassA), 99, DefinitionId.None, Skills);

            Assert.IsFalse(result.IsAllowed);
            Assert.AreEqual(SkillUpgradeRejection.UnknownSkill, result.Reason);
        }

        [Test]
        public void AnUnlearnedSkillCannotBeUpgraded()
        {
            AddRankedSkill("skill.ranked", new[] { 1, 5 });

            SkillUpgradeEligibility result =
                EvaluateUpgrade(NewSkills(), NewCharacter(ClassA), 99, "skill.ranked");

            Assert.IsFalse(result.IsAllowed);
            Assert.AreEqual(SkillUpgradeRejection.NotLearned, result.Reason);
            Assert.AreEqual(0, result.CurrentRank);
        }

        [Test]
        public void ASkillAtItsMaximumRankCannotBeUpgraded()
        {
            AddRankedSkill("skill.ranked", new[] { 1, 5 });

            SkillUpgradeEligibility result =
                EvaluateUpgrade(Holding("skill.ranked", 2), NewCharacter(ClassA), 99, "skill.ranked");

            Assert.IsFalse(result.IsAllowed);
            Assert.AreEqual(SkillUpgradeRejection.AlreadyMaxRank, result.Reason);
            Assert.AreEqual(2, result.CurrentRank);
            Assert.AreEqual(0, result.NextRank);
        }

        [Test]
        public void ASingleRankSkillCannotBeUpgradedAtAll()
        {
            AddRankedSkill("skill.single", new[] { 1 });

            Assert.AreEqual(SkillUpgradeRejection.AlreadyMaxRank,
                EvaluateUpgrade(Holding("skill.single", 1), NewCharacter(ClassA), 99, "skill.single")
                    .Reason);
        }

        [Test]
        public void AMissingNextRankEntryFailsDeterministically()
        {
            // Claims to reach rank 5 but authors no level table at all.
            AddSkill("skill.gappy", maxLevel: 5);

            SkillUpgradeEligibility result =
                EvaluateUpgrade(Holding("skill.gappy", 1), NewCharacter(ClassA), 99, "skill.gappy");

            Assert.IsFalse(result.IsAllowed);
            Assert.AreEqual(SkillUpgradeRejection.NextRankUnavailable, result.Reason);
            Assert.AreEqual(2, result.NextRank);
        }

        [Test]
        public void ACharacterBelowTheNextRanksLevelIsRefused()
        {
            AddRankedSkill("skill.ranked", new[] { 1, 30 });

            SkillUpgradeEligibility result =
                EvaluateUpgrade(Holding("skill.ranked", 1), NewCharacter(ClassA), 29, "skill.ranked");

            Assert.IsFalse(result.IsAllowed);
            Assert.AreEqual(SkillUpgradeRejection.LevelTooLow, result.Reason);
            Assert.AreEqual(30, result.RequiredLevel,
                "The requirement is reported whether or not it was met.");
        }

        [Test]
        public void AnotherClassesSkillCannotBeUpgraded()
        {
            AddRankedSkill("skill.ranked", new[] { 1, 2 }, requiredClass: ClassA);

            SkillUpgradeEligibility result =
                EvaluateUpgrade(Holding("skill.ranked", 1), NewCharacter(ClassB), 99, "skill.ranked");

            Assert.IsFalse(result.IsAllowed);
            Assert.AreEqual(SkillUpgradeRejection.ClassRequirementNotMet, result.Reason);
        }

        [Test]
        public void AJobSkillCannotBeUpgradedAfterLeavingTheJob()
        {
            // The requirement is re-checked at every rank, so a job change that invalidates
            // a skill stops its progression rather than being noticed only at learn time.
            AddRankedSkill("skill.ranked", new[] { 1, 2 }, requiredJob: JobA);

            SkillUpgradeEligibility result =
                EvaluateUpgrade(Holding("skill.ranked", 1), NewCharacter(ClassA, JobB), 99,
                    "skill.ranked");

            Assert.IsFalse(result.IsAllowed);
            Assert.AreEqual(SkillUpgradeRejection.JobRequirementNotMet, result.Reason);
        }

        [Test]
        public void AMissingPrerequisiteBlocksUpgradeAndIsNamed()
        {
            AddRankedSkill("skill.base", new[] { 1, 1 });
            AddRankedSkill("skill.ranked", new[] { 1, 2 },
                prerequisites: new[] { Requires("skill.base", 1) });

            SkillUpgradeEligibility result =
                EvaluateUpgrade(Holding("skill.ranked", 1), NewCharacter(ClassA), 99, "skill.ranked");

            Assert.IsFalse(result.IsAllowed);
            Assert.AreEqual(SkillUpgradeRejection.PrerequisiteNotLearned, result.Reason);
            Assert.AreEqual(Id("skill.base"), result.BlockingPrerequisite);
            Assert.AreEqual(1, result.RequiredPrerequisiteRank);
        }

        [Test]
        public void APrerequisiteHeldAtTooLowARankBlocksUpgradeAndIsNamed()
        {
            AddRankedSkill("skill.base", new[] { 1, 1, 1 });
            AddRankedSkill("skill.ranked", new[] { 1, 2 },
                prerequisites: new[] { Requires("skill.base", 3) });

            CharacterSkillsState learned = Holding("skill.ranked", 1);
            learned.SetRank(Id("skill.base"), 2);

            SkillUpgradeEligibility result =
                EvaluateUpgrade(learned, NewCharacter(ClassA), 99, "skill.ranked");

            Assert.IsFalse(result.IsAllowed);
            Assert.AreEqual(SkillUpgradeRejection.PrerequisiteRankTooLow, result.Reason);
            Assert.AreEqual(Id("skill.base"), result.BlockingPrerequisite);
            Assert.AreEqual(3, result.RequiredPrerequisiteRank);
        }

        // ---------- determinism ----------

        [Test]
        public void TheFirstFailureIsReportedNotAllOfThem()
        {
            AddRankedSkill("skill.base", new[] { 1, 1 });
            AddRankedSkill("skill.hard", new[] { 1, 50 }, requiredClass: ClassA,
                prerequisites: new[] { Requires("skill.base", 2) });

            SkillUpgradeEligibility result =
                EvaluateUpgrade(Holding("skill.hard", 1), NewCharacter(ClassB), 1, "skill.hard");

            Assert.AreEqual(SkillUpgradeRejection.ClassRequirementNotMet, result.Reason);
        }

        [Test]
        public void EvaluationIsDeterministic()
        {
            AddRankedSkill("skill.base", new[] { 1, 1 });
            AddRankedSkill("skill.ranked", new[] { 1, 40 },
                prerequisites: new[] { Requires("skill.base", 2) });

            CharacterSkillsState learned = Holding("skill.ranked", 1);
            CharacterClassState characterClass = NewCharacter(ClassA);

            SkillUpgradeEligibility first = EvaluateUpgrade(learned, characterClass, 5, "skill.ranked");
            SkillUpgradeEligibility second = EvaluateUpgrade(learned, characterClass, 5, "skill.ranked");

            Assert.AreEqual(first.Reason, second.Reason);
            Assert.AreEqual(first.CurrentRank, second.CurrentRank);
            Assert.AreEqual(first.NextRank, second.NextRank);
            Assert.AreEqual(first.RequiredLevel, second.RequiredLevel);
            Assert.AreEqual(first.ToString(), second.ToString());
        }

        [Test]
        public void EvaluationChangesNothing()
        {
            AddRankedSkill("skill.ranked", new[] { 1, 5 });
            CharacterSkillsState learned = Holding("skill.ranked", 1);
            Revision before = learned.Revision;

            EvaluateUpgrade(learned, NewCharacter(ClassA), 99, "skill.ranked");

            Assert.AreEqual(before, learned.Revision);
            Assert.AreEqual(1, learned.GetRankOrDefault(Id("skill.ranked"), -1));
        }

        [Test]
        public void APrerequisiteCycleTerminatesWithoutRecursing()
        {
            AddRankedSkill("skill.a", new[] { 1, 1 }, prerequisites: new[] { Requires("skill.b", 1) });
            AddRankedSkill("skill.b", new[] { 1, 1 }, prerequisites: new[] { Requires("skill.a", 1) });

            // Each holds the other's prerequisite, so both are upgradable; the point is that
            // evaluation terminates rather than walking the cycle.
            CharacterSkillsState learned = Holding("skill.a", 1);
            learned.SetRank(Id("skill.b"), 1);

            Assert.IsTrue(EvaluateUpgrade(learned, NewCharacter(ClassA), 99, "skill.a").IsAllowed);
            Assert.IsTrue(EvaluateUpgrade(learned, NewCharacter(ClassA), 99, "skill.b").IsAllowed);
        }

        [Test]
        public void NullArgumentsAreRejected()
        {
            var evaluator = new SkillUpgradeEvaluator();
            CharacterSkillsState learned = NewSkills();
            CharacterClassState characterClass = NewCharacter(ClassA);

            Assert.Throws<ArgumentNullException>(
                () => evaluator.Evaluate(null, characterClass, 1, Id("skill.x"), Skills));
            Assert.Throws<ArgumentNullException>(
                () => evaluator.Evaluate(learned, null, 1, Id("skill.x"), Skills));
            Assert.Throws<ArgumentNullException>(
                () => evaluator.Evaluate(learned, characterClass, 1, Id("skill.x"), null));
        }
    }
}
