using System;
using System.Reflection;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    internal sealed class CharacterSnapshotTests : ConsistencyTestBase
    {
        [Test]
        public void CapturesTheCharacterFaithfully()
        {
            Character character = Create(Swordsman);

            CharacterSnapshot snapshot = CharacterSnapshot.Capture(character);

            Assert.AreEqual(character.Identity.CharacterId, snapshot.CharacterId);
            Assert.AreEqual(character.Identity.Owner, snapshot.Owner);
            Assert.AreEqual(character.Identity.Name, snapshot.Name);
            Assert.AreEqual(character.Identity.Gender, snapshot.Gender);
            Assert.AreEqual(character.Class.BaseClass, snapshot.BaseClass);
            Assert.AreEqual(character.Class.CurrentJob, snapshot.CurrentJob);
            Assert.AreEqual(character.Progression.Level, snapshot.Level);
            Assert.AreEqual(character.Progression.Experience, snapshot.Experience);
            Assert.AreEqual(character.Resources.CurrentHealth, snapshot.CurrentHealth);
            Assert.AreEqual(character.Resources.CurrentMana, snapshot.CurrentMana);
            Assert.AreEqual(character.Stats.Count, snapshot.StatCount);
        }

        [Test]
        public void CapturingChangesNothingIncludingRevisions()
        {
            Character character = Create();
            Revision identity = character.Identity.Revision;
            Revision stats = character.Stats.Revision;
            Revision resources = character.Resources.Revision;
            int health = character.Resources.CurrentHealth;

            CharacterSnapshot.Capture(character);
            CharacterSnapshot.Capture(character);

            Assert.AreEqual(identity, character.Identity.Revision);
            Assert.AreEqual(stats, character.Stats.Revision);
            Assert.AreEqual(resources, character.Resources.Revision);
            Assert.AreEqual(health, character.Resources.CurrentHealth);
        }

        [Test]
        public void ASnapshotDoesNotFollowLaterChanges()
        {
            Character character = Create();
            CharacterSnapshot before = CharacterSnapshot.Capture(character);

            character.Identity.Rename("Renamed");
            character.Resources.ChangeHealth(-5, new ResourceLimits(130, 30));

            Assert.AreEqual("Hero", before.Name, "A snapshot is a reading, not a window.");
            Assert.AreNotEqual(character.Identity.Name, before.Name);
            Assert.AreNotEqual(character.Resources.CurrentHealth, before.CurrentHealth);
        }

        [Test]
        public void CountsAppearanceSelections()
        {
            AddAppearance("hair_001", AppearanceSlot.Hair, GenderAvailability.Any);
            AddAppearance("face_001", AppearanceSlot.Face, GenderAvailability.Any);

            Character character = Create();

            Assert.AreEqual(0, CharacterSnapshot.Capture(character).AppearanceSelectionCount);

            character.Appearance.Select(AppearanceSlot.Hair, new DefinitionId("hair_001"));
            character.Appearance.Select(AppearanceSlot.Face, new DefinitionId("face_001"));

            Assert.AreEqual(2, CharacterSnapshot.Capture(character).AppearanceSelectionCount);
        }

        [Test]
        public void IsFullyReadOnly()
        {
            foreach (PropertyInfo property in typeof(CharacterSnapshot).GetProperties())
            {
                Assert.IsFalse(property.GetSetMethod() != null,
                    property.Name + " must not be publicly writable.");
            }
        }

        [Test]
        public void ExposesNoCollectionOrUnityObject()
        {
            foreach (PropertyInfo property in typeof(CharacterSnapshot).GetProperties())
            {
                Type type = property.PropertyType;

                Assert.IsFalse(typeof(UnityEngine.Object).IsAssignableFrom(type),
                    property.Name + " must not expose a Unity object.");
                Assert.IsFalse(type.IsArray, property.Name + " must not expose an array.");
                Assert.IsFalse(
                    type.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(type),
                    property.Name + " must not expose a collection.");
            }
        }

        [Test]
        public void UsesStableIdentifiersOnly()
        {
            Character character = Create();
            CharacterSnapshot snapshot = CharacterSnapshot.Capture(character);

            Assert.AreEqual(typeof(CharacterId), snapshot.CharacterId.GetType());
            Assert.AreEqual(typeof(OwnerId), snapshot.Owner.GetType());
            Assert.AreEqual(typeof(DefinitionId), snapshot.BaseClass.GetType());
            Assert.IsTrue(snapshot.CharacterId.IsValid);
        }

        [Test]
        public void CarriesNoSerializationOrNetworkingContract()
        {
            // A domain read boundary, not a packet.
            Assert.AreEqual(0,
                typeof(CharacterSnapshot).GetCustomAttributes(typeof(SerializableAttribute), false).Length);

            Assembly gameplay = typeof(CharacterSnapshot).Assembly;

            foreach (AssemblyName referenced in gameplay.GetReferencedAssemblies())
            {
                Assert.IsFalse(referenced.Name.StartsWith("FishNet", StringComparison.Ordinal));
                Assert.IsFalse(referenced.Name.StartsWith("UnityEditor", StringComparison.Ordinal));
                Assert.IsFalse(referenced.Name.StartsWith("ChibiFantasy.UI", StringComparison.Ordinal));
                Assert.IsFalse(referenced.Name.StartsWith("ChibiFantasy.Backend", StringComparison.Ordinal));
            }
        }

        [Test]
        public void NullCharacterThrows()
        {
            Assert.Throws<ArgumentNullException>(() => CharacterSnapshot.Capture(null));
        }
    }
}
