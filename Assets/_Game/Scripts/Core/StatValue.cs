using System;
using UnityEngine;

namespace ChibiFantasy.Core
{
    /// <summary>
    /// An absolute value for a stat, identified by definition rather than by enum so new
    /// stats can be authored as content.
    /// </summary>
    /// <remarks>
    /// Used for base values (a monster's base STR, a class's starting VIT). For additive
    /// or multiplicative adjustments see <see cref="StatModifier"/>.
    /// </remarks>
    [Serializable]
    public struct StatValue
    {
        [SerializeField] private DefinitionId _stat;
        [SerializeField] private float _value;

        public StatValue(DefinitionId stat, float value)
        {
            _stat = stat;
            _value = value;
        }

        public DefinitionId Stat => _stat;

        public float Value => _value;
    }
}
