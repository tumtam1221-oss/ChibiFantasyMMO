using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// What a currency is.
    /// </summary>
    /// <remarks>
    /// <b>Not only gold.</b> Nothing in code names a currency. A balance is keyed by
    /// <see cref="GameDefinition.Id"/>, so a second currency -- a token, a guild
    /// contribution point, an event coin -- is an asset and a row, not a code change.
    ///
    /// <b>Its relationship to the currency <em>item</em>.</b> Phase 09 charges enhancement
    /// and fusion costs against an ordinary <see cref="ItemCategory.Currency"/> item held in
    /// a bag, and explicitly recorded that no wallet existed. Phase 13 adds the wallet
    /// because trade and shops need a balance that survives a full inventory and is not
    /// capped by a stack size. The two are deliberately kept apart rather than merged:
    /// <see cref="BackingItem"/> names the item this currency corresponds to, which is the
    /// seam a later phase uses to convert coins picked up as loot into a balance. Nothing in
    /// this phase rewrites Phase 09, and no code reads a balance where an item cost is meant.
    ///
    /// Flat and DB-friendly: one row of a future <c>currency_definition</c> table is an id,
    /// two keys, an icon, a ceiling and a flag.
    /// </remarks>
    public sealed class CurrencyDefinition : GameDefinition
    {
        [SerializeField] private LocalizationKey _nameKey;
        [SerializeField] private LocalizationKey _descriptionKey;
        [SerializeField] private AssetRef _icon;

        [Tooltip("Highest balance a character may hold. Zero or less means no authored ceiling.")]
        [SerializeField] private long _maximumBalance;

        [Tooltip("ItemDefinition this currency corresponds to. Invalid means it has no item form.")]
        [SerializeField] private DefinitionId _backingItem;

        [Tooltip("Turns the currency off without deleting it. Stored inverted so existing content stays enabled.")]
        [SerializeField] private bool _disabled;

        public LocalizationKey NameKey => _nameKey;

        public LocalizationKey DescriptionKey => _descriptionKey;

        public AssetRef Icon => _icon;

        /// <summary>
        /// The authored ceiling, or <see cref="int.MaxValue"/> when none was authored.
        /// </summary>
        /// <remarks>
        /// Held as a <c>long</c> in the field so an author can type a value above
        /// <see cref="int.MaxValue"/> and be clamped to something a balance can actually
        /// hold, rather than silently wrapping to a negative ceiling. Balances themselves
        /// are <c>int</c>: currency is a count, and a float count is how rounding errors
        /// become an economy exploit.
        /// </remarks>
        public int MaximumBalance
        {
            get
            {
                if (_maximumBalance <= 0L) return int.MaxValue;
                return _maximumBalance > int.MaxValue ? int.MaxValue : (int)_maximumBalance;
            }
        }

        /// <summary>Reference to the <see cref="ItemDefinition"/> form of this currency.</summary>
        /// <remarks>The migration seam described above. Nothing in Phase 13 reads it to
        /// resolve a balance; it exists so a later phase can turn dropped coins into one.</remarks>
        public DefinitionId BackingItem => _backingItem;

        /// <summary>Whether the currency may be held or moved.</summary>
        /// <remarks>Stored inverted, for the reason <see cref="DropEntry.Enabled"/> gives.</remarks>
        public bool Enabled => !_disabled;
    }
}
