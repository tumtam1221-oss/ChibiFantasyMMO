using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// What a card is.
    /// </summary>
    /// <remarks>
    /// Cards slot into equipment to contribute stat modifiers and effects. Rolling a card
    /// drop and applying a socketed card to a piece of gear are Gameplay concerns; an
    /// owned card is runtime state belonging to a future CardInstance.
    /// </remarks>
    public sealed class CardDefinition : GameDefinition
    {
        [SerializeField] private LocalizationKey _nameKey;
        [SerializeField] private LocalizationKey _descriptionKey;
        [SerializeField] private AssetRef _icon;

        [SerializeField] private DefinitionId _rarity;

        [SerializeField] private DefinitionId _sourceMonster;
        [SerializeField] private DefinitionId _dropTable;

        [SerializeField] private StatModifier[] _statModifiers = new StatModifier[0];
        [SerializeField] private DefinitionId[] _grantedEffects = new DefinitionId[0];

        [SerializeField] private EquipmentSlot _allowedSlot = EquipmentSlot.None;
        [SerializeField] private EquipmentCategory _allowedCategory = EquipmentCategory.None;

        [Tooltip("Equipment subtypes this card fits. Empty means unrestricted.")]
        [SerializeField] private EquipmentSubtype[] _allowedSubtypes = new EquipmentSubtype[0];

        [Tooltip("Conditional effects, such as extra damage against a monster rank.")]
        [SerializeField] private CardEffect[] _effects = new CardEffect[0];

        [Tooltip("How many copies of this card one piece may hold. Below one reads as one.")]
        [SerializeField] private int _maxPerEquipment = 1;

        [Tooltip("Turns the card off without deleting it. Stored inverted so existing content stays enabled.")]
        [SerializeField] private bool _disabled;

        public LocalizationKey NameKey => _nameKey;

        public LocalizationKey DescriptionKey => _descriptionKey;

        public AssetRef Icon => _icon;

        /// <summary>Reference to a <see cref="RarityDefinition"/>.</summary>
        public DefinitionId Rarity => _rarity;

        /// <summary>Reference to the <see cref="MonsterDefinition"/> this card comes from.</summary>
        public DefinitionId SourceMonster => _sourceMonster;

        /// <summary>Reference to a drop table definition.</summary>
        public DefinitionId DropTable => _dropTable;

        /// <summary>Modifiers contributed to the equipment holding this card.</summary>
        public StatModifier[] StatModifiers => _statModifiers ?? NoModifiers;

        /// <summary>References to <see cref="StatusEffectDefinition"/> granted while socketed.</summary>
        public DefinitionId[] GrantedEffects => _grantedEffects ?? NoIds;

        /// <summary>Slot restriction. None means any slot.</summary>
        public EquipmentSlot AllowedSlot => _allowedSlot;

        /// <summary>Category restriction. None means any category.</summary>
        public EquipmentCategory AllowedCategory => _allowedCategory;

        /// <summary>Subtype restriction. Empty means any subtype.</summary>
        public EquipmentSubtype[] AllowedSubtypes => _allowedSubtypes ?? NoSubtypes;

        /// <summary>
        /// Conditional effects the card contributes.
        /// </summary>
        /// <remarks>Separate from <see cref="StatModifiers"/> because they depend on what is
        /// being fought. See <see cref="CardEffect"/> for why, and for what does and does not
        /// consume them yet.</remarks>
        public CardEffect[] Effects => _effects ?? NoEffects;

        /// <summary>
        /// How many copies of this card one piece may hold.
        /// </summary>
        /// <remarks>One when unauthored, which is the restrictive reading and matches
        /// <see cref="StatusStoneConfig.MaxPerEquipment"/>. A blank meaning "unlimited" would
        /// let a bad import stack one card into every socket.</remarks>
        public int MaxPerEquipment => _maxPerEquipment < 1 ? 1 : _maxPerEquipment;

        /// <summary>Whether the card may be socketed.</summary>
        /// <remarks>Stored inverted, for the reason <see cref="DropEntry.Enabled"/> gives.</remarks>
        public bool Enabled => !_disabled;

        /// <summary>
        /// Whether this card fits a piece of equipment.
        /// </summary>
        /// <remarks>
        /// <b>The card's own rule, not the status stone's.</b> A stone additionally gates on
        /// rarity and item level and can fail its socketing roll; a card does neither -- it
        /// either fits the gear or it does not, and inserting one is certain. Reusing
        /// <see cref="StatusStoneConfig"/> would have brought a success chance and a failure
        /// behaviour into a system that has no concept of either, and a card would have
        /// inherited the ability to be destroyed on insertion.
        ///
        /// Every restriction is authored against a <em>class</em> of equipment -- slot,
        /// category, subtype -- never against an equipment id. Nothing here compares a
        /// <see cref="DefinitionId"/> to a literal.
        ///
        /// An unauthored restriction means unrestricted, so a card authored before a field
        /// existed still fits what it always fitted.
        /// </remarks>
        public bool Fits(EquipmentDefinition equipment)
        {
            if (equipment == null) return false;

            if (_allowedSlot != EquipmentSlot.None && equipment.Slot != _allowedSlot) return false;

            if (_allowedCategory != EquipmentCategory.None
                && equipment.EquipmentCategory != _allowedCategory)
            {
                return false;
            }

            EquipmentSubtype[] subtypes = AllowedSubtypes;
            if (subtypes.Length == 0) return true;

            for (int i = 0; i < subtypes.Length; i++)
            {
                if (subtypes[i] == equipment.Subtype) return true;
            }

            return false;
        }

        private static readonly StatModifier[] NoModifiers = new StatModifier[0];
        private static readonly DefinitionId[] NoIds = new DefinitionId[0];
        private static readonly EquipmentSubtype[] NoSubtypes = new EquipmentSubtype[0];
        private static readonly CardEffect[] NoEffects = new CardEffect[0];
    }
}
