using System;
using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// One authored place monsters come from.
    /// </summary>
    /// <remarks>
    /// <b>It references a definition, never a class.</b> A spawn point knows a
    /// <see cref="DefinitionId"/> and nothing about what that monster is, so authoring a
    /// new encounter is content. A boss spawn is this with a boss's id and its own
    /// respawn figures.
    ///
    /// Flat and DB-friendly: one row of a future <c>monster_spawn</c> table is a map, a
    /// monster id, a position, a radius and two limits.
    /// </remarks>
    public readonly struct MonsterSpawnPoint
    {
        public MonsterSpawnPoint(DefinitionId monster, CombatPosition position,
            float radius = 0f, int maxAlive = 1, float respawnDelaySeconds = 0f,
            DefinitionId map = default)
        {
            Monster = monster;
            Position = position;
            Radius = radius < 0f ? 0f : radius;
            MaxAlive = maxAlive < 1 ? 1 : maxAlive;
            RespawnDelaySeconds = respawnDelaySeconds < 0f ? 0f : respawnDelaySeconds;
            Map = map;
        }

        /// <summary>Reference to a <see cref="MonsterDefinition"/>.</summary>
        public DefinitionId Monster { get; }

        public CombatPosition Position { get; }

        /// <summary>How far from the point a spawn may appear. Zero means exactly on it.</summary>
        public float Radius { get; }

        /// <summary>How many of this monster may be alive from this point at once.</summary>
        public int MaxAlive { get; }

        /// <summary>
        /// Seconds before a defeated one comes back.
        /// </summary>
        /// <remarks>Zero or less defers to the monster's own
        /// <see cref="RespawnSettings.RespawnDelaySeconds"/>, so a point need only override
        /// what it actually wants to differ.</remarks>
        public float RespawnDelaySeconds { get; }

        /// <summary>Reference to a <see cref="MapDefinition"/>. Checked against the monster's
        /// authored restrictions.</summary>
        public DefinitionId Map { get; }

        public bool IsValid => Monster.IsValid;
    }

    /// <summary>Why a spawn was refused.</summary>
    public enum SpawnRejection
    {
        None = 0,

        /// <summary>No registry was supplied.</summary>
        MissingContext = 1,

        /// <summary>The point names no monster, or it could not be resolved.</summary>
        UnknownMonster = 2,

        /// <summary>The monster is not authored for this map.</summary>
        MapNotAllowed = 3,

        /// <summary>As many are alive from this point as it allows.</summary>
        AtCapacity = 4,

        /// <summary>The authored maximum health is not a number a monster can live at.</summary>
        InvalidHealth = 5
    }

    /// <summary>
    /// Spawns monsters, and decides when a defeated one may come back.
    /// </summary>
    /// <remarks>
    /// <b>Caller-supplied time.</b> Nothing here reads a clock. Elapsed seconds arrive as
    /// an argument, the same contract <c>SkillCooldownState</c> and
    /// <c>AttackStateMachine</c> already keep, which is what keeps this assembly
    /// engine-free and respawn behaviour reproducible in a test.
    ///
    /// <b>It tracks what it spawned.</b> Capacity is a rule about a place, so the place has
    /// to know its own population. A spawner that counted the world would need to know
    /// about the world.
    /// </remarks>
    public sealed class MonsterSpawnService
    {
        private readonly List<MonsterRuntimeState> _alive = new List<MonsterRuntimeState>();
        private readonly List<float> _pendingRespawns = new List<float>();

        private readonly MonsterSpawnPoint _point;
        private readonly DefinitionId _maxHealthStat;

        /// <summary>
        /// Creates a spawner for one point.
        /// </summary>
        /// <param name="point">Where and what to spawn.</param>
        /// <param name="maxHealthStat">
        /// Id of the authored stat that means maximum health. Supplied because which stat
        /// that is is content: nothing here knows a stat by name.
        /// </param>
        public MonsterSpawnService(MonsterSpawnPoint point, DefinitionId maxHealthStat)
        {
            _point = point;
            _maxHealthStat = maxHealthStat;
        }

        public MonsterSpawnPoint Point => _point;

        /// <summary>Everything currently alive from this point.</summary>
        public IReadOnlyList<MonsterRuntimeState> Alive => _alive;

        public int AliveCount => _alive.Count;

        /// <summary>How many defeated monsters are waiting to come back.</summary>
        public int PendingRespawnCount => _pendingRespawns.Count;

        /// <summary>The last refusal, for reporting.</summary>
        public SpawnRejection LastRejection { get; private set; }

        /// <summary>
        /// Spawns one, if the point allows it.
        /// </summary>
        /// <returns>The new monster, or null with <see cref="LastRejection"/> set.</returns>
        public MonsterRuntimeState TrySpawn(IDefinitionRegistry<MonsterDefinition> monsters,
            CombatTeam team, CombatPosition? at = null)
        {
            LastRejection = SpawnRejection.None;

            if (monsters == null)
            {
                LastRejection = SpawnRejection.MissingContext;
                return null;
            }

            MonsterDefinition definition;
            if (!_point.Monster.IsValid
                || !monsters.TryGet(_point.Monster, out definition) || definition == null)
            {
                LastRejection = SpawnRejection.UnknownMonster;
                return null;
            }

            if (!IsMapAllowed(definition))
            {
                LastRejection = SpawnRejection.MapNotAllowed;
                return null;
            }

            if (_alive.Count >= _point.MaxAlive)
            {
                LastRejection = SpawnRejection.AtCapacity;
                return null;
            }

            int maxHealth;
            if (!definition.TryGetStat(_maxHealthStat, out maxHealth) || maxHealth <= 0)
            {
                // A monster that cannot hold health would spawn already dead and pay out
                // its loot immediately. Refusing points at the authoring mistake.
                LastRejection = SpawnRejection.InvalidHealth;
                return null;
            }

            var monster = new MonsterRuntimeState(InstanceId.New(), definition,
                at ?? _point.Position, maxHealth, team);

            _alive.Add(monster);
            return monster;
        }

        /// <summary>
        /// Removes the defeated and starts their respawn timers.
        /// </summary>
        /// <remarks>Called after combat has settled. A monster is only retired once its
        /// defeat has been claimed, so a caller cannot sweep away a corpse before its loot
        /// and experience were handed out.</remarks>
        public int RetireDefeated()
        {
            int retired = 0;

            for (int i = _alive.Count - 1; i >= 0; i--)
            {
                MonsterRuntimeState monster = _alive[i];
                if (monster.IsAlive || !monster.IsDefeatClaimed) continue;

                _alive.RemoveAt(i);
                _pendingRespawns.Add(RespawnDelay(monster.Definition));
                retired++;
            }

            return retired;
        }

        /// <summary>
        /// Advances respawn timers and returns how many are due.
        /// </summary>
        /// <param name="deltaSeconds">
        /// Elapsed time, supplied by the caller. Negative or zero advances nothing, so a
        /// paused game cannot respawn anything.
        /// </param>
        public int Tick(float deltaSeconds)
        {
            if (deltaSeconds <= 0f || _pendingRespawns.Count == 0) return 0;

            int due = 0;

            for (int i = _pendingRespawns.Count - 1; i >= 0; i--)
            {
                float remaining = _pendingRespawns[i] - deltaSeconds;

                if (remaining > 0f)
                {
                    _pendingRespawns[i] = remaining;
                    continue;
                }

                _pendingRespawns.RemoveAt(i);
                due++;
            }

            return due;
        }

        /// <summary>Forgets everything, for a shutdown or an area reset.</summary>
        public void Clear()
        {
            _alive.Clear();
            _pendingRespawns.Clear();
        }

        /// <summary>The point's own delay when it set one, otherwise the monster's.</summary>
        private float RespawnDelay(MonsterDefinition definition)
        {
            if (_point.RespawnDelaySeconds > 0f) return _point.RespawnDelaySeconds;

            float authored = definition.Respawn.RespawnDelaySeconds;
            return authored > 0f ? authored : 0f;
        }

        /// <summary>
        /// Whether the monster is authored for the point's map.
        /// </summary>
        /// <remarks>An empty restriction list means unrestricted, and a point with no map
        /// set is not checked at all -- so content authored before maps existed still
        /// spawns.</remarks>
        /// <summary>
        /// Whether this point's map is one the monster is authored for.
        /// </summary>
        /// <remarks>Delegated to <see cref="MonsterSpawnPlacement.IsMapAllowed"/> so the
        /// content pass and the spawner cannot answer the same question differently.</remarks>
        private bool IsMapAllowed(MonsterDefinition definition)
        {
            return MonsterSpawnPlacement.IsMapAllowed(definition, _point.Map);
        }
    }
}
