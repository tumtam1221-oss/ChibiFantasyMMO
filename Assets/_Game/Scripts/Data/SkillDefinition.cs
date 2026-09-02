using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>How a skill behaves at the highest level.</summary>
    public enum SkillCategory
    {
        None = 0,
        Active = 1,
        Passive = 2,
        Toggle = 3,
        Buff = 4
    }

    /// <summary>What a skill may be aimed at.</summary>
    public enum SkillTargetType
    {
        None = 0,
        Self = 1,
        SingleAlly = 2,
        SingleEnemy = 3,
        AreaAroundSelf = 4,
        AreaAtPoint = 5,
        Party = 6
    }

    /// <summary>Which pool a skill draws from.</summary>
    /// <remarks>Closed technical category: each resource has its own pool and regeneration
    /// handling.</remarks>
    public enum SkillResourceType
    {
        None = 0,
        Mana = 1,
        Stamina = 2,
        Health = 3,
        Rage = 4
    }

    /// <summary>
    /// What a skill <em>is</em>: its static metadata and costs.
    /// </summary>
    /// <remarks>
    /// Contains no execution whatsoever. Damage and healing formulas, projectile spawning,
    /// animation, VFX, server RPCs and live cooldown tracking are all Gameplay/Network
    /// concerns. A learned skill's level and remaining cooldown are runtime state.
    /// </remarks>
    public sealed class SkillDefinition : GameDefinition
    {
        [SerializeField] private LocalizationKey _nameKey;
        [SerializeField] private LocalizationKey _descriptionKey;
        [SerializeField] private AssetRef _icon;

        [SerializeField] private SkillCategory _category = SkillCategory.None;
        [SerializeField] private SkillTargetType _targetType = SkillTargetType.None;
        [SerializeField] private int _maxLevel = 1;

        [SerializeField] private SkillResourceType _resourceType = SkillResourceType.None;
        [SerializeField] private float _baseResourceCost;
        [SerializeField] private float _cooldownSeconds;
        [SerializeField] private float _castTimeSeconds;
        [SerializeField] private float _range;

        [SerializeField] private DefinitionId _requiredClass;
        [SerializeField] private DefinitionId _requiredJob;
        [SerializeField] private DefinitionId[] _prerequisiteSkills = new DefinitionId[0];

        public LocalizationKey NameKey => _nameKey;

        public LocalizationKey DescriptionKey => _descriptionKey;

        public AssetRef Icon => _icon;

        public SkillCategory Category => _category;

        public SkillTargetType TargetType => _targetType;

        public int MaxLevel => _maxLevel;

        public SkillResourceType ResourceType => _resourceType;

        /// <summary>Cost at skill level one. Per-level scaling is a Gameplay concern.</summary>
        public float BaseResourceCost => _baseResourceCost;

        public float CooldownSeconds => _cooldownSeconds;

        public float CastTimeSeconds => _castTimeSeconds;

        public float Range => _range;

        /// <summary>Reference to a <see cref="ClassDefinition"/>, or none.</summary>
        public DefinitionId RequiredClass => _requiredClass;

        /// <summary>Reference to a <see cref="JobDefinition"/>, or none.</summary>
        public DefinitionId RequiredJob => _requiredJob;

        /// <summary>References to other <see cref="SkillDefinition"/> that must be learned first.</summary>
        public DefinitionId[] PrerequisiteSkills => _prerequisiteSkills;
    }
}
