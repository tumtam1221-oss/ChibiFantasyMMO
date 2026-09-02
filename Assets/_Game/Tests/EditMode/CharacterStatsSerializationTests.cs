using System;
using System.Reflection;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    internal sealed class CharacterStatsSerializationTests : StatsTestBase
    {
        [Test]
        public void SurvivesSerializationRoundTrip()
        {
            CharacterStatsState original = NewStats();
            original.Set(new DefinitionId("stat.str"), 15);
            original.Set(new DefinitionId("stat.luk"), 3);

            string json = JsonUtility.ToJson(original);
            CharacterStatsState restored = JsonUtility.FromJson<CharacterStatsState>(json);

            Assert.AreEqual(original.CharacterId, restored.CharacterId);
            Assert.AreEqual(original.Revision, restored.Revision);
            Assert.AreEqual(2, restored.Count);
            Assert.AreEqual(15, restored.GetOrDefault(new DefinitionId("stat.str"), 0));
            Assert.AreEqual(3, restored.GetOrDefault(new DefinitionId("stat.luk"), 0));
        }

        [Test]
        public void AllSixAttributesSurviveSerialization()
        {
            CharacterStatsState original = NewStats();

            for (int i = 0; i < CoreStatIds.Length; i++)
            {
                original.Set(new DefinitionId(CoreStatIds[i]), 20 + i);
            }

            CharacterStatsState restored =
                JsonUtility.FromJson<CharacterStatsState>(JsonUtility.ToJson(original));

            Assert.AreEqual(6, restored.Count);

            for (int i = 0; i < CoreStatIds.Length; i++)
            {
                Assert.AreEqual(20 + i, restored.GetOrDefault(new DefinitionId(CoreStatIds[i]), -1),
                    CoreStatIds[i]);
            }
        }

        [Test]
        public void RestoredStateRemainsUsable()
        {
            CharacterStatsState original = NewStats();
            original.Set(new DefinitionId("stat.str"), 10);

            CharacterStatsState restored =
                JsonUtility.FromJson<CharacterStatsState>(JsonUtility.ToJson(original));

            // The backing list is replaced by deserialization; the state must still work.
            restored.Set(new DefinitionId("stat.agi"), 5);

            Assert.AreEqual(2, restored.Count);
            Assert.AreEqual(5, restored.GetOrDefault(new DefinitionId("stat.agi"), 0));
        }

        [Test]
        public void StatValuesAreIntegersNotFloats()
        {
            FieldInfo field = typeof(CharacterStatEntry).GetField(
                "_value", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(field);
            Assert.AreEqual(typeof(int), field.FieldType,
                "Persisted base stats are counted, so they must not drift as floats.");
        }

        [Test]
        public void StatEntryIsDistinctFromAuthoredStatValue()
        {
            // StatValue pairs a stat with a float and describes authored content.
            // CharacterStatEntry pairs a stat with an int and describes player state.
            Assert.AreNotEqual(typeof(StatValue), typeof(CharacterStatEntry));
            Assert.AreEqual(typeof(float),
                typeof(StatValue).GetField("_value", BindingFlags.Instance | BindingFlags.NonPublic)
                    .FieldType);
        }

        [Test]
        public void StatsAreIdentifiedByDefinitionIdNotOrdinalOrIndex()
        {
            FieldInfo statField = typeof(CharacterStatEntry).GetField(
                "_stat", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(statField);
            Assert.AreEqual(typeof(DefinitionId), statField.FieldType);
        }

        [Test]
        public void CarriesNoUnityObjectOrForbiddenDependency()
        {
            Assert.IsFalse(typeof(UnityEngine.Object).IsAssignableFrom(typeof(CharacterStatsState)));

            Assembly data = typeof(CharacterStatsState).Assembly;
            foreach (AssemblyName referenced in data.GetReferencedAssemblies())
            {
                Assert.IsFalse(referenced.Name.StartsWith("FishNet", StringComparison.Ordinal));
                Assert.IsFalse(referenced.Name.StartsWith("UnityEditor", StringComparison.Ordinal));
                Assert.IsFalse(referenced.Name.StartsWith("ChibiFantasy.Gameplay", StringComparison.Ordinal));
            }
        }

        [Test]
        public void NoClassBalanceOrDerivedStatLeakedIntoState()
        {
            string[] forbidden =
            {
                "Swordsman", "Cleric", "Mage", "Archer",
                "Attack", "Defense", "MaxHp", "MaxMp", "Critical"
            };

            foreach (MemberInfo member in typeof(CharacterStatsState).GetMembers())
            {
                foreach (string name in forbidden)
                {
                    Assert.IsFalse(member.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0,
                        "Class balance and derived stats belong to later layers, found " + member.Name);
                }
            }
        }

        [Test]
        public void IsNotEmbeddedInCharacterState()
        {
            foreach (FieldInfo field in typeof(CharacterState).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                Assert.AreNotEqual(typeof(CharacterStatsState), field.FieldType);
            }
        }
    }
}
