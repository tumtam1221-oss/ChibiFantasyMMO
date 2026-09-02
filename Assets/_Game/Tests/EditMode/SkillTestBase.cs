using System.Collections.Generic;
using System.Reflection;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Shared fixtures for skill tests.
    /// </summary>
    /// <remarks>
    /// Every skill here is a TEST FIXTURE with a deliberately generic name. No production
    /// skill is authored in this step, and no skill name appears in runtime code.
    /// </remarks>
    internal abstract class SkillTestBase
    {
        protected DefinitionRegistry<SkillDefinition> Skills;
        protected DefinitionRegistry<ClassDefinition> Classes;
        protected DefinitionRegistry<JobDefinition> Jobs;
        private List<Object> _created;

        protected const string ClassA = "class.a";
        protected const string ClassB = "class.b";
        protected const string JobA = "job.a";
        protected const string JobB = "job.b";

        [SetUp]
        public void SetUp()
        {
            Skills = new DefinitionRegistry<SkillDefinition>();
            Classes = new DefinitionRegistry<ClassDefinition>();
            Jobs = new DefinitionRegistry<JobDefinition>();
            _created = new List<Object>();

            AddClass(ClassA);
            AddClass(ClassB);
            AddJob(JobA, ClassA);
            AddJob(JobB, ClassB);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object created in _created)
            {
                Object.DestroyImmediate(created);
            }
        }

        protected ValidationReport Validate(SkillDefinition skill)
        {
            var validator = new DefinitionValidator(
                new IDefinitionValidationRule[] { new SkillValidationRule(Skills, Classes, Jobs) });
            return validator.Validate(skill, Skills);
        }

        protected ClassDefinition AddClass(string id)
        {
            var definition = ScriptableObject.CreateInstance<ClassDefinition>();
            JsonUtility.FromJsonOverwrite("{\"_id\":{\"_value\":\"" + id + "\"}}", definition);
            _created.Add(definition);
            Classes.Register(definition);
            return definition;
        }

        protected JobDefinition AddJob(string id, string baseClass)
        {
            var definition = ScriptableObject.CreateInstance<JobDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_baseClass\":{\"_value\":\"" + baseClass
                + "\"},\"_tier\":1,\"_levelRequirement\":1}", definition);
            _created.Add(definition);
            Jobs.Register(definition);
            return definition;
        }

        /// <summary>
        /// Builds a skill. Scalars are authored as JSON, arrays by reflection.
        /// </summary>
        /// <remarks>Defaults to a coherent active skill, because real content always states
        /// a category, a target and a resource. A test wanting another combination
        /// overrides those fields directly.</remarks>
        protected SkillDefinition AddSkill(string id, int maxLevel = 1, float cost = 0f,
            float cooldown = 0f, float castTime = 0f, float range = 0f,
            string requiredClass = null, string requiredJob = null,
            SkillLevelEntry[] levels = null, SkillPrerequisite[] prerequisites = null,
            bool register = true)
        {
            var definition = ScriptableObject.CreateInstance<SkillDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_maxLevel\":" + maxLevel
                + ",\"_baseResourceCost\":" + cost + ",\"_cooldownSeconds\":" + cooldown
                + ",\"_castTimeSeconds\":" + castTime + ",\"_range\":" + range
                + ",\"_category\":" + (int)SkillCategory.Active
                + ",\"_targetType\":" + (int)SkillTargetType.SingleEnemy
                + ",\"_resourceType\":" + (int)SkillResourceType.Mana
                + ",\"_requiredClass\":{\"_value\":\"" + (requiredClass ?? string.Empty)
                + "\"},\"_requiredJob\":{\"_value\":\"" + (requiredJob ?? string.Empty) + "\"}}",
                definition);

            if (levels != null)
            {
                SetPrivate(definition, "_levels", levels);
            }

            if (prerequisites != null)
            {
                SetPrivate(definition, "_prerequisites", prerequisites);
            }

            _created.Add(definition);

            if (register)
            {
                Skills.Register(definition);
            }

            return definition;
        }

        protected static SkillLevelEntry Level(int level, int requiredCharacterLevel,
            float cost = 0f, float cooldown = 0f, SkillEffect[] effects = null)
        {
            return new SkillLevelEntry(level, requiredCharacterLevel, cost, cooldown,
                effects ?? new SkillEffect[0]);
        }

        protected static void SetPrivate(Object target, string field, object value)
        {
            target.GetType()
                .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(target, value);
        }

        /// <summary>Authors the skills a new character of this class begins with.</summary>
        protected static void SetStartingSkills(ClassDefinition characterClass, params string[] skills)
        {
            SetPrivate(characterClass, "_startingSkills", Ids(skills));
        }

        /// <summary>Authors the skills this job unlocks.</summary>
        protected static void SetJobSkills(JobDefinition job, params string[] skills)
        {
            SetPrivate(job, "_skills", Ids(skills));
        }

        /// <summary>Null entries become <see cref="DefinitionId.None"/>, so a test can
        /// author the unset reference an authoring mistake actually produces.</summary>
        protected static DefinitionId[] Ids(params string[] values)
        {
            var ids = new DefinitionId[values.Length];

            for (int i = 0; i < values.Length; i++)
            {
                ids[i] = values[i] == null ? DefinitionId.None : new DefinitionId(values[i]);
            }

            return ids;
        }
    }
}
