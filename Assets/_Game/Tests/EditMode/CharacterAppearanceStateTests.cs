using System;
using System.Reflection;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    public sealed class CharacterAppearanceStateTests
    {
        private static CharacterAppearanceState NewAppearance(out CharacterId id)
        {
            id = CharacterId.New();
            return new CharacterAppearanceState(id);
        }

        [Test]
        public void IsPersistentStateAndStartsAtInitialRevision()
        {
            CharacterAppearanceState appearance = NewAppearance(out CharacterId id);

            Assert.IsInstanceOf<IPersistentState>(appearance);
            Assert.IsInstanceOf<IVersionedState>(appearance);
            Assert.IsNotInstanceOf<IRuntimeState>(appearance);
            Assert.AreEqual(Revision.Initial, appearance.Revision);
            Assert.AreEqual(id, appearance.CharacterId);
        }

        [Test]
        public void RequiresACharacter()
        {
            Assert.Throws<ArgumentException>(() => new CharacterAppearanceState(CharacterId.None));
        }

        [Test]
        public void EverySlotIsSelectedByDefinitionId()
        {
            CharacterAppearanceState appearance = NewAppearance(out _);

            appearance.Select(AppearanceSlot.Face, new DefinitionId("face_001"));
            appearance.Select(AppearanceSlot.Eyes, new DefinitionId("eye_001"));
            appearance.Select(AppearanceSlot.Hair, new DefinitionId("hair_001"));
            appearance.Select(AppearanceSlot.HairColor, new DefinitionId("hair_color_001"));
            appearance.Select(AppearanceSlot.SkinTone, new DefinitionId("skin_001"));

            Assert.AreEqual(new DefinitionId("face_001"), appearance.Face);
            Assert.AreEqual(new DefinitionId("eye_001"), appearance.Eyes);
            Assert.AreEqual(new DefinitionId("hair_001"), appearance.Hair);
            Assert.AreEqual(new DefinitionId("hair_color_001"), appearance.HairColor);
            Assert.AreEqual(new DefinitionId("skin_001"), appearance.SkinTone);

            Assert.AreEqual(new DefinitionId("hair_001"), appearance.Get(AppearanceSlot.Hair));
        }

        [Test]
        public void EverySelectionFieldIsADefinitionId()
        {
            foreach (FieldInfo field in typeof(CharacterAppearanceState).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (field.FieldType == typeof(CharacterId) || field.FieldType == typeof(Revision))
                {
                    continue;
                }

                Assert.AreEqual(typeof(DefinitionId), field.FieldType,
                    field.Name + " must be a DefinitionId, never an asset path or Unity reference.");
            }
        }

        [Test]
        public void SelectionAdvancesRevisionOncePerChange()
        {
            CharacterAppearanceState appearance = NewAppearance(out _);

            appearance.Select(AppearanceSlot.Hair, new DefinitionId("hair_001"));
            Assert.AreEqual(1, appearance.Revision.Value);

            appearance.Select(AppearanceSlot.Hair, new DefinitionId("hair_002"));
            Assert.AreEqual(2, appearance.Revision.Value);
        }

        [Test]
        public void RejectedSelectionDoesNotAdvanceRevision()
        {
            CharacterAppearanceState appearance = NewAppearance(out _);

            Assert.Throws<ArgumentOutOfRangeException>(
                () => appearance.Select(AppearanceSlot.None, new DefinitionId("x")));

            Assert.AreEqual(Revision.Initial, appearance.Revision);
        }

        [Test]
        public void CharacterIdentityIsPreservedAcrossSelections()
        {
            CharacterAppearanceState appearance = NewAppearance(out CharacterId id);

            appearance.Select(AppearanceSlot.Face, new DefinitionId("face_001"));
            appearance.Select(AppearanceSlot.Hair, new DefinitionId("hair_009"));

            Assert.AreEqual(id, appearance.CharacterId);
        }

        [Test]
        public void SurvivesSerializationWithSelectionsIdentityAndRevisionIntact()
        {
            CharacterAppearanceState original = NewAppearance(out CharacterId id);
            original.Select(AppearanceSlot.Face, new DefinitionId("face_003"));
            original.Select(AppearanceSlot.Hair, new DefinitionId("hair_007"));
            original.Select(AppearanceSlot.SkinTone, new DefinitionId("skin_002"));

            string json = JsonUtility.ToJson(original);
            CharacterAppearanceState restored = JsonUtility.FromJson<CharacterAppearanceState>(json);

            Assert.AreEqual(id, restored.CharacterId);
            Assert.AreEqual(new DefinitionId("face_003"), restored.Face);
            Assert.AreEqual(new DefinitionId("hair_007"), restored.Hair);
            Assert.AreEqual(new DefinitionId("skin_002"), restored.SkinTone);
            Assert.AreEqual(original.Revision, restored.Revision);
        }

        [Test]
        public void DoesNotDuplicateOwnerOrGender()
        {
            // Ownership and gender have exactly one home, on CharacterState.
            foreach (FieldInfo field in typeof(CharacterAppearanceState).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                Assert.AreNotEqual(typeof(OwnerId), field.FieldType,
                    "Ownership must not be duplicated onto appearance.");
                Assert.AreNotEqual(typeof(CharacterGender), field.FieldType,
                    "Gender must not be duplicated onto appearance.");
            }
        }

        [Test]
        public void IsNotEmbeddedInCharacterState()
        {
            // Composition: appearance is a sibling aggregate, so CharacterState stays small.
            foreach (FieldInfo field in typeof(CharacterState).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                Assert.AreNotEqual(typeof(CharacterAppearanceState), field.FieldType);
            }
        }

        [Test]
        public void CarriesNoUnityObjectOrForbiddenDependency()
        {
            Assert.IsFalse(typeof(UnityEngine.Object).IsAssignableFrom(typeof(CharacterAppearanceState)));

            Assembly data = typeof(CharacterAppearanceState).Assembly;
            foreach (AssemblyName referenced in data.GetReferencedAssemblies())
            {
                Assert.IsFalse(referenced.Name.StartsWith("FishNet", StringComparison.Ordinal));
                Assert.IsFalse(referenced.Name.StartsWith("UnityEditor", StringComparison.Ordinal));
            }
        }
    }
}
