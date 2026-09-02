using System;
using System.Reflection;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    public sealed class CharacterStateTests
    {
        private static CharacterState NewCharacter()
        {
            return new CharacterState(
                CharacterId.New(), new OwnerId("account:1"), "Hero", CharacterGender.Female);
        }

        [Test]
        public void CanBeCreatedWithIdentityAndProfile()
        {
            CharacterState character = NewCharacter();

            Assert.IsTrue(character.CharacterId.IsValid);
            Assert.AreEqual(new OwnerId("account:1"), character.Owner);
            Assert.AreEqual("Hero", character.Name);
            Assert.AreEqual(CharacterGender.Female, character.Gender);
            Assert.AreEqual(Revision.Initial, character.Revision);
        }

        [Test]
        public void IsClassifiedAsPersistentState()
        {
            Assert.IsInstanceOf<IPersistentState>(NewCharacter());
            Assert.IsInstanceOf<IVersionedState>(NewCharacter());
            Assert.IsNotInstanceOf<IRuntimeState>(NewCharacter());
        }

        [Test]
        public void IsNotAGameInstance()
        {
            // A character owns content; it is not a copy of authored content.
            Assert.IsFalse(typeof(GameInstance).IsAssignableFrom(typeof(CharacterState)));
            Assert.IsFalse(typeof(IGameInstance).IsAssignableFrom(typeof(CharacterState)));
        }

        [Test]
        public void RejectsInvalidConstruction()
        {
            Assert.Throws<ArgumentException>(() => new CharacterState(
                CharacterId.None, new OwnerId("a"), "Hero", CharacterGender.Male));
            Assert.Throws<ArgumentException>(() => new CharacterState(
                CharacterId.New(), OwnerId.None, "Hero", CharacterGender.Male));
            Assert.Throws<ArgumentException>(() => new CharacterState(
                CharacterId.New(), new OwnerId("a"), "   ", CharacterGender.Male));
            Assert.Throws<ArgumentException>(() => new CharacterState(
                CharacterId.New(), new OwnerId("a"), "Hero", CharacterGender.Unspecified));
        }

        [Test]
        public void IdentityAndOwnerSurviveMutation()
        {
            CharacterState character = NewCharacter();
            CharacterId id = character.CharacterId;
            OwnerId owner = character.Owner;

            character.Rename("Renamed");
            character.Rename("RenamedAgain");

            Assert.AreEqual(id, character.CharacterId);
            Assert.AreEqual(owner, character.Owner);
            Assert.AreEqual("RenamedAgain", character.Name);
        }

        [Test]
        public void RenameAdvancesRevisionExactlyOncePerSuccess()
        {
            CharacterState character = NewCharacter();

            Assert.AreEqual(0, character.Revision.Value);

            character.Rename("A");
            Assert.AreEqual(1, character.Revision.Value);

            character.Rename("B");
            Assert.AreEqual(2, character.Revision.Value);
        }

        [Test]
        public void FailedRenameDoesNotAdvanceRevisionOrChangeState()
        {
            CharacterState character = NewCharacter();
            Revision before = character.Revision;

            Assert.Throws<ArgumentException>(() => character.Rename(""));

            Assert.AreEqual(before, character.Revision);
            Assert.AreEqual("Hero", character.Name);
        }

        [Test]
        public void SurvivesSerializationWithIdentityOwnerAndRevisionIntact()
        {
            CharacterState original = NewCharacter();
            original.Rename("Persisted");

            string json = JsonUtility.ToJson(original);
            CharacterState restored = JsonUtility.FromJson<CharacterState>(json);

            Assert.AreEqual(original.CharacterId, restored.CharacterId);
            Assert.AreEqual(original.Owner, restored.Owner);
            Assert.AreEqual(original.Revision, restored.Revision);
            Assert.AreEqual("Persisted", restored.Name);
            Assert.AreEqual(CharacterGender.Female, restored.Gender);
        }

        [Test]
        public void CarriesNoUnityObjectOrForbiddenDependency()
        {
            Assert.IsFalse(typeof(UnityEngine.Object).IsAssignableFrom(typeof(CharacterState)));

            for (Type current = typeof(CharacterState); current != null; current = current.BaseType)
            {
                Assert.AreNotEqual("MonoBehaviour", current.Name);
                Assert.AreNotEqual("NetworkBehaviour", current.Name);
                Assert.AreNotEqual("ScriptableObject", current.Name);
            }

            Assembly data = typeof(CharacterState).Assembly;
            foreach (AssemblyName referenced in data.GetReferencedAssemblies())
            {
                Assert.IsFalse(referenced.Name.StartsWith("FishNet", StringComparison.Ordinal));
                Assert.IsFalse(referenced.Name.StartsWith("UnityEditor", StringComparison.Ordinal));
            }
        }

        [Test]
        public void DoesNotHardCodeStartingClassesOrStoreClassAtAll()
        {
            string[] forbidden = { "Swordsman", "Cleric", "Mage", "Archer" };

            foreach (FieldInfo field in typeof(CharacterState).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                foreach (string name in forbidden)
                {
                    Assert.IsFalse(field.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0,
                        "Starting classes must be content, never fields.");
                }

                Assert.AreNotEqual(typeof(ClassDefinition), field.FieldType,
                    "A character must never hold a direct ScriptableObject definition reference.");
            }
        }

        [Test]
        public void CharacterGenderIsDistinctFromGenderAvailability()
        {
            Assert.AreNotEqual(typeof(CharacterGender), typeof(GenderAvailability));
            Assert.IsFalse(Enum.IsDefined(typeof(CharacterGender), "Any"),
                "Any is a class-availability permission, not a character's gender.");
            Assert.IsTrue(Enum.IsDefined(typeof(CharacterGender), "Male"));
            Assert.IsTrue(Enum.IsDefined(typeof(CharacterGender), "Female"));
            Assert.AreEqual(0, (int)CharacterGender.Unspecified,
                "Unset must not silently read as a real gender.");
        }
    }
}
