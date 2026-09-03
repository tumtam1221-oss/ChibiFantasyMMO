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
    /// is a table id, an item id, two quantities, a chance, an enabled flag and two
    /// optional gates. The row's identity is its table plus its ordinal in
    /// <see cref="DropTableDefinition.Entries"/>; no surrogate id is stored here because
    /// nothing in the domain references a single entry.
    ///
    /// <b>Probability convention, stated once.</b> <see cref="Chance"/> is a
    /// <em>fraction in 0..1</em>, never a percentage. A designer's "0.0001%" is authored as
    /// <c>0.000001</c> and an admin's future <c>drop_entry.chance</c> column holds the same
    /// number. There is exactly one convention and it is this one; any UI that wants to
    /// show a percentage multiplies at the edge.
    ///
    /// <see cref="Chance"/> at zero or less means guaranteed, not never -- an unauthored
    /// chance must not silently make an entry impossible.
    ///
    /// <b>Precision.</b> A 32-bit float carries a full binary exponent, so an ultra-rare
    /// <c>1e-7</c> is held with a <em>relative</em> error near 6e-8 -- orders of magnitude
    /// finer than any rate an operator would distinguish. The representation is not the
    /// limit on how rare a drop can be authored, so this stays a float rather than growing
    /// a fixed-point scheme the resolver and a future SQL column would both have to learn.
    /// </remarks>
    [Serializable]
    public struct DropEntry
    {
        [SerializeField] private DefinitionId _item;
        [SerializeField] private int _minQuantity;
        [SerializeField] private int _maxQuantity;

        [Tooltip("Probability as a fraction in 0..1, never a percentage. Zero or less is guaranteed.")]
        [SerializeField] private float _chance;

        [Tooltip("Turns the row off without deleting it. Stored inverted so existing content stays enabled.")]
        [SerializeField] private bool _disabled;

        [Tooltip("Rarity stamped on dropped equipment. Invalid leaves the authored tier.")]
        [SerializeField] private DefinitionId _rarityOverride;

        [Tooltip("Lowest killer level this entry applies to. Zero means no floor.")]
        [SerializeField] private int _minKillerLevel;

        [Tooltip("Highest killer level this entry applies to. Zero means no ceiling.")]
        [SerializeField] private int _maxKillerLevel;

        public DropEntry(DefinitionId item, int minQuantity, int maxQuantity, float chance = 0f,
            DefinitionId rarityOverride = default, int minKillerLevel = 0, int maxKillerLevel = 0,
            bool enabled = true)
        {
            _item = item;
            _minQuantity = minQuantity;
            _maxQuantity = maxQuantity;
            _chance = chance;
            _rarityOverride = rarityOverride;
            _minKillerLevel = minKillerLevel;
            _maxKillerLevel = maxKillerLevel;
            _disabled = !enabled;
        }

        /// <summary>Reference to the dropped <see cref="ItemDefinition"/>.</summary>
        public DefinitionId Item => _item;

        public int MinQuantity => _minQuantity;

        /// <summary>Top of the range. Below the minimum reads as a fixed quantity.</summary>
        public int MaxQuantity => _maxQuantity < _minQuantity ? _minQuantity : _maxQuantity;

        /// <summary>
        /// Probability as a fraction in 0..1. Zero or less is guaranteed, so a blank is not
        /// "never".
        /// </summary>
        /// <remarks>The single number an operator changes. Nothing in code compares it to a
        /// literal and no system has its own copy of it, which is what makes a live
        /// configuration change take effect with no rebuild.</remarks>
        public float Chance => _chance;

        /// <summary>
        /// Whether the row participates in a roll.
        /// </summary>
        /// <remarks>
        /// Stored inverted. A serialized <c>bool</c> added to an existing asset deserializes
        /// as <c>false</c>, so a field named <c>_enabled</c> would silently switch off every
        /// drop authored before it existed. <c>_disabled</c> defaults to the harmless
        /// answer. A future <c>drop_entry.enabled</c> column maps to this property, not to
        /// the field.
        /// </remarks>
        public bool Enabled => !_disabled;

        public bool IsGuaranteed => _chance <= 0f;

        /// <summary>
        /// Whether the authored probability is a number a roll can use.
        /// </summary>
        /// <remarks>
        /// NaN and infinity come from bad imports and bad admin input, and both are worse
        /// than a missing row: NaN compares false against everything, so an entry carrying
        /// one would look like a drop that simply never happens rather than like a
        /// configuration error. Content validation reports it and the resolver skips it, so
        /// neither silently clamps a value an operator typed.
        /// </remarks>
        public bool IsChanceValid
        {
            get
            {
                if (float.IsNaN(_chance) || float.IsInfinity(_chance)) return false;
                return _chance <= 1f;
            }
        }

        /// <summary>
        /// Rarity stamped on what drops.
        /// </summary>
        /// <remarks>How a table drops the same sword at different tiers without authoring
        /// the sword twice. Invalid leaves the item's own authored rarity, which is the
        /// normal case.</remarks>
        public DefinitionId RarityOverride => _rarityOverride;

        public int MinKillerLevel => _minKillerLevel;

        public int MaxKillerLevel => _maxKillerLevel;

        public bool IsValid => _item.IsValid && _minQuantity > 0 && IsChanceValid;

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
