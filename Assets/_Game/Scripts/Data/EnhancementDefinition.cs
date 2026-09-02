using System;
using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>What happens to the item when an enhancement attempt fails.</summary>
    /// <remarks>Closed technical category: each outcome is a distinct branch the server
    /// must implement explicitly.</remarks>
    public enum EnhancementFailureBehavior
    {
        None = 0,
        LoseMaterials = 1,
        DegradeLevel = 2,
        ResetToZero = 3,
        DestroyItem = 4
    }

    /// <summary>
    /// The authored rule for advancing from one enhancement level to the next.
    /// </summary>
    /// <remarks>
    /// Pure data. No roll is performed here; the server owns the outcome, and the schema
    /// only states the odds, costs and consequences.
    /// </remarks>
    [Serializable]
    public struct EnhancementStep
    {
        [SerializeField] private int _fromLevel;
        [SerializeField] private float _successChance;
        [SerializeField] private EnhancementFailureBehavior _failureBehavior;
        [SerializeField] private StatModifier[] _grantedModifiers;
        [SerializeField] private DefinitionId _materialItem;
        [SerializeField] private int _materialAmount;
        [SerializeField] private int _currencyCost;

        /// <summary>Level being advanced from. The step produces level FromLevel + 1.</summary>
        public int FromLevel => _fromLevel;

        /// <summary>Authored success probability in the range zero to one.</summary>
        public float SuccessChance => _successChance;

        public EnhancementFailureBehavior FailureBehavior => _failureBehavior;

        /// <summary>Modifiers added to the item once this step succeeds.</summary>
        public StatModifier[] GrantedModifiers => _grantedModifiers;

        /// <summary>Reference to the required <see cref="ItemDefinition"/> material.</summary>
        public DefinitionId MaterialItem => _materialItem;

        public int MaterialAmount => _materialAmount;

        public int CurrencyCost => _currencyCost;
    }

    /// <summary>
    /// What an enhancement track is: the rules a class of equipment upgrades by.
    /// </summary>
    /// <remarks>
    /// Referenced by <see cref="EquipmentDefinition.EnhancementRule"/>, so several items
    /// can share one track and new tracks can be authored without code changes.
    ///
    /// Executing an enhancement, rolling against <see cref="EnhancementStep.SuccessChance"/>
    /// and consuming materials are all server-authoritative Gameplay concerns. An item's
    /// current enhancement level is runtime state.
    /// </remarks>
    public sealed class EnhancementDefinition : GameDefinition
    {
        [SerializeField] private LocalizationKey _nameKey;
        [SerializeField] private EquipmentCategory _allowedCategory = EquipmentCategory.None;
        [SerializeField] private EquipmentSubtype[] _allowedSubtypes = new EquipmentSubtype[0];

        [SerializeField] private int _minLevel;
        [SerializeField] private int _maxLevel;

        [SerializeField] private EnhancementStep[] _steps = new EnhancementStep[0];

        public LocalizationKey NameKey => _nameKey;

        /// <summary>Equipment category this track applies to. None means unrestricted.</summary>
        public EquipmentCategory AllowedCategory => _allowedCategory;

        /// <summary>Subtype restriction. Empty means unrestricted.</summary>
        public EquipmentSubtype[] AllowedSubtypes => _allowedSubtypes;

        public int MinLevel => _minLevel;

        public int MaxLevel => _maxLevel;

        /// <summary>Authored steps, one per level transition.</summary>
        public EnhancementStep[] Steps => _steps;
    }
}
