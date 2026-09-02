using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Shared setup for stat tests. The stat ids used here are fixtures, not the game's
    /// authored stat content.
    /// </summary>
    internal abstract class StatsTestBase
    {
        protected DefinitionRegistry<StatDefinition> Definitions;
        private List<StatDefinition> _created;

        /// <summary>The six core attributes, as fixture ids only.</summary>
        protected static readonly string[] CoreStatIds =
        {
            "stat.str", "stat.agi", "stat.vit", "stat.int", "stat.dex", "stat.luk"
        };

        [SetUp]
        public void SetUp()
        {
            Definitions = new DefinitionRegistry<StatDefinition>();
            _created = new List<StatDefinition>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (StatDefinition definition in _created)
            {
                Object.DestroyImmediate(definition);
            }
        }

        protected StatDefinition AddStat(string id, int min, int max)
        {
            var definition = ScriptableObject.CreateInstance<StatDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_isPrimary\":true,\"_minValue\":" + min
                + ",\"_maxValue\":" + max + "}", definition);
            _created.Add(definition);
            Definitions.Register(definition);
            return definition;
        }

        /// <summary>Registers all six core attributes with a generous ceiling.</summary>
        protected void AddCoreStats(int max = 999)
        {
            foreach (string id in CoreStatIds)
            {
                AddStat(id, 0, max);
            }
        }

        protected static CharacterStatsState NewStats()
        {
            return new CharacterStatsState(CharacterId.New());
        }
    }
}
