using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// A single stat, authored as content.
    /// </summary>
    /// <remarks>
    /// The game's primary stats (STR, AGI, VIT, INT, DEX, LUK) are represented as
    /// definition assets rather than an enum, so derived stats (attack, defence, crit
    /// rate, cast speed and so on) can be added later as content without changing code
    /// or invalidating serialized data.
    ///
    /// Nothing here computes anything. Derived-stat formulas belong to Gameplay.
    /// </remarks>
    public sealed class StatDefinition : GameDefinition
    {
        [SerializeField] private LocalizationKey _nameKey;
        [SerializeField] private LocalizationKey _abbreviationKey;
        [SerializeField] private bool _isPrimary;
        [SerializeField] private float _defaultValue;
        [SerializeField] private int _minValue;
        [SerializeField] private int _maxValue = int.MaxValue;

        public LocalizationKey NameKey => _nameKey;

        /// <summary>Short form shown in dense UI, for example "STR".</summary>
        public LocalizationKey AbbreviationKey => _abbreviationKey;

        /// <summary>True for directly allocated stats; false for stats derived from others.</summary>
        public bool IsPrimary => _isPrimary;

        /// <summary>Value assumed when no contributor supplies this stat.</summary>
        public float DefaultValue => _defaultValue;

        /// <summary>Lowest base value a character may hold in this stat.</summary>
        /// <remarks>Bounds are integers because a character's base stats are counted, not
        /// measured; see <see cref="CharacterStatEntry"/>. They constrain persisted player
        /// state and are enforced by <see cref="CharacterStatsValidator"/>, not by the
        /// state itself, which cannot see content.</remarks>
        public int MinValue => _minValue;

        /// <summary>Highest base value a character may hold in this stat.</summary>
        /// <remarks>Defaults to no practical ceiling, so a stat is unconstrained until a
        /// designer decides otherwise rather than being capped by an invented number.</remarks>
        public int MaxValue => _maxValue;
    }
}
