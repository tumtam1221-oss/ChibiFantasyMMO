using System;
using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>One line of a vendor's stock.</summary>
    /// <remarks>
    /// <b>An item id and a price.</b> Nothing runtime: what a player buys becomes an
    /// ordinary <see cref="ItemInstance"/> or <see cref="EquipmentInstance"/> like anything
    /// else, so there is deliberately no <c>ShopItem</c> type. A future player trade or
    /// player shop operates on the very same objects.
    ///
    /// <b>Flat because it has to persist.</b> One row of a future <c>npc_shop</c> table.
    /// </remarks>
    [Serializable]
    public struct ShopEntry
    {
        [SerializeField] private DefinitionId _item;

        [Tooltip("What it costs. Zero or less means the vendor gives it away.")]
        [SerializeField] private int _price;

        [Tooltip("How many are available. Zero or less means unlimited.")]
        [SerializeField] private int _stock;

        [SerializeField] private bool _enabled;

        public ShopEntry(DefinitionId item, int price, int stock = 0, bool enabled = true)
        {
            _item = item;
            _price = price;
            _stock = stock;
            _enabled = enabled;
        }

        /// <summary>Reference to the sold <see cref="ItemDefinition"/>.</summary>
        public DefinitionId Item => _item;

        public int Price => _price;

        /// <summary>Zero or less means the vendor never runs out.</summary>
        public int Stock => _stock;

        public bool IsUnlimited => _stock <= 0;

        public bool Enabled => _enabled;

        public bool IsValid => _item.IsValid;

        public override string ToString()
        {
            return _item + " @" + _price + (IsUnlimited ? string.Empty : " x" + _stock);
        }
    }

    /// <summary>
    /// A vendor's stock list.
    /// </summary>
    /// <remarks>
    /// <b>A system vendor, not a player shop.</b> This is the NPC that sells potions.
    /// Player-to-player marketplaces are a later phase and a different thing entirely; the
    /// only overlap is that both end up handing over normal item instances, which is
    /// exactly why nothing special is defined here.
    ///
    /// <b>Data only.</b> Buying, selling, currency and stock decrement are not implemented
    /// in this phase -- the definition exists so an NPC's shop role has something real to
    /// resolve to and so content can be authored now.
    /// </remarks>
    public sealed class ShopDefinition : GameDefinition
    {
        [SerializeField] private LocalizationKey _nameKey;
        [SerializeField] private ShopEntry[] _entries = new ShopEntry[0];

        [Tooltip("Fraction of an item's sell price this vendor pays. Zero means it buys nothing.")]
        [SerializeField] private float _buyBackRate;

        public LocalizationKey NameKey => _nameKey;

        /// <summary>What it sells. Never null.</summary>
        public ShopEntry[] Entries => _entries ?? NoEntries;

        /// <summary>
        /// What it pays for a player's goods.
        /// </summary>
        /// <remarks>A rate rather than a price list, so selling needs no second table.
        /// Zero means the vendor does not buy, which is the safe default for content
        /// authored before this existed.</remarks>
        public float BuyBackRate => _buyBackRate;

        private static readonly ShopEntry[] NoEntries = new ShopEntry[0];
    }
}
