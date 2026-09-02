using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Shared setup for learned-skill state tests.
    /// </summary>
    /// <remarks>
    /// Every skill id here is a TEST FIXTURE with a deliberately generic name. No
    /// production skill content is authored in this step.
    /// </remarks>
    internal abstract class CharacterSkillsTestBase
    {
        protected DefinitionRegistry<SkillDefinition> Definitions;
        private List<SkillDefinition> _created;

        protected const string SkillA = "skill.a";
        protected const string SkillB = "skill.b";
        protected const string SkillC = "skill.c";

        [SetUp]
        public void SetUp()
        {
            Definitions = new DefinitionRegistry<SkillDefinition>();
            _created = new List<SkillDefinition>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (SkillDefinition definition in _created)
            {
                Object.DestroyImmediate(definition);
            }
        }

        /// <summary>Registers a skill fixture whose only relevant field is its rank ceiling.</summary>
        protected SkillDefinition AddSkill(string id, int maxLevel = 5)
        {
            var definition = ScriptableObject.CreateInstance<SkillDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_maxLevel\":" + maxLevel + "}", definition);
            _created.Add(definition);
            Definitions.Register(definition);
            return definition;
        }

        protected void AddSkills(int maxLevel = 5)
        {
            AddSkill(SkillA, maxLevel);
            AddSkill(SkillB, maxLevel);
            AddSkill(SkillC, maxLevel);
        }

        protected static CharacterSkillsState NewSkills()
        {
            return new CharacterSkillsState(CharacterId.New());
        }

        protected static DefinitionId Id(string value)
        {
            return new DefinitionId(value);
        }

        protected ValidationReport Validate(CharacterSkillsState skills)
        {
            return new CharacterSkillsValidator().Validate(skills, Definitions);
        }
    }
}
