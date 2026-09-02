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

        /// <summary>Localization key for the tier's display name.</summary>
        public LocalizationKey NameKey => _nameKey;

        /// <summary>Relative rank. Higher is rarer. Gaps are allowed so tiers can be inserted.</summary>
        public int Order => _order;

        /// <summary>Tint used by UI for names, borders and drop notifications.</summary>
        public Color DisplayColor => _displayColor;
    }
}
