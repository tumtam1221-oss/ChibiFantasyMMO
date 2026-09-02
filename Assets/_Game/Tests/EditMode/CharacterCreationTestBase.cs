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
    /// Shared fixtures for character creation.
    /// </summary>
    /// <remarks>
    /// The four starting classes and every number here are TEST FIXTURES authored exactly
    /// as production content would be. No class name or balance figure appears in any
    /// runtime type.
    /// </remarks>
    internal abstract class CharacterCreationTestBase
    {
        protected DefinitionRegistry<ClassDefinition> Classes;
        protected DefinitionRegistry<StatDefinition> Stats;
        protected DefinitionRegistry<AppearanceOptionDefinition> Appearance;
        protected List<DerivedStatFormulaDefinition> Formulas;
        protected CharacterProgressionDefinition Curve;
        private List<Object> _created;

        protected const string Swordsman = "class.swordsman";
        protected const string Cleric = "class.cleric";
        protected const string Mage = "class.mage";
        protected const string Archer = "class.archer";

        protected const string Str = "stat.str";
        protected const string Vit = "stat.vit";
        protected const string MaxHp = "stat.max_hp";
        protected const string MaxMp = "stat.max_mp";

        [SetUp]
        public void SetUp()
        {
            Classes = new DefinitionRegistry<ClassDefinition>();
            Stats = new DefinitionRegistry<StatDefinition>();
            Appearance = new DefinitionRegistry<AppearanceOptionDefinition>();
            Formulas = new List<DerivedStatFormulaDefinition>();
            _created = new List<Object>();

            AddStat(Str, true);
            AddStat(Vit, true);
            AddStat(MaxHp, false);
            AddStat(MaxMp, false);

            // FIXTURES: MaxHP = 50 + VIT x 10, MaxMP = 10 + STR x 2.
            Formulas.Add(Formula("f.hp", MaxHp, 50, new StatTerm(new DefinitionId(Vit), 10, 1)));
            Formulas.Add(Formula("f.mp", MaxMp, 10, new StatTerm(new DefinitionId(Str), 2, 1)));

            Curve = MakeCurve();

            AddClass(Swordsman, GenderAvailability.Any, 10, 8);
            AddClass(Cleric, GenderAvailability.Any, 4, 6);
            AddClass(Mage, GenderAvailability.Any, 3, 4);
            AddClass(Archer, GenderAvailability.Any, 6, 5);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object created in _created)
            {
                Object.DestroyImmediate(created);
            }
        }

        protected T Track<T>(T asset) where T : Object
        {
            _created.Add(asset);
            return asset;
        }

        protected StatDefinition AddStat(string id, bool primary)
        {
            var definition = Track(ScriptableObject.CreateInstance<StatDefinition>());
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_isPrimary\":" + (primary ? "true" : "false")
                + ",\"_minValue\":0,\"_maxValue\":999999}", definition);
            Stats.Register(definition);
            return definition;
        }

        protected DerivedStatFormulaDefinition Formula(string id, string derived, int constant,
            params StatTerm[] terms)
        {
            var definition = Track(ScriptableObject.CreateInstance<DerivedStatFormulaDefinition>());
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_derivedStat\":{\"_value\":\"" + derived
                + "\"},\"_constant\":" + constant + "}", definition);
            SetPrivate(definition, "_terms", terms);
            return definition;
        }

        protected CharacterProgressionDefinition MakeCurve()
        {
            var definition = Track(ScriptableObject.CreateInstance<CharacterProgressionDefinition>());
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"progression.default\"},\"_minLevel\":1,\"_maxLevel\":3,"
                + "\"_experienceToNextLevel\":[100,200]}", definition);
            return definition;
        }

        /// <summary>Authors a class with base stats as float content, as 04.4 defines them.</summary>
        protected ClassDefinition AddClass(string id, GenderAvailability availability,
            float strength, float vitality)
        {
            var definition = Track(ScriptableObject.CreateInstance<ClassDefinition>());
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_genderAvailability\":" + (int)availability
                + ",\"_jobChangeLevel\":15}", definition);
            SetPrivate(definition, "_baseStats", new[]
            {
                new StatValue(new DefinitionId(Str), strength),
                new StatValue(new DefinitionId(Vit), vitality)
            });
            Classes.Register(definition);
            return definition;
        }

        protected AppearanceOptionDefinition AddAppearance(string id, AppearanceSlot slot,
            GenderAvailability availability)
        {
            var definition = Track(ScriptableObject.CreateInstance<AppearanceOptionDefinition>());
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_slot\":" + (int)slot
                + ",\"_genderAvailability\":" + (int)availability + "}", definition);
            Appearance.Register(definition);
            return definition;
        }

        protected static void SetPrivate(Object target, string field, object value)
        {
            target.GetType()
                .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(target, value);
        }

        protected CharacterCreationContent Content()
        {
            return new CharacterCreationContent(Classes, Stats, Appearance, Formulas, Curve,
                new DefinitionId(MaxHp), new DefinitionId(MaxMp));
        }

        protected static CharacterCreationInput Input(string startingClass,
            CharacterGender gender = CharacterGender.Male, string name = "Hero",
            string owner = "account:1", IList<AppearanceChoice> appearance = null)
        {
            return new CharacterCreationInput(
                new OwnerId(owner), name, gender, new DefinitionId(startingClass), appearance);
        }
    }
}
