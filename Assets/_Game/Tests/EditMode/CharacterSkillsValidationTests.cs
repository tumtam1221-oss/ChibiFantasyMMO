using System;
using System.Collections.Generic;
using System.Reflection;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Validation of learned-skill state against skill content.
    /// </summary>
    /// <remarks>
    /// The faults here are ones <see cref="CharacterSkillsState"/> refuses to create
    /// through its own API, so they are injected directly into the backing list. That is
    /// not a contrivance: it is exactly the shape state arrives in from a database row, a
    /// save file or a patched content set, which is the only reason a validator is needed
    /// at all.
    /// </remarks>
    internal sealed class CharacterSkillsValidationTests : CharacterSkillsTestBase
    {
        /// <summary>Injects entries past the guards, as deserialization would.</summary>
        private static CharacterSkillsState Corrupt(params CharacterSkillEntry[] entries)
        {
            CharacterSkillsState skills = NewSkills();
            typeof(CharacterSkillsState)
                .GetField("_skills", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(skills, new List<CharacterSkillEntry>(entries));
            return skills;
        }

        // ---------- valid ----------

        [Test]
        public void ValidLearnedSkillsPassValidation()
        {
            AddSkills();
            CharacterSkillsState skills = NewSkills();
            skills.SetRank(Id(SkillA), 1);
            skills.SetRank(Id(SkillB), 3);
            skills.SetRank(Id(SkillC), 5);

            ValidationReport report = Validate(skills);

            Assert.IsTrue(report.IsValid,
                report.Messages.Count > 0 ? report.Messages[0].ToString() : string.Empty);
            Assert.AreEqual(0, report.ErrorCount);
        }

        [Test]
        public void KnowingNothingIsValid()
        {
            AddSkills();

            Assert.IsTrue(Validate(NewSkills()).IsValid);
        }

        [Test]
        public void RankAtTheSkillsMaximumIsValid()
        {
            AddSkill(SkillA, 3);
            CharacterSkillsState skills = NewSkills();
            skills.SetRank(Id(SkillA), 3);

            Assert.IsTrue(Validate(skills).IsValid);
        }

        // ---------- invalid ----------

        [Test]
        public void AnUnsetSkillIdIsReported()
        {
            AddSkills();
            CharacterSkillsState skills = Corrupt(new CharacterSkillEntry(DefinitionId.None, 1));

            ValidationReport report = Validate(skills);

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.MissingDefinitionId, report.Messages[0].Code);
        }

        [Test]
        public void ADuplicateSkillIsReported()
        {
            AddSkills();
            CharacterSkillsState skills = Corrupt(
                new CharacterSkillEntry(Id(SkillA), 1),
                new CharacterSkillEntry(Id(SkillA), 2));

            ValidationReport report = Validate(skills);

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.DuplicateDefinitionId, report.Messages[0].Code);
            StringAssert.Contains("more than once", report.Messages[0].Message);
        }

        [Test]
        public void ARankBelowOneIsReported()
        {
            AddSkills();
            CharacterSkillsState skills = Corrupt(new CharacterSkillEntry(Id(SkillA), 0));

            ValidationReport report = Validate(skills);

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.ValueOutOfRange, report.Messages[0].Code);
            StringAssert.Contains("below one", report.Messages[0].Message);
        }

        [Test]
        public void ARankAboveTheSkillsMaximumIsReported()
        {
            AddSkill(SkillA, 3);
            CharacterSkillsState skills = NewSkills();
            skills.SetRank(Id(SkillA), 9);

            ValidationReport report = Validate(skills);

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.ValueOutOfRange, report.Messages[0].Code);
            StringAssert.Contains("above the skill's maximum of 3", report.Messages[0].Message);
        }

        [Test]
        public void ASkillWithNoDefinitionIsReportedAsOrphanedRatherThanDeleted()
        {
            AddSkills();
            CharacterSkillsState skills = NewSkills();
            skills.Learn(Id("skill.removedbyapatch"));

            ValidationReport report = Validate(skills);

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.MissingReference, report.Messages[0].Code);
            StringAssert.Contains("orphaned by a patch", report.Messages[0].Message);
            Assert.AreEqual(1, skills.Count, "Validation must report, never repair.");
        }

        [Test]
        public void SkillsNotAttachedToACharacterAreReported()
        {
            AddSkills();
            var skills = new CharacterSkillsState();

            ValidationReport report = Validate(skills);

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.MissingDefinitionId, report.Messages[0].Code);
            StringAssert.Contains("not attached to a character", report.Messages[0].Message);
        }

        [Test]
        public void EveryFaultIsReportedRatherThanOnlyTheFirst()
        {
            AddSkill(SkillA, 2);
            CharacterSkillsState skills = Corrupt(
                new CharacterSkillEntry(DefinitionId.None, 1),
                new CharacterSkillEntry(Id(SkillA), 7),
                new CharacterSkillEntry(Id("skill.ghost"), 1),
                new CharacterSkillEntry(Id(SkillA), 1));

            ValidationReport report = Validate(skills);

            Assert.AreEqual(4, report.ErrorCount,
                "Fixing corrupt state one entry at a time is a guessing game.");
        }

        // ---------- behaviour ----------

        [Test]
        public void ValidationDoesNotMutateTheState()
        {
            AddSkill(SkillA, 2);
            CharacterSkillsState skills = NewSkills();
            skills.SetRank(Id(SkillA), 2);
            skills.Learn(Id("skill.ghost"));

            string before = JsonUtility.ToJson(skills);
            Revision revision = skills.Revision;

            Validate(skills);

            Assert.AreEqual(before, JsonUtility.ToJson(skills));
            Assert.AreEqual(revision, skills.Revision);
        }

        [Test]
        public void ValidationIsDeterministic()
        {
            AddSkill(SkillA, 2);
            CharacterSkillsState skills = Corrupt(
                new CharacterSkillEntry(DefinitionId.None, 1),
                new CharacterSkillEntry(Id(SkillA), 7),
                new CharacterSkillEntry(Id("skill.ghost"), 0));

            ValidationReport first = Validate(skills);
            ValidationReport second = Validate(skills);

            Assert.AreEqual(first.Messages.Count, second.Messages.Count);

            for (int i = 0; i < first.Messages.Count; i++)
            {
                Assert.AreEqual(first.Messages[i].Code, second.Messages[i].Code, "index " + i);
                Assert.AreEqual(first.Messages[i].Message, second.Messages[i].Message, "index " + i);
            }
        }

        [Test]
        public void NullArgumentsAreRejected()
        {
            var validator = new CharacterSkillsValidator();

            Assert.Throws<ArgumentNullException>(() => validator.Validate(null, Definitions));
            Assert.Throws<ArgumentNullException>(() => validator.Validate(NewSkills(), null));
        }

        [Test]
        public void NoCombatConcernIsValidated()
        {
            // Cooldown, cost, target legality and damage are runtime questions this
            // validator must not have started answering.
            string[] forbidden = { "Cooldown", "Cost", "Target", "Damage", "Resource", "Cast" };

            foreach (MemberInfo member in typeof(CharacterSkillsValidator).GetMembers())
            {
                foreach (string name in forbidden)
                {
                    Assert.IsFalse(
                        member.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0,
                        "Combat belongs to a later step, found " + member.Name);
                }
            }
        }
    }
}
