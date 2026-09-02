using ChibiFantasy.Data;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    internal sealed class CharacterLevelUpTests : ProgressionTestBase
    {
        [Test]
        public void GainBelowThresholdAccumulatesWithoutLevelling()
        {
            CharacterProgressionDefinition curve = StandardCurve();
            CharacterProgressionState progression = NewProgression(curve);

            progression.AddExperience(99, curve);

            Assert.AreEqual(1, progression.Level);
            Assert.AreEqual(99L, progression.Experience);
        }

        [Test]
        public void GainExactlyReachingThresholdLevelsAndLeavesNothingOver()
        {
            CharacterProgressionDefinition curve = StandardCurve();
            CharacterProgressionState progression = NewProgression(curve);

            progression.AddExperience(100, curve);

            Assert.AreEqual(2, progression.Level);
            Assert.AreEqual(0L, progression.Experience);
        }

        [Test]
        public void GainAboveThresholdCarriesTheRemainderForward()
        {
            CharacterProgressionDefinition curve = StandardCurve();
            CharacterProgressionState progression = NewProgression(curve);

            progression.AddExperience(130, curve);

            Assert.AreEqual(2, progression.Level);
            Assert.AreEqual(30L, progression.Experience);
        }

        [Test]
        public void OneGainCanCrossSeveralLevels()
        {
            CharacterProgressionDefinition curve = StandardCurve();
            CharacterProgressionState progression = NewProgression(curve);

            // 100 to reach 2, 200 to reach 3, 300 to reach 4, then 50 spare.
            progression.AddExperience(650, curve);

            Assert.AreEqual(4, progression.Level);
            Assert.AreEqual(50L, progression.Experience);
        }

        [Test]
        public void CrossingSeveralLevelsStillAdvancesRevisionOnlyOnce()
        {
            CharacterProgressionDefinition curve = StandardCurve();
            CharacterProgressionState progression = NewProgression(curve);

            progression.AddExperience(650, curve);

            Assert.AreEqual(1, progression.Revision.Value,
                "One call is one change, however many levels it crosses.");
        }

        [Test]
        public void SuccessiveGainsAdvanceRevisionOncePerCall()
        {
            CharacterProgressionDefinition curve = StandardCurve();
            CharacterProgressionState progression = NewProgression(curve);

            progression.AddExperience(10, curve);
            progression.AddExperience(10, curve);
            progression.AddExperience(10, curve);

            Assert.AreEqual(3, progression.Revision.Value);
        }

        [Test]
        public void ZeroGainIsAValidNoChangeThatStillCounts()
        {
            CharacterProgressionDefinition curve = StandardCurve();
            CharacterProgressionState progression = NewProgression(curve);

            progression.AddExperience(0, curve);

            Assert.AreEqual(1, progression.Level);
            Assert.AreEqual(0L, progression.Experience);
            Assert.AreEqual(1, progression.Revision.Value);
        }

        [Test]
        public void LevelNeverExceedsTheCurveMaximum()
        {
            CharacterProgressionDefinition curve = StandardCurve();
            CharacterProgressionState progression = NewProgression(curve);

            progression.AddExperience(long.MaxValue / 2, curve);

            Assert.AreEqual(4, progression.Level);
            Assert.AreEqual(curve.MaxLevel, progression.Level);
            Assert.IsTrue(progression.IsAtMaxLevel(curve));
        }

        [Test]
        public void ExperienceIsRetainedAtMaximumLevelNotDiscarded()
        {
            CharacterProgressionDefinition curve = StandardCurve();
            CharacterProgressionState progression = NewProgression(curve);

            progression.AddExperience(650, curve);   // level 4, 50 spare
            progression.AddExperience(1000, curve);

            Assert.AreEqual(4, progression.Level);
            Assert.AreEqual(1050L, progression.Experience,
                "Banked experience must survive so a future cap raise can honour it.");
        }

        [Test]
        public void BankedExperienceConvertsWhenTheCapIsRaised()
        {
            CharacterProgressionDefinition small = StandardCurve();
            CharacterProgressionState progression = NewProgression(small);

            progression.AddExperience(650, small);    // level 4 of 4, 50 spare
            progression.AddExperience(1000, small);   // banked: 1050 at max level

            // A later patch extends the same curve with two more levels.
            CharacterProgressionDefinition extended =
                Curve("progression_test_extended", 1, 6, 100, 200, 300, 400, 500);

            progression.AddExperience(0, extended);

            Assert.AreEqual(6, progression.Level, "Banked experience should now pay for levels 5 and 6.");
            Assert.AreEqual(150L, progression.Experience);
        }

        [Test]
        public void SingleLevelCurveNeverLevels()
        {
            CharacterProgressionDefinition flat = Curve("flat", 1, 1);
            CharacterProgressionState progression = NewProgression(flat);

            progression.AddExperience(5000, flat);

            Assert.AreEqual(1, progression.Level);
            Assert.AreEqual(5000L, progression.Experience);
            Assert.IsTrue(progression.IsAtMaxLevel(flat));
        }

        [Test]
        public void CurveMayStartAboveLevelOne()
        {
            CharacterProgressionDefinition curve = Curve("high", 10, 12, 500, 600);
            CharacterProgressionState progression = NewProgression(curve);

            Assert.AreEqual(10, progression.Level);

            progression.AddExperience(1100, curve);

            Assert.AreEqual(12, progression.Level);
            Assert.AreEqual(0L, progression.Experience);
        }
    }
}
