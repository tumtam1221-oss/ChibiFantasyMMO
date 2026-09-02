using System;
using System.Reflection;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    internal sealed class CharacterAggregateTests : ConsistencyTestBase
    {
        private static readonly Type[] PersistentParts =
        {
            typeof(CharacterState), typeof(CharacterClassState),
            typeof(CharacterAppearanceState), typeof(CharacterProgressionState),
            typeof(CharacterStatsState)
        };

        [Test]
        public void TheAggregateHoldsEveryPartOfACharacter()
        {
            Character character = Create();

            Assert.IsNotNull(character.Identity);
            Assert.IsNotNull(character.Class);
            Assert.IsNotNull(character.Appearance);
            Assert.IsNotNull(character.Progression);
            Assert.IsNotNull(character.Stats);
            Assert.IsNotNull(character.Resources);
        }

        [Test]
        public void ThePersistentRuntimeSplitIsPreserved()
        {
            Character character = Create();

            Assert.IsInstanceOf<IPersistentState>(character.Identity);
            Assert.IsInstanceOf<IPersistentState>(character.Class);
            Assert.IsInstanceOf<IPersistentState>(character.Appearance);
            Assert.IsInstanceOf<IPersistentState>(character.Progression);
            Assert.IsInstanceOf<IPersistentState>(character.Stats);

            Assert.IsInstanceOf<IRuntimeState>(character.Resources);
            Assert.IsNotInstanceOf<IPersistentState>(character.Resources);
        }

        [Test]
        public void OnlyPersistentPartsCarryASerializationContract()
        {
            foreach (Type type in PersistentParts)
            {
                Assert.AreEqual(1,
                    type.GetCustomAttributes(typeof(SerializableAttribute), false).Length,
                    type.Name + " must remain serializable.");
            }

            Assert.AreEqual(0,
                typeof(CharacterResourceState)
                    .GetCustomAttributes(typeof(SerializableAttribute), false).Length,
                "Runtime resources must not be persisted.");
        }

        [Test]
        public void EveryPersistentPartRoundTripsDeterministically()
        {
            Character character = Create(Cleric, CharacterGender.Female);
            character.Stats.Set(new DefinitionId(Str), 7);

            AssertRoundTrip(character.Identity);
            AssertRoundTrip(character.Class);
            AssertRoundTrip(character.Appearance);
            AssertRoundTrip(character.Progression);
            AssertRoundTrip(character.Stats);
        }

        private static void AssertRoundTrip<T>(T state) where T : class
        {
            string json = JsonUtility.ToJson(state);
            T restored = JsonUtility.FromJson<T>(json);

            Assert.AreEqual(json, JsonUtility.ToJson(restored),
                typeof(T).Name + " must serialize deterministically.");
        }

        [Test]
        public void NoPersistentPartHoldsAUnityObject()
        {
            foreach (Type type in PersistentParts)
            {
                Assert.IsFalse(typeof(UnityEngine.Object).IsAssignableFrom(type), type.Name);

                foreach (FieldInfo field in type.GetFields(
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    Assert.IsFalse(typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType),
                        type.Name + "." + field.Name);
                }
            }
        }

        [Test]
        public void ThereIsExactlyOneCharacterAggregate()
        {
            Assembly gameplay = typeof(Character).Assembly;
            int aggregates = 0;

            foreach (Type type in gameplay.GetTypes())
            {
                if (!type.IsPublic)
                {
                    continue;
                }

                Assert.AreNotEqual("NewCharacter", type.Name,
                    "The aggregate was renamed; a leftover would be a duplicate.");
                Assert.AreNotEqual("CharacterAggregate", type.Name);
                Assert.AreNotEqual("PlayerCharacterData", type.Name);

                if (type.Name == "Character")
                {
                    aggregates++;
                }
            }

            Assert.AreEqual(1, aggregates);
        }

        [Test]
        public void TheAggregateAddsNoStateOfItsOwn()
        {
            Assert.AreEqual(6, typeof(Character).GetProperties().Length,
                "The aggregate exposes six parts and nothing else.");
        }

        [Test]
        public void ConsistencyCheckingDoesNotMutateAnything()
        {
            Character character = Create();

            CharacterId id = character.Identity.CharacterId;
            OwnerId owner = character.Identity.Owner;
            Revision identity = character.Identity.Revision;
            Revision classRevision = character.Class.Revision;
            Revision progression = character.Progression.Revision;
            Revision stats = character.Stats.Revision;
            Revision resources = character.Resources.Revision;
            int level = character.Progression.Level;
            int health = character.Resources.CurrentHealth;

            Check(character);
            Check(character);

            Assert.AreEqual(id, character.Identity.CharacterId);
            Assert.AreEqual(owner, character.Identity.Owner);
            Assert.AreEqual(identity, character.Identity.Revision);
            Assert.AreEqual(classRevision, character.Class.Revision);
            Assert.AreEqual(progression, character.Progression.Revision);
            Assert.AreEqual(stats, character.Stats.Revision);
            Assert.AreEqual(resources, character.Resources.Revision);
            Assert.AreEqual(level, character.Progression.Level);
            Assert.AreEqual(health, character.Resources.CurrentHealth);
        }

        [Test]
        public void ConsistencyCheckingIsDeterministic()
        {
            Character character = Create();
            character.Class.SetJob(new DefinitionId("job.ghost"));

            ValidationReport first = Check(character);
            ValidationReport second = Check(character);

            Assert.AreEqual(first.Messages.Count, second.Messages.Count);

            for (int i = 0; i < first.Messages.Count; i++)
            {
                Assert.AreEqual(first.Messages[i].Code, second.Messages[i].Code, "index " + i);
                Assert.AreEqual(first.Messages[i].Message, second.Messages[i].Message, "index " + i);
            }
        }

        [Test]
        public void NoRepairMethodExists()
        {
            string[] forbidden = { "Repair", "Fix", "Normalize", "Correct", "Sanitize" };

            foreach (MemberInfo member in typeof(CharacterConsistencyValidator).GetMembers())
            {
                foreach (string name in forbidden)
                {
                    Assert.IsFalse(member.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0,
                        "Validation reports; it never repairs. Found " + member.Name);
                }
            }
        }
    }
}
