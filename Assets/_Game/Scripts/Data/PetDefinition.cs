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

        [Tooltip("PetDefinition the pet becomes. Invalid means this stage is terminal.")]
        [SerializeField] private DefinitionId _evolvedForm;

        [Tooltip("ItemDefinition consumed to evolve. Invalid means no material cost.")]
        [SerializeField] private DefinitionId _requiredItem;

        [Tooltip("How many of the required item. Below one reads as one.")]
        [SerializeField] private int _requiredItemQuantity;

        public PetEvolutionStage(DefinitionId evolvedForm, int requiredLevel = 1,
            int requiredExperience = 0, DefinitionId grantedBuff = default,
            DefinitionId requiredItem = default, int requiredItemQuantity = 1)
        {
            _nameKey = default;
            _requiredLevel = requiredLevel;
            _requiredExperience = requiredExperience;
            _model = default;
            _auraEffect = default;
            _grantedBuff = grantedBuff;
            _passiveAbilities = null;
            _evolvedForm = evolvedForm;
            _requiredItem = requiredItem;
            _requiredItemQuantity = requiredItemQuantity;
        }

        public LocalizationKey NameKey => _nameKey;

        public int RequiredLevel => _requiredLevel;

        public int RequiredExperience => _requiredExperience;

        public AssetRef Model => _model;

        /// <summary>Visual effect shown once this stage is reached.</summary>
        public AssetRef AuraEffect => _auraEffect;

        /// <summary>Reference to a <see cref="StatusEffectDefinition"/> granted to the owner.</summary>
        public DefinitionId GrantedBuff => _grantedBuff;

        /// <summary>References to <see cref="SkillDefinition"/> passives active at this stage.</summary>
        public DefinitionId[] PassiveAbilities => _passiveAbilities ?? NoIds;

        /// <summary>
        /// The <see cref="PetDefinition"/> the pet becomes on reaching this stage.
        /// </summary>
        /// <remarks>
        /// Evolution changes what the pet <em>is</em>, so the outcome is another authored
        /// definition rather than a flag. That is what lets one pet evolve into something
        /// with a different model, category, follow behaviour and buff, and it is what makes
        /// a chain <c>A -&gt; B -&gt; C</c> expressible without a ladder type. A cycle
        /// <c>A -&gt; B -&gt; A</c> is expressible too, which is exactly why content
        /// validation looks for one.
        ///
        /// Invalid means the stage is terminal: the pet reaches it and evolves no further.
        /// </remarks>
        public DefinitionId EvolvedForm => _evolvedForm;

        /// <summary>Reference to an <see cref="ItemDefinition"/> spent to evolve. Invalid means free.</summary>
        public DefinitionId RequiredItem => _requiredItem;

        /// <summary>How many of <see cref="RequiredItem"/>. Never below one.</summary>
        public int RequiredItemQuantity => _requiredItemQuantity < 1 ? 1 : _requiredItemQuantity;

        private static readonly DefinitionId[] NoIds = new DefinitionId[0];
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

        [Tooltip("Experience needed to reach level 2, 3, 4 ... in order. Empty means the pet never levels.")]
        [SerializeField] private int[] _experienceThresholds = new int[0];

        [Tooltip("Highest level. Zero or less means the thresholds decide.")]
        [SerializeField] private int _maxLevel;

        [Tooltip("StatusEffectDefinition granted to the owner before any evolution.")]
        [SerializeField] private DefinitionId _baseBuff;

        [SerializeField] private AssetRef _soundEffect;

        [Tooltip("How high above the ground a floating follower sits. Zero means grounded.")]
        [SerializeField] private float _verticalOffset;

        [Tooltip("Whether this form appears as an aura on its owner instead of as a follower.")]
        [SerializeField] private bool _auraForm;

        [Tooltip("Turns the pet off without deleting it. Stored inverted so existing content stays enabled.")]
        [SerializeField] private bool _disabled;

        public LocalizationKey NameKey => _nameKey;

        public LocalizationKey DescriptionKey => _descriptionKey;

        public AssetRef Icon => _icon;

        /// <summary>Base model, before any evolution stage overrides it.</summary>
        public AssetRef Model => _model;

        public PetCategory Category => _category;

        public PetFollowBehavior FollowBehavior => _followBehavior;

        /// <summary>Authored stages in ascending order. May be empty for pets that never evolve.</summary>
        public PetEvolutionStage[] EvolutionStages => _evolutionStages ?? NoStages;

        /// <summary>
        /// Cumulative experience needed for each level after the first.
        /// </summary>
        /// <remarks>
        /// Index zero is what reaches level two. Cumulative rather than per-level because a
        /// pet's total experience is the number that persists, and deriving a level from a
        /// running total needs no history -- which is what makes progression reproducible
        /// after a reload and impossible to double-count.
        ///
        /// Integers throughout. Experience is a count, and a float count invites comparisons
        /// that are true on one machine and false on another.
        /// </remarks>
        public int[] ExperienceThresholds => _experienceThresholds ?? NoThresholds;

        /// <summary>
        /// The highest level this pet reaches.
        /// </summary>
        /// <remarks>Zero means the authored thresholds decide it, which is the normal case:
        /// a pet with three thresholds caps at level four. An explicit value below that is a
        /// stricter cap and wins, because a cap is a restriction.</remarks>
        public int MaxLevel => _maxLevel;

        /// <summary>Reference to a <see cref="StatusEffectDefinition"/> granted before any evolution.</summary>
        public DefinitionId BaseBuff => _baseBuff;

        public AssetRef SoundEffect => _soundEffect;

        /// <summary>Height a floating follower sits at. Presentation reads it; gameplay does not.</summary>
        public float VerticalOffset => _verticalOffset;

        /// <summary>
        /// Whether this form is present as an aura on its owner rather than as a follower.
        /// </summary>
        /// <remarks>
        /// A property of the <em>form</em>, not of the transition that produced it. An
        /// evolved pet that becomes light around its owner is a definition that says so, so
        /// anything holding a pet can answer "is this a follower" from what the pet is now,
        /// without knowing how it got there. Authoring it on the evolution step instead would
        /// mean a summoned pet had to look backwards through a chain to draw itself.
        ///
        /// A pet that keeps walking around after evolving is an equally legitimate outcome,
        /// which is why this is authored rather than implied by having evolved.
        /// </remarks>
        public bool IsAuraForm => _auraForm;

        /// <summary>Whether the pet may be obtained and summoned.</summary>
        /// <remarks>Stored inverted, for the reason <see cref="DropEntry.Enabled"/> gives.</remarks>
        public bool Enabled => !_disabled;

        /// <summary>
        /// The level cap actually in force.
        /// </summary>
        /// <remarks>The stricter of the authored ceiling and what the thresholds can reach,
        /// resolved once here so no caller has to know the precedence and two screens cannot
        /// read a pet as capping at different levels.</remarks>
        public int EffectiveMaxLevel
        {
            get
            {
                int fromThresholds = ExperienceThresholds.Length + 1;

                if (_maxLevel > 0 && _maxLevel < fromThresholds) return _maxLevel;
                return fromThresholds;
            }
        }

        private static readonly PetEvolutionStage[] NoStages = new PetEvolutionStage[0];
        private static readonly int[] NoThresholds = new int[0];
    }
}
