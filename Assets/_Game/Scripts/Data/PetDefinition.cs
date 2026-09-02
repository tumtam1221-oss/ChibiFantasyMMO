using System;
using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>Broad pet family.</summary>
    public enum PetCategory
    {
        Companion = 0,
        Mount = 1,
        Battle = 2,
        Support = 3,
        Cosmetic = 4
    }

    /// <summary>How a pet positions itself relative to its owner.</summary>
    public enum PetFollowBehavior
    {
        Follow = 0,
        Orbit = 1,
        Shoulder = 2,
        Stationary = 3
    }

    /// <summary>
    /// One authored evolution stage of a pet.
    /// </summary>
    /// <remarks>
    /// Each stage carries its own model, aura, buff and passives, so different pets can
    /// evolve into genuinely different outcomes rather than sharing a fixed ladder.
    /// A pet's current stage, level and experience are runtime state on the owned pet.
    /// </remarks>
    [Serializable]
    public struct PetEvolutionStage
    {
        [SerializeField] private LocalizationKey _nameKey;
        [SerializeField] private int _requiredLevel;
        [SerializeField] private int _requiredExperience;
        [SerializeField] private AssetRef _model;
        [SerializeField] private AssetRef _auraEffect;
        [SerializeField] private DefinitionId _grantedBuff;
        [SerializeField] private DefinitionId[] _passiveAbilities;

        public LocalizationKey NameKey => _nameKey;

        public int RequiredLevel => _requiredLevel;

        public int RequiredExperience => _requiredExperience;

        public AssetRef Model => _model;

        /// <summary>Visual effect shown once this stage is reached.</summary>
        public AssetRef AuraEffect => _auraEffect;

        /// <summary>Reference to a <see cref="StatusEffectDefinition"/> granted to the owner.</summary>
        public DefinitionId GrantedBuff => _grantedBuff;

        /// <summary>References to <see cref="SkillDefinition"/> passives active at this stage.</summary>
        public DefinitionId[] PassiveAbilities => _passiveAbilities;
    }

    /// <summary>
    /// What a pet species or form is.
    /// </summary>
    /// <remarks>
    /// No following, experience gain, evolution or buff application. A player's actual pet,
    /// with its level, experience and reached stage, is runtime state belonging to a future
    /// PetInstance.
    /// </remarks>
    public sealed class PetDefinition : GameDefinition
    {
        [SerializeField] private LocalizationKey _nameKey;
        [SerializeField] private LocalizationKey _descriptionKey;
        [SerializeField] private AssetRef _icon;
        [SerializeField] private AssetRef _model;

        [SerializeField] private PetCategory _category = PetCategory.Companion;
        [SerializeField] private PetFollowBehavior _followBehavior = PetFollowBehavior.Follow;

        [SerializeField] private PetEvolutionStage[] _evolutionStages = new PetEvolutionStage[0];

        public LocalizationKey NameKey => _nameKey;

        public LocalizationKey DescriptionKey => _descriptionKey;

        public AssetRef Icon => _icon;

        /// <summary>Base model, before any evolution stage overrides it.</summary>
        public AssetRef Model => _model;

        public PetCategory Category => _category;

        public PetFollowBehavior FollowBehavior => _followBehavior;

        /// <summary>Authored stages in ascending order. May be empty for pets that never evolve.</summary>
        public PetEvolutionStage[] EvolutionStages => _evolutionStages;
    }
}
