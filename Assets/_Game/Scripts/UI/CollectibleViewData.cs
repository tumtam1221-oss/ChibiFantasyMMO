using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.UI
{
    /// <summary>
    /// What a panel needs to draw a Devil Fruit.
    /// </summary>
    /// <remarks>
    /// <b>A snapshot, and keys rather than text.</b> Every string a player reads is a
    /// <see cref="LocalizationKey"/> resolved at the edge; no name, no description and no
    /// effect wording is built here. Holding the definition instead would let a panel reach
    /// through it into content and would pin a ScriptableObject for as long as the view
    /// lived.
    /// </remarks>
    public readonly struct DevilFruitViewData
    {
        public DevilFruitViewData(DefinitionId fruit, LocalizationKey nameKey,
            LocalizationKey descriptionKey, AssetRef icon, DefinitionId rarity, bool isActive,
            DefinitionId passive, DefinitionId active, int grantedEffects, int immunities,
            bool hasVisual, bool hasSound)
        {
            Fruit = fruit;
            NameKey = nameKey;
            DescriptionKey = descriptionKey;
            Icon = icon;
            Rarity = rarity;
            IsActive = isActive;
            PassiveAbility = passive;
            ActiveAbility = active;
            GrantedEffectCount = grantedEffects;
            ImmunityCount = immunities;
            HasVisualEffect = hasVisual;
            HasSoundEffect = hasSound;
        }

        public DefinitionId Fruit { get; }

        public LocalizationKey NameKey { get; }

        public LocalizationKey DescriptionKey { get; }

        public AssetRef Icon { get; }

        /// <summary>Reference to a <see cref="RarityDefinition"/>, for the tier a panel shows.</summary>
        public DefinitionId Rarity { get; }

        /// <summary>Whether this is the fruit the character currently carries.</summary>
        public bool IsActive { get; }

        public DefinitionId PassiveAbility { get; }

        public DefinitionId ActiveAbility { get; }

        public int GrantedEffectCount { get; }

        /// <summary>Effect and category refusals together, which is what a player reads as one line.</summary>
        public int ImmunityCount { get; }

        public bool HasVisualEffect { get; }

        public bool HasSoundEffect { get; }

        public bool IsValid => Fruit.IsValid;

        public static DevilFruitViewData None => default;
    }

    /// <summary>What a panel needs to draw a card.</summary>
    public readonly struct CardViewData
    {
        public CardViewData(DefinitionId card, LocalizationKey nameKey,
            LocalizationKey descriptionKey, AssetRef icon, DefinitionId rarity,
            DefinitionId sourceMonster, int modifierCount, int effectCount,
            EquipmentSlot allowedSlot, EquipmentCategory allowedCategory, bool isSocketed,
            int socketIndex)
        {
            Card = card;
            NameKey = nameKey;
            DescriptionKey = descriptionKey;
            Icon = icon;
            Rarity = rarity;
            SourceMonster = sourceMonster;
            ModifierCount = modifierCount;
            EffectCount = effectCount;
            AllowedSlot = allowedSlot;
            AllowedCategory = allowedCategory;
            IsSocketed = isSocketed;
            SocketIndex = socketIndex;
        }

        public DefinitionId Card { get; }

        public LocalizationKey NameKey { get; }

        public LocalizationKey DescriptionKey { get; }

        public AssetRef Icon { get; }

        public DefinitionId Rarity { get; }

        /// <summary>Reference to the <see cref="MonsterDefinition"/> it comes from.</summary>
        public DefinitionId SourceMonster { get; }

        public int ModifierCount { get; }

        /// <summary>How many conditional effects it carries. See <see cref="CardEffect"/>.</summary>
        public int EffectCount { get; }

        /// <summary><see cref="EquipmentSlot.None"/> means it fits any slot.</summary>
        public EquipmentSlot AllowedSlot { get; }

        /// <summary><see cref="EquipmentCategory.None"/> means it fits any category.</summary>
        public EquipmentCategory AllowedCategory { get; }

        public bool IsSocketed { get; }

        /// <summary>Which socket it sits in, or minus one.</summary>
        public int SocketIndex { get; }

        public bool IsValid => Card.IsValid;

        public static CardViewData None => default;
    }

    /// <summary>What a panel needs to draw a pet.</summary>
    /// <remarks>
    /// <see cref="ExperienceForNext"/> is the total the next level needs, not the remainder,
    /// because that is the number the service computes and a view that subtracted for itself
    /// would be doing progression arithmetic. Zero means there is no next level, which a bar
    /// shows as full rather than dividing by.
    /// </remarks>
    public readonly struct PetViewData
    {
        public PetViewData(DefinitionId pet, InstanceId instance, LocalizationKey nameKey,
            AssetRef icon, int level, int experience, int experienceForNext, int maxLevel,
            bool canEvolve, int evolutionLevelRequired, DefinitionId grantedBuff, bool isSummoned,
            bool isAuraForm, PetFollowBehavior followBehavior)
        {
            Pet = pet;
            Instance = instance;
            NameKey = nameKey;
            Icon = icon;
            Level = level;
            Experience = experience;
            ExperienceForNext = experienceForNext;
            MaxLevel = maxLevel;
            CanEvolve = canEvolve;
            EvolutionLevelRequired = evolutionLevelRequired;
            GrantedBuff = grantedBuff;
            IsSummoned = isSummoned;
            IsAuraForm = isAuraForm;
            FollowBehavior = followBehavior;
        }

        public DefinitionId Pet { get; }

        public InstanceId Instance { get; }

        public LocalizationKey NameKey { get; }

        public AssetRef Icon { get; }

        public int Level { get; }

        public int Experience { get; }

        /// <summary>Total experience the next level needs. Zero at the ceiling.</summary>
        public int ExperienceForNext { get; }

        public int MaxLevel { get; }

        /// <summary>
        /// Whether the service would accept an evolution right now.
        /// </summary>
        /// <remarks>Advisory. <c>PetService.CanEvolve</c> is asked for this rather than the
        /// rules being re-derived, and it is asked again when the player actually presses
        /// the button, so a stale hint cannot authorise anything.</remarks>
        public bool CanEvolve { get; }

        /// <summary>The level the next stage wants, for a "reach level 10" line. Zero if none.</summary>
        public int EvolutionLevelRequired { get; }

        /// <summary>Reference to the <see cref="StatusEffectDefinition"/> it grants its owner.</summary>
        public DefinitionId GrantedBuff { get; }

        public bool IsSummoned { get; }

        /// <summary>Whether it is an aura on its owner rather than a follower.</summary>
        public bool IsAuraForm { get; }

        public PetFollowBehavior FollowBehavior { get; }

        public bool IsValid => Pet.IsValid;

        public static PetViewData None => default;
    }
}
