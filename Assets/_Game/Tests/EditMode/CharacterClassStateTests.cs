using System;
using System.Reflection;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    internal sealed class CharacterClassStateTests : ClassJobTestBase
    {
        [TestCase(Swordsman)]
        [TestCase(Cleric)]
        [TestCase(Mage)]
        [TestCase(Archer)]
        public void EachStartingClassIsRepresentable(string startingClass)
        {
            CharacterClassState state = NewCharacter(startingClass);

            Assert.AreEqual(new DefinitionId(startingClass), state.BaseClass);
            Assert.IsFalse(state.HasChangedJob);
            Assert.AreEqual(DefinitionId.None, state.CurrentJob);
        }

        [Test]
        public void IsPersistentStateWithStableIdentity()
        {
            CharacterId id = CharacterId.New();
            var state = new CharacterClassState(id, new DefinitionId(Mage));

            Assert.IsInstanceOf<IPersistentState>(state);
            Assert.IsInstanceOf<IVersionedState>(state);
            Assert.IsNotInstanceOf<IRuntimeState>(state);
            Assert.AreEqual(id, state.CharacterId);
            Assert.AreEqual(Revision.Initial, state.Revision);
        }

        [Test]
        public void RequiresACharacterAndAClass()
        {
            Assert.Throws<ArgumentException>(
                () => new CharacterClassState(CharacterId.None, new DefinitionId(Mage)));
            Assert.Throws<ArgumentException>(
                () => new CharacterClassState(CharacterId.New(), DefinitionId.None));
        }

        [Test]
        public void ClassAndJobAreStoredAsDefinitionIds()
        {
            foreach (FieldInfo field in typeof(CharacterClassState).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (field.FieldType == typeof(CharacterId) || field.FieldType == typeof(Revision))
                {
                    continue;
                }

                Assert.AreEqual(typeof(DefinitionId), field.FieldType,
                    field.Name + " must be a stable id.");
            }
        }

        [Test]
        public void NoScriptableObjectOrUnityReferenceIsStored()
        {
            Assert.IsFalse(typeof(UnityEngine.Object).IsAssignableFrom(typeof(CharacterClassState)));

            foreach (FieldInfo field in typeof(CharacterClassState).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                Assert.IsFalse(typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType),
                    field.Name + " must not hold a Unity object.");
                Assert.AreNotEqual(typeof(ClassDefinition), field.FieldType);
                Assert.AreNotEqual(typeof(JobDefinition), field.FieldType);
            }
        }

        [Test]
        public void SetJobAdvancesRevisionOnceForARealChange()
        {
            CharacterClassState state = NewCharacter(Swordsman);

            state.SetJob(new DefinitionId("job.sword.first"));
            Assert.AreEqual(1, state.Revision.Value);
            Assert.IsTrue(state.HasChangedJob);

            state.SetJob(new DefinitionId("job.sword.branch_a"));
            Assert.AreEqual(2, state.Revision.Value);
        }

        [Test]
        public void SettingTheJobAlreadyHeldChangesNothing()
        {
            CharacterClassState state = NewCharacter(Swordsman);
            state.SetJob(new DefinitionId("job.sword.first"));
            Revision after = state.Revision;

            state.SetJob(new DefinitionId("job.sword.first"));

            Assert.AreEqual(after, state.Revision);
        }

        [Test]
        public void SetJobRejectsAnEmptyTargetWithoutChangingState()
        {
            CharacterClassState state = NewCharacter(Swordsman);

            Assert.Throws<ArgumentException>(() => state.SetJob(DefinitionId.None));

            Assert.AreEqual(Revision.Initial, state.Revision);
            Assert.IsFalse(state.HasChangedJob);
        }

        [Test]
        public void BaseClassNeverChanges()
        {
            CharacterClassState state = NewCharacter(Archer);
            DefinitionId baseClass = state.BaseClass;

            state.SetJob(new DefinitionId("job.archer.first"));

            Assert.AreEqual(baseClass, state.BaseClass);

            foreach (MemberInfo member in typeof(CharacterClassState).GetMembers())
            {
                Assert.AreNotEqual("SetBaseClass", member.Name,
                    "A class change would invalidate every job beneath it.");
            }
        }

        [Test]
        public void SurvivesSerializationDeterministically()
        {
            CharacterClassState original = NewCharacter(Cleric);
            original.SetJob(new DefinitionId("job.cleric.first"));

            string json = JsonUtility.ToJson(original);
            CharacterClassState restored = JsonUtility.FromJson<CharacterClassState>(json);

            Assert.AreEqual(original.CharacterId, restored.CharacterId);
            Assert.AreEqual(original.BaseClass, restored.BaseClass);
            Assert.AreEqual(original.CurrentJob, restored.CurrentJob);
            Assert.AreEqual(original.Revision, restored.Revision);
            Assert.AreEqual(json, JsonUtility.ToJson(restored), "Serialization must be stable.");
        }

        [Test]
        public void DoesNotDuplicateLevelOrStats()
        {
            foreach (FieldInfo field in typeof(CharacterClassState).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                Assert.IsFalse(field.Name.IndexOf("level", StringComparison.OrdinalIgnoreCase) >= 0,
                    "Level lives on CharacterProgressionState.");
                Assert.IsFalse(field.Name.IndexOf("stat", StringComparison.OrdinalIgnoreCase) >= 0,
                    "Stats live on CharacterStatsState.");
                Assert.IsFalse(field.Name.IndexOf("exp", StringComparison.OrdinalIgnoreCase) >= 0);
            }
        }

        [Test]
        public void CarriesNoForbiddenDependency()
        {
            Assembly data = typeof(CharacterClassState).Assembly;

            foreach (AssemblyName referenced in data.GetReferencedAssemblies())
            {
                Assert.IsFalse(referenced.Name.StartsWith("FishNet", StringComparison.Ordinal));
                Assert.IsFalse(referenced.Name.StartsWith("UnityEditor", StringComparison.Ordinal));
                Assert.IsFalse(referenced.Name.StartsWith("ChibiFantasy.UI", StringComparison.Ordinal));
                Assert.IsFalse(referenced.Name.StartsWith("ChibiFantasy.Backend", StringComparison.Ordinal));
            }
        }
    }
}
