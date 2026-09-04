using System.Collections.Generic;
using ChibiFantasy.Core;

namespace ChibiFantasy.Contracts
{
    /// <summary>Why a configured spawn point or AI override was refused.</summary>
    /// <remarks>
    /// Named individually because an operator has to be able to fix the row. "Invalid
    /// configuration" tells somebody a nest is empty; "initial_spawn_count exceeds
    /// max_alive" tells them which number to change.
    /// </remarks>
    public enum SpawnConfigurationRejection
    {
        None = 0,

        /// <summary>No map, no monster, or nothing to configure.</summary>
        MissingContext = 1,

        /// <summary>The map is not authored content on this server.</summary>
        UnknownMap = 2,

        /// <summary>The monster is not authored content on this server.</summary>
        UnknownMonster = 3,

        /// <summary>A nest that can hold nobody is not a nest.</summary>
        MaxAliveNotPositive = 4,

        /// <summary>A negative initial population is meaningless.</summary>
        InitialCountNegative = 5,

        /// <summary>More monsters were asked for than the nest can hold.</summary>
        InitialCountExceedsMaxAlive = 6,

        /// <summary>A negative radius.</summary>
        RadiusNegative = 7,

        /// <summary>A negative respawn delay.</summary>
        RespawnNegative = 8,

        /// <summary>A coordinate that is NaN or an infinity.</summary>
        PositionNotFinite = 9,

        /// <summary>An aggression value that is not a known behaviour.</summary>
        UnknownAggression = 10,

        /// <summary>An AI range, cooldown or speed below zero, or not a number.</summary>
        AiValueInvalid = 11
    }

    /// <summary>
    /// One configured nest, as an operator described it.
    /// </summary>
    /// <remarks>
    /// <b>A carrier, not a rule.</b> It holds what the database said and enforces
    /// nothing; <see cref="SpawnConfigurationValidator"/> decides whether it may become a
    /// live spawn point. Keeping those apart is what lets an invalid row be reported with
    /// a reason instead of throwing halfway through a load.
    ///
    /// Shaped to match Phase 10's <c>MonsterSpawnPoint</c> field for field, because that
    /// is the type the runtime consumes. A different shape would need a translation layer,
    /// and a translation layer is where spawn rules quietly diverge.
    /// </remarks>
    public readonly struct MonsterSpawnConfiguration
    {
        public MonsterSpawnConfiguration(string spawnPointId, DefinitionId map,
            DefinitionId monster, float x, float y, float z, float radius,
            int initialCount, int maxAlive, float respawnSeconds, string groupId = null)
        {
            SpawnPointId = spawnPointId ?? string.Empty;
            Map = map;
            Monster = monster;
            X = x;
            Y = y;
            Z = z;
            Radius = radius;
            InitialCount = initialCount;
            MaxAlive = maxAlive;
            RespawnSeconds = respawnSeconds;
            GroupId = groupId ?? string.Empty;
        }

        /// <summary>The row's own id, so a reload can tell one nest from another.</summary>
        public string SpawnPointId { get; }

        public DefinitionId Map { get; }

        public DefinitionId Monster { get; }

        public float X { get; }

        public float Y { get; }

        public float Z { get; }

        public float Radius { get; }

        public int InitialCount { get; }

        public int MaxAlive { get; }

        public float RespawnSeconds { get; }

        /// <summary>Optional grouping. Empty when the nest belongs to none.</summary>
        public string GroupId { get; }

        public override string ToString()
        {
            return SpawnPointId + ": " + Monster + " x" + MaxAlive + " on " + Map;
        }
    }

