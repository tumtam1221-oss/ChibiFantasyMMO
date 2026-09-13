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

        [Tooltip("How close the target must still be when the blow lands, in metres. The "
            + "server validates the hit at that moment against this. Zero or less means it "
            + "cannot attack.")]
        [SerializeField] private float _attackRange = 1.5f;

        [Tooltip("How close it gets before it commits to a swing, in metres. Zero uses the "
            + "attack range. Smaller than the attack range for a monster whose attack "
            + "carries it forward: it closes in, commits, and the lunge covers the rest.")]
        [SerializeField] private float _attackStartRange;

        [Tooltip("Seconds between attacks. Zero or less means as fast as combat allows.")]
        [SerializeField] private float _attackCooldownSeconds = 2f;

        [Tooltip("How far it will chase before giving up and going home. Zero means no leash.")]
        [SerializeField] private float _leashRange;

        [Tooltip("World units per second.")]
        [SerializeField] private float _moveSpeed = 2f;

        [Tooltip("Metres per second while strolling near home. Zero uses Move Speed.")]
        [SerializeField] private float _wanderSpeed;

        [Tooltip("Seconds of anticipation before a swing lands. Zero strikes instantly.")]
        [SerializeField] private float _attackWindupSeconds;

        [Tooltip("Seconds committed after a swing, during which it neither moves nor swings.")]
        [SerializeField] private float _attackRecoverySeconds;

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

        /// <summary>
        /// How close the target must be when the blow lands, in metres.
        /// </summary>
        /// <remarks>The impact validation range. <see cref="AttackStartRange"/> is where it
        /// commits; this is where it can still connect once the wind-up has run. The
        /// difference between the two is what a forward lunge is allowed to cover.</remarks>
        public float AttackRange => _attackRange;

        /// <summary>
        /// How close it must get before committing to a swing, in metres.
        /// </summary>
        /// <remarks>Authored separately from <see cref="AttackRange"/> because the two answer
        /// different questions. A training slime that begins its wind-up three body widths
        /// from the player, then jumps, reads as attacking thin air; one that closes to
        /// almost touching, jumps, and lands on the player reads as a bite. Unset, it is the
        /// attack range, which is what every monster authored before this field did.</remarks>
        public float AttackStartRange => _attackStartRange > 0f ? _attackStartRange : _attackRange;

        /// <summary>Seconds between attacks. Zero or less defers to combat's own pacing.</summary>
        public float AttackCooldownSeconds => _attackCooldownSeconds;

        /// <summary>
        /// How long it winds up before a swing lands.
        /// </summary>
        /// <remarks>
        /// <b>The pause that makes an attack readable.</b> Without one a monster that walks
        /// into reach strikes on the same tick it arrives, which a player experiences as
        /// damage arriving from nowhere -- there was nothing to see coming. Authored rather
        /// than constant because a boss telegraphing for a second and a slime bobbing for a
        /// fifth of one is a balance decision, not a code one.
        ///
        /// Server-side: this delays the damage, not merely the animation. A windup that only
        /// existed in the presentation would be a lie the client tells about when it was
        /// hit.
        /// </remarks>
        public float AttackWindupSeconds => _attackWindupSeconds < 0f ? 0f : _attackWindupSeconds;

        /// <summary>
        /// How long it is committed after a swing, before it can do anything else.
        /// </summary>
        /// <remarks>The other half of a readable rhythm: a monster that snaps back to
        /// chasing the instant it has swung reads as a machine. During recovery it neither
        /// moves nor swings again.</remarks>
        public float AttackRecoverySeconds =>
            _attackRecoverySeconds < 0f ? 0f : _attackRecoverySeconds;

        /// <summary>
        /// How far from home it will chase before giving up.
        /// </summary>
        /// <remarks>Zero means no leash. Measured from the spawn point rather than from the
        /// target, so a monster cannot be walked across a map by a player retreating in a
        /// straight line.</remarks>
        public float LeashRange => _leashRange;

        public float MoveSpeed => _moveSpeed;

        /// <summary>
        /// How fast it strolls when it has nothing to fight.
        /// </summary>
        /// <remarks>
        /// <b>Slower than a chase, on purpose.</b> A creature that mills about its camp at
        /// the same speed it hunts reads as agitated rather than idle, and a whole camp of
        /// them reads as a swarm. Falls back to <see cref="MoveSpeed"/> when a monster does
        /// not author one, so existing content behaves exactly as it did.
        /// </remarks>
        public float WanderSpeed => _wanderSpeed > 0f ? _wanderSpeed : _moveSpeed;

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
