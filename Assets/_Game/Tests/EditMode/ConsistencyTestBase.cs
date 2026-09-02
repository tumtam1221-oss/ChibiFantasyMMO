using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>Shared helpers for character consistency tests.</summary>
    internal abstract class ConsistencyTestBase : CharacterCreationTestBase
    {
        private DefinitionRegistry<JobDefinition> _jobs;

        protected DefinitionRegistry<JobDefinition> Jobs
        {
            get { return _jobs ?? (_jobs = new DefinitionRegistry<JobDefinition>()); }
        }

        protected Character Create(string startingClass = Swordsman,
            CharacterGender gender = CharacterGender.Male)
        {
            new CharacterCreationService().TryCreate(
                Input(startingClass, gender), Content(), out Character character, out _);
            return character;
        }

        protected ValidationReport Check(Character character)
        {
            return new CharacterConsistencyValidator().Validate(character, Content(), Jobs);
        }

        protected JobDefinition AddJob(string id, string baseClass, int levelRequirement)
        {
            var definition = Track(ScriptableObject.CreateInstance<JobDefinition>());
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_baseClass\":{\"_value\":\"" + baseClass
                + "\"},\"_tier\":1,\"_levelRequirement\":" + levelRequirement + "}", definition);
            Jobs.Register(definition);
            return definition;
        }

        /// <summary>Rebuilds an aggregate swapping one part, to model a corrupted character.</summary>
        protected static Character With(Character source, CharacterClassState classState)
        {
            return new Character(source.Identity, classState, source.Appearance,
                source.Progression, source.Stats, source.Resources);
        }
    }
}