    /// <summary>
    /// An AI override for one monster type.
    /// </summary>
    /// <remarks>
    /// <b>Absent means "use what the monster was authored with".</b> Every value is
    /// optional, so a row that only makes Goblins defensive does not have to restate five
    /// numbers nobody intended to change -- and this never becomes a second copy of
    /// <c>MonsterDefinition</c>.
    ///
    /// A negative carries absence across the wire, because JSON numbers have no null the
    /// reader distinguishes and every real value here is non-negative.
    /// </remarks>
    public readonly struct MonsterAiConfiguration
    {
        public MonsterAiConfiguration(DefinitionId monster, int aggression = -1,
            float detectionRange = -1f, float chaseRange = -1f, float attackRange = -1f,
            float attackCooldown = -1f, float moveSpeed = -1f)
        {
            Monster = monster;
            Aggression = aggression;
            DetectionRange = detectionRange;
            ChaseRange = chaseRange;
            AttackRange = attackRange;
            AttackCooldown = attackCooldown;
            MoveSpeed = moveSpeed;
        }

        public DefinitionId Monster { get; }

        /// <summary>Phase 10 <c>MonsterAggressionType</c> as an int. Negative for absent.</summary>
        public int Aggression { get; }

        public float DetectionRange { get; }

        /// <summary>
        /// How far it will follow before giving up.
        /// </summary>
        /// <remarks>Maps onto the monster's authored <c>LeashRange</c>, which is the bound
        /// this project has used since Phase 10. Named chase range because that is what an
        /// operator calls it; introducing a second bound would give a monster two answers
        /// to the same question.</remarks>
        public float ChaseRange { get; }

        public float AttackRange { get; }

        public float AttackCooldown { get; }

        public float MoveSpeed { get; }

        public bool HasAggression => Aggression >= 0;

        public bool HasDetectionRange => DetectionRange >= 0f;

        public bool HasChaseRange => ChaseRange >= 0f;

        public bool HasAttackRange => AttackRange >= 0f;

        public bool HasAttackCooldown => AttackCooldown >= 0f;

        public bool HasMoveSpeed => MoveSpeed >= 0f;

        /// <summary>Whether this row changes anything at all.</summary>
        public bool OverridesAnything => HasAggression || HasDetectionRange || HasChaseRange
            || HasAttackRange || HasAttackCooldown || HasMoveSpeed;

        public override string ToString()
        {
            return Monster + (HasAggression ? " aggression " + Aggression : " (no override)");
        }
    }

    /// <summary>What one configuration row was judged to be.</summary>
    public readonly struct SpawnConfigurationVerdict
    {
        private SpawnConfigurationVerdict(bool accepted, SpawnConfigurationRejection reason)
        {
            IsAccepted = accepted;
            Reason = reason;
        }

        public bool IsAccepted { get; }

        public SpawnConfigurationRejection Reason { get; }

        public static SpawnConfigurationVerdict Accepted =>
            new SpawnConfigurationVerdict(true, SpawnConfigurationRejection.None);

        public static SpawnConfigurationVerdict Refused(SpawnConfigurationRejection reason)
        {
            return new SpawnConfigurationVerdict(false, reason);
        }

        public override string ToString()
        {
            return IsAccepted ? "accepted" : "refused: " + Reason;
        }
    }

    /// <summary>Everything one map's configuration turned out to be.</summary>
    public sealed class MapSpawnConfiguration
    {
        public MapSpawnConfiguration(DefinitionId map,
            IReadOnlyList<MonsterSpawnConfiguration> spawnPoints,
            IReadOnlyList<MonsterAiConfiguration> aiConfigurations)
        {
            Map = map;
            SpawnPoints = spawnPoints ?? System.Array.Empty<MonsterSpawnConfiguration>();
            AiConfigurations = aiConfigurations
                ?? System.Array.Empty<MonsterAiConfiguration>();
        }

        public DefinitionId Map { get; }

        public IReadOnlyList<MonsterSpawnConfiguration> SpawnPoints { get; }

        public IReadOnlyList<MonsterAiConfiguration> AiConfigurations { get; }

        public override string ToString()
        {
            return Map + ": " + SpawnPoints.Count + " nests, "
                + AiConfigurations.Count + " ai overrides";
        }
    }

    /// <summary>
    /// Where a world server reads its spawn configuration.
    /// </summary>
    /// <remarks>
    /// The same shape as every other authority seam here: a world server implemented
    /// against it names no HTTP, no PHP and no SQL. It is read-only by construction --
    /// there is no write method, because configuration is an operator's to change and
    /// nothing a player can reach may mutate it.
    /// </remarks>
    public interface IMonsterSpawnConfigurationSource
    {
        /// <summary>Reads one map's configuration, or null if it could not be read.</summary>
        MapSpawnConfiguration Load(DefinitionId map);
    }
}
