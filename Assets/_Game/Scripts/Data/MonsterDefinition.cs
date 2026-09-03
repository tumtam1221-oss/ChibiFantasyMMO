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

        [Header("Engagement")]
        [Tooltip("How far it notices a target. Zero or less means it never notices one.")]
        [SerializeField] private float _detectionRange;

        [Tooltip("How close it must be to strike. Zero or less means it cannot attack.")]
        [SerializeField] private float _attackRange = 1.5f;

        [Tooltip("Seconds between attacks. Zero or less means as fast as combat allows.")]
        [SerializeField] private float _attackCooldownSeconds = 2f;

        [Tooltip("How far it will chase before giving up and going home. Zero means no leash.")]
        [SerializeField] private float _leashRange;

        [Tooltip("World units per second.")]
        [SerializeField] private float _moveSpeed = 2f;

        [Tooltip("Maps it may be spawned on. Empty means unrestricted.")]
        [SerializeField] private DefinitionId[] _allowedMaps = new DefinitionId[0];

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

        /// <summary>
        /// How far it notices a target.
        /// </summary>
        /// <remarks>Zero or less means it never notices one, which is the correct reading
        /// for a training dummy and for anything authored before this field existed.
        /// Whether it <em>acts</em> on noticing is <see cref="AggressionType"/>.</remarks>
        public float DetectionRange => _detectionRange;

        /// <summary>How close it must be to strike.</summary>
        public float AttackRange => _attackRange;

        /// <summary>Seconds between attacks. Zero or less defers to combat's own pacing.</summary>
        public float AttackCooldownSeconds => _attackCooldownSeconds;

        /// <summary>
        /// How far from home it will chase before giving up.
        /// </summary>
        /// <remarks>Zero means no leash. Measured from the spawn point rather than from the
        /// target, so a monster cannot be walked across a map by a player retreating in a
        /// straight line.</remarks>
        public float LeashRange => _leashRange;

        public float MoveSpeed => _moveSpeed;

        /// <summary>References to <see cref="MapDefinition"/>. Empty means unrestricted.</summary>
        public DefinitionId[] AllowedMaps => _allowedMaps ?? NoIds;

        /// <summary>
        /// Reads one authored base stat.
        /// </summary>
        /// <remarks>
        /// Absent is not zero: a stat nobody authored has no value, and the caller decides
        /// what that means -- the same contract <c>DerivedStatsResult.TryGet</c> and
        /// <see cref="ICombatant"/>'s stat lookup already use. Monsters therefore need no
        /// derived-stat pipeline of their own; their combat figures are authored directly.
        /// </remarks>
        public bool TryGetStat(DefinitionId stat, out int value)
        {
            StatValue[] stats = _baseStats;

            if (stats != null && stat.IsValid)
            {
                for (int i = 0; i < stats.Length; i++)
                {
                    if (stats[i].Stat != stat) continue;

                    float raw = stats[i].Value;
                    value = raw > int.MaxValue ? int.MaxValue
                        : raw < int.MinValue ? int.MinValue : (int)raw;
                    return true;
                }
            }

            value = 0;
            return false;
        }

        private static readonly DefinitionId[] NoIds = new DefinitionId[0];
    }
}
