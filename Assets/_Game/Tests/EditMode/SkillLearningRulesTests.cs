using System;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The rules: which requirements are checked, and which reason each failure gives.
    /// </summary>
    internal sealed class SkillLearningRulesTests : SkillLearningTestBase
    {
        // ---------- allowed ----------

        [Test]
        public void ASkillWithNoRequirementsCanBeLearned()
        {
            AddSkill("skill.free");

            SkillLearnEligibility result = Evaluate(NewSkills(), NewCharacter(ClassA), 1, "skill.free");

            Assert.IsTrue(result.IsAllowed);
            Assert.AreEqual(SkillLearnRejection.None, result.Reason);
        }

        [Test]
        public void AllRequirementsMetIsAllowed()
        {
            AddSkill("skill.base");
            AddSkill("skill.advanced", requiredLevel: 20, requiredClass: ClassA, requiredJob: JobA,
                prerequisites: new[] { Requires("skill.base", 3) });

            CharacterSkillsState learned = NewSkills();
            learned.SetRank(Id("skill.base"), 3);

            SkillLearnEligibility result =
                Evaluate(learned, NewCharacter(ClassA, JobA), 20, "skill.advanced");

            Assert.IsTrue(result.IsAllowed, result.ToString());
            Assert.AreEqual(20, result.RequiredLevel);
        }

        [Test]
        public void AClassSkillCanBeLearnedByThatClass()
        {
            AddSkill("skill.classbound", requiredClass: ClassA);

            Assert.IsTrue(Evaluate(NewSkills(), NewCharacter(ClassA), 1, "skill.classbound").IsAllowed);
        }

        [Test]
        public void AJobSkillCanBeLearnedByThatJob()
        {
            AddSkill("skill.jobbound", requiredClass: ClassA, requiredJob: JobA);

            Assert.IsTrue(
                Evaluate(NewSkills(), NewCharacter(ClassA, JobA), 1, "skill.jobbound").IsAllowed);
        }

        [Test]
        public void ExactlyMeetingTheLevelIsEnough()
        {
            AddSkill("skill.gated", requiredLevel: 15);

            Assert.IsTrue(Evaluate(NewSkills(), NewCharacter(ClassA), 15, "skill.gated").IsAllowed);
        }

        [Test]
        public void ExactlyMeetingAPrerequisiteRankIsEnough()
        {
            AddSkill("skill.base");
            AddSkill("skill.advanced", prerequisites: new[] { Requires("skill.base", 4) });

            CharacterSkillsState learned = NewSkills();
            learned.SetRank(Id("skill.base"), 4);

            Assert.IsTrue(Evaluate(learned, NewCharacter(ClassA), 1, "skill.advanced").IsAllowed);
        }

        // ---------- refused ----------

        [Test]
        public void AnUnknownSkillCannotBeLearned()
        {
            SkillLearnEligibility result = Evaluate(NewSkills(), NewCharacter(ClassA), 99, "skill.ghost");

            Assert.IsFalse(result.IsAllowed);
            Assert.AreEqual(SkillLearnRejection.UnknownSkill, result.Reason);
        }

        [Test]
        public void AnUnsetSkillIdCannotBeLearned()
        {
            SkillLearnEligibility result = new SkillLearningEvaluator().Evaluate(
                NewSkills(), NewCharacter(ClassA), 99, DefinitionId.None, Skills);

            Assert.IsFalse(result.IsAllowed);
            Assert.AreEqual(SkillLearnRejection.UnknownSkill, result.Reason);
        }

        [Test]
        public void AnAlreadyLearnedSkillCannotBeLearnedAgain()
        {
            AddSkill("skill.known");
            CharacterSkillsState learned = NewSkills();
            learned.Learn(Id("skill.known"));

            SkillLearnEligibility result = Evaluate(learned, NewCharacter(ClassA), 1, "skill.known");

            Assert.IsFalse(result.IsAllowed);
            Assert.AreEqual(SkillLearnRejection.AlreadyLearned, result.Reason);
        }

        [Test]
        public void ACharacterBelowTheRequiredLevelIsRefused()
        {
            AddSkill("skill.gated", requiredLevel: 15);

            SkillLearnEligibility result = Evaluate(NewSkills(), NewCharacter(ClassA), 14, "skill.gated");

            Assert.IsFalse(result.IsAllowed);
            Assert.AreEqual(SkillLearnRejection.LevelTooLow, result.Reason);
            Assert.AreEqual(15, result.RequiredLevel,
                "The requirement is reported whether or not it was met.");
        }

        [Test]
        public void AnotherClassesSkillIsRefused()
        {
            AddSkill("skill.classbound", requiredClass: ClassA);

            SkillLearnEligibility result =
                Evaluate(NewSkills(), NewCharacter(ClassB), 99, "skill.classbound");

            Assert.IsFalse(result.IsAllowed);
            Assert.AreEqual(SkillLearnRejection.ClassRequirementNotMet, result.Reason);
        }

        [Test]
        public void AJobSkillIsRefusedToACharacterHoldingNoJob()
        {
            AddSkill("skill.jobbound", requiredClass: ClassA, requiredJob: JobA);

            SkillLearnEligibility result =
                Evaluate(NewSkills(), NewCharacter(ClassA), 99, "skill.jobbound");

            Assert.IsFalse(result.IsAllowed);
            Assert.AreEqual(SkillLearnRejection.JobRequirementNotMet, result.Reason);
        }

        [Test]
        public void AJobSkillIsRefusedToACharacterHoldingAnotherJob()
        {
            AddSkill("skill.jobbound", requiredJob: JobA);

            SkillLearnEligibility result =
                Evaluate(NewSkills(), NewCharacter(ClassA, JobB), 99, "skill.jobbound");

            Assert.IsFalse(result.IsAllowed);
            Assert.AreEqual(SkillLearnRejection.JobRequirementNotMet, result.Reason);
        }

        [Test]
        public void AMissingPrerequisiteIsRefusedAndNamed()
        {
            AddSkill("skill.base");
            AddSkill("skill.advanced", prerequisites: new[] { Requires("skill.base", 2) });

            SkillLearnEligibility result =
                Evaluate(NewSkills(), NewCharacter(ClassA), 99, "skill.advanced");

            Assert.IsFalse(result.IsAllowed);
            Assert.AreEqual(SkillLearnRejection.PrerequisiteNotLearned, result.Reason);
            Assert.AreEqual(Id("skill.base"), result.BlockingPrerequisite);
            Assert.AreEqual(2, result.RequiredPrerequisiteRank);
        }

        [Test]
        public void APrerequisiteHeldAtTooLowARankIsRefusedAndNamed()
        {
            AddSkill("skill.base");
            AddSkill("skill.advanced", prerequisites: new[] { Requires("skill.base", 5) });

            CharacterSkillsState learned = NewSkills();
            learned.SetRank(Id("skill.base"), 4);

            SkillLearnEligibility result = Evaluate(learned, NewCharacter(ClassA), 99, "skill.advanced");

            Assert.IsFalse(result.IsAllowed);
            Assert.AreEqual(SkillLearnRejection.PrerequisiteRankTooLow, result.Reason);
            Assert.AreEqual(Id("skill.base"), result.BlockingPrerequisite);
            Assert.AreEqual(5, result.RequiredPrerequisiteRank);
        }

        [Test]
        public void EveryPrerequisiteMustBeMet()
        {
            AddSkill("skill.one");
            AddSkill("skill.two");
            AddSkill("skill.advanced",
                prerequisites: new[] { Requires("skill.one", 1), Requires("skill.two", 1) });

            CharacterSkillsState learned = NewSkills();
            learned.Learn(Id("skill.one"));

            SkillLearnEligibility result = Evaluate(learned, NewCharacter(ClassA), 99, "skill.advanced");

            Assert.IsFalse(result.IsAllowed);
            Assert.AreEqual(Id("skill.two"), result.BlockingPrerequisite);

            learned.Learn(Id("skill.two"));
            Assert.IsTrue(Evaluate(learned, NewCharacter(ClassA), 99, "skill.advanced").IsAllowed);
        }

        // ---------- determinism and ordering ----------

        [Test]
        public void TheFirstFailureIsReportedNotAllOfThem()
        {
            // Wrong class, missing prerequisite and too low a level, all at once. The
            // project convention for a domain rule is one deterministic reason; accumulating
            // every fault is what ValidationReport is for, and the two stay separate.
            AddSkill("skill.base");
            AddSkill("skill.hard", requiredLevel: 50, requiredClass: ClassA,
                prerequisites: new[] { Requires("skill.base", 3) });

            SkillLearnEligibility result = Evaluate(NewSkills(), NewCharacter(ClassB), 1, "skill.hard");

            Assert.AreEqual(SkillLearnRejection.ClassRequirementNotMet, result.Reason);
        }

        [Test]
        public void EvaluationIsDeterministic()
        {
            AddSkill("skill.base");
            AddSkill("skill.advanced", requiredLevel: 30,
                prerequisites: new[] { Requires("skill.base", 2) });

            CharacterSkillsState learned = NewSkills();
            CharacterClassState characterClass = NewCharacter(ClassA);

            SkillLearnEligibility first = Evaluate(learned, characterClass, 5, "skill.advanced");
            SkillLearnEligibility second = Evaluate(learned, characterClass, 5, "skill.advanced");

            Assert.AreEqual(first.Reason, second.Reason);
            Assert.AreEqual(first.RequiredLevel, second.RequiredLevel);
            Assert.AreEqual(first.BlockingPrerequisite, second.BlockingPrerequisite);
            Assert.AreEqual(first.ToString(), second.ToString());
        }

        [Test]
        public void EvaluationChangesNothing()
        {
            AddSkill("skill.free");
            CharacterSkillsState learned = NewSkills();
            learned.Learn(Id("skill.free"));
            Revision before = learned.Revision;
            int count = learned.Count;

            Evaluate(learned, NewCharacter(ClassA), 1, "skill.free");

            Assert.AreEqual(before, learned.Revision);
            Assert.AreEqual(count, learned.Count);
        }

        [Test]
        public void APrerequisiteCycleLeavesBothUnlearnableWithoutRecursing()
        {
            // A requires B, B requires A. Nothing recurses into a prerequisite's own
            // prerequisites, so this terminates; both are simply unreachable, which is a
            // content fault rather than a runtime hazard.
            AddSkill("skill.a", prerequisites: new[] { Requires("skill.b", 1) });
            AddSkill("skill.b", prerequisites: new[] { Requires("skill.a", 1) });

            CharacterSkillsState learned = NewSkills();

            Assert.AreEqual(SkillLearnRejection.PrerequisiteNotLearned,
                Evaluate(learned, NewCharacter(ClassA), 99, "skill.a").Reason);
            Assert.AreEqual(SkillLearnRejection.PrerequisiteNotLearned,
                Evaluate(learned, NewCharacter(ClassA), 99, "skill.b").Reason);
        }

        [Test]
        public void ASkillWithNoLevelTableHasNoLevelGate()
        {
            // The schema keeps the character-level requirement on the rank-one level entry.
            // A skill authoring no table states no gate, and no default is invented.
            AddSkill("skill.ungated");

            SkillLearnEligibility result = Evaluate(NewSkills(), NewCharacter(ClassA), 1, "skill.ungated");

            Assert.IsTrue(result.IsAllowed);
            Assert.AreEqual(0, result.RequiredLevel);
        }

        [Test]
        public void APrerequisiteNamingNoSkillIsIgnoredRatherThanBlocking()
        {
            // An unset prerequisite is a content fault that skill validation reports; it is
            // not a reason to refuse a player at runtime.
            AddSkill("skill.advanced",
                prerequisites: new[] { new SkillPrerequisite(DefinitionId.None, 1) });

            Assert.IsTrue(Evaluate(NewSkills(), NewCharacter(ClassA), 1, "skill.advanced").IsAllowed);
        }

        [Test]
        public void NullArgumentsAreRejected()
        {
            var evaluator = new SkillLearningEvaluator();
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
