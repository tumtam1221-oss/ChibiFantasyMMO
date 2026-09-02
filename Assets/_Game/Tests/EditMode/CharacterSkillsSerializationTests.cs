using System;
using System.Reflection;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Round-trip persistence, and the boundaries learned-skill state must not cross.
    /// </summary>
    internal sealed class CharacterSkillsSerializationTests : CharacterSkillsTestBase
    {
        [Test]
        public void EmptySkillsSurviveARoundTrip()
        {
            CharacterSkillsState original = NewSkills();

            CharacterSkillsState restored =
                JsonUtility.FromJson<CharacterSkillsState>(JsonUtility.ToJson(original));

            Assert.AreEqual(original.CharacterId, restored.CharacterId);
            Assert.AreEqual(original.Revision, restored.Revision);
            Assert.AreEqual(0, restored.Count);
        }

        [Test]
        public void OneLearnedSkillSurvivesARoundTrip()
        {
            CharacterSkillsState original = NewSkills();
            original.Learn(Id(SkillA));

            CharacterSkillsState restored =
                JsonUtility.FromJson<CharacterSkillsState>(JsonUtility.ToJson(original));

            Assert.AreEqual(original.CharacterId, restored.CharacterId);
            Assert.AreEqual(1, restored.Count);
            Assert.AreEqual(1, restored.GetRankOrDefault(Id(SkillA), -1));
        }

        [Test]
        public void SeveralSkillsAtDifferentRanksSurviveARoundTrip()
        {
            CharacterSkillsState original = NewSkills();
            original.SetRank(Id(SkillA), 1);
            original.SetRank(Id(SkillB), 3);
            original.SetRank(Id(SkillC), 5);

            CharacterSkillsState restored =
                JsonUtility.FromJson<CharacterSkillsState>(JsonUtility.ToJson(original));

            Assert.AreEqual(3, restored.Count);
            Assert.AreEqual(1, restored.GetRankOrDefault(Id(SkillA), -1));
            Assert.AreEqual(3, restored.GetRankOrDefault(Id(SkillB), -1));
            Assert.AreEqual(5, restored.GetRankOrDefault(Id(SkillC), -1));
        }

        [Test]
        public void OrderAndRevisionSurviveARoundTrip()
        {
            CharacterSkillsState original = NewSkills();
            original.Learn(Id(SkillC));
            original.Learn(Id(SkillA));
            original.SetRank(Id(SkillC), 4);

            CharacterSkillsState restored =
                JsonUtility.FromJson<CharacterSkillsState>(JsonUtility.ToJson(original));

            Assert.AreEqual(original.Revision, restored.Revision,
                "Serialization must not alter the revision.");
            Assert.AreEqual(3, restored.Revision.Value);
            Assert.AreEqual(Id(SkillC), restored.Skills[0].Skill);
            Assert.AreEqual(Id(SkillA), restored.Skills[1].Skill);
        }

        [Test]
        public void RestoredStateRemainsUsable()
        {
            CharacterSkillsState original = NewSkills();
            original.Learn(Id(SkillA));

            CharacterSkillsState restored =
                JsonUtility.FromJson<CharacterSkillsState>(JsonUtility.ToJson(original));

            // The backing list is replaced by deserialization; the state must still work.
            restored.SetRank(Id(SkillB), 2);

            Assert.AreEqual(2, restored.Count);
            Assert.AreEqual(2, restored.GetRankOrDefault(Id(SkillB), -1));
        }

        [Test]
        public void SkillsAreIdentifiedByDefinitionIdNotIndexOrObject()
        {
            FieldInfo skill = typeof(CharacterSkillEntry).GetField(
                "_skill", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(skill);
            Assert.AreEqual(typeof(DefinitionId), skill.FieldType,
                "A learned skill must survive content being added and reordered.");

            FieldInfo rank = typeof(CharacterSkillEntry).GetField(
                "_rank", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.AreEqual(typeof(int), rank.FieldType, "Ranks are counted, not measured.");
        }

        [Test]
        public void NoSkillDefinitionDataWasCopiedIntoState()
        {
            // Copying any of these would make a character's saved skill immune to a patch
            // that retunes it, which is the failure this whole split exists to prevent.
            string[] forbidden =
            {
                "Name", "Description", "Icon", "Category", "TargetType", "ResourceType",
                "Cost", "Cooldown", "CastTime", "Range", "Effect", "Scaling", "Animation",
                "VisualEffect", "SoundEffect", "MaxLevel"
            };

            foreach (MemberInfo member in typeof(CharacterSkillEntry).GetMembers())
            {
                foreach (string name in forbidden)
                {
                    Assert.IsFalse(
                        member.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0,
                        "Definition data must not be copied into state, found " + member.Name);
                }
            }
        }

        [Test]
        public void LearnedSkillsAreClassifiedAsPersistentState()
        {
            Assert.IsInstanceOf<IPersistentState>(NewSkills());
            Assert.IsNotInstanceOf<IRuntimeState>(NewSkills());
            Assert.IsNotNull(
                typeof(CharacterSkillsState).GetCustomAttribute<SerializableAttribute>(false),
                "Persistent state must remain serializable.");
        }

        [Test]
        public void NoRuntimeOrCombatSkillStateWasIntroduced()
        {
            // A learned skill is persistent. Cooldown timers, cast progress and queued
            // casts are runtime state for a combat system that does not exist; no empty
            // placeholder was created for them, so nothing can accidentally be persisted.
            string[] forbidden =
            {
                "SkillRuntimeState", "SkillCooldownState", "SkillCooldown", "CooldownState",
                "CooldownManager", "SkillCastState", "CastState", "SkillInstance",
                "SkillExecutor", "SkillExecution"
            };

            Assembly[] assemblies =
            {
                typeof(CharacterSkillsState).Assembly,
                typeof(ChibiFantasy.Gameplay.Character).Assembly
            };

            foreach (Assembly assembly in assemblies)
            {
                foreach (Type type in assembly.GetTypes())
                {
                    foreach (string name in forbidden)
                    {
                        Assert.AreNotEqual(name, type.Name,
                            "Skill runtime and execution belong to a later step.");
                    }
                }
            }
        }

        [Test]
        public void NoSecondIdentityTypeWasIntroducedForSkills()
        {
            string[] forbidden =
            {
                "SkillOwnerId", "CharacterSkillOwnerId", "PlayerSkillId", "SkillId",
                "LearnedSkillId"
            };

            foreach (Type type in typeof(CharacterSkillsState).Assembly.GetTypes())
            {
                foreach (string name in forbidden)
                {
                    Assert.AreNotEqual(name, type.Name,
                        "A character plus a DefinitionId already identifies a learned skill.");
                }
            }
        }

        [Test]
        public void CarriesNoUnityObjectOrForbiddenDependency()
        {
            Assert.IsFalse(typeof(UnityEngine.Object).IsAssignableFrom(typeof(CharacterSkillsState)));

            foreach (AssemblyName referenced in
                typeof(CharacterSkillsState).Assembly.GetReferencedAssemblies())
            {
                Assert.IsFalse(referenced.Name.StartsWith("FishNet", StringComparison.Ordinal));
                Assert.IsFalse(referenced.Name.StartsWith("UnityEditor", StringComparison.Ordinal));
                Assert.IsFalse(referenced.Name.StartsWith("ChibiFantasy.Gameplay", StringComparison.Ordinal));
                Assert.IsFalse(referenced.Name.StartsWith("ChibiFantasy.Backend", StringComparison.Ordinal));
                Assert.IsFalse(referenced.Name.StartsWith("ChibiFantasy.UI", StringComparison.Ordinal));
            }
        }

        [Test]
        public void IsNotEmbeddedInCharacterState()
        {
            foreach (FieldInfo field in typeof(CharacterState).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                Assert.AreNotEqual(typeof(CharacterSkillsState), field.FieldType,
                    "Learned skills are a sibling aggregate, not part of the identity record.");
            }
        }
    }
}
