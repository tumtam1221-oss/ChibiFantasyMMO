using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    internal sealed class JobChangeApplyTests : ClassJobTestBase
    {
        private bool TryApply(CharacterClassState state, int level, string target,
            out JobChangeEligibility eligibility)
        {
            return new JobChangeEvaluator().TryApply(
                state, level, new DefinitionId(target), Classes, Jobs, out eligibility);
        }

        [Test]
        public void AllowedChangeIsAppliedAndAdvancesRevisionOnce()
        {
            AddSwordsmanTree();
            CharacterClassState state = NewCharacter(Swordsman);

            bool applied = TryApply(state, 15, "job.sword.first", out JobChangeEligibility result);

            Assert.IsTrue(applied);
            Assert.IsTrue(result.IsAllowed);
            Assert.AreEqual(new DefinitionId("job.sword.first"), state.CurrentJob);
            Assert.IsTrue(state.HasChangedJob);
            Assert.AreEqual(1, state.Revision.Value);
        }

        [Test]
        public void RefusedChangeLeavesStateAndRevisionUntouched()
        {
            AddSwordsmanTree();
            CharacterClassState state = NewCharacter(Swordsman);

            bool applied = TryApply(state, 14, "job.sword.first", out JobChangeEligibility result);

            Assert.IsFalse(applied);
            Assert.AreEqual(JobChangeRejection.LevelTooLow, result.Reason);
            Assert.IsFalse(state.HasChangedJob);
            Assert.AreEqual(DefinitionId.None, state.CurrentJob);
            Assert.AreEqual(Revision.Initial, state.Revision);
        }

        [Test]
        public void RefusedChangeAfterAnEarlierOneKeepsTheEarlierJob()
        {
            AddSwordsmanTree();
            CharacterClassState state = NewCharacter(Swordsman);
            TryApply(state, 15, "job.sword.first", out _);
            Revision afterFirst = state.Revision;

            bool applied = TryApply(state, 20, "job.sword.branch_a", out JobChangeEligibility result);

            Assert.IsFalse(applied);
            Assert.AreEqual(JobChangeRejection.LevelTooLow, result.Reason);
            Assert.AreEqual(new DefinitionId("job.sword.first"), state.CurrentJob);
            Assert.AreEqual(afterFirst, state.Revision);
        }

        [Test]
        public void TheFullIntendedProgressionCanBeWalked()
        {
            AddSwordsmanTree();
            CharacterClassState state = NewCharacter(Swordsman);

            Assert.IsTrue(TryApply(state, 15, "job.sword.first", out _));
            Assert.IsTrue(TryApply(state, 35, "job.sword.branch_b", out _));
            Assert.IsTrue(TryApply(state, 60, "job.sword.third_b", out _));

            Assert.AreEqual(new DefinitionId("job.sword.third_b"), state.CurrentJob);
            Assert.AreEqual(new DefinitionId(Swordsman), state.BaseClass);
            Assert.AreEqual(3, state.Revision.Value, "Three real changes, three revisions.");
        }

        [Test]
        public void ADeeperTreeNeedsNoCodeChange()
        {
            // A fourth tier is authored, not coded. Nothing in the rules knows about tiers.
            AddSwordsmanTree();
            AddJob("job.sword.fourth", Swordsman, 4, 90, "job.sword.third_a");

            JobDefinition third = null;
            foreach (JobDefinition job in Jobs.All)
            {
                if (job.Id == new DefinitionId("job.sword.third_a"))
                {
                    third = job;
                }
            }

            typeof(JobDefinition)
                .GetField("_nextJobs", System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic)
                .SetValue(third, new[] { new DefinitionId("job.sword.fourth") });

            CharacterClassState state = NewCharacter(Swordsman);
            TryApply(state, 15, "job.sword.first", out _);
            TryApply(state, 35, "job.sword.branch_a", out _);
            TryApply(state, 60, "job.sword.third_a", out _);

            Assert.IsTrue(TryApply(state, 90, "job.sword.fourth", out _));
            Assert.AreEqual(new DefinitionId("job.sword.fourth"), state.CurrentJob);
        }

        [Test]
        public void ThreeBranchesAreJustAsValidAsTwo()
        {
            // Branch count is content. Nothing assumes two.
            AddClass(Mage, 15, "job.mage.first");
            AddJob("job.mage.first", Mage, 1, 15, null,
                "job.mage.a", "job.mage.b", "job.mage.c");
            AddJob("job.mage.a", Mage, 2, 35, "job.mage.first");
            AddJob("job.mage.b", Mage, 2, 35, "job.mage.first");
            AddJob("job.mage.c", Mage, 2, 35, "job.mage.first");

            CharacterClassState state = NewCharacter(Mage);
            TryApply(state, 15, "job.mage.first", out _);

            Assert.IsTrue(TryApply(state, 35, "job.mage.c", out _));
            Assert.AreEqual(new DefinitionId("job.mage.c"), state.CurrentJob);
        }

        [Test]
        public void ApplyingDoesNotTouchStatsOrResources()
        {
            AddSwordsmanTree();
            CharacterId id = CharacterId.New();
            var classState = new CharacterClassState(id, new DefinitionId(Swordsman));

            var stats = new CharacterStatsState(id);
            stats.Set(new DefinitionId("stat.str"), 10);
            Revision statsBefore = stats.Revision;

            var progression = new CharacterProgressionState(id, 15, 0);
            Revision progressionBefore = progression.Revision;

            new JobChangeEvaluator().TryApply(
                classState, progression.Level, new DefinitionId("job.sword.first"),
                Classes, Jobs, out _);

            Assert.AreEqual(statsBefore, stats.Revision, "Class changes grant no stats yet.");
            Assert.AreEqual(10, stats.GetOrDefault(new DefinitionId("stat.str"), 0));
            Assert.AreEqual(progressionBefore, progression.Revision);
            Assert.AreEqual(15, progression.Level);
        }
    }
}
