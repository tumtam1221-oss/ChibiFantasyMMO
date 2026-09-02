using System.Collections.Generic;
using System.Reflection;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Shared setup for class and job tests.
    /// </summary>
    /// <remarks>
    /// The tree built here is a TEST FIXTURE. The four starting classes and the
    /// 15/35/60 gates are authored as assets exactly as production content would be; no
    /// class name or level gate appears in any runtime type.
    /// </remarks>
    internal abstract class ClassJobTestBase
    {
        protected DefinitionRegistry<ClassDefinition> Classes;
        protected DefinitionRegistry<JobDefinition> Jobs;
        private List<Object> _created;

        protected const string Swordsman = "class.swordsman";
        protected const string Cleric = "class.cleric";
        protected const string Mage = "class.mage";
        protected const string Archer = "class.archer";

        [SetUp]
        public void SetUp()
        {
            Classes = new DefinitionRegistry<ClassDefinition>();
            Jobs = new DefinitionRegistry<JobDefinition>();
            _created = new List<Object>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object created in _created)
            {
                Object.DestroyImmediate(created);
            }
        }

        protected ClassDefinition AddClass(string id, int jobChangeLevel, params string[] nextJobs)
        {
            var definition = ScriptableObject.CreateInstance<ClassDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_jobChangeLevel\":" + jobChangeLevel + "}",
                definition);
            SetIds(definition, "_nextJobs", nextJobs);
            _created.Add(definition);
            Classes.Register(definition);
            return definition;
        }

        protected JobDefinition AddJob(string id, string baseClass, int tier, int levelRequirement,
            string prerequisite, params string[] nextJobs)
        {
            var definition = ScriptableObject.CreateInstance<JobDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_baseClass\":{\"_value\":\"" + baseClass
                + "\"},\"_tier\":" + tier + ",\"_levelRequirement\":" + levelRequirement
                + ",\"_prerequisiteJob\":{\"_value\":\"" + (prerequisite ?? string.Empty) + "\"}}",
                definition);
            SetIds(definition, "_nextJobs", nextJobs);
            _created.Add(definition);
            Jobs.Register(definition);
            return definition;
        }

        private static void SetIds(Object target, string field, string[] ids)
        {
            var values = new DefinitionId[ids.Length];

            for (int i = 0; i < ids.Length; i++)
            {
                values[i] = new DefinitionId(ids[i]);
            }

            target.GetType()
                .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(target, values);
        }

        /// <summary>
        /// The intended shape, authored as content: class at 1, first job at 15, two
        /// branches at 35, third job at 60.
        /// </summary>
        protected void AddSwordsmanTree()
        {
            AddClass(Swordsman, 15, "job.sword.first");
            AddJob("job.sword.first", Swordsman, 1, 15, null, "job.sword.branch_a", "job.sword.branch_b");
            AddJob("job.sword.branch_a", Swordsman, 2, 35, "job.sword.first", "job.sword.third_a");
            AddJob("job.sword.branch_b", Swordsman, 2, 35, "job.sword.first", "job.sword.third_b");
            AddJob("job.sword.third_a", Swordsman, 3, 60, "job.sword.branch_a");
            AddJob("job.sword.third_b", Swordsman, 3, 60, "job.sword.branch_b");
        }

        protected static CharacterClassState NewCharacter(string baseClass)
        {
            return new CharacterClassState(CharacterId.New(), new DefinitionId(baseClass));
        }
    }
}
