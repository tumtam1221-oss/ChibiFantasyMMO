using System;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// A living monster in the world.
    /// </summary>
    /// <remarks>
    /// <b>One model for every monster.</b> There is no goblin type and no boss type. What a
    /// monster is comes from its <see cref="MonsterDefinition"/>, read at runtime; what
    /// <em>this</em> one currently is -- where it stands, how hurt it is, what it is looking
    /// at -- is here. A subclass per monster would put content in code, and a boss is a
    /// definition with a different rank.
    ///
    /// <b>Health is a number, not a state of being.</b> Aliveness is derived from it, the
    /// same rule <see cref="CharacterResourceState"/> and
    /// <see cref="CombatantExtensions.IsAlive"/> already keep, so there is no dead flag to
    /// fall out of step with the figure.
    ///
    /// <b>Rewards are claimed once.</b> <see cref="TryClaimDefeat"/> is the guard: a monster
    /// can be reduced to zero health by several sources in the same frame, and the loot,
    /// the experience and the quest credit must be handed out for the first of them only.
    /// The flag lives here rather than in each of the three systems, because three
    /// independent guards is three chances to disagree.
    ///
    /// Runtime, not persistent: a server owns this and it does not survive a restart.
    /// </remarks>
    public sealed class MonsterRuntimeState : IRuntimeState
    {
        private readonly MonsterDefinition _definition;
        private int _currentHealth;
        private Revision _revision;
        private bool _defeatClaimed;

        public MonsterRuntimeState(InstanceId instanceId, MonsterDefinition definition,
            CombatPosition spawnPosition, int maxHealth, CombatTeam team)
        {
            if (!instanceId.IsValid)
            {
                throw new ArgumentException("A monster requires a valid identity.", nameof(instanceId));
            }

            _definition = definition ?? throw new ArgumentNullException(nameof(definition));

            InstanceId = instanceId;
            SpawnPosition = spawnPosition;
            Position = spawnPosition;
            Team = team;

            MaxHealth = maxHealth < 0 ? 0 : maxHealth;
            _currentHealth = MaxHealth;
            _revision = Revision.Initial;
        }

        /// <summary>Runtime identity, the same shape a character combatant uses.</summary>
        public InstanceId InstanceId { get; }

        /// <summary>What it is. Every authored figure is read through this.</summary>
        public DefinitionId DefinitionId => _definition.Id;

        public MonsterDefinition Definition => _definition;

        /// <summary>The authored level. Monsters do not gain levels.</summary>
        public int Level => _definition.Level;

        public CombatTeam Team { get; set; }

        /// <summary>Where it stands. Written by whatever moves it.</summary>
        public CombatPosition Position { get; set; }

        /// <summary>Where it belongs. What a leash and a return are measured against.</summary>
        public CombatPosition SpawnPosition { get; }

        public int CurrentHealth => _currentHealth;

        /// <summary>
        /// The ceiling, supplied at spawn.
        /// </summary>
        /// <remarks>Passed in rather than read from the definition here, because which
        /// authored stat means "maximum health" is content and the caller is the one that
        /// knows its id. See <see cref="MonsterSpawnService"/>.</remarks>
        public int MaxHealth { get; }

        public bool IsAlive => _currentHealth > 0;

        /// <summary>Who it is currently fighting. Cleared when that target stops being valid.</summary>
        public InstanceId TargetId { get; private set; }

        public bool HasTarget => TargetId.IsValid;

        public Revision Revision => _revision;

        /// <summary>
        /// Moves health by a delta, clamped into range.
        /// </summary>
        /// <remarks>Mirrors <see cref="CharacterResourceState.ChangeHealth"/> deliberately,
        /// including that a change of nothing does not advance the revision -- a monster
        /// taking zero damage must not look like a state change to anything watching.</remarks>
        public void ApplyHealthDelta(long delta)
        {
            long sum = _currentHealth + delta;

            int clamped = sum <= 0 ? 0
                : sum >= MaxHealth ? MaxHealth
                : (int)sum;

            if (clamped == _currentHealth) return;

            _currentHealth = clamped;
            _revision = _revision.Next();
        }

        /// <summary>Sets health directly, clamped. For spawning and for server correction.</summary>
        public void SetHealth(int value)
        {
            ApplyHealthDelta((long)value - _currentHealth);
        }

        /// <summary>Points it at something.</summary>
        public void SetTarget(InstanceId targetId)
        {
            if (TargetId == targetId) return;

            TargetId = targetId;
            _revision = _revision.Next();
        }

        public void ClearTarget()
        {
            SetTarget(InstanceId.None);
        }

        /// <summary>
        /// Claims the one defeat this monster can be defeated.
        /// </summary>
        /// <remarks>
        /// Returns true exactly once, and only when it is actually dead. Everything paid out
        /// for a kill -- experience, loot, quest credit -- hangs off this single answer, so
        /// two killing blows landing together cannot pay twice and a caller that asks again
        /// gets nothing.
        ///
        /// Claiming is itself a state change, so it advances the revision.
        /// </remarks>
        public bool TryClaimDefeat()
        {
            if (IsAlive || _defeatClaimed) return false;

            _defeatClaimed = true;
            _revision = _revision.Next();
            return true;
        }

        /// <summary>Whether the defeat has already been paid out.</summary>
        public bool IsDefeatClaimed => _defeatClaimed;

        /// <summary>
        /// Returns it to full health at its spawn point, ready to live again.
        /// </summary>
        /// <remarks>Clears the defeat claim, because this is a new life and it may be
        /// defeated again. Respawn timing is <see cref="MonsterSpawnService"/>'s
        /// business.</remarks>
        public void Respawn()
        {
            Position = SpawnPosition;
            _currentHealth = MaxHealth;
            _defeatClaimed = false;
            TargetId = InstanceId.None;
            _revision = _revision.Next();
        }

        /// <summary>Distance from home, for leash rules.</summary>
        public float SqrDistanceFromSpawn => Position.SqrDistanceTo(SpawnPosition);

        public override string ToString()
        {
            return DefinitionId + " [" + InstanceId + "] " + _currentHealth + "/" + MaxHealth;
        }
    }
}
