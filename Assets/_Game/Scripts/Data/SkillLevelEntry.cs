using System;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// What one level of a skill costs, demands and does.
    /// </summary>
    /// <remarks>
    /// A skill is one definition with a table of levels, not one class per level and not a
    /// hard-coded ladder from one to ten. How many levels a skill has, what each costs and
    /// what character level each demands are all authored, so a designer retunes a skill by
    /// editing an asset.
    ///
    /// Cost and cooldown live here rather than only on the skill because they are the two
    /// things that almost always change per level. Cast time and range stay on the skill,
    /// since a skill whose reach changes with rank is unusual enough that inventing the
    /// field now would be speculation; adding them later is additive.
    ///
    /// Effects are described, never executed. See <see cref="SkillEffect"/>.
    /// </remarks>
    [Serializable]
    public struct SkillLevelEntry
    {
        [SerializeField] private int _level;
        [SerializeField] private int _requiredCharacterLevel;
        [SerializeField] private float _resourceCost;
        [SerializeField] private float _cooldownSeconds;
        [SerializeField] private SkillEffect[] _effects;

        public SkillLevelEntry(int level, int requiredCharacterLevel, float resourceCost,
            float cooldownSeconds, SkillEffect[] effects)
        {
            _level = level;
            _requiredCharacterLevel = requiredCharacterLevel;
            _resourceCost = resourceCost;
            _cooldownSeconds = cooldownSeconds;
            _effects = effects;
        }

        /// <summary>Which rank of the skill this describes. Levels run from one upward.</summary>
        public int Level => _level;

        /// <summary>Character level needed to hold the skill at this rank.</summary>
        public int RequiredCharacterLevel => _requiredCharacterLevel;

        /// <summary>Cost in the skill's resource type. Never negative.</summary>
        public float ResourceCost => _resourceCost;

        /// <summary>Cooldown at this rank. Never negative.</summary>
        public float CooldownSeconds => _cooldownSeconds;

        /// <summary>What the skill does at this rank. May be empty.</summary>
        public SkillEffect[] Effects => _effects;
    }
}
