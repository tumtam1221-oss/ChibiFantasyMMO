using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Shared setup for progression tests. Curves here are tiny fixtures, not the game's
    /// real level table, which is authored content.
    /// </summary>
    internal abstract class ProgressionTestBase
    {
        private List<CharacterProgressionDefinition> _created;

        [SetUp]
        public void SetUp()
        {
            _created = new List<CharacterProgressionDefinition>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (CharacterProgressionDefinition definition in _created)
            {
                Object.DestroyImmediate(definition);
            }
        }

        /// <summary>Builds a curve from explicit per-level costs.</summary>
        protected CharacterProgressionDefinition Curve(string id, int minLevel, int maxLevel,
            params long[] costs)
        {
            var definition = ScriptableObject.CreateInstance<CharacterProgressionDefinition>();

            var json = new System.Text.StringBuilder();
            json.Append("{\"_id\":{\"_value\":\"").Append(id).Append("\"},");
            json.Append("\"_minLevel\":").Append(minLevel).Append(",");
            json.Append("\"_maxLevel\":").Append(maxLevel).Append(",");
            json.Append("\"_experienceToNextLevel\":[");

            for (int i = 0; i < costs.Length; i++)
            {
                if (i > 0)
                {
                    json.Append(",");
                }

                json.Append(costs[i]);
            }

            json.Append("]}");

            JsonUtility.FromJsonOverwrite(json.ToString(), definition);
            _created.Add(definition);
            return definition;
        }

        /// <summary>Levels 1 to 4, costing 100, 200 and 300.</summary>
        protected CharacterProgressionDefinition StandardCurve()
        {
            return Curve("progression_test", 1, 4, 100, 200, 300);
        }

        protected static CharacterProgressionState NewProgression(CharacterProgressionDefinition curve)
        {
            return new CharacterProgressionState(CharacterId.New(), curve);
        }
    }
}
