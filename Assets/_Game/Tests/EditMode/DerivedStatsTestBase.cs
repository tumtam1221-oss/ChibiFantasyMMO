using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Shared setup for derived-stat tests.
    /// </summary>
    /// <remarks>
    /// Every number here is a TEST FIXTURE, not game balance. "100 + VIT x 10" exists to
    /// prove the engine arithmetic, and no such formula appears in production code.
    /// </remarks>
    internal abstract class DerivedStatsTestBase
    {
        protected DefinitionRegistry<StatDefinition> Stats;
        private List<Object> _created;

        // Fixture ids for the six attributes and a few derived stats.
        protected const string Str = "stat.str";
        protected const string Agi = "stat.agi";
        protected const string Vit = "stat.vit";
        protected const string MaxHp = "stat.max_hp";
        protected const string PhysicalAttack = "stat.physical_attack";

        [SetUp]
        public void SetUp()
        {
            Stats = new DefinitionRegistry<StatDefinition>();
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

        protected StatDefinition AddStat(string id, bool primary, int min = 0, int max = int.MaxValue)
        {
            var definition = ScriptableObject.CreateInstance<StatDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_isPrimary\":" + (primary ? "true" : "false")
                + ",\"_minValue\":" + min + ",\"_maxValue\":" + max + "}", definition);
            _created.Add(definition);
            Stats.Register(definition);
            return definition;
        }

        /// <summary>Registers the primary attributes used by these fixtures.</summary>
        protected void AddPrimaries()
        {
            AddStat(Str, true);
            AddStat(Agi, true);
            AddStat(Vit, true);
        }

        protected DerivedStatFormulaDefinition Formula(string id, string derivedStat, int constant,
            params StatTerm[] terms)
        {
            var definition = ScriptableObject.CreateInstance<DerivedStatFormulaDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_derivedStat\":{\"_value\":\""
                + derivedStat + "\"},\"_constant\":" + constant + "}", definition);

            typeof(DerivedStatFormulaDefinition)
                .GetField("_terms", System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic)
                .SetValue(definition, terms);

            _created.Add(definition);
            return definition;
        }

        protected static StatTerm Term(string source, int numerator, int denominator)
        {
            return new StatTerm(new DefinitionId(source), numerator, denominator);
        }

        protected static CharacterStatsState BaseStats(params (string stat, int value)[] entries)
        {
            var stats = new CharacterStatsState(CharacterId.New());

            foreach ((string stat, int value) in entries)
            {
                stats.Set(new DefinitionId(stat), value);
            }

            return stats;
        }

        protected static readonly StatModifier[] NoModifiers = new StatModifier[0];
    }
}
