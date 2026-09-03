using System;
using System.Reflection;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The mutation path: what a rank advance does to persistent state, and what a refusal
    /// must leave alone.
    /// </summary>
    internal sealed class SkillUpgradeApplyTests : SkillUpgradeTestBase
    {
        [Test]
        public void AnAllowedUpgradeRaisesTheRankByExactlyOne()
        {
            AddRankedSkill("skill.ranked", new[] { 1, 10, 20 });
            CharacterSkillsState learned = Holding("skill.ranked", 1);
            Revision before = learned.Revision;

            bool applied = TryUpgrade(learned, NewCharacter(ClassA), 99, "skill.ranked",
                out SkillUpgradeEligibility result);

            Assert.IsTrue(applied);
            Assert.IsTrue(result.IsAllowed);
            Assert.AreEqual(2, learned.GetRankOrDefault(Id("skill.ranked"), -1));
            Assert.AreEqual(before.Value + 1, learned.Revision.Value);
        }

        [Test]
        public void UpgradingNeverSkipsARank()
        {
            AddRankedSkill("skill.ranked", new[] { 1, 1, 1, 1, 1 });
            CharacterSkillsState learned = Holding("skill.ranked", 1);
            CharacterClassState characterClass = NewCharacter(ClassA);

            // Five ranks means four decisions, each one gated; there is no jump-to-rank API.
            for (int expected = 2; expected <= 5; expected++)
            {
                Assert.IsTrue(TryUpgrade(learned, characterClass, 99, "skill.ranked", out _));
                Assert.AreEqual(expected, learned.GetRankOrDefault(Id("skill.ranked"), -1));
            }

            Assert.IsFalse(TryUpgrade(learned, characterClass, 99, "skill.ranked", out _));
            Assert.AreEqual(5, learned.GetRankOrDefault(Id("skill.ranked"), -1));
        }

        [Test]
        public void ARankGatedByLevelCannotBePassedUntilTheLevelIsReached()
        {
            AddRankedSkill("skill.ranked", new[] { 1, 10, 20 });
            CharacterSkillsState learned = Holding("skill.ranked", 1);
            CharacterClassState characterClass = NewCharacter(ClassA);

            Assert.IsTrue(TryUpgrade(learned, characterClass, 10, "skill.ranked", out _));
            Assert.IsFalse(TryUpgrade(learned, characterClass, 10, "skill.ranked", out _),
                "Rank three demands level 20.");
            Assert.AreEqual(2, learned.GetRankOrDefault(Id("skill.ranked"), -1));

            Assert.IsTrue(TryUpgrade(learned, characterClass, 20, "skill.ranked", out _));
            Assert.AreEqual(3, learned.GetRankOrDefault(Id("skill.ranked"), -1));
        }

        [Test]
        public void ARefusedUpgradeLeavesRankAndRevisionUntouched()
        {
            AddRankedSkill("skill.ranked", new[] { 1, 30 });
            CharacterSkillsState learned = Holding("skill.ranked", 1);
            Revision before = learned.Revision;

            bool applied = TryUpgrade(learned, NewCharacter(ClassA), 29, "skill.ranked",
                out SkillUpgradeEligibility result);

            Assert.IsFalse(applied);
            Assert.AreEqual(SkillUpgradeRejection.LevelTooLow, result.Reason);
            Assert.AreEqual(1, learned.GetRankOrDefault(Id("skill.ranked"), -1));
            Assert.AreEqual(before, learned.Revision);
        }

        [Test]
        public void AnUnlearnedSkillIsNotCreatedByUpgrading()
        {
            AddRankedSkill("skill.ranked", new[] { 1, 2 });
            CharacterSkillsState learned = NewSkills();

            bool applied = TryUpgrade(learned, NewCharacter(ClassA), 99, "skill.ranked",
                out SkillUpgradeEligibility result);

            Assert.IsFalse(applied);
            Assert.AreEqual(SkillUpgradeRejection.NotLearned, result.Reason);
            Assert.AreEqual(0, learned.Count, "Upgrading must never learn a skill.");
            Assert.AreEqual(Revision.Initial, learned.Revision);
        }

        [Test]
        public void AnUnknownSkillCannotCreateAPhantomEntry()
        {
            CharacterSkillsState learned = NewSkills();

            Assert.IsFalse(TryUpgrade(learned, NewCharacter(ClassA), 99, "skill.ghost", out _));
            Assert.AreEqual(0, learned.Count);
            Assert.AreEqual(Revision.Initial, learned.Revision);
        }

        [Test]
        public void RepeatedUpgradingAtMaxRankFailsCleanlyAndChangesNothing()
        {
            AddRankedSkill("skill.ranked", new[] { 1, 2 });
            CharacterSkillsState learned = Holding("skill.ranked", 2);
            Revision before = learned.Revision;

            for (int i = 0; i < 3; i++)
            {
                Assert.IsFalse(TryUpgrade(learned, NewCharacter(ClassA), 99, "skill.ranked",
                    out SkillUpgradeEligibility result));
                Assert.AreEqual(SkillUpgradeRejection.AlreadyMaxRank, result.Reason);
            }

            Assert.AreEqual(2, learned.GetRankOrDefault(Id("skill.ranked"), -1));
            Assert.AreEqual(1, learned.Count);
            Assert.AreEqual(before, learned.Revision);
        }

        [Test]
        public void UpgradingNeverCreatesADuplicateEntry()
        {
            AddRankedSkill("skill.ranked", new[] { 1, 1, 1 });
            CharacterSkillsState learned = Holding("skill.ranked", 1);
            CharacterClassState characterClass = NewCharacter(ClassA);

            TryUpgrade(learned, characterClass, 99, "skill.ranked", out _);
            TryUpgrade(learned, characterClass, 99, "skill.ranked", out _);

            Assert.AreEqual(1, learned.Count, "SetRank replaces in place; it must not append.");
            Assert.AreEqual(3, learned.GetRankOrDefault(Id("skill.ranked"), -1));
        }

        [Test]
        public void EachSuccessfulUpgradeAdvancesTheRevisionExactlyOnce()
        {
            AddRankedSkill("skill.ranked", new[] { 1, 1, 1, 1 });
            CharacterSkillsState learned = Holding("skill.ranked", 1);
            CharacterClassState characterClass = NewCharacter(ClassA);
            int start = learned.Revision.Value;

            TryUpgrade(learned, characterClass, 99, "skill.ranked", out _);
            Assert.AreEqual(start + 1, learned.Revision.Value);

            TryUpgrade(learned, characterClass, 99, "skill.ranked", out _);
            Assert.AreEqual(start + 2, learned.Revision.Value);

            TryUpgrade(learned, characterClass, 99, "skill.ranked", out _);
            Assert.AreEqual(start + 3, learned.Revision.Value);
        }

        [Test]
        public void OtherLearnedSkillsAreLeftAlone()
        {
            AddRankedSkill("skill.one", new[] { 1, 1, 1 });
            AddRankedSkill("skill.two", new[] { 1, 1, 1 });
            AddRankedSkill("skill.three", new[] { 1, 1, 1 });

            CharacterSkillsState learned = NewSkills();
            learned.SetRank(Id("skill.one"), 1);
            learned.SetRank(Id("skill.two"), 2);
            learned.SetRank(Id("skill.three"), 3);

            Assert.IsTrue(TryUpgrade(learned, NewCharacter(ClassA), 99, "skill.two", out _));

            Assert.AreEqual(3, learned.Count);
            Assert.AreEqual(1, learned.GetRankOrDefault(Id("skill.one"), -1));
            Assert.AreEqual(3, learned.GetRankOrDefault(Id("skill.two"), -1));
            Assert.AreEqual(3, learned.GetRankOrDefault(Id("skill.three"), -1));
        }

        [Test]
        public void ARefusalAfterEarlierUpgradesKeepsThem()
        {
            AddRankedSkill("skill.ranked", new[] { 1, 1, 50 });
            CharacterSkillsState learned = Holding("skill.ranked", 1);
            CharacterClassState characterClass = NewCharacter(ClassA);

            Assert.IsTrue(TryUpgrade(learned, characterClass, 5, "skill.ranked", out _));
            Revision afterFirst = learned.Revision;

            Assert.IsFalse(TryUpgrade(learned, characterClass, 5, "skill.ranked", out _));

            Assert.AreEqual(2, learned.GetRankOrDefault(Id("skill.ranked"), -1));
            Assert.AreEqual(afterFirst, learned.Revision);
        }

        [Test]
        public void UpgradingTouchesNothingButTheLearnedSkills()
        {
            AddRankedSkill("skill.ranked", new[] { 1, 1 }, requiredClass: ClassA);
            CharacterClassState characterClass = NewCharacter(ClassA, JobA);
            Revision before = characterClass.Revision;

            TryUpgrade(Holding("skill.ranked", 1), characterClass, 99, "skill.ranked", out _);

            Assert.AreEqual(before, characterClass.Revision);
            Assert.AreEqual(Id(ClassA), characterClass.BaseClass);
            Assert.AreEqual(Id(JobA), characterClass.CurrentJob);
        }

        [Test]
        public void LearningThenRankingUpComposeThroughTheSameState()
        {
            AddRankedSkill("skill.ranked", new[] { 1, 10 });
            CharacterSkillsState learned = NewSkills();
            CharacterClassState characterClass = NewCharacter(ClassA);

            Assert.IsTrue(TryLearn(learned, characterClass, 1, "skill.ranked", out _));
            Assert.AreEqual(1, learned.GetRankOrDefault(Id("skill.ranked"), -1));

            Assert.IsFalse(TryUpgrade(learned, characterClass, 9, "skill.ranked", out _));
            Assert.IsTrue(TryUpgrade(learned, characterClass, 10, "skill.ranked", out _));

            Assert.AreEqual(2, learned.GetRankOrDefault(Id("skill.ranked"), -1));
            Assert.AreEqual(1, learned.Count);
            Assert.AreEqual(2, learned.Revision.Value);
        }

        [Test]
        public void UpgradedRanksSurviveSerialization()
        {
            AddRankedSkill("skill.one", new[] { 1, 1, 1 });
            AddRankedSkill("skill.two", new[] { 1, 1 });
            CharacterSkillsState learned = NewSkills();
            learned.SetRank(Id("skill.one"), 1);
            learned.SetRank(Id("skill.two"), 1);
            CharacterClassState characterClass = NewCharacter(ClassA);

            TryUpgrade(learned, characterClass, 99, "skill.one", out _);
            TryUpgrade(learned, characterClass, 99, "skill.one", out _);

            CharacterSkillsState restored = UnityEngine.JsonUtility
                .FromJson<CharacterSkillsState>(UnityEngine.JsonUtility.ToJson(learned));

            Assert.AreEqual(2, restored.Count);
            Assert.AreEqual(3, restored.GetRankOrDefault(Id("skill.one"), -1));
            Assert.AreEqual(1, restored.GetRankOrDefault(Id("skill.two"), -1));
            Assert.AreEqual(learned.Revision, restored.Revision);
        }

        [Test]
        public void NullArgumentsAreRejectedBeforeAnythingIsWritten()
        {
            var evaluator = new SkillUpgradeEvaluator();
            CharacterSkillsState learned = NewSkills();
            CharacterClassState characterClass = NewCharacter(ClassA);

            Assert.Throws<ArgumentNullException>(() => evaluator.TryUpgrade(
                null, characterClass, 1, Id("skill.x"), Skills, out _));
            Assert.Throws<ArgumentNullException>(() => evaluator.TryUpgrade(
                learned, null, 1, Id("skill.x"), Skills, out _));
            Assert.Throws<ArgumentNullException>(() => evaluator.TryUpgrade(
                learned, characterClass, 1, Id("skill.x"), null, out _));

            Assert.AreEqual(Revision.Initial, learned.Revision);
        }

        // ---------- boundaries ----------

        [Test]
        public void TheRuleLayerOwnsNoState()
        {
            Assert.IsEmpty(typeof(SkillUpgradeEvaluator).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public));
        }

        [Test]
        public void NoSecondRankRepresentationWasIntroduced()
        {
            // Rank is the integer CharacterSkillEntry already holds. A parallel rank type
            // would immediately be a second truth.
            string[] forbidden =
            {
                "SkillRank", "SkillRankState", "SkillLevelState", "LearnedSkillRank",
                "SkillInstance", "SkillProgressionState"
            };

            foreach (Type type in typeof(SkillUpgradeEvaluator).Assembly.GetTypes())
            {
                foreach (string name in forbidden)
                {
                    Assert.AreNotEqual(name, type.Name);
                }
            }

            Assert.AreEqual(typeof(int), typeof(CharacterSkillEntry)
                .GetField("_rank", BindingFlags.Instance | BindingFlags.NonPublic).FieldType);
        }

        [Test]
        public void NoSkillPointSystemWasInvented()
        {
            foreach (string name in Enum.GetNames(typeof(SkillUpgradeRejection)))
            {
                Assert.IsFalse(name.IndexOf("Point", StringComparison.OrdinalIgnoreCase) >= 0,
                    "A reason for a requirement nothing represents would be a fiction.");
                Assert.IsFalse(name.IndexOf("Cost", StringComparison.OrdinalIgnoreCase) >= 0);
            }
        }

        [Test]
        public void NoCombatOrCastingWasIntroduced()
        {
            // PHASE 07.3 added skill execution; see SkillLearningApplyTests for why these
            // two names left the list and why the duplicate-system names stayed.
            string[] forbidden =
            {
                "SkillCaster", "CastSkill", "CombatResolver",
                "DamageResolver", "HealResolver", "CooldownManager", "TargetResolver"
            };

            foreach (Type type in typeof(SkillUpgradeEvaluator).Assembly.GetTypes())
            {
                foreach (string name in forbidden)
                {
                    Assert.AreNotEqual(name, type.Name, "Combat belongs to a later step.");
                }
            }
        }

        [Test]
        public void CarriesNoForbiddenDependency()
        {
            foreach (AssemblyName referenced in
                typeof(SkillUpgradeEvaluator).Assembly.GetReferencedAssemblies())
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
