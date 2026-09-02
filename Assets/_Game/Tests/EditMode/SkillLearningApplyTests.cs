using System;
using System.Reflection;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The mutation path: what learning actually does to persistent state, and what a
    /// refusal must leave alone.
    /// </summary>
    internal sealed class SkillLearningApplyTests : SkillLearningTestBase
    {
        [Test]
        public void AnAllowedLearnAddsExactlyOneSkillAtRankOne()
        {
            AddSkill("skill.free");
            CharacterSkillsState learned = NewSkills();

            bool applied = TryLearn(learned, NewCharacter(ClassA), 1, "skill.free",
                out SkillLearnEligibility result);

            Assert.IsTrue(applied);
            Assert.IsTrue(result.IsAllowed);
            Assert.AreEqual(1, learned.Count);
            Assert.AreEqual(1, learned.GetRankOrDefault(Id("skill.free"), -1));
            Assert.AreEqual(1, learned.Revision.Value);
        }

        [Test]
        public void ARefusedLearnLeavesStateAndRevisionUntouched()
        {
            AddSkill("skill.gated", requiredLevel: 15);
            CharacterSkillsState learned = NewSkills();

            bool applied = TryLearn(learned, NewCharacter(ClassA), 14, "skill.gated",
                out SkillLearnEligibility result);

            Assert.IsFalse(applied);
            Assert.AreEqual(SkillLearnRejection.LevelTooLow, result.Reason);
            Assert.AreEqual(0, learned.Count);
            Assert.IsFalse(learned.Knows(Id("skill.gated")));
            Assert.AreEqual(Revision.Initial, learned.Revision);
        }

        [Test]
        public void AnUnknownSkillCannotCreateAPhantomLearnedSkill()
        {
            CharacterSkillsState learned = NewSkills();

            bool applied = TryLearn(learned, NewCharacter(ClassA), 99, "skill.ghost",
                out SkillLearnEligibility result);

            Assert.IsFalse(applied);
            Assert.AreEqual(SkillLearnRejection.UnknownSkill, result.Reason);
            Assert.AreEqual(0, learned.Count);
            Assert.AreEqual(Revision.Initial, learned.Revision);
        }

        [Test]
        public void RepeatedLearningCannotCreateADuplicate()
        {
            AddSkill("skill.free");
            CharacterSkillsState learned = NewSkills();

            Assert.IsTrue(TryLearn(learned, NewCharacter(ClassA), 1, "skill.free", out _));
            Revision afterFirst = learned.Revision;

            bool second = TryLearn(learned, NewCharacter(ClassA), 1, "skill.free",
                out SkillLearnEligibility result);

            Assert.IsFalse(second);
            Assert.AreEqual(SkillLearnRejection.AlreadyLearned, result.Reason);
            Assert.AreEqual(1, learned.Count);
            Assert.AreEqual(afterFirst, learned.Revision,
                "A refused repeat must not advance the revision.");
        }

        [Test]
        public void EachSuccessfulLearnAdvancesTheRevisionExactlyOnce()
        {
            AddSkill("skill.one");
            AddSkill("skill.two");
            AddSkill("skill.three");
            CharacterSkillsState learned = NewSkills();
            CharacterClassState characterClass = NewCharacter(ClassA);

            TryLearn(learned, characterClass, 1, "skill.one", out _);
            Assert.AreEqual(1, learned.Revision.Value);

            TryLearn(learned, characterClass, 1, "skill.two", out _);
            Assert.AreEqual(2, learned.Revision.Value);

            TryLearn(learned, characterClass, 1, "skill.three", out _);
            Assert.AreEqual(3, learned.Revision.Value);
        }

        [Test]
        public void SeveralIndependentSkillsRemainIntact()
        {
            AddSkill("skill.one");
            AddSkill("skill.two");
            AddSkill("skill.blocked", requiredLevel: 40);
            CharacterSkillsState learned = NewSkills();
            CharacterClassState characterClass = NewCharacter(ClassA);

            TryLearn(learned, characterClass, 1, "skill.one", out _);
            TryLearn(learned, characterClass, 1, "skill.two", out _);
            TryLearn(learned, characterClass, 1, "skill.blocked", out _);

            Assert.AreEqual(2, learned.Count);
            Assert.IsTrue(learned.Knows(Id("skill.one")));
            Assert.IsTrue(learned.Knows(Id("skill.two")));
            Assert.IsFalse(learned.Knows(Id("skill.blocked")));
            Assert.AreEqual(2, learned.Revision.Value);
        }

        [Test]
        public void ARefusalAfterEarlierSuccessesKeepsThem()
        {
            AddSkill("skill.one");
            AddSkill("skill.gated", requiredLevel: 50);
            CharacterSkillsState learned = NewSkills();
            CharacterClassState characterClass = NewCharacter(ClassA);

            TryLearn(learned, characterClass, 1, "skill.one", out _);
            Revision afterFirst = learned.Revision;

            Assert.IsFalse(TryLearn(learned, characterClass, 1, "skill.gated", out _));

            Assert.AreEqual(1, learned.Count);
            Assert.IsTrue(learned.Knows(Id("skill.one")));
            Assert.AreEqual(afterFirst, learned.Revision);
        }

        [Test]
        public void APrerequisiteChainCanBeLearnedInOrderButNotOutOfIt()
        {
            AddSkill("skill.base");
            AddSkill("skill.advanced", prerequisites: new[] { Requires("skill.base", 2) });
            CharacterSkillsState learned = NewSkills();
            CharacterClassState characterClass = NewCharacter(ClassA);

            Assert.IsFalse(TryLearn(learned, characterClass, 99, "skill.advanced", out _),
                "The advanced skill must not be reachable before its prerequisite.");
            Assert.AreEqual(0, learned.Count);

            Assert.IsTrue(TryLearn(learned, characterClass, 99, "skill.base", out _));
            Assert.IsFalse(TryLearn(learned, characterClass, 99, "skill.advanced", out _),
                "Rank one is not the rank two the prerequisite demands.");

            learned.SetRank(Id("skill.base"), 2);
            Assert.IsTrue(TryLearn(learned, characterClass, 99, "skill.advanced", out _));
            Assert.AreEqual(2, learned.Count);
        }

        [Test]
        public void LearningIsTheOnlyThingApplyDoes()
        {
            // A job is not granted, a level is not spent, no resource is consumed. The
            // class state the rules read must come back exactly as it went in.
            AddSkill("skill.free", requiredClass: ClassA);
            CharacterClassState characterClass = NewCharacter(ClassA, JobA);
            Revision before = characterClass.Revision;

            TryLearn(NewSkills(), characterClass, 1, "skill.free", out _);

            Assert.AreEqual(before, characterClass.Revision);
            Assert.AreEqual(Id(ClassA), characterClass.BaseClass);
            Assert.AreEqual(Id(JobA), characterClass.CurrentJob);
        }

        [Test]
        public void LearnedSkillsSurviveSerializationAfterBeingTaught()
        {
            AddSkill("skill.one");
            AddSkill("skill.two");
            CharacterSkillsState learned = NewSkills();
            CharacterClassState characterClass = NewCharacter(ClassA);

            TryLearn(learned, characterClass, 1, "skill.one", out _);
            TryLearn(learned, characterClass, 1, "skill.two", out _);

            CharacterSkillsState restored = UnityEngine.JsonUtility
                .FromJson<CharacterSkillsState>(UnityEngine.JsonUtility.ToJson(learned));

            Assert.AreEqual(2, restored.Count);
            Assert.AreEqual(learned.Revision, restored.Revision);
            Assert.IsTrue(restored.Knows(Id("skill.one")));
            Assert.IsTrue(restored.Knows(Id("skill.two")));
        }

        [Test]
        public void NullArgumentsAreRejectedBeforeAnythingIsWritten()
        {
            var evaluator = new SkillLearningEvaluator();
            CharacterSkillsState learned = NewSkills();
            CharacterClassState characterClass = NewCharacter(ClassA);

            Assert.Throws<ArgumentNullException>(() => evaluator.TryLearn(
                null, characterClass, 1, Id("skill.x"), Skills, out _));
            Assert.Throws<ArgumentNullException>(() => evaluator.TryLearn(
                learned, null, 1, Id("skill.x"), Skills, out _));
            Assert.Throws<ArgumentNullException>(() => evaluator.TryLearn(
                learned, characterClass, 1, Id("skill.x"), null, out _));

            Assert.AreEqual(Revision.Initial, learned.Revision);
        }

        // ---------- boundaries ----------

        [Test]
        public void TheRuleLayerOwnsNoState()
        {
            // It reads existing aggregates and holds nothing, so no level, class, job or
            // learned skill can drift out of sync with its source of truth.
            Assert.IsEmpty(typeof(SkillLearningEvaluator).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public));
        }

        [Test]
        public void NoCombatOrCastingWasIntroduced()
        {
            string[] forbidden =
            {
                "SkillExecutor", "SkillCaster", "CastSkill", "ExecuteSkill", "CombatResolver",
                "DamageResolver", "HealResolver", "CooldownManager", "TargetResolver",
                "ResourceCostProcessor"
            };

            foreach (Type type in typeof(SkillLearningEvaluator).Assembly.GetTypes())
            {
                foreach (string name in forbidden)
                {
                    Assert.AreNotEqual(name, type.Name, "Combat belongs to a later step.");
                }

                foreach (MemberInfo member in type.GetMembers())
                {
                    foreach (string name in forbidden)
                    {
                        Assert.AreNotEqual(name, member.Name, "Combat belongs to a later step.");
                    }
                }
            }
        }

        [Test]
        public void CarriesNoForbiddenDependency()
        {
            foreach (AssemblyName referenced in
                typeof(SkillLearningEvaluator).Assembly.GetReferencedAssemblies())
            {
                Assert.IsFalse(referenced.Name.StartsWith("FishNet", StringComparison.Ordinal));
                Assert.IsFalse(referenced.Name.StartsWith("UnityEditor", StringComparison.Ordinal));
                Assert.IsFalse(referenced.Name.StartsWith("ChibiFantasy.Backend", StringComparison.Ordinal));
                Assert.IsFalse(referenced.Name.StartsWith("ChibiFantasy.UI", StringComparison.Ordinal));
                Assert.IsFalse(referenced.Name.StartsWith("ChibiFantasy.Network", StringComparison.Ordinal));
            }
        }

        [Test]
        public void NoSkillPointSystemWasInvented()
        {
            // Nothing in the project represents a skill point or a learning cost, so no
            // such requirement is checked and no parallel resource system was created.
            string[] forbidden = { "SkillPoint", "SkillPoints", "LearningCost", "SkillCost" };

            foreach (Type type in typeof(SkillLearningEvaluator).Assembly.GetTypes())
            {
                foreach (string name in forbidden)
                {
                    Assert.AreNotEqual(name, type.Name);
                }
            }

            foreach (string name in Enum.GetNames(typeof(SkillLearnRejection)))
            {
                Assert.IsFalse(name.IndexOf("Point", StringComparison.OrdinalIgnoreCase) >= 0,
                    "A reason for a requirement nothing represents would be a fiction.");
            }
        }
    }
}
