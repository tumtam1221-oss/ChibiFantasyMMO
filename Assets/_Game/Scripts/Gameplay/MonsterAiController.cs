using System;
using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>What a monster is currently doing.</summary>
    /// <remarks>Closed technical category: each value is a distinct branch with distinct
    /// transitions, and adding one is a code change regardless. Which state a monster is in
    /// is decided from authored ranges, not from its identity.</remarks>
    public enum MonsterAiState
    {
        /// <summary>Standing at home, looking for something.</summary>
        Idle = 0,

        /// <summary>Moving around near home, still looking.</summary>
        Wander = 1,

        /// <summary>Something was noticed. A moment of reaction before pursuit.</summary>
        Detect = 2,

        /// <summary>Closing on a target.</summary>
        Chase = 3,

        /// <summary>In range and striking.</summary>
        Attack = 4,

        /// <summary>Going home, having lost or given up on a target.</summary>
        Return = 5,

        /// <summary>Not standing. Nothing further happens.</summary>
        Dead = 6
    }

    /// <summary>
    /// Drives one monster's behaviour.
    /// </summary>
    /// <remarks>
    /// <b>One controller for every monster.</b> The transitions below are the same for a
    /// rat and for a world boss; what differs is the authored detection range, attack
    /// range, cooldown and leash, all read from the <see cref="MonsterDefinition"/>. A
    /// controller per monster type would put content in code.
    ///
    /// <b>It decides; it does not act.</b> Reaching <see cref="MonsterAiState.Attack"/>
    /// raises <see cref="WantsToAttack"/>, and the caller runs that through the existing
    /// combat runtime. Nothing here touches a health value, applies damage or mutates a
    /// character -- so the AI cannot become a second combat path.
    ///
    /// <b>Caller-supplied time.</b> No clock is read, matching
    /// <c>SkillCooldownState</c> and <c>AttackStateMachine</c>. That keeps the assembly
    /// engine-free and makes a five-second chase reproducible in a test.
    ///
    /// <b>Targeting is deliberately small.</b> Nearest eligible enemy in range, dropped
    /// when it dies, leaves, or the leash runs out. There is no threat table: party hate,
    /// taunts and boss aggro are later systems, and <see cref="SelectTarget"/> is the one
    /// method they will replace.
    /// </remarks>
    public sealed class MonsterAiController
    {
        private readonly MonsterRuntimeState _monster;
        private float _attackCooldownRemaining;
        private float _stateElapsed;

        /// <summary>How long the reaction pause between noticing and pursuing lasts.</summary>
        /// <remarks>A constant rather than authored content: it exists so a monster does not
        /// snap instantly from idle to attacking, which reads as a bug. Content that wants
        /// to tune reaction time would author it, and this becomes the default.</remarks>
        public const float DetectDurationSeconds = 0.3f;

        public MonsterAiController(MonsterRuntimeState monster)
        {
            _monster = monster ?? throw new ArgumentNullException(nameof(monster));
            State = MonsterAiState.Idle;
        }

        public MonsterRuntimeState Monster => _monster;

        public MonsterAiState State { get; private set; }

        /// <summary>
        /// True on the tick the monster wants to strike.
        /// </summary>
        /// <remarks>An intention, not an attack. The caller turns it into a
        /// <see cref="CombatAction"/> through the existing runner, which is what keeps
        /// combat rules in one place.</remarks>
        public bool WantsToAttack { get; private set; }

        /// <summary>Seconds until it may strike again.</summary>
        public float AttackCooldownRemaining => _attackCooldownRemaining;

        /// <summary>How long it has been in the current state.</summary>
        public float StateElapsed => _stateElapsed;

        /// <summary>
        /// Advances one tick.
        /// </summary>
        /// <param name="deltaSeconds">Elapsed time, supplied by the caller.</param>
        /// <param name="candidates">
        /// Everything that could be a target. The caller decides what is worth offering;
        /// this filters by team, life and range.
        /// </param>
        public void Tick(float deltaSeconds, IReadOnlyList<ICombatant> candidates)
        {
            WantsToAttack = false;

            if (deltaSeconds < 0f) deltaSeconds = 0f;

            if (_attackCooldownRemaining > 0f)
            {
                _attackCooldownRemaining -= deltaSeconds;
                if (_attackCooldownRemaining < 0f) _attackCooldownRemaining = 0f;
            }

            // Death outranks everything, including a target it was mid-swing on.
            if (!_monster.IsAlive)
            {
                Enter(MonsterAiState.Dead, deltaSeconds);
                _monster.ClearTarget();
                return;
            }

            if (State == MonsterAiState.Dead)
            {
                // It came back. Respawn put it home; behaviour starts over.
                Enter(MonsterAiState.Idle, deltaSeconds);
            }

            _stateElapsed += deltaSeconds;

            MonsterDefinition definition = _monster.Definition;
            ICombatant target = ResolveTarget(candidates);

            // Leash first: a monster dragged too far from home gives up whatever it is
            // doing, so a player cannot walk one across a map.
            if (IsLeashed(definition))
            {
                _monster.ClearTarget();
                Enter(MonsterAiState.Return, deltaSeconds);
                return;
            }

            if (target == null)
            {
                _monster.ClearTarget();

                if (State == MonsterAiState.Chase || State == MonsterAiState.Attack
                    || State == MonsterAiState.Detect)
                {
                    // It had something and lost it.
                    Enter(MonsterAiState.Return, deltaSeconds);
                    return;
                }

                if (State == MonsterAiState.Return && IsHome())
                {
                    Enter(MonsterAiState.Idle, deltaSeconds);
                    return;
                }

                if (State != MonsterAiState.Return) Enter(MonsterAiState.Idle, deltaSeconds);
                return;
            }

            _monster.SetTarget(target.CombatantId);

            float sqrDistance = _monster.Position.SqrDistanceTo(target.Position);
            float attackRange = definition.AttackRange;

            if (attackRange > 0f && sqrDistance <= attackRange * attackRange)
            {
                Enter(MonsterAiState.Attack, deltaSeconds);

                if (_attackCooldownRemaining <= 0f)
                {
                    WantsToAttack = true;

                    float cooldown = definition.AttackCooldownSeconds;
                    _attackCooldownRemaining = cooldown > 0f ? cooldown : 0f;
                }

                return;
            }

            // Noticed but not yet committed: a brief pause so the turn reads as a reaction.
            if (State == MonsterAiState.Idle || State == MonsterAiState.Wander
                || State == MonsterAiState.Return)
            {
                Enter(MonsterAiState.Detect, deltaSeconds);
                return;
            }

            if (State == MonsterAiState.Detect && _stateElapsed < DetectDurationSeconds) return;

            Enter(MonsterAiState.Chase, deltaSeconds);
        }

        /// <summary>Sends it home without a target. What a reset or a wipe calls.</summary>
        public void ForceReturn()
        {
            _monster.ClearTarget();
            WantsToAttack = false;
            Enter(MonsterAiState.Return, 0f);
        }

        /// <summary>
        /// Picks something to fight.
        /// </summary>
        /// <remarks>
        /// The nearest living enemy inside the authored detection range. Deliberately the
        /// whole targeting policy: a threat table, taunts, party hate and boss aggro are
        /// later systems, and this is the single method they replace.
        ///
        /// A passive monster never picks anything, so it only fights back once something
        /// else sets its target.
        /// </remarks>
        public ICombatant SelectTarget(IReadOnlyList<ICombatant> candidates)
        {
            if (candidates == null) return null;

            MonsterDefinition definition = _monster.Definition;

            if (definition.AggressionType == MonsterAggressionType.Passive) return null;

            float range = definition.DetectionRange;
            if (range <= 0f) return null;

            float sqrRange = range * range;
            ICombatant best = null;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                ICombatant candidate = candidates[i];
                if (!candidate.IsAlive()) continue;
                if (candidate.CombatantId == _monster.InstanceId) continue;

                // Reuses the existing relationship rule rather than comparing teams here,
                // so "who is an enemy" has one definition in the project.
                if (CombatTeams.Relate(Combatant(), candidate) != CombatRelationship.Hostile) continue;

                float sqr = _monster.Position.SqrDistanceTo(candidate.Position);
                if (sqr > sqrRange || sqr >= bestSqr) continue;

                best = candidate;
                bestSqr = sqr;
            }

            return best;
        }

        /// <summary>
        /// Keeps the current target if it is still worth having, otherwise picks a new one.
        /// </summary>
        /// <remarks>Holding on matters: re-picking the nearest every tick would make a
        /// monster swap targets whenever two players crossed, which reads as broken.</remarks>
        private ICombatant ResolveTarget(IReadOnlyList<ICombatant> candidates)
        {
            if (candidates == null) return null;

            if (_monster.HasTarget)
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (candidates[i].CombatantId != _monster.TargetId) continue;
                    return IsStillValid(candidates[i]) ? candidates[i] : null;
                }

                // The target is not even on the list any more: it left, or logged out.
                return null;
            }

            return SelectTarget(candidates);
        }

        /// <summary>
        /// Whether a target is worth keeping.
        /// </summary>
        /// <remarks>Kept inside the leash rather than the detection range, so a monster does
        /// not forget someone who stepped one pace back. Giving up is the leash's job.</remarks>
        private bool IsStillValid(ICombatant target)
        {
            if (!target.IsAlive()) return false;

            MonsterDefinition definition = _monster.Definition;
            float leash = definition.LeashRange;

            if (leash <= 0f) return true;

            return target.Position.SqrDistanceTo(_monster.SpawnPosition) <= leash * leash * 4f;
        }

        private bool IsLeashed(MonsterDefinition definition)
        {
            float leash = definition.LeashRange;
            if (leash <= 0f) return false;

            return _monster.SqrDistanceFromSpawn > leash * leash;
        }

        /// <summary>Close enough to home to count as arrived.</summary>
        private bool IsHome()
        {
            const float Tolerance = 0.01f;
            return _monster.SqrDistanceFromSpawn <= Tolerance * Tolerance;
        }

        private void Enter(MonsterAiState state, float deltaSeconds)
        {
            if (State == state) return;

            State = state;

            // The tick that entered a state has already elapsed against the previous one.
            _stateElapsed = deltaSeconds;
        }

        private ICombatant Combatant()
        {
            return _cached ?? (_cached = new MonsterCombatant(_monster));
        }

        private MonsterCombatant _cached;
    }
}
