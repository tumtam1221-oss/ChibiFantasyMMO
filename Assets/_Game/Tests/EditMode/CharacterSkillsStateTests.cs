using System;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The persistent side: what a character knows, at what rank, and how change is counted.
    /// </summary>
    internal sealed class CharacterSkillsStateTests : CharacterSkillsTestBase
    {
        [Test]
        public void ACharacterStartsKnowingNothing()
        {
            CharacterSkillsState skills = NewSkills();

            Assert.AreEqual(0, skills.Count);
            Assert.AreEqual(Revision.Initial, skills.Revision);
            Assert.IsFalse(skills.Knows(Id(SkillA)));
        }

        [Test]
        public void ALearnedSkillIsKnownAtRankOne()
        {
            CharacterSkillsState skills = NewSkills();

            Assert.IsTrue(skills.Learn(Id(SkillA)));

            Assert.IsTrue(skills.Knows(Id(SkillA)));
            Assert.IsTrue(skills.TryGetRank(Id(SkillA), out int rank));
            Assert.AreEqual(1, rank);
            Assert.AreEqual(1, skills.Count);
        }

        [Test]
        public void TheSkillDefinitionIdIsPreservedExactly()
        {
            CharacterSkillsState skills = NewSkills();
            skills.Learn(Id(SkillA));

            Assert.AreEqual(Id(SkillA), skills.Skills[0].Skill);
        }

        [Test]
        public void RankIsPreserved()
        {
            CharacterSkillsState skills = NewSkills();
            skills.SetRank(Id(SkillA), 4);

            Assert.AreEqual(4, skills.GetRankOrDefault(Id(SkillA), -1));
            Assert.AreEqual(4, skills.Skills[0].Rank);
        }

        [Test]
        public void ACharacterCanKnowSeveralSkillsAtDifferentRanks()
        {
            CharacterSkillsState skills = NewSkills();
            skills.SetRank(Id(SkillA), 1);
            skills.SetRank(Id(SkillB), 3);
            skills.SetRank(Id(SkillC), 5);

            Assert.AreEqual(3, skills.Count);
            Assert.AreEqual(1, skills.GetRankOrDefault(Id(SkillA), -1));
            Assert.AreEqual(3, skills.GetRankOrDefault(Id(SkillB), -1));
            Assert.AreEqual(5, skills.GetRankOrDefault(Id(SkillC), -1));
        }

        [Test]
        public void SkillsAreStoredInTheOrderTheyWereLearned()
        {
            CharacterSkillsState skills = NewSkills();
            skills.Learn(Id(SkillC));
            skills.Learn(Id(SkillA));
            skills.Learn(Id(SkillB));

            Assert.AreEqual(Id(SkillC), skills.Skills[0].Skill);
            Assert.AreEqual(Id(SkillA), skills.Skills[1].Skill);
            Assert.AreEqual(Id(SkillB), skills.Skills[2].Skill);
        }

        [Test]
        public void ASkillCanNeverBeRecordedTwice()
        {
            CharacterSkillsState skills = NewSkills();
            skills.Learn(Id(SkillA));
            skills.SetRank(Id(SkillA), 2);
            skills.SetRank(Id(SkillA), 3);

            Assert.AreEqual(1, skills.Count, "Upgrading must replace in place, not append.");
            Assert.AreEqual(3, skills.GetRankOrDefault(Id(SkillA), -1));
        }

        [Test]
        public void UpgradingRaisesTheRankOfTheSkillAlreadyKnown()
        {
            CharacterSkillsState skills = NewSkills();
            skills.Learn(Id(SkillA));
            skills.SetRank(Id(SkillA), 5);

            Assert.AreEqual(5, skills.GetRankOrDefault(Id(SkillA), -1));
        }

        [Test]
        public void ForgettingRemovesTheSkill()
        {
            CharacterSkillsState skills = NewSkills();
            skills.Learn(Id(SkillA));
            skills.Learn(Id(SkillB));

            Assert.IsTrue(skills.Forget(Id(SkillA)));

            Assert.IsFalse(skills.Knows(Id(SkillA)));
            Assert.IsTrue(skills.Knows(Id(SkillB)));
            Assert.AreEqual(1, skills.Count);
        }

        [Test]
        public void AnUnknownSkillReadsAsTheFallback()
        {
            CharacterSkillsState skills = NewSkills();

            Assert.IsFalse(skills.TryGetRank(Id(SkillA), out int rank));
            Assert.AreEqual(0, rank);
            Assert.AreEqual(-1, skills.GetRankOrDefault(Id(SkillA), -1));
        }

        // ---------- revision ----------

        [Test]
        public void EachSuccessfulChangeAdvancesTheRevisionExactlyOnce()
        {
            CharacterSkillsState skills = NewSkills();

            skills.Learn(Id(SkillA));
            Assert.AreEqual(1, skills.Revision.Value);

            skills.SetRank(Id(SkillA), 2);
            Assert.AreEqual(2, skills.Revision.Value);

            skills.Learn(Id(SkillB));
            Assert.AreEqual(3, skills.Revision.Value);

            skills.Forget(Id(SkillA));
            Assert.AreEqual(4, skills.Revision.Value);
        }

        [Test]
        public void ReadingNeverAdvancesTheRevision()
        {
            CharacterSkillsState skills = NewSkills();
            skills.Learn(Id(SkillA));
            Revision before = skills.Revision;

            skills.Knows(Id(SkillA));
            skills.TryGetRank(Id(SkillA), out int _);
            skills.GetRankOrDefault(Id(SkillA), 0);
            int count = skills.Count;
            var listed = skills.Skills;

            Assert.AreEqual(before, skills.Revision);
            Assert.AreEqual(1, count);
            Assert.AreEqual(1, listed.Count);
        }

        [Test]
        public void RelearningAKnownSkillIsNotAChange()
        {
            CharacterSkillsState skills = NewSkills();
            skills.Learn(Id(SkillA));
            Revision before = skills.Revision;

            Assert.IsFalse(skills.Learn(Id(SkillA)));
            Assert.AreEqual(before, skills.Revision);
            Assert.AreEqual(1, skills.Count);
        }

        [Test]
        public void SettingTheRankAlreadyHeldIsNotAChange()
        {
            CharacterSkillsState skills = NewSkills();
            skills.SetRank(Id(SkillA), 3);
            Revision before = skills.Revision;

            skills.SetRank(Id(SkillA), 3);

            Assert.AreEqual(before, skills.Revision);
        }

        [Test]
        public void ForgettingASkillNeverKnownIsNotAChange()
        {
            CharacterSkillsState skills = NewSkills();
            skills.Learn(Id(SkillA));
            Revision before = skills.Revision;

            Assert.IsFalse(skills.Forget(Id(SkillB)));
            Assert.AreEqual(before, skills.Revision);
        }

        [Test]
        public void ARejectedChangeLeavesStateAndRevisionAlone()
        {
            CharacterSkillsState skills = NewSkills();
            skills.Learn(Id(SkillA));
            Revision before = skills.Revision;

            Assert.Throws<ArgumentException>(() => skills.Learn(DefinitionId.None));
            Assert.Throws<ArgumentException>(() => skills.SetRank(DefinitionId.None, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => skills.SetRank(Id(SkillB), 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => skills.SetRank(Id(SkillB), -3));

            Assert.AreEqual(before, skills.Revision);
            Assert.AreEqual(1, skills.Count);
            Assert.IsFalse(skills.Knows(Id(SkillB)));
        }

        [Test]
        public void IdentityStaysStableWhileSkillsChange()
        {
            var characterId = CharacterId.New();
            var skills = new CharacterSkillsState(characterId);

            skills.Learn(Id(SkillA));
            skills.SetRank(Id(SkillA), 4);
            skills.Forget(Id(SkillA));

            Assert.AreEqual(characterId, skills.CharacterId);
            Assert.AreEqual(3, skills.Revision.Value);
        }

        [Test]
        public void SkillsMustBelongToACharacter()
        {
            Assert.Throws<ArgumentException>(() => new CharacterSkillsState(CharacterId.None));
        }

        [Test]
        public void TheListedViewCannotBeCastBackAndMutated()
        {
            CharacterSkillsState skills = NewSkills();
            skills.Learn(Id(SkillA));

            Assert.IsNotInstanceOf<System.Collections.Generic.List<CharacterSkillEntry>>(
                skills.Skills);
        }
    }
}
