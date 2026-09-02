using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>How a Devil Fruit is taken on by a character.</summary>
    public enum DevilFruitUsage
    {
        Consumed = 0,
        Equipped = 1,
        Toggled = 2
    }

    /// <summary>
    /// What a Devil Fruit is.
    /// </summary>
    /// <remarks>
    /// No fruit is hard-coded. The planned ten are content entries authored as assets of
    /// this type, so the roster can change without touching code.
    ///
    /// Nothing here concerns acquisition. Drop chance, world-boss tables and the intended
    /// ultra-rare roll live in loot and economy systems, not in the schema. Whether a given
    /// player has eaten or equipped a fruit is runtime state belonging to a future
    /// DevilFruitInstance.
    /// </remarks>
    public sealed class DevilFruitDefinition : GameDefinition
    {
        [SerializeField] private LocalizationKey _nameKey;
        [SerializeField] private LocalizationKey _descriptionKey;
        [SerializeField] private AssetRef _icon;

        [SerializeField] private DefinitionId _rarity;

        [SerializeField] private DefinitionId _sourceBoss;
        [SerializeField] private DefinitionId _dropTable;

        [SerializeField] private DevilFruitUsage _usage = DevilFruitUsage.Consumed;
        [SerializeField] private DefinitionId _uniqueEffectId;

        [SerializeField] private DefinitionId _passiveAbility;
        [SerializeField] private DefinitionId _activeAbility;
        [SerializeField] private DefinitionId[] _grantedEffects = new DefinitionId[0];
        [SerializeField] private DefinitionId[] _immunities = new DefinitionId[0];

        [SerializeField] private AssetRef _visualEffect;
        [SerializeField] private AssetRef _soundEffect;

        public LocalizationKey NameKey => _nameKey;

        public LocalizationKey DescriptionKey => _descriptionKey;

        public AssetRef Icon => _icon;

        /// <summary>Reference to a <see cref="RarityDefinition"/>.</summary>
        public DefinitionId Rarity => _rarity;

        /// <summary>Reference to the <see cref="MonsterDefinition"/> world boss that drops it.</summary>
        public DefinitionId SourceBoss => _sourceBoss;

        /// <summary>Reference to a drop table definition. Drop odds live there, not here.</summary>
        public DefinitionId DropTable => _dropTable;

        public DevilFruitUsage Usage => _usage;

        /// <summary>Identifies the one-off signature effect, letting systems special-case a
        /// fruit by data rather than by a hard-coded name.</summary>
        public DefinitionId UniqueEffectId => _uniqueEffectId;

        /// <summary>Reference to a passive <see cref="SkillDefinition"/>.</summary>
        public DefinitionId PassiveAbility => _passiveAbility;

        /// <summary>Reference to an active <see cref="SkillDefinition"/>.</summary>
        public DefinitionId ActiveAbility => _activeAbility;

        /// <summary>References to <see cref="StatusEffectDefinition"/> this fruit inflicts or grants,
        /// such as silence or a debuff.</summary>
        public DefinitionId[] GrantedEffects => _grantedEffects;

        /// <summary>References to <see cref="StatusEffectDefinition"/> the bearer becomes immune to.</summary>
        public DefinitionId[] Immunities => _immunities;

        public AssetRef VisualEffect => _visualEffect;

        public AssetRef SoundEffect => _soundEffect;
    }
}
