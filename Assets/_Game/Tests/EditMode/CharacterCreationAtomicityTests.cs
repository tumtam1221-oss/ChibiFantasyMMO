using System;
using System.Reflection;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    internal sealed class CharacterCreationAtomicityTests : CharacterCreationTestBase
    {
        private bool TryCreate(CharacterCreationInput input, out Character created,
            out ValidationReport report)
        {
            return new CharacterCreationService().TryCreate(input, Content(), out created, out report);
        }

        [Test]
        public void FailedCreationLeavesNothingBehind()
        {
            int classesBefore = Classes.Count;
            int statsBefore = Stats.Count;
            int appearanceBefore = Appearance.Count;

            Assert.IsFalse(TryCreate(Input("class.ghost"), out Character created, out _));

            Assert.IsNull(created, "No partially built character may escape.");
            Assert.AreEqual(classesBefore, Classes.Count);
            Assert.AreEqual(statsBefore, Stats.Count);
            Assert.AreEqual(appearanceBefore, Appearance.Count);
        }

        [Test]
        public void CreationDoesNotAlterDefinitions()
        {
            Classes.TryGet(new DefinitionId(Swordsman), out ClassDefinition swordsman);
            int jobChangeLevel = swordsman.JobChangeLevel;
            int baseStatCount = swordsman.BaseStats.Length;
            GenderAvailability availability = swordsman.GenderAvailability;
            int minLevel = Curve.MinLevel;

            TryCreate(Input(Swordsman), out _, out _);

            Assert.AreEqual(jobChangeLevel, swordsman.JobChangeLevel);
            Assert.AreEqual(baseStatCount, swordsman.BaseStats.Length);
            Assert.AreEqual(availability, swordsman.GenderAvailability);
            Assert.AreEqual(minLevel, Curve.MinLevel);
        }

        [Test]
        public void ValidateIsPureAndUsableWithoutCreating()
        {
            var validator = new CharacterCreationValidator();
            CharacterCreationContent content = Content();

            ValidationReport first = validator.Validate(Input(Mage), content);
            ValidationReport second = validator.Validate(Input(Mage), content);

            Assert.IsTrue(first.IsValid);
            Assert.AreEqual(first.Messages.Count, second.Messages.Count);
            Assert.AreEqual(Classes.Count, content.Classes.Count);
        }

        [Test]
        public void NewAggregatesStartAtInitialRevision()
        {
            TryCreate(Input(Swordsman), out Character character, out _);

            Assert.AreEqual(Revision.Initial, character.Identity.Revision);
            Assert.AreEqual(Revision.Initial, character.Class.Revision);
            Assert.AreEqual(Revision.Initial, character.Progression.Revision);
            Assert.AreEqual(Revision.Initial, character.Resources.Revision);
        }

        [Test]
        public void MutatingAfterCreationFollowsExistingRevisionRules()
        {
            TryCreate(Input(Swordsman), out Character character, out _);
            CharacterId id = character.Identity.CharacterId;

            character.Identity.Rename("Renamed");
            character.Stats.Set(new DefinitionId(Str), 11);

            Assert.AreEqual(1, character.Identity.Revision.Value);
            Assert.AreEqual(id, character.Identity.CharacterId, "Identity survives mutation.");
            Assert.IsTrue(character.Stats.Revision.IsNewerThan(Revision.Initial));
        }

        [Test]
        public void PersistentAggregatesSerializeDeterministically()
        {
            TryCreate(Input(Cleric, CharacterGender.Female, "Nun"), out Character character, out _);

            string identity = UnityEngine.JsonUtility.ToJson(character.Identity);
            string classState = UnityEngine.JsonUtility.ToJson(character.Class);

            var restoredIdentity = UnityEngine.JsonUtility.FromJson<CharacterState>(identity);
            var restoredClass = UnityEngine.JsonUtility.FromJson<CharacterClassState>(classState);

            Assert.AreEqual(identity, UnityEngine.JsonUtility.ToJson(restoredIdentity));
            Assert.AreEqual(classState, UnityEngine.JsonUtility.ToJson(restoredClass));
            Assert.AreEqual(character.Identity.CharacterId, restoredIdentity.CharacterId);
            Assert.AreEqual(character.Class.BaseClass, restoredClass.BaseClass);
        }

        [Test]
        public void NoUnityObjectIsStoredInPersistentState()
        {
            Type[] persistent =
            {
                typeof(CharacterState), typeof(CharacterClassState),
                typeof(CharacterAppearanceState), typeof(CharacterProgressionState),
                typeof(CharacterStatsState)
            };

            foreach (Type type in persistent)
            {
                Assert.IsFalse(typeof(UnityEngine.Object).IsAssignableFrom(type), type.Name);

                foreach (FieldInfo field in type.GetFields(
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    Assert.IsFalse(typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType),
                        type.Name + "." + field.Name + " must not hold a Unity object.");
                }
            }
        }

        [Test]
        public void NullArgumentsThrow()
        {
            var service = new CharacterCreationService();

            Assert.Throws<ArgumentNullException>(
                () => service.TryCreate(null, Content(), out _, out _));
            Assert.Throws<ArgumentNullException>(
                () => service.TryCreate(Input(Mage), null, out _, out _));
        }

        [Test]
        public void NoClassSpecificCodePathExists()
        {
            string[] forbidden = { "Swordsman", "Cleric", "Mage", "Archer" };

            Type[] introduced =
            {
                typeof(CharacterCreationService), typeof(CharacterCreationValidator),
                typeof(CharacterCreationInput), typeof(Character)
            };

            foreach (Type type in introduced)
            {
                foreach (MemberInfo member in type.GetMembers())
                {
                    foreach (string name in forbidden)
                    {
                        Assert.IsFalse(member.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0,
                            type.Name + "." + member.Name + " is class specific.");
                    }
                }
            }
        }

        [Test]
        public void NoSkillEquipmentInventoryOrCombatWasCreated()
        {
            string[] forbidden =
            {
                "Skill", "Equip", "Inventory", "Item", "Combat", "Damage", "Quest", "Npc"
            };

            foreach (MemberInfo member in typeof(Character).GetMembers())
            {
                foreach (string name in forbidden)
                {
                    Assert.IsFalse(member.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0,
                        "Found " + member.Name + "; those belong to later systems.");
                }
            }
        }
    }
}
