using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// A rarity tier, authored as content rather than hard-coded.
    /// </summary>
    /// <remarks>
    /// Rarity is referenced by items, equipment, cards and Devil Fruits, so it is defined
    /// once here instead of duplicating a rarity enum across every definition.
    ///
    /// Progressions such as Normal, Rare, Elite, Epic, Legendary and Top Tier are content
    /// entries, not code. <see cref="Order"/> gives them a comparable ranking so new tiers
    /// can be inserted without touching any system that compares rarity.
    /// </remarks>
    public sealed class RarityDefinition : GameDefinition
    {
        [SerializeField] private LocalizationKey _nameKey;
        [SerializeField] private int _order;
        [SerializeField] private Color _displayColor = Color.white;

        [Header("Equipment effects")]
        [Tooltip("Modifiers every piece at this tier grants, on top of its own.")]
        [SerializeField] private StatModifier[] _statModifiers = new StatModifier[0];

        [Tooltip("Extra enchant sockets this tier grants beyond the piece's authored count.")]
        [SerializeField] private int _bonusEnchantSlots;

        [Tooltip("Enhancement ceiling for this tier. Zero or less means the tier imposes none.")]
        [SerializeField] private int _maxEnhancementLevel;

        /// <summary>Localization key for the tier's display name.</summary>
        public LocalizationKey NameKey => _nameKey;

        /// <summary>Relative rank. Higher is rarer. Gaps are allowed so tiers can be inserted.</summary>
        public int Order => _order;

        /// <summary>Tint used by UI for names, borders and drop notifications.</summary>
        public Color DisplayColor => _displayColor;

        /// <summary>
        /// Modifiers a piece gains purely for being this tier.
        /// </summary>
        /// <remarks>
        /// Authored on the tier rather than baked into each item, so re-balancing Rare
        /// touches one asset instead of every rare sword. Collected by
        /// <c>EquipmentModifierResolver</c> exactly once per worn piece -- there is no
        /// accumulation, so a piece cannot pick up its tier bonus twice.
        ///
        /// Never null: a tier authored before this field existed reads as empty.
        /// </remarks>
        public StatModifier[] StatModifiers => _statModifiers ?? NoModifiers;

        /// <summary>
        /// Extra sockets the tier adds to <see cref="EquipmentDefinition.StatusStoneSlots"/>.
        /// </summary>
        /// <remarks>Additive rather than absolute so a piece keeps its own authored socket
        /// count as the floor: making an item Legendary can only give it more room, never
        /// silently take sockets away and orphan the stones already in them.</remarks>
        public int BonusEnchantSlots => _bonusEnchantSlots;

        /// <summary>
        /// The tier's own enhancement ceiling.
        /// </summary>
        /// <remarks>Zero or less means the tier imposes no limit and only the item's
        /// <see cref="EquipmentDefinition.MaxEnhancementLevel"/> applies. When both are
        /// authored the lower wins, because a cap is a restriction and the stricter
        /// restriction is the one a player is subject to.</remarks>
        public int MaxEnhancementLevel => _maxEnhancementLevel;

        private static readonly StatModifier[] NoModifiers = new StatModifier[0];
    }
}
