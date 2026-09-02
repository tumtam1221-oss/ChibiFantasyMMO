using System;
using System.Reflection;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    internal sealed class CharacterProgressionStateTests : ProgressionTestBase
    {
        [Test]
        public void StartsAtCurveMinimumWithNoExperience()
        {
            CharacterProgressionState progression = NewProgression(StandardCurve());

            Assert.AreEqual(1, progression.Level);
            Assert.AreEqual(0L, progression.Experience);
            Assert.AreEqual(Revision.Initial, progression.Revision);
            Assert.IsTrue(progression.CharacterId.IsValid);
        }

        [Test]
        public void IsPersistentState()
        {
            CharacterProgressionState progression = NewProgression(StandardCurve());

            Assert.IsInstanceOf<IPersistentState>(progression);
            Assert.IsInstanceOf<IVersionedState>(progression);
            Assert.IsNotInstanceOf<IRuntimeState>(progression);
        }

        [Test]
        public void RestoreRejectsInvalidValues()
        {
            Assert.Throws<ArgumentException>(
                () => new CharacterProgressionState(CharacterId.None, 1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new CharacterProgressionState(CharacterId.New(), 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new CharacterProgressionState(CharacterId.New(), -3, 0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new CharacterProgressionState(CharacterId.New(), 1, -1));
        }

        [Test]
        public void RestoresPersistedValues()
        {
            CharacterId id = CharacterId.New();
            var progression = new CharacterProgressionState(id, 12, 450);

            Assert.AreEqual(id, progression.CharacterId);
            Assert.AreEqual(12, progression.Level);
            Assert.AreEqual(450L, progression.Experience);
        }

        [Test]
        public void NegativeExperienceGainIsRejectedAndChangesNothing()
        {
            CharacterProgressionDefinition curve = StandardCurve();
            CharacterProgressionState progression = NewProgression(curve);
            progression.AddExperience(50, curve);

            int level = progression.Level;
            long experience = progression.Experience;
            Revision revision = progression.Revision;

            Assert.Throws<ArgumentOutOfRangeException>(() => progression.AddExperience(-1, curve));

            Assert.AreEqual(level, progression.Level);
            Assert.AreEqual(experience, progression.Experience);
            Assert.AreEqual(revision, progression.Revision);
        }

        [Test]
        public void NullCurveIsRejected()
        {
            CharacterProgressionState progression = NewProgression(StandardCurve());

            Assert.Throws<ArgumentNullException>(() => progression.AddExperience(10, null));
            Assert.Throws<ArgumentNullException>(() => progression.IsAtMaxLevel(null));
            Assert.Throws<ArgumentNullException>(
                () => new CharacterProgressionState(CharacterId.New(), null));
        }

        [Test]
        public void LevelOutsideTheCurveIsRejected()
        {
            CharacterProgressionDefinition curve = StandardCurve();
            var progression = new CharacterProgressionState(CharacterId.New(), 99, 0);

            Assert.Throws<ArgumentException>(() => progression.AddExperience(10, curve));
            Assert.AreEqual(Revision.Initial, progression.Revision);
        }

        [Test]
        public void OverflowIsRejectedRatherThanWrapping()
        {
            CharacterProgressionDefinition curve = Curve("big", 1, 2, long.MaxValue);
            var progression = new CharacterProgressionState(CharacterId.New(), 1, long.MaxValue - 5);

            Assert.Throws<OverflowException>(() => progression.AddExperience(100, curve));
            Assert.AreEqual(long.MaxValue - 5, progression.Experience);
            Assert.AreEqual(Revision.Initial, progression.Revision);
        }

        [Test]
        public void ReadsDoNotAdvanceRevision()
        {
            CharacterProgressionDefinition curve = StandardCurve();
            CharacterProgressionState progression = NewProgression(curve);

            int ignoredLevel = progression.Level;
            long ignoredExperience = progression.Experience;
            bool ignoredMax = progression.IsAtMaxLevel(curve);

            Assert.AreEqual(Revision.Initial, progression.Revision);
            Assert.AreEqual(1, ignoredLevel);
            Assert.AreEqual(0L, ignoredExperience);
            Assert.IsFalse(ignoredMax);
        }

        [Test]
        public void CharacterIdentityIsPreservedThroughProgression()
        {
            CharacterProgressionDefinition curve = StandardCurve();
            var progression = new CharacterProgressionState(CharacterId.New(), curve);
            CharacterId id = progression.CharacterId;

            progression.AddExperience(1000, curve);

            Assert.AreEqual(id, progression.CharacterId);
        }

        [Test]
        public void SurvivesSerializationRoundTrip()
        {
            CharacterProgressionDefinition curve = StandardCurve();
            CharacterProgressionState original = NewProgression(curve);
            original.AddExperience(250, curve);

            string json = JsonUtility.ToJson(original);
            CharacterProgressionState restored = JsonUtility.FromJson<CharacterProgressionState>(json);

            Assert.AreEqual(original.CharacterId, restored.CharacterId);
            Assert.AreEqual(original.Level, restored.Level);
            Assert.AreEqual(original.Experience, restored.Experience);
            Assert.AreEqual(original.Revision, restored.Revision);
        }

        [Test]
        public void ExperienceUsesALongNotAnInt()
        {
            FieldInfo field = typeof(CharacterProgressionState).GetField(
                "_experience", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(field);
            Assert.AreEqual(typeof(long), field.FieldType);
        }

        [Test]
        public void CarriesNoUnityObjectOrForbiddenDependency()
        {
            Assert.IsFalse(typeof(UnityEngine.Object).IsAssignableFrom(typeof(CharacterProgressionState)));

            Assembly data = typeof(CharacterProgressionState).Assembly;
            foreach (AssemblyName referenced in data.GetReferencedAssemblies())
            {
                Assert.IsFalse(referenced.Name.StartsWith("FishNet", StringComparison.Ordinal));
                Assert.IsFalse(referenced.Name.StartsWith("UnityEditor", StringComparison.Ordinal));
            }
        }

        [Test]
        public void IsNotEmbeddedInCharacterState()
        {
            foreach (FieldInfo field in typeof(CharacterState).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                Assert.AreNotEqual(typeof(CharacterProgressionState), field.FieldType);
            }
        }
    }
}
