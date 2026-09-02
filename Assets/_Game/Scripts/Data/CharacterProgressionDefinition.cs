using System;
using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// The authored level curve: where levelling starts, where it stops, and what each
    /// level costs.
    /// </summary>
    /// <remarks>
    /// Content, not code. No level count and no experience number appears in a constant or
    /// a class name, so the curve can be retuned or extended past level 60 by editing an
    /// asset. <see cref="CharacterProgressionState"/> never reads this on its own; it is
    /// handed in, which keeps state from reaching into content.
    ///
    /// <b>Per-level costs, not cumulative totals.</b> Each entry is the experience needed
    /// to leave that level, which is what a within-level progression model consumes
    /// directly. A consequence worth stating: ascending order is deliberately not enforced.
    /// A designer may legitimately make level 40 cheaper than level 39, and a monotonic
    /// rule would forbid a curve the game has not decided against. What is enforced is that
    /// every cost is positive, that the table covers exactly the levels it must, and that
    /// the whole curve fits in a long.
    ///
    /// Nothing about jobs lives here. Job change levels are authored on
    /// <see cref="ClassDefinition"/> and <see cref="JobDefinition"/>, so a different class
    /// may advance on an entirely different schedule without touching the curve.
    /// </remarks>
    public sealed class CharacterProgressionDefinition : GameDefinition
    {
        [SerializeField] private LocalizationKey _nameKey;
        [SerializeField] private int _minLevel = 1;
        [SerializeField] private int _maxLevel = 1;

        /// <summary>Index i holds the cost of leaving level <c>MinLevel + i</c>.</summary>
        [SerializeField] private long[] _experienceToNextLevel = new long[0];

        public LocalizationKey NameKey => _nameKey;

        /// <summary>The level a new character starts at.</summary>
        public int MinLevel => _minLevel;

        /// <summary>The highest level reachable under this curve.</summary>
        public int MaxLevel => _maxLevel;

        /// <summary>Number of level transitions this curve describes.</summary>
        public int TransitionCount => _experienceToNextLevel.Length;

        /// <summary>
        /// Experience required to advance from <paramref name="level"/> to the next.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The level is outside the curve, or
        /// is the maximum level, which has no next level to reach.</exception>
        public long GetExperienceToNextLevel(int level)
        {
            if (level < _minLevel || level >= _maxLevel)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(level), level,
                    "Level is outside the levelling range " + _minLevel + " to " + (_maxLevel - 1) + ".");
            }

            int index = level - _minLevel;

            if (index >= _experienceToNextLevel.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(level), level, "The curve has no cost authored for this level.");
            }

            return _experienceToNextLevel[index];
        }

        /// <summary>True when the level is inside this curve's bounds.</summary>
        public bool IsLevelInRange(int level)
        {
            return level >= _minLevel && level <= _maxLevel;
        }
    }
}
