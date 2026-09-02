using System;
using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// Another skill that must be known, to a given level, before this one can be.
    /// </summary>
    /// <remarks>
    /// The level is the reason this is a pairing rather than a bare id. "Knows Fireball"
    /// and "knows Fireball at level five" are different gates, and a skill tree that cannot
    /// express the second forces the requirement into code.
    /// </remarks>
    [Serializable]
    public struct SkillPrerequisite
    {
        [SerializeField] private DefinitionId _skill;
        [SerializeField] private int _level;

        public SkillPrerequisite(DefinitionId skill, int level)
        {
            _skill = skill;
            _level = level;
        }

        /// <summary>Reference to the required <see cref="SkillDefinition"/>.</summary>
        public DefinitionId Skill => _skill;

        /// <summary>Minimum level that skill must have reached. At least one.</summary>
        public int Level => _level;
    }
}
