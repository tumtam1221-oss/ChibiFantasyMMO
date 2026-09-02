using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// The derived stats computed for a character at one moment.
    /// </summary>
    /// <remarks>
    /// <b>Calculated, never authoritative.</b> This is not persistent state and does not
    /// implement <see cref="IPersistentState"/>. Storing derived stats as their own truth
    /// would let them drift from the base stats and formulas that produced them, and would
    /// make a balance patch a data migration. They are recomputed instead.
    ///
    /// It is not <see cref="IRuntimeState"/> either. That contract exists to carry a
    /// revision for change detection, and a value recomputed from its inputs has nothing to
    /// track: the inputs already carry revisions. Classifying it would be labelling, not
    /// design.
    ///
    /// Immutable once built, so a consumer cannot alter a computed figure and pass it on as
    /// if the calculator had produced it.
    ///
    /// A stat with no formula is <em>absent</em> rather than zero, which is what lets a
    /// caller tell missing configuration from a legitimately computed zero.
    /// </remarks>
    public sealed class DerivedStatsResult
    {
        private readonly CharacterStatEntry[] _stats;
        private readonly ReadOnlyCollection<CharacterStatEntry> _view;

        public DerivedStatsResult(CharacterId characterId, IList<CharacterStatEntry> stats)
        {
            if (stats == null)
            {
                throw new ArgumentNullException(nameof(stats));
            }

            CharacterId = characterId;
            _stats = new CharacterStatEntry[stats.Count];
            stats.CopyTo(_stats, 0);
            _view = Array.AsReadOnly(_stats);
        }

        /// <summary>The character these figures were computed for.</summary>
        public CharacterId CharacterId { get; }

        /// <summary>Every computed stat, in the order its formula was supplied.</summary>
        public IReadOnlyList<CharacterStatEntry> Stats => _view;

        public int Count => _stats.Length;

        /// <summary>Whether a value was computed for a stat.</summary>
        public bool Contains(DefinitionId stat)
        {
            return IndexOf(stat) >= 0;
        }

        /// <summary>
        /// Reads a computed stat.
        /// </summary>
        /// <returns>False when no formula produced this stat, which is different from a
        /// formula that produced zero.</returns>
        public bool TryGet(DefinitionId stat, out int value)
        {
            int index = IndexOf(stat);

            if (index < 0)
            {
                value = 0;
                return false;
            }

            value = _stats[index].Value;
            return true;
        }

        private int IndexOf(DefinitionId stat)
        {
            for (int i = 0; i < _stats.Length; i++)
            {
                if (_stats[i].Stat == stat)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
