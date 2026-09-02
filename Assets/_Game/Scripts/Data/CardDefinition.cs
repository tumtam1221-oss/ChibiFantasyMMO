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
        public StatModifier[] StatModifiers => _statModifiers;

        /// <summary>References to <see cref="StatusEffectDefinition"/> granted while socketed.</summary>
        public DefinitionId[] GrantedEffects => _grantedEffects;

        /// <summary>Slot restriction. None means any slot.</summary>
        public EquipmentSlot AllowedSlot => _allowedSlot;

        /// <summary>Category restriction. None means any category.</summary>
        public EquipmentCategory AllowedCategory => _allowedCategory;
    }
}
