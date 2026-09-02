using System;
using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>How a monster moves through the world.</summary>
    public enum MonsterMovementType
    {
        Stationary = 0,
        Ground = 1,
        Flying = 2,
        Swimming = 3
    }

    /// <summary>When a monster decides to engage.</summary>
    public enum MonsterAggressionType
    {
        Passive = 0,
        Defensive = 1,
        Aggressive = 2,
        AssistOnly = 3
    }

    /// <summary>Encounter tier, driving UI treatment and reward scale.</summary>
    public enum MonsterRank
    {
        Normal = 0,
        Elite = 1,
        MiniBoss = 2,
        Boss = 3,
        WorldBoss = 4
    }

    /// <summary>Authored respawn parameters. Pure data; no timer runs here.</summary>
    [Serializable]
    public struct RespawnSettings
    {
        [SerializeField] private float _respawnDelaySeconds;
        [SerializeField] private int _maxAliveInArea;

        public RespawnSettings(float respawnDelaySeconds, int maxAliveInArea)
        {
            _respawnDelaySeconds = respawnDelaySeconds;
            _maxAliveInArea = maxAliveInArea;
        }

        public float RespawnDelaySeconds => _respawnDelaySeconds;

        public int MaxAliveInArea => _maxAliveInArea;
    }

    /// <summary>
    /// What a monster is: static content shared by every spawn of it.
    /// </summary>
    /// <remarks>
    /// No spawning, AI, aggro, pathing, combat or loot rolling. A living monster in the
    /// world, with current health and threat table, is server-owned runtime state.
    /// </remarks>
    public sealed class MonsterDefinition : GameDefinition
    {
        [SerializeField] private LocalizationKey _nameKey;
        [SerializeField] private int _level = 1;
        [SerializeField] private MonsterRank _rank = MonsterRank.Normal;

        [SerializeField] private StatValue[] _baseStats = new StatValue[0];
        [SerializeField] private ElementType _element = ElementType.Neutral;

        [SerializeField] private MonsterMovementType _movementType = MonsterMovementType.Ground;
        [SerializeField] private MonsterAggressionType _aggressionType = MonsterAggressionType.Passive;

        [SerializeField] private AssetRef _model;
        [SerializeField] private AssetRef _animatorController;

        [SerializeField] private DefinitionId _lootTable;
        [SerializeField] private int _experienceReward;
        [SerializeField] private int _currencyReward;

        [SerializeField] private RespawnSettings _respawn;

        public LocalizationKey NameKey => _nameKey;

        public int Level => _level;

        public MonsterRank Rank => _rank;

        public StatValue[] BaseStats => _baseStats;

        public ElementType Element => _element;

        public MonsterMovementType MovementType => _movementType;

        public MonsterAggressionType AggressionType => _aggressionType;

        public AssetRef Model => _model;

        public AssetRef AnimatorController => _animatorController;

        /// <summary>Reference to a loot table definition. Rolling is a Gameplay concern.</summary>
        public DefinitionId LootTable => _lootTable;

        public int ExperienceReward => _experienceReward;

        public int CurrencyReward => _currencyReward;

        public RespawnSettings Respawn => _respawn;
    }
}
