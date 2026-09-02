using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>Broad nature of a status effect.</summary>
    public enum StatusEffectCategory
    {
        None = 0,
        Buff = 1,
        Debuff = 2,
        DamageOverTime = 3,
        HealOverTime = 4,
        Control = 5
    }

    /// <summary>What happens when an effect is applied while already present.</summary>
    public enum StatusEffectStackBehavior
    {
        RefreshDuration = 0,
        AddStack = 1,
        Ignore = 2,
        ReplaceIfStronger = 3
    }

    /// <summary>Loss-of-control category, if any.</summary>
    /// <remarks>Closed technical category: each value suppresses a specific set of player
    /// actions and must be handled explicitly by the server.</remarks>
    public enum ControlEffectType
    {
        None = 0,
        Stun = 1,
        Silence = 2,
        Root = 3,
        Sleep = 4,
        Fear = 5,
        Slow = 6,
        Disarm = 7
    }

    /// <summary>
    /// What a status effect <em>is</em>.
    /// </summary>
    /// <remarks>
    /// No ticking, application, expiry or dispel logic lives here. An effect actually
    /// applied to a character, with its remaining duration and stack count, is runtime
    /// state owned by the server.
    /// </remarks>
    public sealed class StatusEffectDefinition : GameDefinition
    {
        [SerializeField] private LocalizationKey _nameKey;
        [SerializeField] private LocalizationKey _descriptionKey;
        [SerializeField] private AssetRef _icon;

        [SerializeField] private StatusEffectCategory _category = StatusEffectCategory.None;
        [SerializeField] private ControlEffectType _controlEffect = ControlEffectType.None;

        [SerializeField] private float _durationSeconds;
        [SerializeField] private StatusEffectStackBehavior _stackBehavior = StatusEffectStackBehavior.RefreshDuration;
        [SerializeField] private int _maxStacks = 1;

        [SerializeField] private StatModifier[] _statModifiers = new StatModifier[0];
        [SerializeField] private AssetRef _visualEffect;

        public LocalizationKey NameKey => _nameKey;

        public LocalizationKey DescriptionKey => _descriptionKey;

        public AssetRef Icon => _icon;

        public StatusEffectCategory Category => _category;

        public ControlEffectType ControlEffect => _controlEffect;

        /// <summary>Authored duration. Zero or less is treated as indefinite by convention;
        /// interpreting that is a Gameplay concern.</summary>
        public float DurationSeconds => _durationSeconds;

        public StatusEffectStackBehavior StackBehavior => _stackBehavior;

        public int MaxStacks => _maxStacks;

        public StatModifier[] StatModifiers => _statModifiers;

        public AssetRef VisualEffect => _visualEffect;
    }
}
