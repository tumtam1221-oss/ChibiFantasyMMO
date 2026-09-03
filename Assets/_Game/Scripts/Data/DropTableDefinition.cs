using System;
using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// One thing that may fall.
    /// </summary>
    /// <remarks>
    /// <b>An item id and some numbers.</b> Nothing distinguishes a copper coin from an
    /// ultra-rare card here except the chance a designer typed, which is the whole point:
    /// when cards and Devil Fruits arrive they are <see cref="ItemDefinition"/>s with a
    /// small <see cref="Chance"/>, and no drop code changes to support them.
    ///
    /// <b>Flat because it has to persist.</b> One row of a future <c>drop_entry</c> table
    /// is a table id, an item id, two quantities, a chance and two optional gates.
    ///
    /// <see cref="Chance"/> at zero or less means guaranteed, not never -- an unauthored
    /// chance must not silently make an entry impossible.
    /// </remarks>
    [Serializable]
    public struct DropEntry
    {
        [SerializeField] private DefinitionId _item;
        [SerializeField] private int _minQuantity;
        [SerializeField] private int _maxQuantity;

        [Tooltip("Chance in 0..1. Zero or less is guaranteed.")]
        [SerializeField] private float _chance;

        [Tooltip("Rarity stamped on dropped equipment. Invalid leaves the authored tier.")]
        [SerializeField] private DefinitionId _rarityOverride;

        [Tooltip("Lowest killer level this entry applies to. Zero means no floor.")]
        [SerializeField] private int _minKillerLevel;

        [Tooltip("Highest killer level this entry applies to. Zero means no ceiling.")]
        [SerializeField] private int _maxKillerLevel;

        public DropEntry(DefinitionId item, int minQuantity, int maxQuantity, float chance = 0f,
            DefinitionId rarityOverride = default, int minKillerLevel = 0, int maxKillerLevel = 0)
        {
            _item = item;
            _minQuantity = minQuantity;
            _maxQuantity = maxQuantity;
            _chance = chance;
            _rarityOverride = rarityOverride;
            _minKillerLevel = minKillerLevel;
            _maxKillerLevel = maxKillerLevel;
        }

        /// <summary>Reference to the dropped <see cref="ItemDefinition"/>.</summary>
        public DefinitionId Item => _item;

        public int MinQuantity => _minQuantity;

        /// <summary>Top of the range. Below the minimum reads as a fixed quantity.</summary>
        public int MaxQuantity => _maxQuantity < _minQuantity ? _minQuantity : _maxQuantity;

        /// <summary>Zero or less is guaranteed, so a blank is not "never".</summary>
        public float Chance => _chance;

        public bool IsGuaranteed => _chance <= 0f;

        /// <summary>
        /// Rarity stamped on what drops.
        /// </summary>
        /// <remarks>How a table drops the same sword at different tiers without authoring
        /// the sword twice. Invalid leaves the item's own authored rarity, which is the
        /// normal case.</remarks>
        public DefinitionId RarityOverride => _rarityOverride;

        public int MinKillerLevel => _minKillerLevel;

        public int MaxKillerLevel => _maxKillerLevel;

        public bool IsValid => _item.IsValid && _minQuantity > 0;

        /// <summary>
        /// Whether this entry applies to a killer of a given level.
        /// </summary>
        /// <remarks>Zero at either end means that end is open, so an entry with no gates
        /// applies to everyone. This is the "optional condition" a level-banded table needs
        /// and is deliberately the only one: richer conditions belong to whatever system
        /// wants them, not to every drop row.</remarks>
        public bool AppliesTo(int killerLevel)
        {
            if (_minKillerLevel > 0 && killerLevel < _minKillerLevel) return false;
            if (_maxKillerLevel > 0 && killerLevel > _maxKillerLevel) return false;
            return true;
        }

        public override string ToString()
        {
            return _item + " x" + _minQuantity + ".." + MaxQuantity
                + (IsGuaranteed ? " (always)" : " @" + _chance);
        }
    }

    /// <summary>
    /// What a monster may drop.
    /// </summary>
    /// <remarks>
    /// <b>Entries are independent by default.</b> A monster is not "one item": every entry
    /// gets its own roll, so a table can guarantee a coin, usually give a hide and rarely
    /// give a relic, all at once. That is the model most MMOs actually use, and assuming
    /// one-drop-per-kill would make guaranteed quest items impossible to author alongside
    /// anything else.
    ///
    /// <see cref="MaxEntries"/> caps how many may land in one roll, for tables that want
    /// "at most two of these". Zero means no cap.
    ///
    /// Rolling is a Gameplay concern; nothing is computed here.
    /// </remarks>
    public sealed class DropTableDefinition : GameDefinition
    {
        [SerializeField] private LocalizationKey _nameKey;
        [SerializeField] private DropEntry[] _entries = new DropEntry[0];

        [Tooltip("Most entries that may drop at once. Zero means no limit.")]
        [SerializeField] private int _maxEntries;

        public LocalizationKey NameKey => _nameKey;

        /// <summary>Every entry, each rolled independently. Never null.</summary>
        public DropEntry[] Entries => _entries ?? NoEntries;

        /// <summary>Zero means every entry that passes its roll drops.</summary>
        public int MaxEntries => _maxEntries;

        private static readonly DropEntry[] NoEntries = new DropEntry[0];
    }
}
