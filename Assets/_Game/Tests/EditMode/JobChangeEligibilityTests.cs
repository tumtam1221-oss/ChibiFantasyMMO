using System;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    internal sealed class JobChangeEligibilityTests : ClassJobTestBase
    {
        private JobChangeEligibility Evaluate(CharacterClassState state, int level, string target)
        {
            return new JobChangeEvaluator()
                .Evaluate(state, level, new DefinitionId(target), Classes, Jobs);
        }

        [Test]
        public void FirstJobAtTheRequiredLevelIsAllowed()
        {
            AddSwordsmanTree();
            CharacterClassState state = NewCharacter(Swordsman);

            JobChangeEligibility result = Evaluate(state, 15, "job.sword.first");

            Assert.IsTrue(result.IsAllowed);
            Assert.AreEqual(JobChangeRejection.None, result.Reason);
            Assert.AreEqual(15, result.RequiredLevel);
        }

        [Test]
        public void BelowTheRequiredLevelIsRejectedButStillReportsIt()
        {
            AddSwordsmanTree();
            CharacterClassState state = NewCharacter(Swordsman);

            JobChangeEligibility result = Evaluate(state, 14, "job.sword.first");

            Assert.IsFalse(result.IsAllowed);
            Assert.AreEqual(JobChangeRejection.LevelTooLow, result.Reason);
            Assert.AreEqual(15, result.RequiredLevel, "A player should see what they need.");
        }

        [Test]
        public void SecondTierBranchAtThirtyFive()
        {
            AddSwordsmanTree();
            CharacterClassState state = NewCharacter(Swordsman);
            state.SetJob(new DefinitionId("job.sword.first"));

            Assert.IsFalse(Evaluate(state, 34, "job.sword.branch_a").IsAllowed);
            Assert.IsTrue(Evaluate(state, 35, "job.sword.branch_a").IsAllowed);
            Assert.IsTrue(Evaluate(state, 35, "job.sword.branch_b").IsAllowed,
                "Both branches are offered; the count is content, not an assumption.");
        }

        [Test]
        public void ThirdJobAtSixty()
        {
            AddSwordsmanTree();
            CharacterClassState state = NewCharacter(Swordsman);
            state.SetJob(new DefinitionId("job.sword.branch_a"));

            Assert.IsFalse(Evaluate(state, 59, "job.sword.third_a").IsAllowed);
            Assert.IsTrue(Evaluate(state, 60, "job.sword.third_a").IsAllowed);
        }

        [Test]
        public void TheOtherBranchesThirdJobIsNotOffered()
        {
            AddSwordsmanTree();
            CharacterClassState state = NewCharacter(Swordsman);
            state.SetJob(new DefinitionId("job.sword.branch_a"));

            JobChangeEligibility result = Evaluate(state, 99, "job.sword.third_b");

            Assert.IsFalse(result.IsAllowed);
            Assert.AreEqual(JobChangeRejection.NotOffered, result.Reason);
        }

        [Test]
        public void SkippingATierIsNotOffered()
        {
            AddSwordsmanTree();
            CharacterClassState state = NewCharacter(Swordsman);

            JobChangeEligibility result = Evaluate(state, 99, "job.sword.branch_a");

            Assert.IsFalse(result.IsAllowed);
            Assert.AreEqual(JobChangeRejection.NotOffered, result.Reason);
        }

        [Test]
        public void AJobFromAnotherClassTreeIsRejected()
        {
            AddSwordsmanTree();
            AddClass(Mage, 15, "job.mage.first");
            AddJob("job.mage.first", Mage, 1, 15, null);

            CharacterClassState state = NewCharacter(Swordsman);

            JobChangeEligibility result = Evaluate(state, 99, "job.mage.first");

            Assert.IsFalse(result.IsAllowed);
            Assert.AreEqual(JobChangeRejection.WrongBaseClass, result.Reason);
        }

        [Test]
        public void AnUnknownJobIsRejected()
        {
            AddSwordsmanTree();
            CharacterClassState state = NewCharacter(Swordsman);

            Assert.AreEqual(JobChangeRejection.UnknownJob, Evaluate(state, 99, "job.nope").Reason);
            Assert.AreEqual(JobChangeRejection.UnknownJob, Evaluate(state, 99, "").Reason);
        }

        [Test]
        public void AnUnknownClassIsRejected()
        {
            AddSwordsmanTree();
            var orphan = new CharacterClassState(CharacterId.New(), new DefinitionId("class.ghost"));

            JobChangeEligibility result = Evaluate(orphan, 99, "job.sword.first");

            Assert.AreEqual(JobChangeRejection.UnknownClass, result.Reason);
        }

        [Test]
        public void TheJobAlreadyHeldIsRejected()
        {
            AddSwordsmanTree();
            CharacterClassState state = NewCharacter(Swordsman);
            state.SetJob(new DefinitionId("job.sword.first"));

            JobChangeEligibility result = Evaluate(state, 99, "job.sword.first");

            Assert.IsFalse(result.IsAllowed);
            Assert.AreEqual(JobChangeRejection.AlreadyHeld, result.Reason);
        }

        [Test]
        public void AFirstJobExpectingAPrerequisiteIsRejected()
        {
            AddClass(Cleric, 15, "job.cleric.broken");
            // Offered straight off the class, yet demands a predecessor nobody has held.
            AddJob("job.cleric.broken", Cleric, 1, 15, "job.cleric.phantom");

            CharacterClassState state = NewCharacter(Cleric);

            JobChangeEligibility result = Evaluate(state, 99, "job.cleric.broken");

            Assert.AreEqual(JobChangeRejection.PrerequisiteNotMet, result.Reason);
        }

        [Test]
        public void EvaluationNeverMutatesState()
        {
            AddSwordsmanTree();
            CharacterClassState state = NewCharacter(Swordsman);
            Revision before = state.Revision;

            Evaluate(state, 99, "job.sword.first");
            Evaluate(state, 1, "job.sword.first");
            Evaluate(state, 99, "job.nope");

            Assert.AreEqual(before, state.Revision);
            Assert.IsFalse(state.HasChangedJob);
            Assert.AreEqual(DefinitionId.None, state.CurrentJob);
        }

        [Test]
        public void EvaluationIsDeterministic()
        {
            AddSwordsmanTree();
            CharacterClassState state = NewCharacter(Swordsman);

            JobChangeEligibility first = Evaluate(state, 15, "job.sword.first");
            JobChangeEligibility second = Evaluate(state, 15, "job.sword.first");

            Assert.AreEqual(first.IsAllowed, second.IsAllowed);
            Assert.AreEqual(first.Reason, second.Reason);
            Assert.AreEqual(first.RequiredLevel, second.RequiredLevel);
        }

        [Test]
        public void NullArgumentsThrow()
        {
            AddSwordsmanTree();
            var evaluator = new JobChangeEvaluator();
            CharacterClassState state = NewCharacter(Swordsman);
            var target = new DefinitionId("job.sword.first");

            Assert.Throws<ArgumentNullException>(
                () => evaluator.Evaluate(null, 15, target, Classes, Jobs));
            Assert.Throws<ArgumentNullException>(
                () => evaluator.Evaluate(state, 15, target, null, Jobs));
            Assert.Throws<ArgumentNullException>(
                () => evaluator.Evaluate(state, 15, target, Classes, null));
        }
    }
}
