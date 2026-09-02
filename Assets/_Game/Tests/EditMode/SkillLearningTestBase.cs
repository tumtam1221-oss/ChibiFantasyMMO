using System.Collections.Generic;
using System.Reflection;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Shared setup for skill-learning rule tests.
    /// </summary>
    /// <remarks>
    /// Every class, job and skill here is a TEST FIXTURE with a deliberately generic name.
    /// No production skill content is authored in this step.
    /// </remarks>
    internal abstract class SkillLearningTestBase
    {
        protected DefinitionRegistry<SkillDefinition> Skills;
        private List<Object> _created;

        protected const string ClassA = "class.a";
        protected const string ClassB = "class.b";
        protected const string JobA = "job.a";
        protected const string JobB = "job.b";

        [SetUp]
        public void SetUp()
        {
            Skills = new DefinitionRegistry<SkillDefinition>();
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

        /// <summary>
        /// Registers a skill fixture.
        /// </summary>
        /// <param name="requiredLevel">Authored on the rank-one level entry, which is where
        /// the schema keeps the character-level gate. Zero authors no level table at all,
        /// which is a skill with no gate.</param>
        protected SkillDefinition AddSkill(string id, int requiredLevel = 0,
            string requiredClass = null, string requiredJob = null,
            SkillPrerequisite[] prerequisites = null, int maxLevel = 5)
        {
            var definition = ScriptableObject.CreateInstance<SkillDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_maxLevel\":" + maxLevel
                + ",\"_requiredClass\":{\"_value\":\"" + (requiredClass ?? string.Empty)
                + "\"},\"_requiredJob\":{\"_value\":\"" + (requiredJob ?? string.Empty) + "\"}}",
                definition);

            if (requiredLevel > 0)
            {
                SetPrivate(definition, "_levels", new[]
                {
                    new SkillLevelEntry(1, requiredLevel, 0f, 0f, new SkillEffect[0])
                });
            }

            if (prerequisites != null)
            {
                SetPrivate(definition, "_prerequisites", prerequisites);
            }

            _created.Add(definition);
            Skills.Register(definition);
            return definition;
        }

        protected static SkillPrerequisite Requires(string skill, int rank)
        {
            return new SkillPrerequisite(new DefinitionId(skill), rank);
        }

        protected static DefinitionId Id(string value)
        {
            return new DefinitionId(value);
        }

        /// <summary>A character of a class, optionally already advanced into a job.</summary>
        protected static CharacterClassState NewCharacter(string baseClass, string job = null)
        {
            var state = new CharacterClassState(CharacterId.New(), new DefinitionId(baseClass));

            if (job != null)
            {
                state.SetJob(new DefinitionId(job));
            }

            return state;
        }

        protected static CharacterSkillsState NewSkills()
        {
            return new CharacterSkillsState(CharacterId.New());
        }

        protected SkillLearnEligibility Evaluate(CharacterSkillsState learned,
            CharacterClassState classState, int level, string target)
        {
            return new SkillLearningEvaluator()
                .Evaluate(learned, classState, level, new DefinitionId(target), Skills);
        }

        protected bool TryLearn(CharacterSkillsState learned, CharacterClassState classState,
            int level, string target, out SkillLearnEligibility eligibility)
        {
            return new SkillLearningEvaluator()
                .TryLearn(learned, classState, level, new DefinitionId(target), Skills,
                    out eligibility);
        }

        protected static void SetPrivate(Object target, string field, object value)
        {
            target.GetType()
                .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(target, value);
        }
    }
}
