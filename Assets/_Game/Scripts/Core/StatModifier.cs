using System;
using UnityEngine;

namespace ChibiFantasy.Core
{
    /// <summary>How a <see cref="StatModifier"/> combines with a base stat.</summary>
    /// <remarks>Closed technical category: adding a third combination form would change
    /// stat resolution code regardless, so an enum is appropriate here.</remarks>
    public enum StatModifierKind
    {
        Flat = 0,
        Percent = 1
    }

    /// <summary>
    /// An adjustment to a stat, contributed by equipment, a card, a status effect, a job
    /// or similar.
    /// </summary>
    /// <remarks>
    /// Carries data only. How modifiers stack, round or clamp is stat-resolution logic
    /// and belongs to Gameplay, not to the schema.
    /// </remarks>
    [Serializable]
    public struct StatModifier
    {
        [SerializeField] private DefinitionId _stat;
        [SerializeField] private StatModifierKind _kind;
        [SerializeField] private float _value;

        public StatModifier(DefinitionId stat, StatModifierKind kind, float value)
        {
            _stat = stat;
            _kind = kind;
            _value = value;
        }

        public DefinitionId Stat => _stat;

        public StatModifierKind Kind => _kind;

        public float Value => _value;
    }
}
